using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.Text;
using System;
using AppHub.Infrastructure.Data;
using static AppHub.WebApi.Config.IdentityServerSetting;
using AppHub.WebApi.Extensions;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;
using AutoMapper;
using Humanizer;
using Microsoft.OpenApi.Models;
namespace AppHub.WebApi;

public static class HostingExtension
{
  private const string CorsPolicy = "_MyAllowSubdomainPolicy";

  private const string IdentityServerSettings = "IdentityServerSettings";

  public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
  {
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(connectionString));

    // configure authentication to use IdentityServer
    var identityServerSettings = builder.Configuration.GetSection("IdentityServerSettings").Get<IdentityServerSettings>();
    builder.Services.Configure<IdentityServerSettings>(builder.Configuration.GetSection(IdentityServerSettings));

    // add the configuration settings to the dependency injection container
    builder.Services.AddSingleton(identityServerSettings);

    builder.Services.AddHttpContextAccessor();

    builder.Services.AddControllers().AddNewtonsoftJson(options =>{options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;});

    builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

    builder.Services.ConfigureDIServices();

    builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, builder => builder.WithOrigins(identityServerSettings.AllowedOrigins).AllowAnyHeader().AllowAnyMethod()));


        // builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, builder => builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        // Add Authorization Policies
        //  builder.Services.ConfigureAuthorizationPolicies();

        builder.WebHost.UseUrls("http://0.0.0.0:80");
        // Adding Authentication
        builder.Services.AddAuthentication(options =>
    {
      options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
      options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
      options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    // Adding Jwt Bearer
    .AddJwtBearer(options =>
    {
      options.SaveToken = true;
      options.RequireHttpsMetadata = false;
      options.TokenValidationParameters = new TokenValidationParameters()
      {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAudience = identityServerSettings.ValidAudience,
        ValidIssuer = identityServerSettings.ValidIssuer,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(identityServerSettings.ApiSecret)),
        ClockSkew = TimeSpan.FromSeconds(0)
      };
    });

    builder.Services.AddAuthorization();

    // configure services for injection
    builder.Services.AddControllers().AddNewtonsoftJson();


    // Add MVC support
    //builder.Services.AddMvc(options => { options.EnableEndpointRouting = true; });


    // Basic essential services first
    builder.Services.AddControllers().AddNewtonsoftJson();

    builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        // builder.Services.AddSwaggerGen();
        RegisterDocumentationGenerators(builder.Services);

    return builder.Build();
  }

  public static WebApplication ConfigurePipeline(this WebApplication app)
  {
    if (app.Environment.IsDevelopment())
    {
      app.UseDeveloperExceptionPage();
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseRouting();          
    app.UseStaticFiles();
    app.UseCors(CorsPolicy);
    app.UseAuthentication();
    app.UseAuthorization();
    app.ConfigureRedundantStatusCodePages(); // Provide JSON responses for standard response codes such as HTTP 401.
    app.ConfigureExceptionHandler();
    //app.UseHttpContextHelper(); // Helper to get Base URL anywhere in application
    //InitializeRoles(app.Services).Wait();
    //InitializeUser(app.Services).Wait();
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapDefaultControllerRoute();
    });

    // Add diagnostic middleware
    app.Use(async (context, next) =>
    {
      try
      {
        Console.WriteLine($"Incoming request to: {context.Request.Path}");
        await next();
        Console.WriteLine($"Response status code: {context.Response.StatusCode}");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error processing request: {ex.Message}");
        throw;
      }
    });

    app.UseRouting();
    app.UseEndpoints(endpoints =>
    {
      endpoints.MapControllers();
    });

    return app;
  }

  private static void RegisterDocumentationGenerators(IServiceCollection services)
  {
    services.AddSwaggerGen(options =>
    {
      options.SwaggerDoc("v1", new OpenApiInfo
      {
        Version = "v1",
        Title = "ApproveverHub.WebApi",
        Description = "An ASP.NET Core Web API for managing ApproveverHub.WebApi items"
      });
      options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
      {
        In = ParameterLocation.Header,
        Description = "Please enter a valid token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "Bearer"
      });
      options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type=ReferenceType.SecurityScheme,
                                    Id="Bearer"
                                }
                            },
                        new string[]{}
                    }
                });

      // using System.Reflection;
      //  var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
      // options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
    });
  }
   

}
