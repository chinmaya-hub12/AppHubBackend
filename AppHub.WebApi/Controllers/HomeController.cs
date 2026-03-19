using AppHub.Core.ApiResponse;
using AppHub.SharedKernel.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AppHub.WebApi.Controllers;
[Route("api/[controller]")]
[ApiController]
public class HomeController : BaseController
{
 

  [HttpGet]
  [Route("Decrypt")]
  public async Task<ActionResult<ApiResponse<string>>> Dec(string password)
  {
    try
    {
      return Ok(new ApiResponse<string>
      {
        success = true,
        message = "Decrypt Password",
        data = ExternalHelper.Decrypt(password)
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
