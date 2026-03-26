using Newtonsoft.Json;

namespace AppHub.Core.Entity;

/// <summary>
/// Stores a cached API response in Azure Cosmos DB for idempotency.
/// When a client retries a request with the same Idempotency-Key header,
/// the original response is returned immediately without re-executing business logic.
///
/// Container: IdempotencyRecord
/// Partition key: /partitionKey  (= the idempotency key itself)
/// TTL: 24 hours (Cosmos auto-deletes after expiry)
/// </summary>
public class IdempotencyRecord : CosmosDocument
{
    /// <summary>Client-supplied idempotency key (from Idempotency-Key header).</summary>
    [JsonProperty("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>HTTP method of the original request (POST, PUT, DELETE, PATCH).</summary>
    [JsonProperty("httpMethod")]
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>Request path e.g. /api/login</summary>
    [JsonProperty("requestPath")]
    public string RequestPath { get; set; } = string.Empty;

    /// <summary>Username of the caller (from JWT or "anonymous").</summary>
    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>HTTP status code of the cached response.</summary>
    [JsonProperty("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>Full JSON body of the cached response.</summary>
    [JsonProperty("responseBody")]
    public string ResponseBody { get; set; } = string.Empty;

    /// <summary>Content-Type of the cached response.</summary>
    [JsonProperty("contentType")]
    public string ContentType { get; set; } = "application/json";

    /// <summary>Whether the original request is still being processed (in-flight).</summary>
    [JsonProperty("isProcessing")]
    public bool IsProcessing { get; set; }

    /// <summary>Cosmos DB TTL in seconds — 86400 = 24 hours.</summary>
    [JsonProperty("ttl")]
    public int Ttl { get; set; } = 86_400;
}
