using AppHub.Core.ApiResponse;
using AppHub.Core.Dto;
using AppHub.Infrastructure.Abstract;
using AppHub.SharedKernel.Utility;
using AppHub.WebApi.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static AppHub.WebApi.Config.IdentityServerSetting;

namespace AppHub.WebApi.Controllers;
[Route("api/[controller]")]
[ApiController]
public class LoginController : ControllerBase
{
  private readonly IdentityServerSettings _serverSettings;
  private readonly IConfiguration _configuration;
  private readonly JwtService _jwtService;
  private readonly IPasswordHasher _passwordHasher;
  private readonly IUserService _iuserService;
  public LoginController(
     IdentityServerSettings serverSettings,
     IConfiguration configuration,
     JwtService jwtService,
     IPasswordHasher passwordHasher,
     IUserService userService

     )
  {
    _serverSettings = serverSettings;
    _configuration = configuration;
    _jwtService = jwtService;
    _passwordHasher = passwordHasher;
    _iuserService = userService;
  }


  [HttpPost]
  public async Task<ActionResult<ApiResponse<string>>> Post([FromBody] loginDto request)
  {
    try
    {

      if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
      {
        return Ok(new ApiResponse<string>
        {
          success = false,
          message = "Username and password are required",
          data = ""
        });
      }

      var user = await _iuserService.GetUser(request);

      if (user.Entity == null)
      {
        return Ok(new ApiResponse<string>
        {
          success = false,
          message = "User not found",
          data = ""
        });
      }


      if (!user.Entity.IsActive || user.Entity.IsDeleted)
      {
        return Ok(new ApiResponse<string>
        {
          success = false,
          message = "Account is inactive or deleted",
          data = ""
        });
      }


      if (!_passwordHasher.VerifyBase64Password(request.Password, user.Entity.Password))
      {
        return Ok(new ApiResponse<string>
        {
          success = false,
          message = "Invalid password",
          data = ""
        });
      }

      var tokenvalue = new
      {
        token = _jwtService.GenerateJwtToken(user.Entity)
      };


      return Ok(new
      {
        success = true,
        message = "Login successful",
        data = tokenvalue
      });
    }
    catch (Exception ex)
    {
      return StatusCode(500, new ApiResponse<string>
      {
        success = false,
        message = "An error occurred during login",
        data = ""
      });
    }
  }



  [HttpGet]
  [Route("Encrypt")]
  public async Task<ActionResult<ApiResponse<string>>> Enc(string password)
  {
    try
    {
      return Ok(new ApiResponse<string>
      {
        success = true,
        message = "Encrypt Password",
        data = ExternalHelper.Encrypt(password)
      });
    }
    catch (Exception ex)
    {
      return StatusCode(500, new ApiResponse<string>
      {
        success = false,
        message = ex.Message
      });
    }
  }
}
