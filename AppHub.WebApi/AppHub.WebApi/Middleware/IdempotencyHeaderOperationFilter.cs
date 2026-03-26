using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AppHub.WebApi.Middleware;

/// <summary>
/// Swagger operation filter — automatically adds the optional Idempotency-Key
/// header parameter to all POST, PUT, DELETE, PATCH operations in Swagger UI.
/// This makes it visible and testable directly from the API docs.
/// </summary>
public class IdempotencyHeaderOperationFilter : IOperationFilter
{
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "post", "put", "delete", "patch" };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod ?? string.Empty;
        if (!MutatingMethods.Contains(method)) return;

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name        = "Idempotency-Key",
            In          = ParameterLocation.Header,
            Required    = false,
            Schema      = new OpenApiSchema { Type = "string", Format = "uuid" },
            Description = "Optional UUID to make this request idempotent. " +
                          "Retrying with the same key returns the cached response " +
                          "without re-executing business logic. Keys expire after 24 hours. " +
                          "Example: " + Guid.NewGuid().ToString(),
            Example     = new Microsoft.OpenApi.Any.OpenApiString(Guid.NewGuid().ToString())
        });
    }
}
