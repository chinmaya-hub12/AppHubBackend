using AppHub.Core.Entity;
using AppHub.Infrastructure.Abstract;
using AppHub.Infrastructure.Concrete;
using AppHub.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

namespace AppHub.Auth.Service;

public static class AuthHostingExtension
{
    public static WebApplication ConfigureAuthServices(this WebApplicationBuilder builder)
    {
        // SQL Server — connection string loaded from Key Vault via IConfiguration
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Cosmos DB — AccountKey loaded from Key Vault
        builder.Services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            return new CosmosClient(cfg["CosmosDb:AccountEndpoint"]!, cfg["CosmosDb:AccountKey"]!,
                new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                });
        });

        builder.Services.AddScoped<ICosmosRepository<AuditLog>, CosmosRepository<AuditLog>>();
        builder.Services.AddScoped<ICosmosRepository<MfaSession>, CosmosRepository<MfaSession>>();

        // Azure Key Vault + Blob Storage services
        builder.Services.AddScoped<IKeyVaultService, KeyVaultService>();
        builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
        builder.Services.AddScoped<IMfaService, MfaService>();
        builder.Services.AddScoped<IAuditService, AuditService>();

        // JWT — ApiSecret loaded from Key Vault
        var apiSecret = builder.Configuration["IdentityServerSettings:ApiSecret"]!;
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret)),
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["IdentityServerSettings:ValidIssuer"],
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["IdentityServerSettings:ValidAudience"],
                    ClockSkew = TimeSpan.Zero
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "AppHub Auth Microservice",
                Version = "v1",
                Description = "Dedicated authentication microservice: login, MFA, token issuance. All secrets from Azure Key Vault."
            });
        });
        builder.Services.AddHealthChecks();
        return builder.Build();
    }

    public static WebApplication ConfigureAuthPipeline(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
