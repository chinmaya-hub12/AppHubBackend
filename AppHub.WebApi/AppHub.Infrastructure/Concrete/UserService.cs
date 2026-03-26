using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ardalis.Specification;
using AutoMapper;
using AppHub.Core.Dto;
using AppHub.Core.Utility;
using AppHub.Infrastructure.Abstract;
using AppHub.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AppHub.Infrastructure.Concrete;
public class UserService:IUserService
{
 
  private AppDbContext Db;
  private readonly IMapper _mapper;
  private readonly ILogger<UserService> _logger;
  public UserService(AppDbContext db, IHttpContextAccessor context,  IMapper mapper,  ILogger<UserService> logger)
  {
    Db = db;
    _mapper = mapper;
    _logger = logger;
  }


  // Get User Details By Procedure sp_UserDetail
  public async Task<Result<UserDto>> GetUser(loginDto request)
  {
    try
    {
      var username = new SqlParameter("@username", request.Username);

      var user = await Db.sp_UserDetail.FromSqlRaw("EXEC [dbo].[sp_UserDetail] @username", username).ToListAsync();

      if(user.Count == 0)
      {
        return Result<UserDto>.Failure("User not found");
      }
      var firstUser = user[0];

      var result = _mapper.Map<UserDto>(firstUser);
      return Result<UserDto>.Success(result);
    }
    catch (Exception ex) {
      Log.Logger = new LoggerConfiguration()
               .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day).CreateLogger();
      Log.Information($"Error in getting User: {ex.Message}");
      _logger.LogError($"Error in getting User: {ex.Message}");
      return Result<UserDto>.Failure("Error in getting User"); 
    }
  }


}
