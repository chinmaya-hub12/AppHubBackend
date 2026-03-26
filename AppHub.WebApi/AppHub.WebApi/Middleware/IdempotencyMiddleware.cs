using AppHub.Infrastructure.Abstract;
using Microsoft.IO;
using System.Text;

namespace AppHub.WebApi.Middleware;

/// <summary>
/// Idempotency middleware.
///
/// HOW TO USE (client side):
///   Add header to any POST/PUT/DELETE/PATCH request:
///     Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
///
///   Use a fresh UUID per unique operation.
///   Reuse the same UUID when retrying a failed/timed-out request.
///
/// BEHAVIOUR:
///   New request      → executes normally, caches response in Cosmos (TTL 24h)
///   Retry (same key) → returns cached response immediately (no DB writes)
///   In-flight dupe   → returns HTTP 409 with retry instructions
///   No header        → request passes through unchanged (no idempotency applied)
///   GET requests     → always pass through (GET is naturally idempotent)
///
/// RESPONSE HEADERS added on every response:
///   Idempotency-Key: {key}        — echoes back the key used
///   X-Idempotent-Replayed: true   — present only when returning a cached response
/// </summary>
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private static readonly RecyclableMemoryStreamManager _streamManager = new();

    // Only these methods benefit from idempotency handling
    private static readonly HashSet<string> IdempotentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    // Paths that are intentionally excluded from idempotency
    // (health checks, swagger, static files)
    private static readonly string[] ExcludedPaths =
    [
        "/health/", "/swagger", "/favicon"
    ];

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyService idempotencyService)
    {
        var method = context.Request.Method;
        var path   = context.Request.Path.Value ?? string.Empty;

        // ── Only apply to mutating methods ────────────────────────────────────
        if (!IdempotentMethods.Contains(method))
        {
            await _next(context);
            return;
        }

        // ── Skip excluded paths ───────────────────────────────────────────────
        if (ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // ── Read Idempotency-Key header ───────────────────────────────────────
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
        {
            // No header supplied — pass through without idempotency
            await _next(context);
            return;
        }

        var idempotencyKey = keyValues.ToString().Trim();

        // Validate key format (must be a non-empty string, max 128 chars)
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"success\":false,\"message\":\"Idempotency-Key header must be 1-128 characters.\"}");
            return;
        }

        var username = context.User.Identity?.Name ?? "anonymous";
        _logger.LogDebug("Idempotency check: key={Key} method={Method} path={Path}",
            idempotencyKey, method, path);

        // ── Check for existing record ─────────────────────────────────────────
        var existing = await idempotencyService.GetAsync(idempotencyKey);

        if (existing != null)
        {
            // Request is still being processed by another thread/instance
            if (existing.IsProcessing)
            {
                _logger.LogWarning("Idempotency key {Key} is currently in-flight — returning 409", idempotencyKey);
                context.Response.StatusCode = 409;
                context.Response.ContentType = "application/json";
                context.Response.Headers["Idempotency-Key"] = idempotencyKey;
                await context.Response.WriteAsync(
                    "{\"success\":false,\"message\":\"Request is currently being processed. " +
                    "Please wait a moment and retry.\",\"retryAfterSeconds\":5}");
                return;
            }

            // ── Replay cached response ────────────────────────────────────────
            _logger.LogInformation("Idempotency REPLAY: key={Key} status={Status}", idempotencyKey, existing.StatusCode);
            context.Response.StatusCode  = existing.StatusCode;
            context.Response.ContentType = existing.ContentType;
            context.Response.Headers["Idempotency-Key"]      = idempotencyKey;
            context.Response.Headers["X-Idempotent-Replayed"] = "true";
            await context.Response.WriteAsync(existing.ResponseBody, Encoding.UTF8);
            return;
        }

        // ── New request — acquire lock ────────────────────────────────────────
        var locked = await idempotencyService.AcquireLockAsync(idempotencyKey, method, path, username);
        if (!locked)
        {
            // Race condition: another concurrent request just created the same key
            context.Response.StatusCode  = 409;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Idempotency-Key"] = idempotencyKey;
            await context.Response.WriteAsync(
                "{\"success\":false,\"message\":\"Duplicate request detected. " +
                "The same Idempotency-Key is already being processed.\",\"retryAfterSeconds\":5}");
            return;
        }

        // ── Execute request, capture response body ────────────────────────────
        var originalBody = context.Response.Body;
        await using var memStream = _streamManager.GetStream();
        context.Response.Body = memStream;

        try
        {
            await _next(context);

            // Read captured response
            memStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(memStream).ReadToEndAsync();

            // Store in Cosmos (non-blocking best-effort)
            _ = Task.Run(async () =>
            {
                try
                {
                    await idempotencyService.StoreResponseAsync(
                        idempotencyKey,
                        context.Response.StatusCode,
                        responseBody,
                        context.Response.ContentType ?? "application/json");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist idempotency response for key {Key}", idempotencyKey);
                }
            });

            // Add idempotency header to response
            context.Response.Headers["Idempotency-Key"] = idempotencyKey;

            // Write response to original stream
            memStream.Seek(0, SeekOrigin.Begin);
            await memStream.CopyToAsync(originalBody);
        }
        catch (Exception ex)
        {
            // On failure — release lock so client can retry
            _logger.LogError(ex, "Request failed for idempotency key {Key} — releasing lock", idempotencyKey);
            _ = Task.Run(() => idempotencyService.ReleaseLockAsync(idempotencyKey));
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
