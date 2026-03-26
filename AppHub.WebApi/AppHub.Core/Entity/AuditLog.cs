using Newtonsoft.Json;

namespace AppHub.Core.Entity;

/// <summary>
/// Audit log document stored in Azure Cosmos DB.
/// </summary>
public class AuditLog : CosmosDocument
{
    [JsonProperty("action")]
    public string Action { get; set; } = string.Empty;

    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    [JsonProperty("ipAddress")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonProperty("userAgent")]
    public string UserAgent { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("details")]
    public string? Details { get; set; }

    [JsonProperty("serviceName")]
    public string ServiceName { get; set; } = "AppHub.WebApi";
}
