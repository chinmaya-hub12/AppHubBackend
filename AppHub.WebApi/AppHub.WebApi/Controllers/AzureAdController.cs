using AppHub.Core.ApiResponse;
using AppHub.Infrastructure.Abstract;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppHub.WebApi.Controllers;

/// <summary>
/// Azure Entra ID (AAD) authentication endpoints.
/// GET  /api/auth/aad/profile  – returns claims from AAD token
/// GET  /api/auth/aad/signin   – initiates AAD OpenID Connect flow
/// </summary>
[Route("api/auth/aad")]
[ApiController]
public class AzureAdController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AzureAdController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    /// <summary>
    /// Returns the AAD token claims for the authenticated user.
    /// Requires Authorization: Bearer {aad-token}
    /// </summary>
    [HttpGet("profile")]
    [Authorize(AuthenticationSchemes = "AzureAd")]
    public async Task<ActionResult<ApiResponse<object>>> GetProfile()
    {
        var username = User.Identity?.Name ?? "unknown";
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();

        await _auditService.LogAsync("AAD_PROFILE_ACCESS", username, true);

        return Ok(new ApiResponse<object>
        {
            success = true,
            message = "Azure Entra ID profile retrieved",
            data = new
            {
                username,
                claims,
                authScheme = "AzureAd"
            }
        });
    }

    /// <summary>
    /// Test endpoint: accessible by EITHER local JWT OR AAD token.
    /// </summary>
    [HttpGet("ping")]
    [Authorize] // Uses default policy (LocalJwt OR AzureAd)
    public IActionResult Ping()
    {
        return Ok(new
        {
            success = true,
            message = "Authenticated via default policy",
            user = User.Identity?.Name,
            scheme = User.Identity?.AuthenticationType
        });
    }
}
