namespace AppHub.Infrastructure.Abstract;

/// <summary>
/// Azure Blob Storage service contract.
/// Supports upload, download, delete, list, and SAS URL generation.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Upload a file stream to a named container.</summary>
    Task<string> UploadAsync(string containerName, string blobName, Stream content, string contentType = "application/octet-stream");

    /// <summary>Download a blob as a stream.</summary>
    Task<Stream> DownloadAsync(string containerName, string blobName);

    /// <summary>Delete a blob.</summary>
    Task<bool> DeleteAsync(string containerName, string blobName);

    /// <summary>Check if a blob exists.</summary>
    Task<bool> ExistsAsync(string containerName, string blobName);

    /// <summary>List all blobs in a container (optional prefix filter).</summary>
    Task<IEnumerable<string>> ListBlobsAsync(string containerName, string? prefix = null);

    /// <summary>Generate a short-lived SAS URL for secure client-side access.</summary>
    Task<string> GenerateSasUrlAsync(string containerName, string blobName, int expiryMinutes = 60);
}
