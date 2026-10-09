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

    /// <summary>
    /// Each game pinned to an engine defect, and its standing failures: the defect, the start of the check's name and part of the reason.
    /// A fix turns up here as a change to make.
    /// </summary>
    public static readonly (string Game, string Defect, string Check, string Reason)[] BlockedChecks =
    [
        ("special-aircraft-overhead", "D7", "p.13 Aircraft: \"May only use Advance actions", "the Aircraft's Advance allowance is 6, expected 36 (6 + 30)"),
        ("special-aircraft-overhead", "D8", "p.13 Aircraft: \"can't be charged\"", "the Aircraft was offered as a charge target"),
        ("special-stealth-at-nine-and-a-half", "D11", "p.14 Stealth:", "\" away: hit 4+ [] "),
    ];

    [Fact]
    public void The_special_games_cover_the_thirty_three_core_rules_three_to_five_a_game()
    {
        var rules = TestProperties.RulebookDocument.Entries.Select(e => e.Name).Order().ToList();
        Assert.Equal(33, rules.Count);
        Assert.Equal(rules, Games.SelectMany(g => g.Covers).Distinct().Order().ToList());
        Assert.All(Games.Where(g => g.Known.Count == 0), g => Assert.InRange(g.Covers.Count, 3, 5));
    }

    [Fact]
    public void Every_exact_check_is_about_entries_its_game_covers()
    {
        // A defect exhibit covers nothing and reports its check against the entry it shows the defect in.
        Assert.All(Games.Where(g => g.Covers.Count > 0), g => Assert.All(g.Checks, c => Assert.Subset(g.Covers.ToHashSet(), (c.Entries ?? []).ToHashSet())));
        Assert.All(Games.SelectMany(g => g.Covers), rule => Assert.Contains(Games, g => g.Checks.Any(c => c.Entries?.Contains(rule) == true)));
    }

    [Fact]
    public async Task The_special_games_pass_in_process_but_for_their_known_defects()
    {
        var report = await new SuiteRunner(Games).RunAsync(new SuiteOptions([SuiteCatalog.SpecialGroup], DriverKind.InProcess, false, TempDir()), null, Ct);
        AssertPassesButForTheBlocked(report);
    }

    internal static void AssertPassesButForTheBlocked(SuiteReport report)
    {
        Assert.Equal(Games.Select(g => g.Name), report.Games.Select(g => g.Name));
        var blocked = BlockedChecks.Select(b => b.Game).ToHashSet();
        Assert.Equal(blocked.Order(), Games.Where(g => g.Known.Count > 0).Select(g => g.Name).Order());
        Assert.All(report.Games.Where(g => !blocked.Contains(g.Name)), g => Assert.True(g.Passed, $"{g.Name}: {string.Join("\n", g.Failures)}"));
        foreach (var g in report.Games.Where(g => blocked.Contains(g.Name)))
        {
            var expected = BlockedChecks.Where(b => b.Game == g.Name).ToList();
            Assert.Equal(expected.Select(b => b.Defect).Distinct().Order(), (g.Known ?? []).Order());
            Assert.All(g.Failures, f => Assert.Contains(expected, b => f.StartsWith($"known defect {b.Defect}: {b.Check}") && f.Contains(b.Reason)));
            Assert.All(expected, b => Assert.Contains(g.Failures, f => f.StartsWith($"known defect {b.Defect}: {b.Check}") && f.Contains(b.Reason)));
        }
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
    public async Task The_special_games_pass_over_the_socket_but_for_their_known_defects()
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
