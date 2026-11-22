using Tabletop.Content;
using Tabletop.Sim.Scenarios;
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
/// The core group's games on the in-process driver (CoreGamesSocketTests: over the socket). A game an engine defect blocks is pinned to
/// exactly the failures it shows, so a fix turns up here as a change to make.
/// </summary>
public class CoreGamesTests
{
    /// <summary>Each blocked game's failures: every failure contains one of these, and each of these is in some failure.</summary>
    static readonly Dictionary<string, string[]> Blocked = new()
    {
    };

    public static TheoryData<string> Games => [.. SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.CoreGroup).Select(g => g.Name)];

    [Fact]
    public void The_core_group_is_a_small_set_of_themed_games_and_one_per_optional_rule()
    {
        var core = SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.CoreGroup && g.Edition == RuleSets.Gf351).ToList();    // a game that runs both editions counts once
        Assert.InRange(core.Count(g => g.Scenario.OptionalRules.Count == 0), 8, 24);
        Assert.Equal(3, core.Count(g => g.Scenario.OptionalRules.Count > 0));                   // one per optional-rule value that is not the default
        Assert.All(core, g => Assert.StartsWith("core-", g.Name));
        Assert.Equal(core.Count, core.Select(g => g.Name).Distinct().Count());
    }

    [Fact]
    public void The_blocked_games_are_the_ones_the_catalog_pins_to_a_known_defect() =>
        Assert.Equal(Blocked.Keys.Order(), SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.CoreGroup && g.Known.Count > 0).Select(g => g.Name).Order());

    // Whole games in process: E2E for the unit suite's time, as `dotnet run --project sim -- suite` plays them all.
    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(Games))]
    public async Task Each_core_game_passes_in_process_or_shows_only_its_known_defect(string name) =>
        AssertResult(name, await new SuiteRunner().RunAsync(new SuiteOptions([name], DriverKind.InProcess, false, TempDir()), null, Ct));

    internal static void AssertResult(string name, SuiteReport report)
    {
        var failures = report.Games.Single().Failures;
        if (!Blocked.TryGetValue(name, out var known)) { Assert.True(report.Passed, string.Join("\n", failures)); return; }
        Assert.All(failures, f => Assert.True(known.Any(f.Contains), $"unexpected failure: {f}"));
        Assert.All(known, k => Assert.True(failures.Any(f => f.Contains(k)), $"the known failure '{k}' did not show: {string.Join("\n", failures)}"));
    }

}

/// <summary>The core group's games over the socket, against the in-process game server over the Docker Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "E2E")]
public class CoreGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = GameServerFactory.OverPostgres(pg.Db);
    public void Dispose() => _factory.Dispose();

    [Theory]
    [MemberData(nameof(CoreGamesTests.Games), MemberType = typeof(CoreGamesTests))]
    public async Task Each_core_game_passes_over_the_socket_or_shows_only_its_known_defect(string name)
    {
        var report = await new SuiteRunner(socket: await SocketAsync()).RunAsync(new SuiteOptions([name], DriverKind.Socket, false, TempDir()), null, Ct);
        CoreGamesTests.AssertResult(name, report);
        Assert.Equal(0, await pg.CountAsync("select count(*) from management.game where starts_with(name, $1)", SuiteRows.GameName(report.RunId, "", keep: false)));
    }

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    async Task<SuiteSocket> SocketAsync()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(
            ScenarioCompiler.CoreDocuments[RuleSets.Gf351], Ct);
        return new TestPool(pg).Socket(() => new FactoryConnector(_factory));
    }
}
