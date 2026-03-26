using AppHub.WebApi;
using Azure.Identity;
using Serilog;

// Bootstrap logger — active before DI is built
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("AppHub WebApi starting up");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── STEP 1: Azure Key Vault (optional — skipped when VaultUri is empty) ──
    // In production: set KeyVault:VaultUri in environment variable or K8s secret.
    // In development: leave empty → app reads from appsettings.json directly.
    var keyVaultUri = builder.Configuration["KeyVault:VaultUri"];
    if (!string.IsNullOrWhiteSpace(keyVaultUri))
    {
        try
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential());
            Log.Information("Azure Key Vault connected: {Uri}", keyVaultUri);
        }
        catch (Exception kvEx)
        {
            // Key Vault failure should NOT crash the app in non-prod environments
            Log.Warning(kvEx, "Azure Key Vault connection failed — continuing without Key Vault. " +
                "Using appsettings.json values instead.");
        }
    }
    else
    {
        Log.Information("KeyVault:VaultUri not set — using local appsettings configuration");
    }

    // ── STEP 2: Serilog full configuration ────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) => lc
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(ctx.Configuration));

    // ── STEP 3: Build services and pipeline ───────────────────────────────────
    var app = builder
        .ConfigureServices()
        .ConfigurePipeline();

    app.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "AppHub WebApi failed to start");
}
finally
{
    Log.Information("AppHub WebApi shut down");
    Log.CloseAndFlush();
}
