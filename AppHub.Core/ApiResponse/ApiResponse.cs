using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppHub.Core.ApiResponse;
public class ApiResponse<T>
{
  public bool success { get; set; } 
  public string message { get; set; }
  public T? data { get; set; } = default(T);

}
