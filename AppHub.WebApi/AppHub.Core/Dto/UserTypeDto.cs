using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Core.Dto;
public class UserTypeDto
{
  public int UserTypeId { get; set; }
  public int? DeptId { get; set; }
  public string? UserTypeName { get; set; }

}
