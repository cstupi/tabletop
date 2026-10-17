using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>
/// Program.cs in the Test environment over a seeder's in-memory stores (a fresh one when none is given): no Postgres, and TestKeys'
/// JWKS in place of Stytch's, so a server test reaches no live service. NpgsqlDataSource is removed, so anything that still asks for
/// Postgres fails at resolution rather than waiting on a connection. OverPostgres is the E2E suite's: the Postgres adapters over the
/// Docker Postgres.
/// </summary>
public class GameServerFactory : WebApplicationFactory<Program>
{
    public const string ProjectId = "project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43";
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }
    readonly InMemorySeeder? _db;
    readonly NpgsqlDataSource? _postgres;

    public GameServerFactory(InMemorySeeder? db = null) => _db = db ?? new InMemorySeeder();
    GameServerFactory(NpgsqlDataSource postgres) => _postgres = postgres;

    public static GameServerFactory OverPostgres(NpgsqlDataSource postgres) => new(postgres);

    /// <summary>Program.cs's registrations with no scheduler behind the engine: tests that seed a Playing game and check the log the session hydrates or the seed appended, not deployment.</summary>
    public static GameServerFactory WithoutScheduler(InMemorySeeder? db = null) => new(db) { ConfigureTestServices = s => s.AddSingleton<Engine.ISchedulerFactory, Engine.NoSchedulerFactory>() };

    /// <summary>The in-memory stores this server reads and writes.</summary>
    public InMemorySeeder Db => _db ?? throw new InvalidOperationException("this factory runs over Postgres");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new GameServerOptions
            {
                DbConnection = "",
                StytchProjectId = ProjectId,
                StytchJwksUrl = $"https://test.stytch.com/v1/sessions/jwks/{ProjectId}",
                MinimumClientVersion = Tabletop.Protocol.ProtocolVersion.Current,
                PresenceRelayHz = 50,
                HandshakeTimeout = TimeSpan.FromSeconds(2),
                AcceptsSetDice = true,                                  // the suite's socket games fix dice, as on the Development server
            });
            services.RemoveAll<NpgsqlDataSource>();
            services.RemoveAll<IGameRows>();
            services.RemoveAll<IEventLog>();
            services.RemoveAll<IPropertiesStore>();
            if (_postgres is { } pg)
            {
                services.AddSingleton<IGameRows>(new PostgresGameRows(pg));
                services.AddSingleton<IEventLog>(new PostgresEventLog(pg));
                services.AddSingleton<IPropertiesStore>(new PostgresPropertiesStore(pg));
            }
            else
            {
                services.AddSingleton<IGameRows>(_db!.GameRows);
                services.AddSingleton<IEventLog>(_db.Log);
                services.AddSingleton<IPropertiesStore>(_db.Properties);
            }
            ConfigureTestServices?.Invoke(services);
            services.AddSingleton<IJwksSource>(TestKeys.Source);
        });
    }
}
