using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Infrastructure.Abstract;
public interface IPasswordHasher
{
  string HashPassword(string password);
  bool VerifyPassword(string password, string hashedPassword);
  bool VerifyBase64Password(string password, string Base64Password);
}
