using Npgsql;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests;

/// <summary>
/// The dev mint's account pool for tests against the in-memory game server: a name is always the same account, seeded on its first
/// use through PostgresFixture (which deletes it when the collection ends), and its sessions are signed by TestKeys against the
/// factory's fake JWKS. Participant i is SocketCredentials.NameFor(i), as from the console; the row is named TestName of it, so a
/// row a crashed test leaves is never taken for the dev mint's pool.
/// </summary>
public sealed class TestPool(PostgresFixture pg)
{
    public static string TestName(string name) => $"test {name}";

    readonly Dictionary<string, Guid> _byName = [];
    readonly SemaphoreSlim _gate = new(1, 1);

    public IReadOnlyCollection<Guid> Accounts { get { lock (_byName) return [.. _byName.Values]; } }

    public async Task<MintedSession> SessionAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_byName.TryGetValue(name, out var id))
            {
                id = await pg.SeedAccountAsync(TestName(name));
                lock (_byName) _byName[name] = id;
            }
            return new MintedSession(TestKeys.Mint(id), id);
        }
        finally { _gate.Release(); }
    }

    public async Task<SocketCredentials> CredentialsAsync(CompiledScenario c)
    {
        var sessions = new List<MintedSession>();
        for (var i = 0; i < c.Info.Participants.Count; i++) sessions.Add(await SessionAsync(SocketCredentials.NameFor(i)));
        return new SocketCredentials(sessions);
    }

    public SuiteSocket Socket(Func<IWebSocketConnector> connector) =>
        new(pg.Db, (c, _) => CredentialsAsync(c), connector, session: (name, _) => SessionAsync(name));

    /// <summary>The accounts a test's own credentials named (a scenario's compiled ids), which nothing else deletes.</summary>
    public static async Task DeleteAccountsAsync(NpgsqlDataSource db, SocketCredentials credentials)
    {
        await using var command = db.CreateCommand("delete from management.account where id = any($1)");
        command.Parameters.AddWithValue(credentials.Sessions.Select(s => s.AccountId).ToArray());
        await command.ExecuteNonQueryAsync();
    }
}
