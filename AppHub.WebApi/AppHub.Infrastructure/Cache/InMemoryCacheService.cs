using AppHub.Infrastructure.Abstract;
using Microsoft.Extensions.Caching.Memory;

namespace AppHub.Infrastructure.Cache;

/// <summary>
/// In-memory fallback cache for local development when Redis is not configured.
/// NOT suitable for production (not distributed, lost on restart).
/// Replace with RedisCacheService in production by setting Redis:ConnectionString.
/// </summary>
public class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, long> _counters = new();
    private readonly object _lock = new();

    public InMemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key) where T : class =>
        Task.FromResult(_cache.TryGetValue(key, out T? val) ? val : null);

    public Task SetAsync<T>(string key, T value, TimeSpan expiry) where T : class
    {
        _cache.Set(key, value, expiry);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        lock (_lock) _counters.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key) =>
        Task.FromResult(_cache.TryGetValue(key, out _));

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) where T : class
    {
        if (_cache.TryGetValue(key, out T? val) && val != null) return val;
        var fresh = await factory();
        _cache.Set(key, fresh, expiry);
        return fresh;
    }

    public Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
    {
        lock (_lock)
        {
            _counters.TryGetValue(key, out var current);
            _counters[key] = current + 1;
            if (expiry.HasValue && current == 0)
            {
                // Schedule expiry via memory cache
                _cache.Set($"__expire_{key}", true, expiry.Value);
            }
            return Task.FromResult(_counters[key]);
        }
    }
}
