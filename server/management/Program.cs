using Tabletop.Management.Configuration;

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("TABLETOP_");
builder.WebHost.UseUrls(builder.Configuration["MANAGEMENT_URL"] ?? "http://0.0.0.0:5100");
// Registered as a factory (not from builder.Configuration here) because WebApplicationFactory
// applies its ConfigureAppConfiguration overrides during builder.Build(), after this point in the
// file runs.
builder.Services.AddSingleton(sp => ManagementOptions.From(sp.GetRequiredService<IConfiguration>()));

var app = builder.Build();
// Resolved eagerly, right after Build() (once ConfigureAppConfiguration overrides have landed), so
// missing required configuration fails startup instead of being silently deferred to first use.
app.Services.GetRequiredService<ManagementOptions>();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
