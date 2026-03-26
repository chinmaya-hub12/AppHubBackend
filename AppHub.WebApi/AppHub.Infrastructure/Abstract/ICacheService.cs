namespace AppHub.Infrastructure.Abstract;

/// <summary>
/// Redis distributed cache service contract.
/// Provides typed get/set/remove with automatic JSON serialization.
/// Used for: user sessions, OTP rate limiting, API response caching,
/// idempotency key fast-lookup (L1 cache before Cosmos DB).
/// </summary>
public interface ICacheService
{
    /// <summary>Get a cached value by key. Returns null if not found or expired.</summary>
    Task<T?> GetAsync<T>(string key) where T : class;

    /// <summary>Set a value with an absolute expiry.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan expiry) where T : class;

    /// <summary>Remove a key immediately.</summary>
    Task RemoveAsync(string key);

    /// <summary>Check if a key exists without fetching the value.</summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// Get or set pattern — returns cached value if present,
    /// otherwise executes factory, caches the result, and returns it.
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) where T : class;

    /// <summary>Increment a counter (used for rate limiting). Returns new value.</summary>
    Task<long> IncrementAsync(string key, TimeSpan? expiry = null);
}
