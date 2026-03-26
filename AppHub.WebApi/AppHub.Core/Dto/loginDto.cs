using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Core.Dto;
public class loginDto
{
  [Required(ErrorMessage = "User Name is required")]
  public string Username { get; set; }

  [Required(ErrorMessage = "Password is required")]
  public string Password { get; set; }
  public bool RememberMe { get; set; }
}
