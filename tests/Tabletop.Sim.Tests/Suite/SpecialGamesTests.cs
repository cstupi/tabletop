using Tabletop.Content;
using Tabletop.Sim.Scenarios;
using System.Net.WebSockets;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>The special group: every core special rule, three to five a game, and the relic wargear (p.36), on both drivers.</summary>
public class SpecialGamesTests
{
    public static IReadOnlyList<TestGame> Games { get; } = SuiteCatalog.All.Where(g => g.Group == SuiteCatalog.SpecialGroup).ToList();

    /// <summary>
    /// Each game pinned to an engine defect, and its standing failures: the defect, the start of the check's name and part of the reason.
    /// A fix turns up here as a change to make.
    /// </summary>
    public static readonly (string Game, string Defect, string Check, string Reason)[] BlockedChecks =
    [
    ];

    [Fact]
    public void The_special_games_cover_the_thirty_three_core_rules_three_to_five_a_game_and_the_relic_wargear()
    {
        var rules = TestProperties.RulebookDocument.Entries.Select(e => e.Name).Order().ToList();
        Assert.Equal(33, rules.Count);
        var relics = PropertiesSeeder.EmbeddedPart(GfEdition.V351.ContentFolder, "relic-wargear.part.json")!.Entries.Select(e => e.Name).ToHashSet();
        var games = Games.Where(g => g.Edition == RuleSets.Gf351).ToList();
        Assert.Equal(rules.Concat(relics).Order(), games.SelectMany(g => g.Covers).Distinct().Order().ToList());
        // A defect exhibit covers nothing; a relic game (p.36) covers its relics alone.
        Assert.All(games.Where(g => g.Known.Count == 0 && g.Covers.Count > 0 && !g.Covers.All(relics.Contains)), g => Assert.InRange(g.Covers.Count, 3, 5));
    }

    [Fact]
    public void The_3_6_special_games_cover_every_3_6_core_rule_and_the_relic_wargear()
    {
        var rules = PropertiesSeeder.EmbeddedPart(GfEdition.V360.ContentFolder, "core.properties.json")!.Entries.Select(e => e.Name).ToList();
        var relics = PropertiesSeeder.EmbeddedPart(GfEdition.V360.ContentFolder, "relic-wargear.part.json")!.Entries.Select(e => e.Name);
        Assert.Equal(rules.Concat(relics).Distinct().Order(), Games.Where(g => g.Edition == RuleSets.Gf360).SelectMany(g => g.Covers).Distinct().Order().ToList());
    }

    [Fact]
    public void Every_exact_check_is_about_entries_its_game_covers()
    {
        // A defect exhibit covers nothing and reports its check against the entry it shows the defect in.
        Assert.All(Games.Where(g => g.Covers.Count > 0), g => Assert.All(g.Checks, c => Assert.Subset(g.Covers.ToHashSet(), (c.Entries ?? []).ToHashSet())));
        Assert.All(Games.SelectMany(g => g.Covers), rule => Assert.Contains(Games, g => g.Checks.Any(c => c.Entries?.Contains(rule) == true)));
    }

    /// <summary>
    /// A twice-completed roll-to-hit (an optional row's extra attacks, taken): the attack's hit is both completions folded, so Rending's
    /// check counts the 6 of the added dice with the pool's.
    /// </summary>
    [Fact]
    public void An_attacks_hit_folds_every_roll_to_hit_completion()
    {
        var game = Games.Single(g => g.Name == "special-regrowth-under-rending-fire");
        var compiled = ScenarioCompiler.Compile(game.Scenario);
        var units = compiled.Board.Armies.SelectMany(a => a.Snapshot.Units).ToList();
        var (red, blue) = (units.Single(u => u.Name == "Red unit 1").Id, units.Single(u => u.Name == "Blue unit 1").Id);
        var rifle = units.Single(u => u.Id == red).Models[0].Weapons.Single(w => w.Name == "Rending Rifle").Id;
        var (attack, resolve) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        RollRecord Roll(string kind, int ap, params (int Raw, bool Success)[] dice) =>
            new(Guid.CreateVersion7(), kind, 4, ap == 0 ? [] : [new RollModifier("AP", -ap)], [.. dice.Select(d => new Die(d.Raw, d.Raw - ap, d.Success))]);
        StepCompleted Step(Guid frame, string step, params RollRecord[] rolls) => new(frame, step, null, rolls, [], null);
        string? Judge(int ap4Dice)
        {
            EventPayload[] log =
            [
                new SequenceStarted(attack, "attack-sequence", new SequenceContext(red, [blue], [rifle], "Hold")),
                Step(attack, "roll-to-hit", Roll("hit", 0, (6, true), (2, false))),
                Step(attack, "roll-to-hit", Roll("hit", 0, (6, true))),
                Step(attack, "score-hits"),
                new SequenceStarted(resolve, "resolve-hits", new SequenceContext(red, [blue], [rifle], null)),
                Step(resolve, "roll-to-block", Roll("defense", 4, [.. Enumerable.Repeat((5, false), ap4Dice)])),
                new SequenceFinished(resolve),
                new SequenceFinished(attack),
            ];
            var run = new SuiteGameRun(game, new RunOutcome.RoundLimit(4), [.. log.Select((p, i) => new GameEvent(i + 1, null, p, null))],
                new EventApplier(compiled.Board, compiled.Info).Projection, []);
            return game.Checks.Single(c => c.Name.StartsWith("p.14 Rending")).Failure(run);
        }

        Assert.Null(Judge(2));
        Assert.Contains("2 sixes to hit but 1 defense dice at AP(4)", Judge(1));
    }

    // Whole games in process: E2E for the unit suite's time, as `dotnet run --project sim -- suite` plays them all.
    [Trait("Category", "E2E")]
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

/// <summary>The same games over the socket, against the in-process game server over the Docker Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "E2E")]
public class SpecialGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = GameServerFactory.OverPostgres(pg.Db);

    public void Dispose() => _factory.Dispose();

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    [Fact]
    public async Task The_special_games_pass_over_the_socket_but_for_their_known_defects()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(ScenarioCompiler.CoreDocuments[RuleSets.Gf351], Ct);
        var socket = new TestPool(pg).Socket(() => new FactoryConnector(_factory));

        var report = await new SuiteRunner(SpecialGamesTests.Games, socket).RunAsync(new SuiteOptions([SuiteCatalog.SpecialGroup], DriverKind.Socket, false, TempDir()), null, Ct);

        SpecialGamesTests.AssertPassesButForTheBlocked(report);
    }
}
