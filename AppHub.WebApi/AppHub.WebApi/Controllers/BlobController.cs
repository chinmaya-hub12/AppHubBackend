using AppHub.Core.ApiResponse;
using AppHub.Core.Dto;
using AppHub.Infrastructure.Abstract;
using AppHub.WebApi.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppHub.WebApi.Controllers;

/// <summary>
/// Azure Blob Storage API — all endpoints require authentication.
///
/// IDEMPOTENCY:
///   POST/DELETE endpoints support idempotency via the Idempotency-Key header.
///   Upload is marked [RequiresIdempotencyKey] — uploading twice with the same
///   key returns the original upload result without creating a duplicate file.
///   DELETE is idempotent by nature (deleting twice = same outcome) but the
///   header is supported so retries return the original response.
///
///   Idempotency-Key: {uuid}   ← include in every POST/DELETE request
///
/// GET endpoints are naturally idempotent — no header needed.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class BlobController : ControllerBase
{
    private readonly IBlobStorageService     _blobService;
    private readonly IAuditService           _auditService;
    private readonly ILogger<BlobController> _logger;

    public BlobController(IBlobStorageService blobService, IAuditService auditService,
        ILogger<BlobController> logger)
    {
        _blobService  = blobService;
        _auditService = auditService;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Upload — multipart/form-data (max 50 MB)
    // Idempotency: same key = same upload result, no duplicate file created
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("upload")]
    [RequiresIdempotencyKey]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<ActionResult<ApiResponse<BlobUploadResultDto>>> Upload(
        IFormFile file,
        [FromQuery] string container = "apphub-documents")
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiResponse<BlobUploadResultDto>
            {
                success = false,
                message = "No file provided"
            });

        var username = User.Identity?.Name ?? "unknown";

        // Use the Idempotency-Key as part of the blob name so retried uploads
        // map to the same blob (idempotent by construction in blob storage too)
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        var blobName = string.IsNullOrEmpty(idempotencyKey)
            ? $"{username}/{Guid.NewGuid()}_{file.FileName}"
            : $"{username}/{idempotencyKey}_{file.FileName}";

        await using var stream = file.OpenReadStream();
        var uri = await _blobService.UploadAsync(container, blobName, stream, file.ContentType);

        await _auditService.LogAsync("BLOB_UPLOAD", username, true,
            $"Container={container}, Blob={blobName}, Size={file.Length}");

        return Ok(new ApiResponse<BlobUploadResultDto>
        {
            success = true,
            message = "File uploaded successfully",
            data = new BlobUploadResultDto
            {
                BlobName      = blobName,
                BlobUri       = uri,
                ContainerName = container,
                SizeBytes     = file.Length,
                UploadedAt    = DateTime.UtcNow
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Upload — Base64 JSON body (for smaller payloads)
    // Idempotency: supported via Idempotency-Key header
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("upload/base64")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<ApiResponse<BlobUploadResultDto>>> UploadBase64(
        [FromBody] BlobUploadDto dto)
    {
        if (string.IsNullOrEmpty(dto.Base64Content))
            return BadRequest(new ApiResponse<BlobUploadResultDto>
            {
                success = false,
                message = "Base64 content is required"
            });

        var username       = User.Identity?.Name ?? "unknown";
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        var blobName = string.IsNullOrEmpty(idempotencyKey)
            ? $"{username}/{Guid.NewGuid()}_{dto.FileName}"
            : $"{username}/{idempotencyKey}_{dto.FileName}";

        var bytes = Convert.FromBase64String(dto.Base64Content);
        await using var stream = new MemoryStream(bytes);
        var uri = await _blobService.UploadAsync(dto.ContainerName, blobName, stream, dto.ContentType);

        await _auditService.LogAsync("BLOB_UPLOAD_BASE64", username, true,
            $"Container={dto.ContainerName}, Blob={blobName}, Size={bytes.Length}");

        return Ok(new ApiResponse<BlobUploadResultDto>
        {
            success = true,
            message = "File uploaded successfully",
            data = new BlobUploadResultDto
            {
                BlobName      = blobName,
                BlobUri       = uri,
                ContainerName = dto.ContainerName,
                SizeBytes     = bytes.Length,
                UploadedAt    = DateTime.UtcNow
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Download — GET is naturally idempotent, no Idempotency-Key needed
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("download/{container}/{*blobName}")]
    public async Task<IActionResult> Download(string container, string blobName)
    {
        var exists = await _blobService.ExistsAsync(container, blobName);
        if (!exists)
            return NotFound(new { success = false, message = "File not found" });

        var stream   = await _blobService.DownloadAsync(container, blobName);
        var fileName = Path.GetFileName(blobName);
        await _auditService.LogAsync("BLOB_DOWNLOAD", User.Identity?.Name ?? "unknown", true,
            $"Container={container}, Blob={blobName}");
        return File(stream, "application/octet-stream", fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAS URL — GET is naturally idempotent
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("sas/{container}/{*blobName}")]
    public async Task<ActionResult<ApiResponse<BlobSasResultDto>>> GetSasUrl(
        string container, string blobName, [FromQuery] int expiryMinutes = 60)
    {
        var exists = await _blobService.ExistsAsync(container, blobName);
        if (!exists)
            return NotFound(new { success = false, message = "File not found" });

        var sasUrl = await _blobService.GenerateSasUrlAsync(container, blobName, expiryMinutes);
        return Ok(new ApiResponse<BlobSasResultDto>
        {
            success = true,
            message = "SAS URL generated",
            data = new BlobSasResultDto
            {
                BlobName      = blobName,
                SasUrl        = sasUrl,
                ExpiryMinutes = expiryMinutes,
                ExpiresAt     = DateTime.UtcNow.AddMinutes(expiryMinutes)
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // List blobs — GET is naturally idempotent
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("list/{container}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<string>>>> List(
        string container, [FromQuery] string? prefix = null)
    {
        var blobs = await _blobService.ListBlobsAsync(container, prefix);
        return Ok(new ApiResponse<IEnumerable<string>>
        {
            success = true,
            message = $"Files in {container}",
            data    = blobs
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete — idempotent by nature (delete twice = same outcome)
    // Idempotency-Key supported so retries get the original response
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("{container}/{*blobName}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(string container, string blobName)
    {
        var deleted = await _blobService.DeleteAsync(container, blobName);
        var username = User.Identity?.Name ?? "unknown";
        await _auditService.LogAsync("BLOB_DELETE", username, true,
            $"Container={container}, Blob={blobName}, Found={deleted}");

        // Always return success — deleting a non-existent blob is still idempotent
        return Ok(new ApiResponse<bool>
        {
            success = true,
            message = deleted ? "File deleted successfully" : "File was already deleted or did not exist",
            data    = deleted
        });
    }
}
