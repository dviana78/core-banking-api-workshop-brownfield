using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace CoreBanking.Api.Models;

[SwaggerSchema("Public account reference.")]
public sealed record AccountReference(
    [property: SwaggerSchema("Opaque account identifier.")] string AccountId,
    [property: SwaggerSchema("Mambu deposit account type.")] string AccountType,
    string Currency, string AccountState);

[SwaggerSchema("Account balances at a shared cutoff.")]
public sealed record AccountBalance(
    [property: SwaggerSchema("Account identifier.")] string AccountId,
    DateTimeOffset AsOf, string Currency,
    [property: SwaggerSchema("Available customer funds.")] decimal AvailableBalance,
    [property: SwaggerSchema("Posted ledger balance.")] decimal BookBalance);

public sealed record CreateAccountRequest
{
    [Required, StringLength(200, MinimumLength = 1), SwaggerSchema("Account display name.")]
    public string Name { get; init; } = "";
    [Required, SwaggerSchema("Canonical product type.")]
    public string AccountType { get; init; } = "";
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "";
}

public sealed record BalanceAdjustmentRequest
{
    [Required, SwaggerSchema("Signed ledger amount; positive credits, negative debits.")]
    public decimal? Amount { get; init; }
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "";
    [Required, StringLength(1000, MinimumLength = 1), SwaggerSchema("Audit-only business reason.")]
    public string Reason { get; init; } = "";
}

public sealed record AdjustmentResponse(
    [property: SwaggerSchema("Recorded adjustment identifier.")] string AdjustmentId,
    string AccountId,
    [property: SwaggerSchema("Signed adjustment amount.")] decimal Amount,
    string Currency,
    [property: SwaggerSchema("Audit business reason.")] string Reason,
    DateTimeOffset RecordedAt);

public sealed record BalanceBusinessError(int ErrorCode, string Message);

public sealed record TokenRequest
{
    [FromForm(Name = "grant_type")]
    public string GrantType { get; init; } = "";
    [FromForm(Name = "client_id")]
    public string ClientId { get; init; } = "";
    [FromForm(Name = "client_secret")]
    public string ClientSecret { get; init; } = "";
    public string? Scope { get; init; }
    public string? Scenario { get; init; }
}

