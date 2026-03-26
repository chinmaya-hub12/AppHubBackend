using AppHub.Infrastructure.Abstract;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppHub.Infrastructure.Concrete;

/// <summary>
/// Azure Key Vault implementation.
/// Uses DefaultAzureCredential – works with:
///   • Managed Identity (production / AKS)
///   • Azure CLI (local dev: run `az login`)
///   • Environment variables: AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID
/// 
/// Secret naming convention in Key Vault (dashes replace colons):
///   appsettings key   "ConnectionStrings:DefaultConnection"
///   Key Vault secret  "ConnectionStrings--DefaultConnection"
/// </summary>
public class KeyVaultService : IKeyVaultService
{
    private readonly SecretClient _client;
    private readonly ILogger<KeyVaultService> _logger;

    public KeyVaultService(IConfiguration configuration, ILogger<KeyVaultService> logger)
    {
        _logger = logger;
        var vaultUri = configuration["KeyVault:VaultUri"]
            ?? throw new InvalidOperationException("KeyVault:VaultUri is not configured.");

        // DefaultAzureCredential automatically picks up Managed Identity in Azure,
        // or developer credentials locally.
        _client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
    }

    public async Task<string> GetSecretAsync(string secretName)
    {
        try
        {
            var response = await _client.GetSecretAsync(secretName);
            return response.Value.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret '{SecretName}' from Key Vault", secretName);
            throw;
        }
    }

    public async Task SetSecretAsync(string secretName, string secretValue)
    {
        try
        {
            await _client.SetSecretAsync(secretName, secretValue);
            _logger.LogInformation("Secret '{SecretName}' saved to Key Vault", secretName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set secret '{SecretName}' in Key Vault", secretName);
            throw;
        }
    }

    public async Task DeleteSecretAsync(string secretName)
    {
        try
        {
            var operation = await _client.StartDeleteSecretAsync(secretName);
            await operation.WaitForCompletionAsync();
            _logger.LogInformation("Secret '{SecretName}' deleted from Key Vault", secretName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret '{SecretName}' from Key Vault", secretName);
            throw;
        }
    }

    public async Task<IEnumerable<string>> ListSecretNamesAsync()
    {
        var names = new List<string>();
        await foreach (var secret in _client.GetPropertiesOfSecretsAsync())
        {
            names.Add(secret.Name);
        }
        return names;
    }
}
