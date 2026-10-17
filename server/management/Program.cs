using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;
using Tabletop.Management.Persistence;
using Tabletop.Management.Releases;

DotEnv.LoadForDevelopment(Directory.GetCurrentDirectory(), DotEnv.ProcessEnvironment());
var builder = WebApplication.CreateBuilder(args.Where(a => a != MigrateCommand.Flag).ToArray());
builder.Configuration.AddEnvironmentVariables("TABLETOP_");
if (args.Contains(MigrateCommand.Flag))
{
    Environment.ExitCode = MigrateCommand.Run(builder.Environment.EnvironmentName, builder.Configuration);
    return;
}
builder.WebHost.UseUrls(builder.Configuration["MANAGEMENT_URL"] ?? "http://127.0.0.1:5100");
// Registered as a factory (not from builder.Configuration here) because WebApplicationFactory
// applies its ConfigureAppConfiguration overrides during builder.Build(), after this point in the
// file runs.
builder.Services.AddSingleton(sp => ManagementOptions.From(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp => new Tabletop.Management.Persistence.Database(sp.GetRequiredService<ManagementOptions>().DbConnection));
builder.Services.AddSingleton<Tabletop.Management.Persistence.IMigrator, Tabletop.Management.Persistence.PostgresMigrator>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    Tabletop.Content.ContentJson.Configure(o.SerializerOptions);
    // Enums bind by name only: an integer (`{"kind":99}`) is a 400, never an undefined value reaching a handler.
    o.SerializerOptions.Converters.Remove(o.SerializerOptions.Converters.OfType<System.Text.Json.Serialization.JsonStringEnumConverter>().Single());
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
});
builder.Services.AddProblemDetails();
builder.Services.AddCors();
builder.Services.AddStytchJwt();
builder.Services.AddProxyAndRateLimits();
builder.Services.AddSingleton<IAccountRepository, AccountRepository>();
builder.Services.AddSingleton<AccountDeleter>();
builder.Services.AddHttpClient(StytchClient.HttpClientName);
builder.Services.AddSingleton(sp => new StytchClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(StytchClient.HttpClientName), sp.GetRequiredService<ManagementOptions>()));
builder.Services.AddSingleton<Tabletop.Management.Content.Assets.IBlobStore>(sp => new Tabletop.Management.Content.Assets.S3BlobStore(sp.GetRequiredService<ManagementOptions>()));
builder.Services.AddSingleton<IAssetRepository, AssetRepository>();
builder.Services.AddSingleton<IFolderRepository, FolderRepository>();
builder.Services.AddSingleton<StarterAssets>();
builder.Services.AddHostedService<UploadSweeper>();
builder.Services.AddHttpClient(ArmyForgeClient.HttpClientName);
builder.Services.AddSingleton(sp => new ArmyForgeClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(ArmyForgeClient.HttpClientName)));
builder.Services.AddSingleton<ICatalogKinds, CatalogKinds>();
builder.Services.AddSingleton<IArmyRepository, ArmyRepository>();
builder.Services.AddSingleton<ILayoutRepository, LayoutRepository>();
builder.Services.AddSingleton<IGameRepository, GameRepository>();
builder.Services.AddSingleton<Sessions>();
builder.Services.AddSingleton(sp => new Tabletop.Management.Releases.ClientReleases(sp.GetRequiredService<IBlobStore>(), TimeProvider.System,
    sp.GetRequiredService<ILogger<Tabletop.Management.Releases.ClientReleases>>()));
if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<DevSessions>();

var app = builder.Build();
// Resolved eagerly, right after Build() (once ConfigureAppConfiguration overrides have landed), so
// missing required configuration fails startup instead of being silently deferred to first use.
var options = app.Services.GetRequiredService<ManagementOptions>();
StartupGuard.Enforce(app.Environment.EnvironmentName, app.Configuration, options);
var devMode = StartupGuard.DevMode(app.Environment.EnvironmentName, app.Configuration);
// Dev mode migrates at startup. Anywhere else the runtime role cannot, so a schema behind the build refuses to start: run --migrate as the owner role first.
var migrator = app.Services.GetRequiredService<Tabletop.Management.Persistence.IMigrator>();
if (devMode) migrator.Run();
else if (migrator.Pending() is { Count: > 0 } pending)
    throw new InvalidOperationException($"The database is behind this build ({string.Join(", ", pending)}); run `Tabletop.Management --migrate` with the owner role's TABLETOP_DB_CONNECTION first.");
app.UseForwardedHeaders();       // first: everything after sees the client's address, from a trusted proxy only
app.UseExceptionHandler(h => h.Run(ctx => Results.Problem(
    statusCode: ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error switch
    {
        Microsoft.AspNetCore.Http.BadHttpRequestException e => e.StatusCode,
        ArgumentOutOfRangeException => 400,
        UnauthorizedAccessException => 401,
        _ => 500,
    })
    .ExecuteAsync(ctx)));
app.UseCors(p => p
    .WithOrigins(options.WebUrl.TrimEnd('/'))
    .WithHeaders("Authorization", "Content-Type", Tabletop.Protocol.ProtocolVersion.Header)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE"));
app.UseMiddleware<Tabletop.Management.Http.ClientVersionMiddleware>();
app.UseMiddleware<Tabletop.Management.Http.BodySizeLimit>();
app.UseAuthentication();
app.UseRateLimiter();            // after authentication: the per-account budget reads the account claim
app.UseAuthorization();
app.MapAccount();
app.MapAuth();
app.MapAssets();
app.MapFolders();
app.MapArmies();
app.MapLayouts();
app.MapProperties();
app.MapGames();
app.MapManifest();
app.MapClientReleases();
if (devMode) app.MapDevSession();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
