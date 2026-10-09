using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Tabletop.GameServer.Configuration;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// What a socket run needs: the database the servers use, a session per participant and a connector to the game server. From the
/// console it comes from the environment (the same variables and defaults as a scenario's socket run); a test hands in its own.
/// Credentials add each account to `minted` as soon as it exists, so a mint that fails part-way still names the accounts to delete.
/// </summary>
public sealed class SuiteSocket(
    NpgsqlDataSource db,
    Func<CompiledScenario, ICollection<Guid>, CancellationToken, Task<SocketCredentials>> credentials,
    Func<IWebSocketConnector> connector,
    bool ownsDatabase = false,
    Func<string, CancellationToken, Task<MintedSession>>? session = null) : IAsyncDisposable
{
    public NpgsqlDataSource Db => db;
    public Task<SocketCredentials> CredentialsAsync(CompiledScenario scenario, ICollection<Guid> minted, CancellationToken ct) =>
        credentials(scenario, minted, ct);
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
            async (scenario, minted, ct) =>
            {
                using var http = new HttpClient { BaseAddress = management };
                return await MintAsync(scenario, http, minted, ct);
            },
            () => new ClientWebSocketConnector(address), ownsDatabase: true,
            session: async (name, ct) =>
            {
                using var http = new HttpClient { BaseAddress = management };
                return await MintOneAsync(http, name, ct);
            });
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record MintAccount(Guid Id, string Name);
    private sealed record MintResponse(string SessionJwt, MintAccount Account);

    /// <summary>SocketCredentials.MintAsync's sessions (the same names and endpoint), one account reported at a time.</summary>
    private static async Task<SocketCredentials> MintAsync(CompiledScenario scenario, HttpClient http, ICollection<Guid> minted, CancellationToken ct)
    {
        var sessions = new List<MintedSession>();
        for (var i = 0; i < scenario.Info.Participants.Count; i++)
        {
            var session = await MintOneAsync(http, SocketCredentials.NameFor(scenario, i), ct);
            minted.Add(session.AccountId);
            sessions.Add(session);
        }
        return new SocketCredentials(sessions);
    }

    private static async Task<MintedSession> MintOneAsync(HttpClient http, string name, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(SocketCredentials.MintPath, new { name }, Json, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"{http.BaseAddress}{SocketCredentials.MintPath.TrimStart('/')} answered 404; the management API must run as a development environment");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MintResponse>(Json, ct)
            ?? throw new InvalidOperationException("the mint returned an empty body");
        return new MintedSession(body.SessionJwt, body.Account.Id);
    }

    public async ValueTask DisposeAsync()
    {
        if (ownsDatabase) await db.DisposeAsync();
    }
}
