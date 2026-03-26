using Newtonsoft.Json;

namespace AppHub.Core.Entity;

/// <summary>
/// Base document for Azure Cosmos DB entities.
/// </summary>
public abstract class CosmosDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("partitionKey")]
    public string PartitionKey { get; set; } = string.Empty;

    [JsonProperty("_ts")]
    public long Timestamp { get; set; }

    [JsonProperty("createdOn")]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    [JsonProperty("modifiedOn")]
    public DateTime? ModifiedOn { get; set; }
}
