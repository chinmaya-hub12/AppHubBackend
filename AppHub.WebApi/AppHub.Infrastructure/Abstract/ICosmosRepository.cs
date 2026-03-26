using AppHub.Core.Entity;

namespace AppHub.Infrastructure.Abstract;

/// <summary>
/// Generic Cosmos DB repository contract.
/// </summary>
public interface ICosmosRepository<T> where T : CosmosDocument
{
    Task<T?> GetByIdAsync(string id, string partitionKey);
    Task<IEnumerable<T>> GetByQueryAsync(string query, Dictionary<string, object>? parameters = null);
    Task<T> CreateAsync(T document);
    Task<T> UpsertAsync(T document);
    Task DeleteAsync(string id, string partitionKey);
}
