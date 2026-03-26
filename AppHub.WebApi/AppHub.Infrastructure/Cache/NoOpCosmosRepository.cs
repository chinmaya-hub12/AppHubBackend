using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;

namespace AppHub.Infrastructure.Cache;

/// <summary>
/// No-op Cosmos repository for local development when CosmosDB is not configured.
/// Returns empty results — app starts and runs without Cosmos.
/// Replace with real CosmosRepository in production by setting CosmosDb config values.
/// </summary>
public class NoOpCosmosRepository<T> : ICosmosRepository<T> where T : CosmosDocument
{
    public Task<T?> GetByIdAsync(string id, string partitionKey) => Task.FromResult<T?>(null);
    public Task<IEnumerable<T>> GetByQueryAsync(string query, Dictionary<string, object>? parameters = null)
        => Task.FromResult(Enumerable.Empty<T>());
    public Task<T> CreateAsync(T document) => Task.FromResult(document);
    public Task<T> UpsertAsync(T document) => Task.FromResult(document);
    public Task DeleteAsync(string id, string partitionKey) => Task.CompletedTask;
}
