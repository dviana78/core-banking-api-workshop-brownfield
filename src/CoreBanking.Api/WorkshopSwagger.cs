using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CoreBanking.Api;

public class WorkshopSwagger : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var policy = context.MethodInfo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().FirstOrDefault()?.Policy;
        if (policy is null) return;
        operation.OperationId = context.ApiDescription.ActionDescriptor.AttributeRouteInfo?.Name;
        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2Auth" } }] = [policy]
        }];
    }
}