namespace AppHub.Core.Dto;

public class BlobUploadDto
{
    public string ContainerName { get; set; } = "apphub-documents";
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    // Base64 encoded file content (for small files via API)
    public string? Base64Content { get; set; }
}

public class BlobUploadResultDto
{
    public string BlobName { get; set; } = string.Empty;
    public string BlobUri { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class BlobSasResultDto
{
    public string BlobName { get; set; } = string.Empty;
    public string SasUrl { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; }
    public DateTime ExpiresAt { get; set; }
}
