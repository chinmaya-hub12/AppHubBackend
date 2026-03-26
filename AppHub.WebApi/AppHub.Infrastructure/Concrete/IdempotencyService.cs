using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppHub.Infrastructure.Concrete;

/// <summary>
/// Cosmos DB backed idempotency service.
///
/// How it works:
///   1. Client sends POST/PUT/DELETE with header: Idempotency-Key: &lt;uuid&gt;
///   2. Middleware calls GetAsync → if record found → return cached response
///   3. If not found → AcquireLockAsync (creates record with isProcessing=true)
///   4. Controller executes normally
///   5. Middleware captures response → StoreResponseAsync (isProcessing=false)
///   6. On any future retry with the same key → step 2 returns cached response
///
/// Concurrent duplicate prevention:
///   If two identical requests arrive simultaneously, the second one sees
///   isProcessing=true and returns HTTP 409 Conflict, telling the client
///   to wait and retry.
///
/// TTL: Records auto-expire after 24 hours (Cosmos TTL = 86400 seconds).
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly Container _container;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(CosmosClient cosmosClient, IConfiguration configuration,
        ILogger<IdempotencyService> logger)
    {
        _logger = logger;
        var dbName = configuration["CosmosDb:DatabaseName"] ?? "AppHubDb";
        _container = cosmosClient.GetContainer(dbName, "IdempotencyRecord");
    }

    public async Task<IdempotencyRecord?> GetAsync(string idempotencyKey)
    {
        try
        {
            var response = await _container.ReadItemAsync<IdempotencyRecord>(
                idempotencyKey, new PartitionKey(idempotencyKey));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read idempotency record for key {Key}", idempotencyKey);
            return null; // fail open — do not block request on idempotency store failure
        }
    }

    public async Task<bool> AcquireLockAsync(string idempotencyKey, string httpMethod,
        string requestPath, string username)
    {
        try
        {
            var record = new IdempotencyRecord
            {
                Id             = idempotencyKey,
                PartitionKey   = idempotencyKey,
                IdempotencyKey = idempotencyKey,
                HttpMethod     = httpMethod,
                RequestPath    = requestPath,
                Username       = username,
                IsProcessing   = true,
                StatusCode     = 0,
                ResponseBody   = string.Empty,
                Ttl            = 86_400
            };

            // CreateItemAsync throws CosmosException 409 if key already exists
            await _container.CreateItemAsync(record, new PartitionKey(idempotencyKey));
            _logger.LogDebug("Idempotency lock acquired for key {Key}", idempotencyKey);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Key already exists — another request is in-flight or already completed
            _logger.LogWarning("Idempotency key {Key} already locked/used", idempotencyKey);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire idempotency lock for key {Key}", idempotencyKey);
            return true; // fail open — allow request through
        }
    }

    public async Task StoreResponseAsync(string idempotencyKey, int statusCode,
        string responseBody, string contentType = "application/json")
    {
        try
        {
            var existing = await GetAsync(idempotencyKey);
            if (existing == null) return;

            existing.StatusCode   = statusCode;
            existing.ResponseBody = responseBody;
            existing.ContentType  = contentType;
            existing.IsProcessing = false;
            existing.ModifiedOn   = DateTime.UtcNow;

            await _container.UpsertItemAsync(existing, new PartitionKey(idempotencyKey));
            _logger.LogDebug("Idempotency response stored for key {Key} (status {StatusCode})",
                idempotencyKey, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store idempotency response for key {Key}", idempotencyKey);
        }
    }

    public async Task ReleaseLockAsync(string idempotencyKey)
    {
        try
        {
            await _container.DeleteItemAsync<IdempotencyRecord>(
                idempotencyKey, new PartitionKey(idempotencyKey));
            _logger.LogDebug("Idempotency lock released for key {Key}", idempotencyKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release idempotency lock for key {Key}", idempotencyKey);
        }
    }
}
