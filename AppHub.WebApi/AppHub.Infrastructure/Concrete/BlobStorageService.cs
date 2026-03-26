using AppHub.Infrastructure.Abstract;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppHub.Infrastructure.Concrete;

/// <summary>
/// Azure Blob Storage service implementation.
/// Connection string is pulled from Azure Key Vault via IConfiguration
/// (Key Vault is wired into IConfiguration in Program.cs, so it works transparently).
///
/// Containers used:
///   apphub-documents   – user-uploaded documents
///   apphub-exports     – generated reports / exports
///   apphub-avatars     – user profile images
///   apphub-backups     – audit log exports
/// </summary>
public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _serviceClient;
    private readonly string _accountName;
    private readonly string _accountKey;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
    {
        _logger = logger;
        var connectionString = configuration["BlobStorage:ConnectionString"]
            ?? throw new InvalidOperationException("BlobStorage:ConnectionString is not configured. Check Key Vault.");

        _serviceClient = new BlobServiceClient(connectionString);

        // Parse account name and key for SAS generation
        _accountName = configuration["BlobStorage:AccountName"] ?? string.Empty;
        _accountKey = configuration["BlobStorage:AccountKey"] ?? string.Empty;
    }

    public async Task<string> UploadAsync(string containerName, string blobName, Stream content, string contentType = "application/octet-stream")
    {
        try
        {
            var container = await GetOrCreateContainerAsync(containerName);
            var blobClient = container.GetBlobClient(blobName);

            await blobClient.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                }
            });
            _logger.LogInformation("Blob '{BlobName}' uploaded to container '{Container}'", blobName, containerName);
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload blob '{BlobName}' to '{Container}'", blobName, containerName);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string containerName, string blobName)
    {
        try
        {
            var container = _serviceClient.GetBlobContainerClient(containerName);
            var blobClient = container.GetBlobClient(blobName);

            var response = await blobClient.DownloadStreamingAsync();
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download blob '{BlobName}' from '{Container}'", blobName, containerName);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string containerName, string blobName)
    {
        try
        {
            var container = _serviceClient.GetBlobContainerClient(containerName);
            var blobClient = container.GetBlobClient(blobName);
            var response = await blobClient.DeleteIfExistsAsync();
            _logger.LogInformation("Blob '{BlobName}' deleted from '{Container}'", blobName, containerName);
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob '{BlobName}' from '{Container}'", blobName, containerName);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string containerName, string blobName)
    {
        var container = _serviceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);
        var response = await blobClient.ExistsAsync();
        return response.Value;
    }

    public async Task<IEnumerable<string>> ListBlobsAsync(string containerName, string? prefix = null)
    {
        var container = _serviceClient.GetBlobContainerClient(containerName);
        var names = new List<string>();

        await foreach (var item in container.GetBlobsAsync(prefix: prefix))
            names.Add(item.Name);

        return names;
    }

    public async Task<string> GenerateSasUrlAsync(string containerName, string blobName, int expiryMinutes = 60)
    {
        if (string.IsNullOrEmpty(_accountName) || string.IsNullOrEmpty(_accountKey))
            throw new InvalidOperationException("BlobStorage:AccountName and BlobStorage:AccountKey required for SAS generation.");

        var container = _serviceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var credential = new StorageSharedKeyCredential(_accountName, _accountKey);
        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        _logger.LogInformation("SAS URL generated for blob '{BlobName}' (expires {Expiry} min)", blobName, expiryMinutes);
        return sasUri.ToString();
    }

    // ── Private helpers ────────────────────────────────────────────────────────
    private async Task<BlobContainerClient> GetOrCreateContainerAsync(string containerName)
    {
        var container = _serviceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);
        return container;
    }
}
