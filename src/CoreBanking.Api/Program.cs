using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

if (args.Contains("--seed-info"))
{
	var accounts = CoreBanking.Api.Data.WorkshopSeed.Accounts();
	Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
	{
		Products = CoreBanking.Api.Data.WorkshopSeed.Products().Length,
		Customers = CoreBanking.Api.Data.WorkshopSeed.Customers().Length,
		Accounts = accounts.Length,
		States = accounts.GroupBy(item => item.AccountState).ToDictionary(group => group.Key, group => group.Count()),
		ProductsDistribution = accounts.GroupBy(item => item.ProductId).ToDictionary(group => group.Key, group => group.Count()),
		Representatives = accounts.GroupBy(item => item.AccountState).ToDictionary(group => group.Key, group => group.First().Id),
		CustomersWithoutAccounts = CoreBanking.Api.Data.WorkshopSeed.Customers().Count(customer => !accounts.Any(item => item.CustomerId == customer.Id))
	}));
	return;
}

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Workshop"))
	throw new InvalidOperationException("This mock API requires Development or Workshop environment.");
var signingKey = builder.Configuration["Workshop:SigningKey"] ?? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
if (System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
	throw new InvalidOperationException("Workshop signing key must contain at least 32 bytes.");
var tokenSettings = new CoreBanking.Api.Controllers.WorkshopTokens(signingKey,
	builder.Configuration["Workshop:ClientSecret"] ?? "workshop-only",
	builder.Configuration.GetValue("Workshop:TokenMinutes", 60));
builder.Services.AddSingleton(tokenSettings);
builder.Services.AddDbContext<CoreBanking.Api.Data.BankingDbContext>(options =>
	Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions.UseSqlServer(options,
		builder.Configuration.GetConnectionString("Banking") ?? "Server=localhost,14333;Database=CoreBankingWorkshop;Integrated Security=True;TrustServerCertificate=True"));
builder.Services.AddHttpClient("Mambu", client =>
{
	client.BaseAddress = new Uri("https://mambu.invalid");
	client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new CoreBanking.Api.Integration.MambuMockHandler());
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddAuthentication("Bearer").AddJwtBearer(options =>
{
	options.MapInboundClaims = false;
	options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
	{
		ValidateIssuer = true, ValidIssuer = CoreBanking.Api.Controllers.WorkshopTokens.Issuer,
		ValidateAudience = true, ValidAudience = CoreBanking.Api.Controllers.WorkshopTokens.Audience,
		ValidateLifetime = true, ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.Zero,
		IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey))
	};
});
builder.Services.AddAuthorization(options =>
{
	foreach (var scope in CoreBanking.Api.Controllers.WorkshopTokens.Scopes)
		options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim("consumer")
			.RequireAssertion(context => context.User.FindAll("scope").SelectMany(claim => claim.Value.Split(' ')).Contains(scope)));
});
builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = 429;
	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
		RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("consumer")?.Value ?? "anonymous", _ => new FixedWindowRateLimiterOptions
		{
			PermitLimit = builder.Configuration.GetValue("Workshop:RateLimit", 500), Window = TimeSpan.FromMinutes(1), QueueLimit = 0
		}));
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
	context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Response.Headers["Correlation-Id"].ToString());
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
	options.InvalidModelStateResponseFactory = context => new Microsoft.AspNetCore.Mvc.ObjectResult(new
	{
		type = "about:blank", title = "Invalid request parameters or body", status = 400,
		correlationId = context.HttpContext.Response.Headers["Correlation-Id"].ToString()
	}) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
	options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
	{
		Title = "Core Banking API - Workshop",
		Version = "v1",
		Description = "Local workshop simulation. Business balance errors deliberately return HTTP 200. Documentation is intentionally partial."
	});
	options.EnableAnnotations();
	options.AddSecurityDefinition("oauth2Auth", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
	{
		Type = Microsoft.OpenApi.Models.SecuritySchemeType.OAuth2,
		Flows = new Microsoft.OpenApi.Models.OpenApiOAuthFlows
		{
			ClientCredentials = new Microsoft.OpenApi.Models.OpenApiOAuthFlow
			{
				TokenUrl = new Uri("/oauth/token", UriKind.Relative),
				Scopes = CoreBanking.Api.Controllers.WorkshopTokens.Scopes.ToDictionary(scope => scope, scope => scope)
			}
		}
	});
	options.OperationFilter<CoreBanking.Api.WorkshopSwagger>();
});
var app = builder.Build();
if (args.Contains("--migrate-only"))
{
	using var scope = app.Services.CreateScope();
	var database = scope.ServiceProvider.GetRequiredService<CoreBanking.Api.Data.BankingDbContext>();
	await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(database.Database);
	return;
}
app.Use(async (context, next) =>
{
	var correlation = context.Request.Headers["Correlation-Id"].ToString();
	context.Response.Headers["Correlation-Id"] = string.IsNullOrEmpty(correlation) ? Guid.NewGuid().ToString() : correlation;
	try { await next(); }
	catch (Exception exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
	{
		context.Response.StatusCode = exception switch
		{
			CoreBanking.Api.Integration.WorkshopFailure failure => failure.Status,
			Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => 409,
			Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException => 503,
			HttpRequestException => 503,
			TaskCanceledException => 504,
			_ => 500
		};
		await context.Response.WriteAsJsonAsync(new
		{
			type = "about:blank", title = exception is CoreBanking.Api.Integration.WorkshopFailure known ? known.Message : "Request could not be completed",
			status = context.Response.StatusCode, correlationId = context.Response.Headers["Correlation-Id"].ToString()
		}, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
	}
});
app.UseStatusCodePages(async context =>
{
	var http = context.HttpContext;
	await http.Response.WriteAsJsonAsync(new
	{
		type = "about:blank", title = Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(http.Response.StatusCode),
		status = http.Response.StatusCode, correlationId = http.Response.Headers["Correlation-Id"].ToString()
	}, options: null, contentType: "application/problem+json", cancellationToken: http.RequestAborted);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Workshop"))
{
	app.UseSwagger();
	app.UseSwaggerUI(options =>
	{
		options.SwaggerEndpoint("/swagger/v1/swagger.json", "Core Banking API v1");
		options.OAuthClientId("workshop-client");
	});
}
app.MapControllers();
app.Run();