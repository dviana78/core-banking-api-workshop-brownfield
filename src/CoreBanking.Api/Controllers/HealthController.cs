using CoreBanking.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoreBanking.Api.Controllers;

[ApiController]
public class HealthController(BankingDbContext database) : ControllerBase
{
    [HttpGet("health")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var connected = await database.Database.CanConnectAsync(cancellationToken);
        return StatusCode(connected ? 200 : 503, new { status = connected ? "Healthy" : "Database unavailable" });
    }
}