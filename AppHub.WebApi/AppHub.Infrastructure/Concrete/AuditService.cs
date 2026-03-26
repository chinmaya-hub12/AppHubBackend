using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AppHub.Infrastructure.Concrete;

/// <summary>
/// Writes security/audit events to Azure Cosmos DB for compliance and monitoring.
/// </summary>
public class AuditService : IAuditService
{
    private readonly ICosmosRepository<AuditLog> _auditRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        ICosmosRepository<AuditLog> auditRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _auditRepo = auditRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(string action, string username, bool success, string? details = null)
    {
        try
        {
            var ctx = _httpContextAccessor.HttpContext;
            string? userAgent = null;
            if (ctx?.Request?.Headers != null && ctx.Request.Headers.TryGetValue("User-Agent", out var ua))
            {
                userAgent = ua.ToString();
            }

            var log = new AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                PartitionKey = username,
                Action = action,
                Username = username,
                IpAddress = ctx?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent = string.IsNullOrEmpty(userAgent) ? "unknown" : userAgent,
                Success = success,
                Details = details,
                ServiceName = "AppHub.WebApi"
            };
            await _auditRepo.CreateAsync(log);
        }
        catch (Exception ex)
        {
            // Audit failure should NOT crash the request
            _logger.LogError(ex, "Failed to write audit log for action {Action} user {Username}", action, username);
        }
    }
}
