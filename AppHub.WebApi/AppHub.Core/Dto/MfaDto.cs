namespace AppHub.Core.Dto;

/// <summary>Step 1 login request (username + password).</summary>
public class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email    { get; set; } = string.Empty; // user's email to receive OTP
}

/// <summary>Step 1 response — tells client MFA is required, returns temp token.</summary>
public class LoginResponseDto
{
    public bool   RequiresMfa { get; set; }
    public string MfaToken    { get; set; } = string.Empty;
    public string Message     { get; set; } = string.Empty;
}

/// <summary>Step 2 request — user submits the 6-digit code from email.</summary>
public class MfaVerifyDto
{
    public string MfaToken { get; set; } = string.Empty; // from Step 1 response
    public string OtpCode  { get; set; } = string.Empty; // 6-digit code from email
}

/// <summary>Final login response after successful MFA verification.</summary>
public class TokenResponseDto
{
    public string Token     { get; set; } = string.Empty;
    public string Username  { get; set; } = string.Empty;
    public string UserType  { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
