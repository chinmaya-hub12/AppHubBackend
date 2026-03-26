using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppHub.Infrastructure.Concrete;

/// <summary>
/// Generic Azure Cosmos DB repository. Each T maps to its own Cosmos container.
/// </summary>
public class CosmosRepository<T> : ICosmosRepository<T> where T : CosmosDocument
{
    private readonly Container _container;
    private readonly ILogger<CosmosRepository<T>> _logger;

    public CosmosRepository(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosRepository<T>> logger)
    {
        _logger = logger;
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "AppHubDb";
        var containerName = typeof(T).Name; // Container named after the document type
        _container = cosmosClient.GetContainer(databaseName, containerName);
    }

    public async Task<T?> GetByIdAsync(string id, string partitionKey)
    {
        try
        {
            var response = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading item {Id} from Cosmos container {Container}", id, _container.Id);
            throw;
        }
    }

    public async Task<IEnumerable<T>> GetByQueryAsync(string query, Dictionary<string, object>? parameters = null)
    {
        var queryDef = new QueryDefinition(query);
        if (parameters != null)
        {
            foreach (var p in parameters)
                queryDef.WithParameter(p.Key, p.Value);
        }

        var results = new List<T>();
        using var feed = _container.GetItemQueryIterator<T>(queryDef);
        while (feed.HasMoreResults)
        {
            var batch = await feed.ReadNextAsync();
            results.AddRange(batch);
        }
        return results;
    }

    public async Task<T> CreateAsync(T document)
    {
        var response = await _container.CreateItemAsync(document, new PartitionKey(document.PartitionKey));
        return response.Resource;
    }

    public async Task<T> UpsertAsync(T document)
    {
        document.ModifiedOn = DateTime.UtcNow;
        var response = await _container.UpsertItemAsync(document, new PartitionKey(document.PartitionKey));
        return response.Resource;
    }

    public async Task DeleteAsync(string id, string partitionKey)
    {
        await _container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey));
    }
}
