using AppHub.Core.Dto;
using AppHub.Core.Enum;
using AppHub.Core.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static AppHub.WebApi.Config.IdentityServerSetting;

namespace AppHub.WebApi.Extensions;

public class JwtService
{
  private readonly IConfiguration _configuration;
  private readonly IdentityServerSettings _serverSettings;
  //private readonly UserManager<ApplicationUser> _userManager;
  public JwtService(
     IConfiguration configuration, IdentityServerSettings serverSettings)
  {
    _serverSettings = serverSettings;
    _configuration = configuration;
  }
  public string GenerateJwtToken(UserDto user)
  {
    var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetValue<string>("IdentityServerSettings:ApiSecret")));
    var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
        {
            new Claim(ClaimTypes.PrimarySid, user.UserTypeId.ToString() ?? string.Empty),
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username ?? string.Empty),
            new Claim(ClaimTypes.Role, user.UserTypeName ?? string.Empty),
            new Claim("MobileNo", user.MobileNo ?? string.Empty),
            new Claim("IsActive", user.IsActive.ToString())
        };

    var token = new JwtSecurityToken(
        issuer: _serverSettings.ValidIssuer,
        audience: _serverSettings.ValidAudience,
        claims: claims,
        expires: DateTime.Now.AddMinutes(_serverSettings.Expiry),
        signingCredentials: credentials
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
  }
}
