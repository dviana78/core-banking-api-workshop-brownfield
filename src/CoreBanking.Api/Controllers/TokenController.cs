using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CoreBanking.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.Annotations;

namespace CoreBanking.Api.Controllers;

public sealed record WorkshopTokens(string SigningKey, string ClientSecret, int Minutes)
{
    public static readonly string[] Scopes = ["accounts.read", "balances.read", "accounts.write", "accounts.close", "balances.adjust"];
    public const string Issuer = "corebanking-workshop";
    public const string Audience = "accounts-api";
}

[ApiController]
[Route("oauth")]
public class TokenController(WorkshopTokens settings) : ControllerBase
{
    [HttpPost("token")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [SwaggerOperation(Summary = "Issue a workshop mock token")]
    public IActionResult Token([FromForm] TokenRequest request)
    {
        if (request.GrantType != "client_credentials") return BadRequest(new { error = "unsupported_grant_type" });
        if (request.ClientId is not ("workshop-client" or "workshop-other") ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(request.ClientSecret)), SHA256.HashData(Encoding.UTF8.GetBytes(settings.ClientSecret))))
            return Unauthorized(new { error = "invalid_client" });
        var scopes = !Request.Form.ContainsKey("scope") ? WorkshopTokens.Scopes : (request.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (scopes.Except(WorkshopTokens.Scopes).Any()) return BadRequest(new { error = "invalid_scope" });
        if (request.Scenario is not (null or "" or "expired")) return BadRequest(new { error = "invalid_scenario" });
        var now = DateTimeOffset.UtcNow;
        var expired = request.Scenario == "expired";
        var claims = new List<Claim>
        {
            new("consumer", request.ClientId), new("scope", string.Join(' ', scopes)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("customer_id", request.ClientId == "workshop-client" ? "cus-100045" : "cus-101000")
        };
        if (request.ClientId == "workshop-client") claims.Add(new("customer_group", "workshop"));
        var token = new JwtSecurityToken(WorkshopTokens.Issuer, WorkshopTokens.Audience, claims,
            notBefore: now.AddHours(expired ? -2 : 0).UtcDateTime,
            expires: expired ? now.AddHours(-1).UtcDateTime : now.AddMinutes(settings.Minutes).UtcDateTime,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
        return Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token), token_type = "Bearer", expires_in = expired ? 0 : settings.Minutes * 60, scope = string.Join(' ', scopes) });
    }
}