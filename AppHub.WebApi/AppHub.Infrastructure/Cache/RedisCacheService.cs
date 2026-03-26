using AppHub.Infrastructure.Abstract;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace AppHub.Infrastructure.Cache;

/// <summary>
/// Redis distributed cache implementation using StackExchange.Redis.
///
/// Used in this solution for:
///  1. OTP rate limiting     — key: "otp:ratelimit:{username}"  TTL: 5 min
///  2. User session cache    — key: "user:session:{userId}"     TTL: 60 min
///  3. Idempotency L1 cache  — key: "idempotency:{key}"         TTL: 24 h
///  4. General API responses — key: "api:{endpoint}:{params}"   TTL: configurable
///
/// Connection string from Key Vault: Redis--ConnectionString
/// Format: "your-redis.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
///
/// Falls back gracefully if Redis is unavailable (fail-open pattern).
/// Production should use Azure Cache for Redis (Basic/Standard/Premium tier).
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis  = redis;
        _db     = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue) return null;
            return JsonSerializer.Deserialize<T>(value!, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key {Key} — returning null", key);
            return null; // fail-open
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOpts);
            await _db.StringSetAsync(key, json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key {Key} — continuing without cache", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for key {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis EXISTS failed for key {Key} — returning false", key);
            return false;
        }
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) where T : class
    {
        var cached = await GetAsync<T>(key);
        if (cached != null) return cached;

        var fresh = await factory();
        await SetAsync(key, fresh, expiry);
        return fresh;
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
    {
        try
        {
            var count = await _db.StringIncrementAsync(key);
            // Set expiry only on first increment (count == 1)
            if (count == 1 && expiry.HasValue)
                await _db.KeyExpireAsync(key, expiry.Value);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis INCR failed for key {Key} — returning 0", key);
            return 0; // fail-open: allow request through
        }
    }
}
