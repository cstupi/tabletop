using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;

namespace Tabletop.GameServer.Tests.Infrastructure;

public class GameServerFactory : WebApplicationFactory<Program>
{
    public const string ProjectId = "project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43";
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    /// <summary>Program.cs's registrations with no scheduler behind the engine: tests that seed a Playing game and check the log the session hydrates or the seed appended, not deployment.</summary>
    public static GameServerFactory WithoutScheduler() => new() { ConfigureTestServices = s => s.AddSingleton<Engine.ISchedulerFactory, Engine.NoSchedulerFactory>() };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new GameServerOptions
            {
                DbConnection = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
                    ?? "Host=localhost;Port=5432;Database=tabletop;Username=tabletop;Password=tabletop",
                StytchProjectId = ProjectId,
                StytchJwksUrl = $"https://test.stytch.com/v1/sessions/jwks/{ProjectId}",
                MinimumClientVersion = Tabletop.Protocol.ProtocolVersion.Current,
                PresenceRelayHz = 50,
                HandshakeTimeout = TimeSpan.FromSeconds(2),
            });
            ConfigureTestServices?.Invoke(services);
            services.AddSingleton<IJwksSource>(TestKeys.Source);
        });
    }
}
