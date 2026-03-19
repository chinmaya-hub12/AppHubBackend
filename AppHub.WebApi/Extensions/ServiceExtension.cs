
using AppHub.Infrastructure.Abstract;
using AppHub.Infrastructure.Concrete;

namespace AppHub.WebApi.Extensions;

public static class ServiceExtension
{
  public static void ConfigureDIServices(this IServiceCollection services)
  {
    services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    services.AddScoped<JwtService>();
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<IPasswordHasher, PasswordHasher>();

  }
}
