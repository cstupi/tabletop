using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;

DotEnv.Load(Directory.GetCurrentDirectory());
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
if (!builder.Environment.IsEnvironment("Test"))
    builder.Services.AddSingleton(GameServerOptions.FromEnvironment(builder.Configuration));
builder.Services.AddHttpClient<HttpJwksSource>();
builder.Services.AddSingleton<IJwksSource>(sp => sp.GetRequiredService<HttpJwksSource>());
builder.Services.AddSingleton<ISessionVerifier, StytchJwtVerifier>();
// Tasks 3-9 register: IGameRows, IEventLog, IRulesEngineFactory, SessionRegistry, ConnectionHandshake, SessionHost.

var app = builder.Build();
var options = app.Services.GetRequiredService<GameServerOptions>();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = options.KeepAliveInterval, KeepAliveTimeout = options.KeepAliveTimeout });

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    // Task 8 replaces this line with: await context.RequestServices.GetRequiredService<ConnectionHandshake>().RunAsync(socket, context.RequestAborted);
    await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "not yet", CancellationToken.None);
});

app.Run();

public partial class Program;
