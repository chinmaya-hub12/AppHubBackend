using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppHub.Core.Dto;
using AppHub.Core.Utility;

namespace AppHub.Infrastructure.Abstract;
public interface IUserService
{
  Task<Result<UserDto>> GetUser(loginDto request);
}
