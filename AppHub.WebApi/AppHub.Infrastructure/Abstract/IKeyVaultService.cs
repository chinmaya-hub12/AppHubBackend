namespace AppHub.Infrastructure.Abstract;

/// <summary>
/// Azure Key Vault service contract.
/// All application secrets are retrieved from Key Vault at runtime.
/// </summary>
public interface IKeyVaultService
{
    /// <summary>Get a secret value by name.</summary>
    Task<string> GetSecretAsync(string secretName);

    /// <summary>Set or update a secret value.</summary>
    Task SetSecretAsync(string secretName, string secretValue);

    /// <summary>Delete a secret (soft-delete).</summary>
    Task DeleteSecretAsync(string secretName);

    /// <summary>List all secret names in the vault.</summary>
    Task<IEnumerable<string>> ListSecretNamesAsync();
}
