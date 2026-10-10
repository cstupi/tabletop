using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

public class PatternCoverageTests
{
    static readonly PropertySet Set = PropertiesJson.ToSet(PropertiesJson.Parse("""
    { "name": "S", "gameRules": "grimdark-future", "rolls": [ "hit" ],
      "entries": [
        { "name": "Stealth", "kind": "model", "aggregation": "all", "ruleText": "S.", "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1 } ] },
        { "name": "Stealth Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Stealth.", "effects": [ { "subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Stealth" } ] },
        { "name": "Banner", "kind": "model", "aggregation": "any", "ruleText": "", "played": false } ] }
    """), Guid.CreateVersion7);
    static readonly IReadOnlySet<string> Generated = new HashSet<string> { "Stealth Aura" };
    static readonly CompiledScenario Stalemate = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
    static readonly Guid Frame = Guid.CreateVersion7();

    static GameEvent Shot(string source) => new(1, null, new StepCompleted(Frame, "roll-to-hit", null,
        [new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier(source, -1)], [new Die(4, 3, false)])], []), null);

    static SuiteGameRun Run(string name, IReadOnlyList<string> covers, string? pattern, params GameEvent[] log) =>
        new(new TestGame(name, "army:x", Stalemate.Scenario, covers, [], []) { Pattern = pattern }, new RunOutcome.RoundLimit(1), log,
            new EventApplier(Stalemate.Board, Stalemate.Info).Projection, []);

    [Fact]
    public void A_text_only_entry_is_listed_on_a_full_run_and_never_uncovered()
    {
        var coverage = Coverage.Of(Set, [], fullRun: true, Generated);
        var banner = coverage.Single(c => c.Name == "Banner");
        Assert.Equal((CoverageState.NotAutomated, false, false), (banner.State, banner.Counts, banner.Uncovered));
        Assert.DoesNotContain(Coverage.Of(Set, [], fullRun: false, Generated), c => c.Name == "Banner");
    }

    [Fact]
    public void A_wrapper_is_covered_by_its_patterns_passing_game_when_its_base_is_covered()
    {
        var runs = new[] { Run("stealth", ["Stealth"], null, Shot("Stealth")), Run("aura", ["Stealth Aura"], "Aura", Shot("Stealth")) };
        var aura = Coverage.Of(Set, runs, fullRun: true, Generated, new HashSet<string> { "stealth", "aura" }).Single(c => c.Name == "Stealth Aura");
        Assert.Equal((CoverageState.Pattern, false, true), (aura.State, aura.Uncovered, aura.Fired));
        Assert.Equal(["aura"], aura.Games);
        Assert.True(Coverage.Complete(Coverage.Of(Set, runs, fullRun: true, Generated)));
    }

    [Fact]
    public void A_wrapper_is_uncovered_without_its_patterns_game_or_its_base_and_unfired_when_the_game_failed()
    {
        var stealth = Run("stealth", ["Stealth"], null, Shot("Stealth"));
        var game = Run("aura", ["Stealth Aura"], "Aura", Shot("Stealth"));
        Assert.True(Coverage.Of(Set, [stealth], fullRun: true, Generated).Single(c => c.Name == "Stealth Aura").Uncovered);
        Assert.True(Coverage.Of(Set, [game with { Game = game.Game with { Covers = [] } } , Run("other", ["Stealth Aura"], "Buff")], fullRun: true, Generated)
            .Single(c => c.Name == "Stealth Aura").Uncovered);                                                         // Stealth uncovered, no Aura game for the Buff game
        var failed = Coverage.Of(Set, [stealth, game], fullRun: true, Generated, new HashSet<string> { "stealth" }).Single(c => c.Name == "Stealth Aura");
        Assert.Equal((false, false), (failed.Uncovered, failed.Fired));
    }

    [Fact]
    public void The_report_shows_covered_by_pattern_and_lists_the_not_automated_names()
    {
        var runs = new[] { Run("stealth", ["Stealth"], null, Shot("Stealth")), Run("aura", ["Stealth Aura"], "Aura", Shot("Stealth")) };
        var coverage = Coverage.Of(Set, runs, fullRun: true, Generated);
        var table = new SuiteReport(Guid.CreateVersion7(), [], coverage, true).Table();
        Assert.Contains("  rule   Stealth Aura  covered by pattern  (aura)", table);
        Assert.Contains("not automated (1): Banner", table);
        Assert.Contains("coverage (2/2 covered, 1 by pattern, 0 covered but not fired; 1 not automated)", table);
    }

    [Fact]
    public void The_generated_part_is_read_from_the_server_assembly()
    {
        Assert.Null(PropertiesSeeder.EmbeddedPart("no-such.part.json"));
        Assert.NotNull(PropertiesSeeder.EmbeddedPart("grimdark-future-army-rules.part.json"));
    }
}
