using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppHub.Infrastructure.Abstract;
using AppHub.SharedKernel.Utility;

namespace AppHub.Infrastructure.Concrete;
public class PasswordHasher: IPasswordHasher
{
  public string HashPassword(string password)
  {
    return BCrypt.Net.BCrypt.HashPassword(password);
  }

  public bool VerifyPassword(string password, string hashedPassword)
  {
    return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
  }

  public bool VerifyBase64Password(string password, string Base64Password)
  {
    var encryptbase64 = ExternalHelper.Encrypt(password);
    if(encryptbase64 == Base64Password)
    {
      return true;
    }
    return false;
  }
}
