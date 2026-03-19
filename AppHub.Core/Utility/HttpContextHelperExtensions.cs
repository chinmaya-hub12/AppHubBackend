using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AppHub.Core.Utility;
public static class HttpContextHelperExtensions
{
  public static IApplicationBuilder UseHttpContextHelper(this IApplicationBuilder app)
  {
    HttpContextHelper.Configure(app.ApplicationServices.GetRequiredService<IHttpContextAccessor>());
    return app;
  }
}
