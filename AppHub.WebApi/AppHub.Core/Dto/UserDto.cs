using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Core.Dto;
public class UserDto
{
  public int UserTypeId {  get; set; }
  public long UserId { get; set; }
  public string? UserTypeName { get; set; }
  public string? MobileNo { get; set; }
  public string? Username { get; set; }
  public string? Password { get; set; }
  public bool IsActive { get; set; }
  public bool IsDeleted { get; set; }
}
