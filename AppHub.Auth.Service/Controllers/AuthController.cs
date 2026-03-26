using AppHub.Core.ApiResponse;
using AppHub.Core.Dto;
using AppHub.Infrastructure.Abstract;
using AppHub.WebApi.Extensions;
using AppHub.WebApi.Middleware;
using Microsoft.AspNetCore.Mvc;
using static AppHub.WebApi.Config.IdentityServerSetting;

namespace AppHub.Auth.Service.Controllers;

/// <summary>
/// Auth microservice — Email OTP MFA login with full idempotency.
///
/// IDEMPOTENCY:
///   Add header on every POST:  Idempotency-Key: {uuid}
///   Retrying a timed-out login won't send a second OTP email.
///   Retrying a timed-out verify-otp won't issue a second JWT.
///   Keys expire after 24 hours.
///
/// STEP 1  POST /api/auth/login
///   Header: Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
///   Body:   { "username": "john", "password": "pass123", "email": "john@company.com" }
///   →  Validates credentials, sends 6-digit OTP to email
///
/// STEP 2  POST /api/auth/verify-otp
///   Header: Idempotency-Key: {different-uuid}
///   Body:   { "mfaToken": "abc...", "otpCode": "847392" }
///   →  Validates OTP, returns JWT
/// </summary>
[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IUserService            _userService;
    private readonly IPasswordHasher         _passwordHasher;
    private readonly IMfaService             _mfaService;
    private readonly IAuditService           _auditService;
    private readonly JwtService              _jwtService;
    private readonly IdentityServerSettings  _serverSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserService            userService,
        IPasswordHasher         passwordHasher,
        IMfaService             mfaService,
        IAuditService           auditService,
        JwtService              jwtService,
        IdentityServerSettings  serverSettings,
        ILogger<AuthController> logger)
    {
        _userService     = userService;
        _passwordHasher  = passwordHasher;
        _mfaService      = mfaService;
        _auditService    = auditService;
        _jwtService      = jwtService;
        _serverSettings  = serverSettings;
        _logger          = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 1 — Validate credentials, send 6-digit OTP email
    // Idempotency: retrying with the same key won't send a second OTP email
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("login")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login(
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
                await _auditService.LogAsync("AUTH_LOGIN_FAILED", request.Username, false, "User not found");
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Invalid username or password"
                });
            }

            if (!user.Entity.IsActive || user.Entity.IsDeleted)
            {
                await _auditService.LogAsync("AUTH_LOGIN_FAILED", request.Username, false, "Account inactive");
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Account is inactive or has been deleted"
                });
            }

            if (!_passwordHasher.VerifyBase64Password(request.Password, user.Entity.Password!))
            {
                await _auditService.LogAsync("AUTH_LOGIN_FAILED", request.Username, false, "Wrong password");
                return Ok(new ApiResponse<LoginResponseDto>
                {
                    success = false,
                    message = "Invalid username or password"
                });
            }

            var mfaToken = await _mfaService.InitiateAsync(request.Username, request.Email);
            await _auditService.LogAsync("AUTH_OTP_SENT", request.Username, true,
                $"OTP sent to {MaskEmail(request.Email)}");

            return Ok(new ApiResponse<LoginResponseDto>
            {
                success = true,
                message  = $"A 6-digit code has been sent to {MaskEmail(request.Email)}. Expires in 5 minutes.",
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
            _logger.LogError(ex, "Auth login error for {Username}", request.Username);
            return StatusCode(500, new ApiResponse<LoginResponseDto>
            {
                success = false,
                message = "An unexpected error occurred. Please try again."
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 2 — Submit the 6-digit OTP from email, receive JWT
    // Idempotency: retrying with the same key returns the same JWT
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
                await _auditService.LogAsync("AUTH_OTP_FAILED", "unknown", false,
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
            await _auditService.LogAsync("AUTH_LOGIN_SUCCESS", username, true);

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
