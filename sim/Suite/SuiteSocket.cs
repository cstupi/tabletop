using Npgsql;
using Tabletop.GameServer.Configuration;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// What a socket run needs: the database the servers use, a session per participant and a connector to the game server. From the
/// console it comes from the environment (the same variables and defaults as a scenario's socket run); a test hands in its own.
/// The sessions are the account pool's (SocketCredentials.Pool), which every game and every run shares.
/// </summary>
public sealed class SuiteSocket(
    NpgsqlDataSource db,
    Func<CompiledScenario, CancellationToken, Task<SocketCredentials>> credentials,
    Func<IWebSocketConnector> connector,
    bool ownsDatabase = false,
    Func<string, CancellationToken, Task<MintedSession>>? session = null) : IAsyncDisposable
{
    public NpgsqlDataSource Db => db;
    public Task<SocketCredentials> CredentialsAsync(CompiledScenario scenario, CancellationToken ct) => credentials(scenario, ct);
    public IWebSocketConnector Connector() => connector();

    /// <summary>A fresh session for the named account (the dev mint makes the account on first use, and a name always gives the same one).</summary>
    public Task<MintedSession> SessionAsync(string name, CancellationToken ct) =>
        (session ?? throw new InvalidOperationException("this socket environment cannot mint a session by name"))(name, ct);

    /// <summary>TABLETOP_DB_CONNECTION (required), TABLETOP_SIM_WS and TABLETOP_SIM_MANAGEMENT, from the process or the nearest .env.</summary>
    public static SuiteSocket FromEnvironment()
    {
        DotEnv.Load(Directory.GetCurrentDirectory());
        var connection = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
            ?? throw new UsageException("TABLETOP_DB_CONNECTION is not set; --driver socket needs the same database the servers use");
        var address = new Uri(Environment.GetEnvironmentVariable("TABLETOP_SIM_WS") ?? SimOptions.DefaultWebSocket);
        var management = new Uri(Environment.GetEnvironmentVariable(SocketCredentials.ManagementUrlVariable) ?? SimOptions.DefaultManagement);
        return new SuiteSocket(NpgsqlDataSource.Create(connection),
            async (scenario, ct) =>
            {
                using var http = new HttpClient { BaseAddress = management };
                return await SocketCredentials.MintAsync(scenario, http, ct);
            },
            () => new ClientWebSocketConnector(address), ownsDatabase: true,
            session: async (name, ct) =>
            {
                using var http = new HttpClient { BaseAddress = management };
                return await SocketCredentials.MintOneAsync(http, name, ct);
            });
    }

    public async ValueTask DisposeAsync()
    {
        if (ownsDatabase) await db.DisposeAsync();
    }
}
