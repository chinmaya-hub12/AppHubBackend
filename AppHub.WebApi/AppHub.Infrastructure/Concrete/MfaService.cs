using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;
using AppHub.Infrastructure.Cache;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace AppHub.Infrastructure.Concrete;

/// <summary>
/// Email OTP MFA — now enhanced with Redis rate limiting.
///
/// Redis keys used:
///   otp:ratelimit:{username}  — counts OTP requests per user (max 5 per 15 min)
///   otp:cooldown:{username}   — 60-second cooldown between consecutive OTP sends
///
/// Flow:
///   1. Check Redis rate limit (reject if exceeded)
///   2. Generate cryptographically random 6-digit OTP
///   3. Hash OTP with SHA-256+salt, store MfaSession in Cosmos DB (TTL 5 min)
///   4. Send email via Azure Communication Services
///   5. On verify: Redis + Cosmos session check, constant-time hash compare
/// </summary>
public class MfaService : IMfaService
{
    private readonly ICosmosRepository<MfaSession> _sessionRepo;
    private readonly ICacheService                  _cache;
    private readonly IConfiguration                 _configuration;
    private readonly ILogger<MfaService>            _logger;

    private const int MaxOtpRequestsPer15Min = 5;
    private const int CooldownSeconds        = 60;
    private const int TtlSeconds             = 300; // 5 minutes

    public MfaService(
        ICosmosRepository<MfaSession> sessionRepo,
        ICacheService                  cache,
        IConfiguration                 configuration,
        ILogger<MfaService>            logger)
    {
        _sessionRepo   = sessionRepo;
        _cache         = cache;
        _configuration = configuration;
        _logger        = logger;
    }

    // ── Step 1: rate-limit → generate OTP → store session → send email ───────
    public async Task<string> InitiateAsync(string username, string email)
    {
        // ── Redis rate limit: max 5 OTP requests per 15 minutes ───────────────
        var rateLimitKey = $"otp:ratelimit:{username}";
        var requestCount = await _cache.IncrementAsync(rateLimitKey, TimeSpan.FromMinutes(15));
        if (requestCount > MaxOtpRequestsPer15Min)
        {
            _logger.LogWarning("OTP rate limit exceeded for user {Username} ({Count} requests)", username, requestCount);
            throw new InvalidOperationException(
                $"Too many OTP requests. Please wait 15 minutes before trying again.");
        }

        // ── Redis cooldown: 60 seconds between consecutive sends ──────────────
        var cooldownKey = $"otp:cooldown:{username}";
        var onCooldown  = await _cache.ExistsAsync(cooldownKey);
        if (onCooldown)
        {
            _logger.LogWarning("OTP cooldown active for user {Username}", username);
            throw new InvalidOperationException(
                "Please wait 60 seconds before requesting another verification code.");
        }
        await _cache.SetAsync(cooldownKey, new { active = true }, TimeSpan.FromSeconds(CooldownSeconds));

        // ── Generate OTP and session ──────────────────────────────────────────
        var otp      = GenerateOtp();
        var mfaToken = Guid.NewGuid().ToString("N");
        var otpHash  = HashOtp(otp, mfaToken);

        var session = new MfaSession
        {
            Id           = Guid.NewGuid().ToString(),
            PartitionKey = username,
            Username     = username,
            Email        = email,
            MfaToken     = mfaToken,
            OtpHash      = otpHash,
            ExpiresAt    = DateTime.UtcNow.AddSeconds(TtlSeconds),
            IsUsed       = false,
            AttemptCount = 0,
            Ttl          = TtlSeconds
        };
        await _sessionRepo.CreateAsync(session);

        // ── Send email ────────────────────────────────────────────────────────
        await SendOtpEmailAsync(email, username, otp);

        _logger.LogInformation("OTP initiated for {Username} → {Email}", username, MaskEmail(email));
        return mfaToken;
    }

    // ── Step 2: verify OTP ────────────────────────────────────────────────────
    public async Task<(bool IsValid, string? Username)> VerifyAsync(string mfaToken, string otpCode)
    {
        var query    = "SELECT * FROM c WHERE c.mfaToken = @token";
        var sessions = await _sessionRepo.GetByQueryAsync(query,
            new Dictionary<string, object> { { "@token", mfaToken } });
        var session  = sessions.FirstOrDefault();

        if (session == null)
        {
            _logger.LogWarning("MFA verify: session not found for token {Token}", mfaToken[..8] + "...");
            return (false, null);
        }
        if (session.IsUsed)
        {
            _logger.LogWarning("MFA verify: session already used for {Username}", session.Username);
            return (false, null);
        }
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("MFA verify: session expired for {Username}", session.Username);
            return (false, null);
        }

        session.AttemptCount++;
        if (session.AttemptCount > 3)
        {
            session.IsUsed = true;
            await _sessionRepo.UpsertAsync(session);
            _logger.LogWarning("MFA verify: max attempts exceeded for {Username}", session.Username);
            return (false, null);
        }

        // Constant-time comparison — prevents timing attacks
        var submittedHash = HashOtp(otpCode.Trim(), mfaToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(submittedHash),
                Encoding.UTF8.GetBytes(session.OtpHash)))
        {
            await _sessionRepo.UpsertAsync(session);
            _logger.LogWarning("MFA verify: wrong OTP for {Username} (attempt {Count})",
                session.Username, session.AttemptCount);
            return (false, null);
        }

        // Success — mark one-time use
        session.IsUsed = true;
        session.Ttl    = 10;
        await _sessionRepo.UpsertAsync(session);

        // Clear rate limit after successful login
        await _cache.RemoveAsync($"otp:ratelimit:{session.Username}");
        await _cache.RemoveAsync($"otp:cooldown:{session.Username}");

        _logger.LogInformation("MFA verify: success for {Username}", session.Username);
        return (true, session.Username);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string GenerateOtp()
    {
        using var rng   = RandomNumberGenerator.Create();
        var bytes       = new byte[4];
        rng.GetBytes(bytes);
        var value = Math.Abs(BitConverter.ToInt32(bytes, 0)) % 1_000_000;
        return value.ToString("D6");
    }

    private static string HashOtp(string otp, string salt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{otp}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";
        var local  = parts[0];
        var masked = local.Length <= 2
            ? new string('*', local.Length)
            : local[0] + new string('*', local.Length - 2) + local[^1];
        return $"{masked}@{parts[1]}";
    }

    private async Task SendOtpEmailAsync(string toEmail, string toName, string otp)
    {
        var acsConnectionString = _configuration["AzureCommunicationServices:ConnectionString"];
        var fromEmail           = _configuration["AzureCommunicationServices:SenderEmail"]
                                  ?? "DoNotReply@yourdomain.com";

        if (string.IsNullOrEmpty(acsConnectionString))
        {
            _logger.LogWarning("[DEV ONLY] ACS not configured. OTP for {User}: {Otp}", toName, otp);
            return;
        }

        var emailClient = new EmailClient(acsConnectionString);
        var emailMsg = new EmailMessage(
            senderAddress: fromEmail,
            recipients: new EmailRecipients(new[] { new EmailAddress(toEmail, toName) }),
            content: new EmailContent($"Your AppHub Verification Code: {otp}")
            {
                Html = $@"<html><body style='font-family:Arial,sans-serif;background:#f5f7fa;padding:30px'>
  <div style='max-width:480px;margin:0 auto;background:#fff;border-radius:12px;
              padding:32px;box-shadow:0 2px 8px rgba(0,0,0,0.08)'>
    <h2 style='color:#1a73e8'>AppHub</h2>
    <p style='color:#555'>Two-Factor Authentication</p>
    <hr style='border:none;border-top:1px solid #eee;margin:20px 0'/>
    <p>Hello <strong>{toName}</strong>,</p>
    <p>Your one-time verification code is:</p>
    <div style='font-size:40px;font-weight:700;letter-spacing:12px;text-align:center;
                background:#f0f4ff;border-radius:8px;padding:20px;color:#1a73e8;margin:24px 0'>
      {otp}
    </div>
    <p style='color:#888;font-size:14px'>
      Expires in <strong>5 minutes</strong>. Do not share this code.
    </p>
  </div></body></html>"
            });

      
        var sendOp = await emailClient.SendAsync(
     Azure.WaitUntil.Completed, emailMsg);

        // Get MessageId from response headers
        var response = sendOp.GetRawResponse();
        string messageId = response.Headers.TryGetValue("x-ms-client-request-id", out var reqId)
            ? reqId
            : "unknown";

        _logger.LogInformation("OTP email sent to {Email}, MessageId={MessageId}",
            MaskEmail(toEmail), messageId);
    }
}
