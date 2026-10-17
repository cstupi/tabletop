using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;
using Tabletop.Sim.Tests.Infrastructure;

namespace Tabletop.Sim.Tests;

/// <summary>
/// The dev mint's account pool for tests against the in-process game server: a name is always the same account, seeded on its first
/// use through the seeder, and its sessions are signed by TestKeys against the factory's fake JWKS. Participant i is
/// SocketCredentials.NameFor(i), as from the console; the row is named TestName of it, so a row a crashed E2E test leaves in Postgres is
/// never taken for the dev mint's pool. Over an InMemorySeeder (the unit suite) the rows are InMemorySocketRows; over PostgresFixture
/// (the E2E suite) SocketRowSeeder, and the fixture deletes the accounts.
/// </summary>
public sealed class TestPool(IRowSeeder seeder, ISocketRows rows)
{
    public TestPool(InMemorySeeder db) : this(db, new InMemorySocketRows(db)) { }
    public TestPool(PostgresFixture pg) : this(pg, new SocketRowSeeder(pg.Db)) { }

    public static string TestName(string name) => $"test {name}";

    readonly Dictionary<string, Guid> _byName = [];
    readonly SemaphoreSlim _gate = new(1, 1);

    public ISocketRows Rows => rows;
    public IReadOnlyCollection<Guid> Accounts { get { lock (_byName) return [.. _byName.Values]; } }

    public async Task<MintedSession> SessionAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_byName.TryGetValue(name, out var id))
            {
                id = await seeder.SeedAccountAsync(TestName(name));
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
        return new SocketCredentials(sessions) { Refresh = (i, _) => SessionAsync(SocketCredentials.NameFor(i)) };
    }

    public SuiteSocket Socket(Func<IWebSocketConnector> connector) =>
        new(rows, (c, _) => CredentialsAsync(c), connector, session: (name, _) => SessionAsync(name));
}
