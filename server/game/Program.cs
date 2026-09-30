using Npgsql;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Session;

DotEnv.Load(Directory.GetCurrentDirectory());
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
if (!builder.Environment.IsEnvironment("Test"))
    builder.Services.AddSingleton(GameServerOptions.FromEnvironment(builder.Configuration));
builder.Services.AddHttpClient<HttpJwksSource>();
builder.Services.AddSingleton<IJwksSource>(sp => sp.GetRequiredService<HttpJwksSource>());
builder.Services.AddSingleton<ISessionVerifier, StytchJwtVerifier>();
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(sp.GetRequiredService<GameServerOptions>().DbConnection));
builder.Services.AddSingleton<IGameRows, PostgresGameRows>();
builder.Services.AddSingleton<IEventLog, PostgresEventLog>();
builder.Services.AddSingleton<IDiceSource, DiceSource>();
builder.Services.AddSingleton<ISchedulerFactory, SchedulerFactory>();
builder.Services.AddSingleton<IRulesEngineFactory, RulesEngineFactory>();
builder.Services.AddSingleton<IPropertiesStore, PostgresPropertiesStore>();
builder.Services.AddSingleton<PropertiesSeeder>();
builder.Services.AddSingleton<SessionServices>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<ConnectionHandshake>();
builder.Services.AddHostedService<SessionHost>();

var app = builder.Build();
if (!app.Environment.IsEnvironment("Test")) await app.Services.GetRequiredService<PropertiesSeeder>().SeedEmbeddedAsync(CancellationToken.None);
var options = app.Services.GetRequiredService<GameServerOptions>();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = options.KeepAliveInterval, KeepAliveTimeout = options.KeepAliveTimeout });

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await context.RequestServices.GetRequiredService<ConnectionHandshake>().RunAsync(socket, context.RequestAborted);
});

app.Run();

public partial class Program;
