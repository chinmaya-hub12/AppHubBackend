using AppHub.Core.ApiResponse;
using AppHub.Core.Dto;
using AppHub.Infrastructure.Abstract;
using AppHub.SharedKernel.Utility;
using AppHub.WebApi.Extensions;
using AppHub.WebApi.Middleware;
using Microsoft.AspNetCore.Mvc;
using static AppHub.WebApi.Config.IdentityServerSetting;

namespace AppHub.WebApi.Controllers;

/// <summary>
/// Authentication — Two-step Email OTP MFA with full idempotency support.
///
/// IDEMPOTENCY:
///   Include header:  Idempotency-Key: {uuid}  on every POST request.
///   Retrying a failed/timed-out request with the SAME key returns the
///   original response without sending a duplicate OTP or issuing two JWTs.
///   Keys expire after 24 hours.
///
/// STEP 1  POST /api/login
///   Header: Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
///   Body:   { "username": "john", "password": "pass123", "email": "john@company.com" }
///   →  Validates credentials, sends 6-digit OTP email, returns mfaToken
///
/// STEP 2  POST /api/login/verify-otp
///   Header: Idempotency-Key: {different-uuid}
///   Body:   { "mfaToken": "abc...", "otpCode": "847392" }
///   →  Validates OTP, returns JWT (same JWT returned on retry)
///
/// ORIGINAL (kept):
///   GET /api/login/Encrypt?password=
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class LoginController : ControllerBase
{
    private readonly IdentityServerSettings  _serverSettings;
    private readonly JwtService              _jwtService;
    private readonly IPasswordHasher         _passwordHasher;
    private readonly IUserService            _userService;
    private readonly IMfaService             _mfaService;
    private readonly IAuditService           _auditService;
    private readonly ILogger<LoginController> _logger;

    public LoginController(
        IdentityServerSettings   serverSettings,
        JwtService               jwtService,
        IPasswordHasher          passwordHasher,
        IUserService             userService,
        IMfaService              mfaService,
        IAuditService            auditService,
        ILogger<LoginController> logger)
    {
        _serverSettings = serverSettings;
        _jwtService     = jwtService;
        _passwordHasher = passwordHasher;
        _userService    = userService;
        _mfaService     = mfaService;
        _auditService   = auditService;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 1 — Validate credentials, send 6-digit OTP email
    // Idempotency: retrying with the same key won't send a second OTP email
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Post(
        [FromBody] LoginRequestDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Email))
            {
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Username, password and email are required"
                });
            }

            var loginDto = new loginDto { Username = request.Username, Password = request.Password };
            var user     = await _userService.GetUser(loginDto);

            if (user.Entity == null)
            {
                await _auditService.LogAsync("LOGIN_FAILED", request.Username, false, "User not found");
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Invalid username or password"
                });
            }

            if (!user.Entity.IsActive || user.Entity.IsDeleted)
            {
                await _auditService.LogAsync("LOGIN_FAILED", request.Username, false, "Account inactive");
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Account is inactive or has been deleted"
                });
            }

            if (!_passwordHasher.VerifyBase64Password(request.Password, user.Entity.Password!))
            {
                await _auditService.LogAsync("LOGIN_FAILED", request.Username, false, "Wrong password");
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Invalid username or password"
                });
            }

            // Credentials verified — send OTP to email
            var mfaToken = await _mfaService.InitiateAsync(request.Username, request.Email);
            await _auditService.LogAsync("OTP_SENT", request.Username, true,
                $"OTP sent to {MaskEmail(request.Email)}");

            return Ok(new ApiResponse<LoginResponseDto>
            {
                success = true,
                message  = $"A 6-digit verification code has been sent to {MaskEmail(request.Email)}. " +
                            "It expires in 5 minutes.",
                data = new LoginResponseDto
                {
                    RequiresMfa = true,
                    MfaToken    = mfaToken,
                    Message     = "Enter the 6-digit code from your email to complete login"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for {Username}", request.Username);
            return StatusCode(500, new ApiResponse<LoginResponseDto>
            {
                success = false,
                message = "An unexpected error occurred. Please try again."
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 2 — Submit 6-digit OTP from email, receive JWT
    // Idempotency: retrying with the same key returns the same JWT token
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("verify-otp")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<ApiResponse<TokenResponseDto>>> VerifyOtp(
        [FromBody] MfaVerifyDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.MfaToken) ||
                string.IsNullOrWhiteSpace(request.OtpCode))
            {
                return Ok(new ApiResponse<TokenResponseDto>
                {
                    success = false,
                    message = "mfaToken and otpCode are required"
                });
            }

            if (request.OtpCode.Length != 6 || !request.OtpCode.All(char.IsDigit))
            {
                return Ok(new ApiResponse<TokenResponseDto>
                {
                    success = false,
                    message = "OTP code must be exactly 6 digits"
                });
            }

            var (isValid, username) = await _mfaService.VerifyAsync(request.MfaToken, request.OtpCode);

            if (!isValid || string.IsNullOrEmpty(username))
            {
                await _auditService.LogAsync("OTP_FAILED", "unknown", false,
                    $"Token={request.MfaToken[..Math.Min(8, request.MfaToken.Length)]}...");
                return Ok(new ApiResponse<TokenResponseDto>
                {
                    success = false,
                    message = "Invalid or expired code. Please login again to receive a new code."
                });
            }

            var loginDto = new loginDto { Username = username, Password = string.Empty };
            var user     = await _userService.GetUser(loginDto);

            if (user.Entity == null)
                return Ok(new ApiResponse<TokenResponseDto>
                {
                    success = false,
                    message = "User not found"
                });

            var token = _jwtService.GenerateJwtToken(user.Entity);
            await _auditService.LogAsync("LOGIN_SUCCESS", username, true);

            return Ok(new ApiResponse<TokenResponseDto>
            {
                success = true,
                message = "Login successful",
                data = new TokenResponseDto
                {
                    Token     = token,
                    Username  = user.Entity.Username ?? username,
                    UserType  = user.Entity.UserTypeName ?? string.Empty,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(_serverSettings.Expiry)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTP verify error");
            return StatusCode(500, new ApiResponse<TokenResponseDto>
            {
                success = false,
                message = "An unexpected error occurred during OTP verification."
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORIGINAL — Encrypt password utility (GET = naturally idempotent)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("Encrypt")]
    public ActionResult<ApiResponse<string>> Enc(string password)
    {
        try
        {
            return Ok(new ApiResponse<string>
            {
                success = true,
                message = "Encrypted password",
                data    = ExternalHelper.Encrypt(password)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<string> { success = false, message = ex.Message });
        }
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var local  = email[..at];
        var domain = email[at..];
        var masked = local.Length <= 2
            ? new string('*', local.Length)
            : local[0] + new string('*', local.Length - 2) + local[^1];
        return masked + domain;
    }
}
