using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Core.Entity;
public class User_Credential
{
  [Key]
  public long UserId { get; set; }
  public int UserTypeId { get; set; }
  public string? Prefix { get; set; }
  public string FirstName { get; set; }
  public string? MiddleName { get; set; }
  public string LastName { get; set; }
  public string Designation { get; set; }
  public string OfficialEmailId { get; set; }
  public int StateId { get; set; }
  public int? DepartmentId { get; set; }
  public int? OrganisationId { get; set; }
  public int? OfficeName { get; set; }
  public string AadharNumber { get; set; }
  public string MobileNo { get; set; }
  public string Username { get; set; }
  public string Password { get; set; }
  public DateTime? RetirementDate { get; set; }
  public DateTime CreatedOn { get; set; }
  public string CreatedBy { get; set; }
  public DateTime? ModifiedOn { get; set; }
  public string? ModifiedBy { get; set; }
  public bool IsActive { get; set; }

}
