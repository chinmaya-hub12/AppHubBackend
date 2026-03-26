namespace AppHub.WebApi.Middleware;

/// <summary>
/// Action filter attribute that enforces presence of the Idempotency-Key header
/// on the decorated endpoint. Apply to any POST/PUT/DELETE/PATCH action where
/// you want to REQUIRE (not just support) idempotency.
///
/// Usage:
///   [RequiresIdempotencyKey]
///   [HttpPost("upload")]
///   public async Task&lt;IActionResult&gt; Upload(...) { ... }
///
/// Without this attribute, idempotency is still SUPPORTED if the client
/// sends the header, but it is not enforced.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequiresIdempotencyKeyAttribute : Attribute, Microsoft.AspNetCore.Mvc.Filters.IActionFilter
{
    public void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (!context.HttpContext.Request.Headers.ContainsKey("Idempotency-Key"))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
            {
                success = false,
                message = "The 'Idempotency-Key' header is required for this endpoint. " +
                          "Generate a UUID (e.g. Guid.NewGuid()) and include it as: " +
                          "Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000",
                example = "Idempotency-Key: " + Guid.NewGuid().ToString()
            });
        }
    }

    public void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context) { }
}
