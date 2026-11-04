using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text.Json;
using CoreBanking.Api.Data;
using CoreBanking.Api.Integration;
using CoreBanking.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace CoreBanking.Api.Controllers;

[ApiController]
[Route("open-finance/system/accounts/v1")]
[Produces("application/json")]
public class AccountsController(BankingDbContext database, IHttpClientFactory clients, IConfiguration configuration) : ControllerBase
{
    private const string IdPattern = "^[A-Za-z0-9_-]{1,64}$";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("customers/{customerId}/accounts", Name = "listCustomerAccounts")]
    [Authorize(Policy = "accounts.read")]
    [SwaggerOperation(Summary = "List customer accounts", Description = "Returns all visible accounts. Incomplete source references fail the entire operation.")]
    [ProducesResponseType(typeof(AccountReference[]), 200)]
    public async Task<IActionResult> ListCustomerAccounts([RegularExpression(IdPattern)] string customerId, CancellationToken cancellationToken)
    {
        if (!Visible(customerId) || !await database.Customers.AnyAsync(item => item.Id == customerId, cancellationToken))
            return Failure(404, "Resource not found");
        var accounts = await database.Accounts.AsNoTracking().Where(item => item.CustomerId == customerId).OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var source = await Send(HttpMethod.Post, "/api/deposits:search", new { accountHolderKey = $"mock-client-{customerId[4..]}" },
            accounts.Select(Source), cancellationToken);
        if (source.ValueKind != JsonValueKind.Array) throw new WorkshopFailure(502, "Invalid account collection");
        return Ok(source.EnumerateArray().Select(Reference).ToArray());
    }

    [HttpPost("customers/{customerId}/accounts", Name = "createAccount")]
    [Authorize(Policy = "accounts.write")]
    [SwaggerOperation(Summary = "Create account", Description = "Resolves internal workshop customer/product mappings. Initial state is PENDING_APPROVAL.")]
    [ProducesResponseType(typeof(AccountReference), 201)]
    public async Task<IActionResult> CreateAccount([RegularExpression(IdPattern)] string customerId, CreateAccountRequest request, CancellationToken cancellationToken)
    {
        if (!Visible(customerId)) return Failure(404, "Resource not found");
        var customer = await database.Customers.FindAsync([customerId], cancellationToken);
        if (customer is null) return Failure(404, "Resource not found");
        if (!WorkshopSeed.Types.Contains(request.AccountType) || request.Currency != "EUR")
            return Failure(400, "Unsupported account type or currency");
        var product = await database.Products.SingleOrDefaultAsync(item => item.AccountType == request.AccountType && item.Currency == request.Currency, cancellationToken);
        if (product is null || Scenario() == "mapping-unavailable") return Failure(503, "Product mapping is unavailable");
        var account = new Account
        {
            Id = $"acc-{Guid.NewGuid():N}", CustomerId = customerId, ProductId = product.Id,
            Name = request.Name, AccountType = request.AccountType, Currency = request.Currency,
            AccountState = "PENDING_APPROVAL", AvailableBalance = 0m, BookBalance = 0m, AsOf = DateTimeOffset.UtcNow
        };
        var source = await Send(HttpMethod.Post, "/api/deposits", new
        {
            name = request.Name, accountHolderKey = customer.HolderKey, accountHolderType = "CLIENT", productTypeKey = product.Id
        }, Source(account), cancellationToken);
        var response = Reference(source);
        if (response.AccountId != account.Id || response.AccountState != "PENDING_APPROVAL")
            throw new WorkshopFailure(502, "Invalid created account representation");
        database.Accounts.Add(account);
        await database.SaveChangesAsync(cancellationToken);
        return StatusCode(201, response);
    }

    [HttpGet("accounts/{accountId}", Name = "getAccount")]
    [Authorize(Policy = "accounts.read")]
    [SwaggerOperation(Summary = "Get account")]
    [ProducesResponseType(typeof(AccountReference), 200)]
    public async Task<IActionResult> GetAccount([RegularExpression(IdPattern)] string accountId, CancellationToken cancellationToken)
    {
        var account = await Find(accountId, cancellationToken);
        if (account is null) return Failure(404, "Resource not found");
        return Ok(Reference(await Send(HttpMethod.Get, $"/api/deposits/{accountId}", null, Source(account), cancellationToken)));
    }

    [HttpGet("accounts/{accountId}/balances", Name = "getAccountBalances")]
    [Authorize(Policy = "balances.read")]
    [SwaggerOperation(Summary = "Get balances", Description = "Returns a shared UTC snapshot. Workshop business errors deliberately use HTTP 200.")]
    [ProducesResponseType(typeof(AccountBalance), 200)]
    public async Task<IActionResult> GetAccountBalances([RegularExpression(IdPattern)] string accountId, CancellationToken cancellationToken)
    {
        var account = await Find(accountId, cancellationToken);
        if (account is null) return Failure(404, "Resource not found");
        var source = await Send(HttpMethod.Get, $"/api/deposits/{accountId}", null, Source(account), cancellationToken);
        var reference = Reference(source);
        if (account.BalanceInquiryBlocked) return Ok(new BalanceBusinessError(677, "Balance is not posible"));
        if (reference.AccountState == "LOCKED") return Ok(new BalanceBusinessError(678, "Balance inquiry is not allowed for a locked account"));
        if (reference.AccountState == "PENDING_APPROVAL") return Ok(new BalanceBusinessError(679, "Balance is unavailable while the account is pending approval"));
        if (!source.TryGetProperty("balances", out var balances) ||
            !balances.TryGetProperty("availableBalance", out var available) || available.ValueKind != JsonValueKind.Number || !available.TryGetDecimal(out var availableAmount) ||
            !balances.TryGetProperty("bookBalance", out var book) || book.ValueKind != JsonValueKind.Number || !book.TryGetDecimal(out var bookAmount))
            return Ok(new BalanceBusinessError(680, "Required balance information is missing"));
        if (!source.TryGetProperty("asOf", out var cutoff) || !cutoff.TryGetDateTimeOffset(out var asOf) || asOf.Offset != TimeSpan.Zero)
            throw new WorkshopFailure(502, "Invalid balance cutoff");
        return Ok(new AccountBalance(reference.AccountId, asOf, reference.Currency, availableAmount, bookAmount));
    }

    [HttpPost("accounts/{accountId}/closure", Name = "closeAccount")]
    [Authorize(Policy = "accounts.close")]
    [SwaggerOperation(Summary = "Close account")]
    [ProducesResponseType(typeof(AccountReference), 200)]
    public async Task<IActionResult> CloseAccount([RegularExpression(IdPattern)] string accountId, CancellationToken cancellationToken)
    {
        if (await Find(accountId, cancellationToken) is null) return Failure(404, "Resource not found");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var account = await LockedAccount(accountId, cancellationToken);
        if (account.AccountState == "CLOSED") return Ok(new AccountReference(account.Id, account.AccountType, account.Currency, account.AccountState));
        if (account.AccountState is not ("ACTIVE" or "APPROVED") || account.BookBalance != 0 || account.AvailableBalance != 0 || account.Holds != 0)
            return Failure(409, "Account closure preconditions are not met");
        var response = await Send(HttpMethod.Post, $"/deposits/{accountId}:changeState", new { accountState = "CLOSED" },
            new { encodedKey = account.Id, accountType = account.AccountType, currencyCode = account.Currency, accountState = "CLOSED" }, cancellationToken);
        var reference = Reference(response);
        if (reference.AccountState != "CLOSED") throw new WorkshopFailure(502, "Invalid closure response");
        account.AccountState = "CLOSED";
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(reference);
    }

    [HttpPost("accounts/{accountId}/balance-adjustments", Name = "createBalanceAdjustment")]
    [Authorize(Policy = "balances.adjust")]
    [SwaggerOperation(Summary = "Post balance adjustment")]
    [ProducesResponseType(typeof(AdjustmentResponse), 201)]
    [ProducesResponseType(202)]
    public async Task<IActionResult> CreateBalanceAdjustment([RegularExpression(IdPattern)] string accountId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, BalanceAdjustmentRequest request, CancellationToken cancellationToken)
    {
        if (await Find(accountId, cancellationToken) is null) return Failure(404, "Resource not found");
        if (!Guid.TryParseExact(idempotencyKey, "D", out var key) || idempotencyKey![14] != '4' || !"89abAB".Contains(idempotencyKey[19]))
            return Failure(400, "Idempotency-Key must be a UUID v4");
        var amount = request.Amount!.Value;
        if (amount == 0 || amount != decimal.Round(amount, 2) || amount < -999999999999999999m || amount > 999999999999999999m)
            return Failure(400, "Amount must be nonzero and use EUR decimal scale within the storage range");
        var consumer = User.FindFirst("consumer")!.Value;
        var payload = $"{accountId}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|{request.Currency}|{request.Reason}";
        var record = await database.Idempotency.FindAsync([consumer, key], cancellationToken);
        if (record is not null) return Replay(record, payload);
        record = new IdempotencyRecord
        {
            Consumer = consumer, Key = key, Payload = payload, CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(configuration.GetValue("Workshop:IdempotencyHours", 24))
        };
        database.Idempotency.Add(record);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var duplicate = await database.Idempotency.FindAsync([consumer, key], cancellationToken);
            if (duplicate is null) throw;
            return Replay(duplicate, payload);
        }
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var account = await LockedAccount(accountId, cancellationToken);
            if (request.Currency != account.Currency) throw new WorkshopFailure(400, "Adjustment currency must match account currency");
            if (account.AccountState != "ACTIVE") throw new WorkshopFailure(409, "Account state does not allow adjustments");
            if (account.AvailableBalance is null || account.BookBalance is null) throw new WorkshopFailure(502, "Required balance information is missing");
            if (amount < 0 && account.AvailableBalance + account.OverdraftLimit + amount < 0)
                throw new WorkshopFailure(409, "Insufficient available funds and configured overdraft");
            if (Math.Abs(account.AvailableBalance.Value + amount) > 999999999999999999m || Math.Abs(account.BookBalance.Value + amount) > 999999999999999999m)
                throw new WorkshopFailure(409, "Resulting balance exceeds the workshop storage range");
            var adjustment = new BalanceAdjustment
            {
                Id = $"adj-{Guid.NewGuid():N}", AccountId = accountId, Amount = amount,
                Currency = request.Currency, Reason = request.Reason, RecordedAt = DateTimeOffset.UtcNow
            };
            var source = await Send(HttpMethod.Post, $"/deposits/{accountId}/transactions/{(amount > 0 ? "deposits" : "withdrawals")}",
                new { amount = Math.Abs(amount), currencyCode = request.Currency, transactionDetails = new { transactionChannelKey = "mock-channel-workshop" } },
                new { encodedKey = adjustment.Id, creationDate = adjustment.RecordedAt }, cancellationToken, key);
            if (!source.TryGetProperty("encodedKey", out var transactionId) || transactionId.GetString() != adjustment.Id)
                throw new WorkshopFailure(502, "Invalid transaction response");
            var response = new AdjustmentResponse(adjustment.Id, accountId, amount, request.Currency, request.Reason, adjustment.RecordedAt);
            account.BookBalance += amount;
            account.AvailableBalance += amount;
            account.AsOf = adjustment.RecordedAt;
            database.Adjustments.Add(adjustment);
            record.State = "Completed";
            record.Status = 201;
            record.Response = JsonSerializer.Serialize(response, Json);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return StatusCode(201, response);
        }
        catch (WorkshopFailure failure)
        {
            database.ChangeTracker.Clear();
            var failed = await database.Idempotency.FindAsync([consumer, key], cancellationToken);
            failed!.State = "Completed";
            failed.Status = failure.Status;
            failed.Response = JsonSerializer.Serialize(ProblemBody(failure.Status, failure.Message), Json);
            await database.SaveChangesAsync(cancellationToken);
            return Replay(failed, payload);
        }
    }

    private bool Visible(string customerId) => User.FindAll("customer_id").Any(claim => claim.Value == customerId) ||
        User.HasClaim("customer_group", "workshop") && customerId != "cus-101000";
    private async Task<Account?> Find(string accountId, CancellationToken cancellationToken)
    {
        var customers = User.FindAll("customer_id").Select(claim => claim.Value).ToArray();
        var portfolio = User.HasClaim("customer_group", "workshop");
        return await database.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == accountId &&
            (customers.Contains(item.CustomerId) || portfolio && item.CustomerId != "cus-101000"), cancellationToken);
    }
    private Task<Account> LockedAccount(string accountId, CancellationToken cancellationToken) => database.Accounts
        .FromSqlInterpolated($"SELECT * FROM [Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {accountId}").SingleAsync(cancellationToken);
    private static object Source(Account account) => new
    {
        encodedKey = account.Id, accountType = account.AccountType, currencyCode = account.Currency,
        accountState = account.AccountState, asOf = account.AsOf,
        balances = new { availableBalance = account.AvailableBalance, bookBalance = account.BookBalance }
    };
    private static AccountReference Reference(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("encodedKey", out var id) || id.ValueKind != JsonValueKind.String ||
            !source.TryGetProperty("accountType", out var type) || type.ValueKind != JsonValueKind.String ||
            !source.TryGetProperty("currencyCode", out var currency) || currency.ValueKind != JsonValueKind.String ||
            !source.TryGetProperty("accountState", out var state) || state.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()) || !WorkshopSeed.Types.Contains(type.GetString()) ||
            currency.GetString() != "EUR" || !WorkshopSeed.States.Contains(state.GetString()))
            throw new WorkshopFailure(502, "Core Banking returned an incomplete account reference");
        return new(id.GetString()!, type.GetString()!, currency.GetString()!, state.GetString()!);
    }
    private string Scenario()
    {
        var scenario = Request.Headers["X-Workshop-Scenario"].ToString();
        if (scenario.Length != 0 && !new[] { "empty-list", "incomplete-reference", "corrupt-json", "upstream-conflict", "upstream-invalid", "upstream-unavailable", "upstream-timeout", "internal-error", "mapping-unavailable" }.Contains(scenario))
            throw new WorkshopFailure(400, "Unknown workshop scenario");
        return scenario;
    }
    private async Task<JsonElement> Send(HttpMethod method, string path, object? body, object snapshot, CancellationToken cancellationToken, Guid? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Options.Set(MambuMockHandler.Snapshot, snapshot);
        request.Options.Set(MambuMockHandler.Scenario, Scenario());
        request.Headers.TryAddWithoutValidation("Correlation-Id", Response.Headers["Correlation-Id"].ToString());
        if (key.HasValue) request.Headers.Add("Idempotency-Key", key.Value.ToString());
        using var response = await clients.CreateClient("Mambu").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new WorkshopFailure((int)response.StatusCode, "Simulated Core Banking request failed");
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new WorkshopFailure(502, "Invalid Core Banking JSON response"); }
    }
    private object ProblemBody(int status, string message) => new
    {
        type = "about:blank", title = message, status, correlationId = Response.Headers["Correlation-Id"].ToString()
    };
    private ObjectResult Failure(int status, string message) => new(ProblemBody(status, message))
    {
        StatusCode = status, ContentTypes = { "application/problem+json" }
    };
    private IActionResult Replay(IdempotencyRecord record, string payload)
    {
        if (record.Payload != payload) return Failure(409, "Idempotency key was already used with a different payload");
        if (record.State == "Processing") return StatusCode(202);
        return new ContentResult { StatusCode = record.Status, ContentType = record.Status >= 400 ? "application/problem+json" : "application/json", Content = record.Response };
    }
}