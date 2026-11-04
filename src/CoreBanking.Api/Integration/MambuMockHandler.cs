using System.Net;
using System.Text;
using System.Text.Json;

namespace CoreBanking.Api.Integration;

public class WorkshopFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public class MambuMockHandler : HttpMessageHandler
{
    public static readonly HttpRequestOptionsKey<object> Snapshot = new("workshop-snapshot");
    public static readonly HttpRequestOptionsKey<string> Scenario = new("workshop-scenario");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RequestUri?.Host != "mambu.invalid" || !request.Options.TryGetValue(Snapshot, out var snapshot))
            throw new WorkshopFailure(502, "Invalid workshop upstream request");
        var path = request.RequestUri.AbsolutePath;
        if (request.Method == HttpMethod.Post)
        {
            if (request.Content is null) throw new WorkshopFailure(502, "Missing upstream request body");
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (path.EndsWith("/transactions/deposits") || path.EndsWith("/transactions/withdrawals"))
            {
                if (!request.Headers.Contains("Idempotency-Key") || body.RootElement.GetProperty("amount").GetDecimal() <= 0)
                    throw new WorkshopFailure(502, "Invalid upstream transaction request");
            }
            else if (path == "/api/deposits")
            {
                if (!body.RootElement.GetProperty("accountHolderKey").GetString()!.StartsWith("mock-client-") ||
                    !body.RootElement.GetProperty("productTypeKey").GetString()!.StartsWith("mock-product-"))
                    throw new WorkshopFailure(503, "Workshop mappings are unavailable");
            }
            else if (!path.EndsWith(":changeState") && path != "/api/deposits:search")
                throw new WorkshopFailure(502, "Unknown workshop source operation");
        }
        else if (request.Method != HttpMethod.Get || !path.StartsWith("/api/deposits/"))
            throw new WorkshopFailure(502, "Unknown workshop source operation");

        request.Options.TryGetValue(Scenario, out var scenario);
        var status = scenario switch
        {
            "upstream-conflict" => 409,
            "upstream-invalid" => 502,
            "upstream-unavailable" or "mapping-unavailable" => 503,
            "upstream-timeout" => 504,
            "internal-error" => 500,
            _ => request.Method == HttpMethod.Post && (path == "/api/deposits" || path.Contains("/transactions/")) ? 201 : 200
        };
        var json = scenario switch
        {
            "corrupt-json" => "{invalid",
            "empty-list" => "[]",
            "incomplete-reference" => "{}",
            _ => JsonSerializer.Serialize(snapshot, Json)
        };
        return new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"), RequestMessage = request
        };
    }
}