using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Core.Entity
{
    public class sp_UserDetail
    {
        [Key]
        public long UserId { get; set; }
        public int UserTypeId { get; set; }
        public string UserTypeName { get; set; }
        public int DepartmentId { get; set; }
        public string Designation { get; set; }
        public string Name { get; set; }
        public string AadharNumber { get; set; }
        public string MobileNo { get; set; }

    }
}
