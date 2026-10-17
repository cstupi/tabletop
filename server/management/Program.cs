using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("TABLETOP_");
builder.WebHost.UseUrls(builder.Configuration["MANAGEMENT_URL"] ?? "http://0.0.0.0:5100");
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
if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<DevSessions>();

var app = builder.Build();
// Resolved eagerly, right after Build() (once ConfigureAppConfiguration overrides have landed), so
// missing required configuration fails startup instead of being silently deferred to first use.
var options = app.Services.GetRequiredService<ManagementOptions>();
app.Services.GetRequiredService<Tabletop.Management.Persistence.IMigrator>().Run();
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
if (app.Environment.IsDevelopment()) app.MapDevSession();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
