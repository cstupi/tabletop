using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>Fired and Coverage over hand-built logs: they need nothing from the server but the log's shape.</summary>
public class FiredAndCoverageTests
{
    static readonly Guid Frame = Guid.CreateVersion7();

    static GameEvent Step(long seq, IReadOnlyList<string>? applied = null, params RollModifier[] modifiers) =>
        new(seq, null, new StepCompleted(Frame, "step", null,
            modifiers.Length == 0 ? [] : [new RollRecord(Guid.CreateVersion7(), "defense", 4, modifiers, [new Die(3, 2, false)])], [], applied), null);

    static readonly CompiledScenario Stalemate = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));

    static SuiteGameRun Run(string name, IReadOnlyList<string> covers, params GameEvent[] log) =>
        new(new TestGame(name, "core", Stalemate.Scenario, covers, [], []), new RunOutcome.RoundLimit(1), log,
            new EventApplier(Stalemate.Board, Stalemate.Info).Projection, []);

    [Fact]
    public void Fired_reads_applied_and_roll_modifier_sources()
    {
        GameEvent[] log = [Step(1, applied: ["Furious"]), Step(2, null, new RollModifier("AP", -1), new RollModifier("cover", 1))];
        Assert.True(Fired.In(log, "Furious"));
        Assert.True(Fired.In(log, "AP"));
        Assert.False(Fired.In(log, "Relentless"));
        Assert.False(Fired.In([Step(1)], "Furious"));     // a step no entry changed: Applied null
    }

    [Fact]
    public void An_undone_step_does_not_count_as_fired()
    {
        GameEvent[] log = [Step(1), Step(2, applied: ["Furious"], new RollModifier("AP", -1)), new(3, null, new Undone(1), null), Step(4)];
        Assert.False(Fired.In(log, "Furious"));
        Assert.False(Fired.In(log, "AP"));
        Assert.True(Fired.In([.. log, Step(5, applied: ["Furious"])], "Furious"));   // fired again after the undo: that one counts
    }

    [Fact]
    public void Coverage_lists_every_catalog_entry_on_a_full_run_and_marks_the_uncovered()
    {
        var set = ScenarioCompiler.CoreProperties;
        var runs = new[]
        {
            Run("a", ["AP", "Furious"], Step(1, applied: ["Furious"])),   // AP covered, never fired
            Run("b", ["Furious"], Step(1, applied: ["Furious"])),
        };
        var coverage = Coverage.Of(set, runs, fullRun: true);

        Assert.Equal(set.Entries.Count, coverage.Count);
        var furious = coverage.Single(c => c.Name == "Furious");
        Assert.Equal(["a", "b"], furious.Games);
        Assert.Equal((true, "rule"), (furious.Fired, furious.Kind));
        var ap = coverage.Single(c => c.Name == "AP");
        Assert.Equal(["a"], ap.Games);
        Assert.False(ap.Fired);
        Assert.True(coverage.Single(c => c.Name == "Relentless").Uncovered);
        Assert.False(Coverage.Complete(coverage));

        // Spells are entries too, and kind spell.
        var spell = set.Entries.First(e => e.Kind == CatalogKind.Spell).Name;
        Assert.Equal("spell", coverage.Single(c => c.Name == spell).Kind);
    }

    [Fact]
    public void Coverage_of_two_sets_reports_both_each_from_its_own_editions_games()
    {
        var a = Run("a", ["Furious"], Step(1, applied: ["Furious"]));
        var b = Run("b", ["Furious", "AP"], Step(1, applied: ["Furious"]));
        b = b with { Game = b.Game with { Edition = RuleSets.Gf360 } };
        var coverage = SuiteRunner.CoverageOf([a, b], fullRun: true, passed: new HashSet<string> { "a", "b" });

        var (v351, v360) = (ScenarioCompiler.CorePropertiesOf(RuleSets.Gf351), ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360));
        Assert.Equal(v351.Entries.Count, coverage.Count(c => c.Edition == RuleSets.Gf351));
        Assert.Equal(v360.Entries.Count, coverage.Count(c => c.Edition == RuleSets.Gf360));
        Assert.Equal([(RuleSets.Gf351, "a"), (RuleSets.Gf360, "b")],
            coverage.Where(c => c.Name == "Furious").Select(c => (c.Edition, Assert.Single(c.Games))));
        Assert.True(coverage.Single(c => c.Name == "AP" && c.Edition == RuleSets.Gf351).Uncovered);   // a 3.6 game never covers a 3.5.1 entry
        Assert.False(coverage.Single(c => c.Name == "AP" && c.Edition == RuleSets.Gf360).Uncovered);
        Assert.False(Coverage.Complete(coverage));

        // A limited run lists only what its games cover, under each game's edition.
        var limited = SuiteRunner.CoverageOf([b], fullRun: false, passed: new HashSet<string> { "b" });
        Assert.Equal(["AP", "Furious"], limited.Select(c => c.Name).Order());
        Assert.All(limited, c => Assert.Equal(RuleSets.Gf360, c.Edition));
        Assert.All(coverage, c => Assert.Equal(ScenarioCompiler.CorePropertiesOf(c.Edition).Name, c.RuleSet));
    }

    /// <summary>
    /// The catalog marks no entry as having no effect in play: an entry with no effect rows (Tough, Fear, Counter-Attack …) is a name
    /// the ruleset's hooks check for, so it counts against coverage like any other.
    /// </summary>
    [Fact]
    public void Hook_rules_with_no_effect_rows_count_against_coverage()
    {
        var set = ScenarioCompiler.CoreProperties;
        Assert.Empty(set.Entry("Tough")!.Effects);
        Assert.True(Coverage.Of(set, [], fullRun: true).Single(c => c.Name == "Tough").Uncovered);
    }

    [Fact]
    public void Only_runs_do_not_fail_on_coverage()
    {
        var coverage = Coverage.Of(ScenarioCompiler.CoreProperties, [Run("a", ["AP"], Step(1, null, new RollModifier("AP", -1)))], fullRun: false);
        Assert.Equal(["AP"], coverage.Select(c => c.Name));     // only what the selected games cover
        Assert.True(Coverage.Complete(coverage));
    }

    [Fact]
    public void FiredAll_names_each_covered_entry_that_never_fired()
    {
        var run = Run("a", ["AP", "Furious", "Relentless"], Step(1, applied: ["Furious"]));
        Assert.Equal(["never fired: AP", "never fired: Relentless"], Checks.FiredAll().FailuresOf(run));      // one reason per entry
        Assert.Empty(Checks.FiredAll().FailuresOf(Run("b", ["Furious"], Step(1, applied: ["Furious"]))));
    }

    /// <summary>A rule that acts where no step completes (a scheduler or legality rule) is shown to fire by the game's own evidence.</summary>
    [Fact]
    public void A_games_evidence_stands_in_for_fired_for_the_entries_it_names()
    {
        GameEvent[] log = [new(1, null, new AskOpened(new Ask(Guid.CreateVersion7(), "deploy_scouts", "", [], new Confirmation(), false, false, null, null)), null)];
        var run = Run("a", ["Scout", "Furious"], log);
        run = run with { Game = run.Game with { Evidence = new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
        {
            ["Scout"] = l => l.Any(e => e.Payload is AskOpened { Ask.Question: "deploy_scouts" }),
        } } };

        Assert.False(Fired.In(log, "Scout"));
        Assert.True(run.Game.FiredIn(log, "Scout"));
        Assert.Equal(["never fired: Furious"], Checks.FiredAll().FailuresOf(run));        // an entry without evidence still reads Fired.In
        var coverage = Coverage.Of(ScenarioCompiler.CoreProperties, [run], fullRun: false);
        Assert.True(coverage.Single(c => c.Name == "Scout").Fired);
        Assert.False(coverage.Single(c => c.Name == "Furious").Fired);

        // The evidence reads the effective log: an undone deploy_scouts is no evidence.
        Assert.False(run.Game.FiredIn([new(0, null, new Undone(0), null), .. log, new(2, null, new Undone(0), null)], "Scout"));
    }

    [Fact]
    public void Ended_and_invariants_read_the_outcome_and_the_invariant_failures()
    {
        var run = Run("a", []);
        Assert.Null(Checks.Ended().Failure(run));
        Assert.NotNull(Checks.Ended().Failure(run with { Outcome = new RunOutcome.Stuck(StuckReason.NoAskNoEvents, "x") }));
        Assert.Null(Checks.Invariants().Failure(run));
        Assert.Equal("one; two", Checks.Invariants().Failure(run with { InvariantFailures = ["one", "two"] }));
        Assert.Equal("seen 1", Checks.Event("p.8", log => $"seen {log.Count}").Failure(run with { EffectiveLog = [Step(1)] }));
    }
}
