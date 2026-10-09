using System.Net.WebSockets;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>
/// The core group's games on both drivers (the socket one against the in-memory game server and the Docker Postgres). A game an engine defect
/// or a missing sim capability blocks is pinned to exactly the failures it shows, so a fix turns up here as a change to make.
/// </summary>
[Collection("Postgres")]
public class CoreGamesTests(PostgresFixture pg) : IDisposable
{
    /// <summary>Each blocked game's failures: every failure contains one of these, and each of these is in some failure.</summary>
    static readonly Dictionary<string, string[]> Blocked = new()
    {
        // deal-wounds opens an empty remove_casualties Allocation when an earlier weapon group destroyed the target (p.8).
        ["core-overkill"] = ["'remove_casualties' (Allocation) admits no answer", "p.8: \"all weapons are fired simultaneously\""],
        // A move in which one model enters difficult terrain lets the unit's other models go past 6" (p.11), under strict.
        ["core-rough-ground"] = ["p.11: \"If any model in a unit moves in or through difficult terrain"],
        // A disembark is accepted with the model's base off the table edge (p.7).
        ["core-transport-run"] = ["NoModelInTwoPlacesOrOffTheSlab"],
        // The sim cannot set a unit aside before deployment, so neither a Scout nor an Ambusher is ever held back (p.13, p.14).
        ["core-deployment"] = ["never fired: Scout, Ambush", "no Scout was deployed", "no Ambusher was deployed"],
    };

    public static TheoryData<string> Games => [.. SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.CoreGroup).Select(g => g.Name)];

    readonly GameServerFactory _factory = new();
    readonly List<Guid> _minted = [];
    public void Dispose() => _factory.Dispose();

    [Fact]
    public void The_core_group_is_about_ten_themed_games()
    {
        var core = SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.CoreGroup).ToList();
        Assert.InRange(core.Count, 8, 12);
        Assert.All(core, g => Assert.StartsWith("core-", g.Name));
        Assert.Equal(core.Count, core.Select(g => g.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Games))]
    public async Task Each_core_game_passes_in_process_or_shows_only_its_known_defect(string name) =>
        AssertResult(name, await new SuiteRunner().RunAsync(new SuiteOptions([name], DriverKind.InProcess, false, TempDir()), null, Ct));

    [Theory]
    [MemberData(nameof(Games))]
    public async Task Each_core_game_passes_over_the_socket_or_shows_only_its_known_defect(string name)
    {
        var report = await new SuiteRunner(socket: await SocketAsync()).RunAsync(new SuiteOptions([name], DriverKind.Socket, false, TempDir()), null, Ct);
        AssertResult(name, report);
        Assert.Equal(0, await pg.CountAsync("select count(*) from management.game where starts_with(name, $1)", SuiteRows.GameName(report.RunId, "", keep: false)));
    }

    static void AssertResult(string name, SuiteReport report)
    {
        var failures = report.Games.Single().Failures;
        if (!Blocked.TryGetValue(name, out var known)) { Assert.True(report.Passed, string.Join("\n", failures)); return; }
        Assert.All(failures, f => Assert.True(known.Any(f.Contains), $"unexpected failure: {f}"));
        Assert.All(known, k => Assert.True(failures.Any(f => f.Contains(k)), $"the known failure '{k}' did not show: {string.Join("\n", failures)}"));
    }

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    async Task<SuiteSocket> SocketAsync()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(
            PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);
        return new SuiteSocket(pg.Db, (c, minted, _) =>
        {
            foreach (var p in c.Info.Participants) { _minted.Add(p.AccountId); minted.Add(p.AccountId); }
            return Task.FromResult(new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList()));
        }, () => new FactoryConnector(_factory));
    }
}
