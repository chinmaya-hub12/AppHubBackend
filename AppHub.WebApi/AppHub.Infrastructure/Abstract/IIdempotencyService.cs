using AppHub.Core.Entity;

namespace AppHub.Infrastructure.Abstract;

/// <summary>
/// Idempotency service contract.
/// Provides get/store operations for cached API responses in Cosmos DB.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Retrieve an existing idempotency record by key.
    /// Returns null if no record exists (i.e. this is a new request).
    /// </summary>
    Task<IdempotencyRecord?> GetAsync(string idempotencyKey);

    /// <summary>
    /// Create a placeholder record (isProcessing = true) to lock the key
    /// before processing begins. Prevents duplicate concurrent execution.
    /// </summary>
    Task<bool> AcquireLockAsync(string idempotencyKey, string httpMethod,
        string requestPath, string username);

    /// <summary>
    /// Persist the completed response so future retries get it immediately.
    /// Marks isProcessing = false.
    /// </summary>
    Task StoreResponseAsync(string idempotencyKey, int statusCode,
        string responseBody, string contentType = "application/json");

    /// <summary>
    /// Release the lock if processing failed (so client can retry).
    /// </summary>
    Task ReleaseLockAsync(string idempotencyKey);
}
