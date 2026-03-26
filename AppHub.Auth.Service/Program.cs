using AppHub.Auth.Service;
using Azure.Identity;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("AppHub Auth Service starting up");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Wire Azure Key Vault into IConfiguration
    var keyVaultUri = builder.Configuration["KeyVault:VaultUri"];
    if (!string.IsNullOrEmpty(keyVaultUri))
    {
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
        Log.Information("Azure Key Vault connected: {Uri}", keyVaultUri);
    }

    builder.Host.UseSerilog((ctx, lc) => lc
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(ctx.Configuration));

    var app = builder
        .ConfigureAuthServices()
        .ConfigureAuthPipeline();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Auth Service unhandled exception");
}
finally
{
    Log.Information("Auth Service shut down complete");
    Log.CloseAndFlush();
}
