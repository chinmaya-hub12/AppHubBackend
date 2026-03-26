namespace AppHub.Infrastructure.Abstract;

public interface IAuditService
{
    Task LogAsync(string action, string username, bool success, string? details = null);
}
