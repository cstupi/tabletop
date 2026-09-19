using Tabletop.Management.Configuration;
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
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
