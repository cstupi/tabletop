using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
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
builder.Services.ConfigureHttpJsonOptions(o => Tabletop.Content.ContentJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddCors();
builder.Services.AddStytchJwt();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddHttpClient(StytchClient.HttpClientName);
builder.Services.AddSingleton(sp => new StytchClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(StytchClient.HttpClientName), sp.GetRequiredService<ManagementOptions>()));
builder.Services.AddSingleton<Tabletop.Management.Content.Assets.IBlobStore>(sp => new Tabletop.Management.Content.Assets.S3BlobStore(sp.GetRequiredService<ManagementOptions>()));
builder.Services.AddSingleton<AssetRepository>();
builder.Services.AddSingleton<FolderRepository>();
builder.Services.AddHttpClient(ArmyForgeClient.HttpClientName);
builder.Services.AddSingleton(sp => new ArmyForgeClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(ArmyForgeClient.HttpClientName)));
builder.Services.AddSingleton<CatalogKinds>();
builder.Services.AddSingleton<ArmyRepository>();
builder.Services.AddSingleton<LayoutRepository>();
builder.Services.AddSingleton<Sessions>();
if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<DevSessions>();

var app = builder.Build();
// Resolved eagerly, right after Build() (once ConfigureAppConfiguration overrides have landed), so
// missing required configuration fails startup instead of being silently deferred to first use.
var options = app.Services.GetRequiredService<ManagementOptions>();
Tabletop.Management.Persistence.Migrator.Run(options.DbConnection);
app.UseExceptionHandler(h => h.Run(ctx => Results.Problem(
    statusCode: ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error switch
    {
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
app.UseAuthentication();
app.UseAuthorization();
app.MapAccount();
app.MapAuth();
app.MapAssets();
app.MapFolders();
app.MapArmies();
app.MapLayouts();
if (app.Environment.IsDevelopment()) app.MapDevSession();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
