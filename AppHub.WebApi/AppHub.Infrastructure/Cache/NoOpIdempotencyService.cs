using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;

namespace AppHub.Infrastructure.Cache;

/// <summary>
/// No-op idempotency service for local development when CosmosDB is not configured.
/// Every request is treated as new — no replay/deduplication in local dev.
/// </summary>
public class NoOpIdempotencyService : IIdempotencyService
{
    public Task<IdempotencyRecord?> GetAsync(string key) => Task.FromResult<IdempotencyRecord?>(null);
    public Task<bool> AcquireLockAsync(string key, string method, string path, string username)
        => Task.FromResult(true);
    public Task StoreResponseAsync(string key, int status, string body, string contentType = "application/json")
        => Task.CompletedTask;
    public Task ReleaseLockAsync(string key) => Task.CompletedTask;
}
