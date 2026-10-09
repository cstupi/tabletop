using System.Net.WebSockets;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>The special group (rules suite plan, Task 5): every core special rule, three to five a game, on both drivers.</summary>
public class SpecialGamesTests
{
    public static IReadOnlyList<TestGame> Games { get; } = SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.SpecialGroup).ToList();

    /// <summary>The game whose rules a sim scenario cannot bring into play yet (set-aside units, a Hero joined to a unit) or the engine does
    /// not implement (Aircraft's move and "can't be charged"), and the checks that stand failing for it.</summary>
    public const string Blocked = "special-scouts-and-ambushers";

    public static readonly string[] BlockedChecks =
    [
        "fired: never fired: Scout, Ambush, Hero",
        "p.14 Scout:", "p.13 Ambush:", "p.14 Hero:", "p.13 Aircraft: \"May only use Advance actions", "p.13 Aircraft: \"can't be charged\"",
    ];

    [Fact]
    public void The_special_games_cover_the_thirty_three_core_rules_three_to_five_a_game()
    {
        var rules = TestProperties.RulebookDocument.Entries.Select(e => e.Name).Order().ToList();
        Assert.Equal(33, rules.Count);
        Assert.Equal(rules, Games.SelectMany(g => g.Covers).Order().ToList());
        Assert.All(Games, g => Assert.InRange(g.Covers.Count, 3, 5));
    }

    [Fact]
    public void Every_exact_check_is_about_entries_its_game_covers()
    {
        Assert.All(Games, g => Assert.All(g.Checks, c => Assert.Subset(g.Covers.ToHashSet(), (c.Entries ?? []).ToHashSet())));
        Assert.All(Games.SelectMany(g => g.Covers), rule => Assert.Contains(Games, g => g.Checks.Any(c => c.Entries?.Contains(rule) == true)));
    }

    [Fact]
    public async Task The_special_games_pass_in_process_but_for_the_blocked_checks()
    {
        var report = await new SuiteRunner(Games).RunAsync(new SuiteOptions([SuiteCatalog.SpecialGroup], DriverKind.InProcess, false, TempDir()), null, Ct);
        AssertPassesButForTheBlocked(report);
    }

    internal static void AssertPassesButForTheBlocked(SuiteReport report)
    {
        Assert.Equal(Games.Select(g => g.Name), report.Games.Select(g => g.Name));
        Assert.All(report.Games.Where(g => g.Name != Blocked), g => Assert.True(g.Passed, $"{g.Name}: {string.Join("\n", g.Failures)}"));
        Assert.All(report.Games.Single(g => g.Name == Blocked).Failures, f => Assert.Contains(BlockedChecks, b => f.StartsWith(b)));
    }
}

/// <summary>The same games over the socket, against the in-memory game server and the Docker Postgres.</summary>
[Collection("Postgres")]
public class SpecialGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    [Fact]
    public async Task The_special_games_pass_over_the_socket_but_for_the_blocked_checks()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);
        var socket = new SuiteSocket(pg.Db, (c, minted, _) =>
        {
            foreach (var p in c.Info.Participants) minted.Add(p.AccountId);
            return Task.FromResult(new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList()));
        }, () => new FactoryConnector(_factory));

        var report = await new SuiteRunner(SpecialGamesTests.Games, socket).RunAsync(new SuiteOptions([SuiteCatalog.SpecialGroup], DriverKind.Socket, false, TempDir()), null, Ct);

        SpecialGamesTests.AssertPassesButForTheBlocked(report);
    }
}
