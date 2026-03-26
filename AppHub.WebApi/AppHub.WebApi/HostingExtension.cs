using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.Text;
using AppHub.Infrastructure.Data;
using static AppHub.WebApi.Config.IdentityServerSetting;
using AppHub.WebApi.Extensions;
using AppHub.WebApi.Middleware;
using Microsoft.OpenApi.Models;
using Microsoft.Identity.Web;

namespace AppHub.WebApi;

public static class HostingExtension
{
    private const string CorsPolicy            = "_MyAllowSubdomainPolicy";
    private const string IdentityServerSection = "IdentityServerSettings";

    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        // ── SQL Server (original, unchanged) ──────────────────────────────────
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(config.GetConnectionString("DefaultConnection")));

        // ── Identity Server settings ──────────────────────────────────────────
        var identitySettings = config.GetSection(IdentityServerSection).Get<IdentityServerSettings>()
            ?? throw new InvalidOperationException("IdentityServerSettings section is missing from configuration.");

        builder.Services.Configure<IdentityServerSettings>(config.GetSection(IdentityServerSection));
        builder.Services.AddSingleton(identitySettings);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache(); // for InMemoryCacheService fallback

        builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

        // Pass config so ServiceExtension can conditionally register Redis / Cosmos / stubs
        builder.Services.ConfigureDIServices(config);

        // ── CORS ──────────────────────────────────────────────────────────────
        var origins = identitySettings.AllowedOrigins ?? new[] { "http://localhost:3000" };
        builder.Services.AddCors(opt =>
            opt.AddPolicy(CorsPolicy, p =>
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

        // ── Authentication: Local JWT + Azure Entra ID ─────────────────────
        builder.Services.AddAuthentication(opt =>
        {
            opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opt.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
            opt.DefaultScheme             = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer("LocalJwt", opt =>
        {
            opt.SaveToken            = true;
            opt.RequireHttpsMetadata = false;
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidAudience            = identitySettings.ValidAudience,
                ValidIssuer              = identitySettings.ValidIssuer,
                IssuerSigningKey         = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(identitySettings.ApiSecret!)),
                ClockSkew = TimeSpan.Zero
            };
        })
        .AddMicrosoftIdentityWebApi(config.GetSection("AzureAd"), jwtBearerScheme: "AzureAd");

        builder.Services.AddAuthorization(opt =>
        {
            opt.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                    "LocalJwt", "AzureAd")
                .RequireAuthenticatedUser().Build();
            opt.AddPolicy("AzureAdOnly", p =>
                p.AddAuthenticationSchemes("AzureAd").RequireAuthenticatedUser());
            opt.AddPolicy("LocalJwtOnly", p =>
                p.AddAuthenticationSchemes("LocalJwt").RequireAuthenticatedUser());
        });

        builder.Services.AddControllers()
            .AddNewtonsoftJson(opt =>
                opt.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc);

        builder.Services.AddEndpointsApiExplorer();
        RegisterSwagger(builder.Services);

        // Health checks
        builder.Services.AddHealthChecks();

        // Recyclable stream manager for idempotency middleware
        builder.Services.AddSingleton<Microsoft.IO.RecyclableMemoryStreamManager>();

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        // Always show Swagger (not just in Development) so teams can test
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "AppHub.WebApi v1");
            c.RoutePrefix = "swagger";
        });

        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseStaticFiles();
        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();

        // Idempotency middleware — after auth, before controllers
        app.UseMiddleware<IdempotencyMiddleware>();

        app.ConfigureRedundantStatusCodePages();
        app.ConfigureExceptionHandler();

        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");
        app.MapControllers();

        return app;
    }

    private static void RegisterSwagger(IServiceCollection services)
    {
        services.AddSwaggerGen(opt =>
        {
            opt.SwaggerDoc("v1", new OpenApiInfo
            {
                Version     = "v1",
                Title       = "AppHub.WebApi",
                Description = "ASP.NET Core 8 · SQL Server · CosmosDB · Redis · Azure Blob · Key Vault · Email OTP MFA · Idempotency · Kubernetes"
            });

            opt.OperationFilter<IdempotencyHeaderOperationFilter>();

            opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header, Name = "Authorization",
                Type = SecuritySchemeType.Http, BearerFormat = "JWT", Scheme = "Bearer",
                Description = "Paste your JWT here: Bearer {token}"
            });
            opt.AddSecurityRequirement(new OpenApiSecurityRequirement
            {{
                new OpenApiSecurityScheme { Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                Array.Empty<string>()
            }});
        });
    }
}
