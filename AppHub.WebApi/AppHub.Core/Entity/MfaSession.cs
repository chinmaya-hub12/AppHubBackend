using Newtonsoft.Json;

namespace AppHub.Core.Entity;

/// <summary>
/// Temporary MFA session stored in Azure Cosmos DB.
/// TTL set to 300 seconds (5 minutes) — Cosmos auto-deletes expired sessions.
/// </summary>
public class MfaSession : CosmosDocument
{
    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;

    [JsonProperty("mfaToken")]
    public string MfaToken { get; set; } = string.Empty;

    /// <summary>Hashed OTP — we never store plaintext OTP.</summary>
    [JsonProperty("otpHash")]
    public string OtpHash { get; set; } = string.Empty;

    [JsonProperty("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonProperty("isUsed")]
    public bool IsUsed { get; set; }

    [JsonProperty("attemptCount")]
    public int AttemptCount { get; set; }

    /// <summary>Cosmos DB TTL in seconds. Container must have default TTL enabled.</summary>
    [JsonProperty("ttl")]
    public int Ttl { get; set; } = 300;
}
