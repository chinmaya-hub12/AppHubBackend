using AppHub.Core.Dto;

namespace AppHub.Infrastructure.Abstract;

/// <summary>
/// Email-based OTP MFA service.
/// Step 1: InitiateAsync  — generates OTP, stores hashed in CosmosDB, sends email
/// Step 2: VerifyAsync    — validates OTP, marks session used, returns username
/// </summary>
public interface IMfaService
{
    /// <summary>
    /// Generate OTP, store hashed session in CosmosDB, send 6-digit code to email.
    /// Returns the mfaToken that must be passed in Step 2.
    /// </summary>
    Task<string> InitiateAsync(string username, string email);

    /// <summary>
    /// Verify the 6-digit OTP.
    /// Returns (isValid, username) — username is non-null only when valid.
    /// </summary>
    Task<(bool IsValid, string? Username)> VerifyAsync(string mfaToken, string otpCode);
}
