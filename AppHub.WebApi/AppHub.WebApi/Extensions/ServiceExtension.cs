using AppHub.Infrastructure.Abstract;
using AppHub.Infrastructure.Cache;
using AppHub.Infrastructure.Concrete;
using AppHub.Core.Entity;
using Microsoft.Azure.Cosmos;
using StackExchange.Redis;

namespace AppHub.WebApi.Extensions;

public static class ServiceExtension
{
    public static void ConfigureDIServices(this IServiceCollection services, IConfiguration config)
    {
        // ── Original services (kept) ──────────────────────────────────────────
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<JwtService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        // ── Azure Key Vault ───────────────────────────────────────────────────
        services.AddScoped<IKeyVaultService, KeyVaultService>();

        // ── Azure Blob Storage ────────────────────────────────────────────────
        services.AddScoped<IBlobStorageService, BlobStorageService>();

        // ── Redis Cache (singleton — ConnectionMultiplexer is thread-safe) ────
        // Connection string from Key Vault: Redis--ConnectionString
        // Format: "your-redis.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
        // Falls back gracefully if Redis is not configured (local dev).
        var redisConnectionString = config["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            // Local dev fallback: in-memory cache implementing ICacheService
            services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        // ── Azure Cosmos DB (singleton) ───────────────────────────────────────
        var cosmosEndpoint = config["CosmosDb:AccountEndpoint"];
        var cosmosKey      = config["CosmosDb:AccountKey"];

        if (!string.IsNullOrWhiteSpace(cosmosEndpoint) && !string.IsNullOrWhiteSpace(cosmosKey))
        {
            services.AddSingleton(_ =>
                new CosmosClient(cosmosEndpoint, cosmosKey, new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                }));
            services.AddScoped<ICosmosRepository<AuditLog>,          CosmosRepository<AuditLog>>();
            services.AddScoped<ICosmosRepository<MfaSession>,        CosmosRepository<MfaSession>>();
            services.AddScoped<ICosmosRepository<IdempotencyRecord>, CosmosRepository<IdempotencyRecord>>();
            services.AddScoped<IIdempotencyService, IdempotencyService>();
        }
        else
        {
            // Cosmos not configured — register no-op stubs so app still starts locally
            services.AddScoped<ICosmosRepository<AuditLog>,          NoOpCosmosRepository<AuditLog>>();
            services.AddScoped<ICosmosRepository<MfaSession>,        NoOpCosmosRepository<MfaSession>>();
            services.AddScoped<ICosmosRepository<IdempotencyRecord>, NoOpCosmosRepository<IdempotencyRecord>>();
            services.AddScoped<IIdempotencyService, NoOpIdempotencyService>();
        }

        // ── MFA + Audit ───────────────────────────────────────────────────────
        services.AddScoped<IMfaService,   MfaService>();
        services.AddScoped<IAuditService, AuditService>();
    }
}
