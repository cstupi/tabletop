using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class ExpectationTests
{
    static CancellationToken Ct => CancellationToken.None;

    // One real stalemate run (three whole rounds), then every expectation kind against what that run produced.
    static async Task<(CompiledScenario Compiled, Projection Projection, IReadOnlyList<GameEvent> Log, RunOutcome Outcome)> Known()
    {
        var scenario = ScenarioCatalog.Get("stalemate");
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, Ct);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(1))), HandPlacement.ChooserFor(1));
        var report = await loop.RunAsync(Ct);
        return (compiled, await driver.ProjectionAsync(Ct), loop.Log.ToList(), report.Outcome);
    }

    static async Task<IReadOnlyList<string>> Check(params Expectation[] expectations)
    {
        var (compiled, projection, log, outcome) = await Known();
        return Expectations.Check(expectations, projection, log, outcome, compiled);
    }

    static readonly string RoundStartedKind = EventKinds.Of(new RoundStarted(1, [], []));

    [Fact]
    public async Task Every_kind_holds_against_the_run_that_satisfies_it()
    {
        var (compiled, projection, _, outcome) = await Known();
        var red = Expectations.ModelsOnTable(projection, compiled, 1);
        var failures = await Check(
            new Expectation.EventOfKind(RoundStartedKind, AtLeast: 1),
            new Expectation.NoEventOfKind("no_such_event_kind"),
            new Expectation.ReachedRound(1),
            new Expectation.ModelsRemaining(1, Expectations.ModelsOnTable(projection, compiled, 1)),
            new Expectation.AskAsked(AskQuestions.EndRound, 1),
            new Expectation.Outcome(outcome.Kind));
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_kind_reports_a_failure_against_the_run_that_does_not_satisfy_it()
    {
        var (compiled, projection, _, outcome) = await Known();
        var survivor = Expectations.ModelsOnTable(projection, compiled, 1) > 0 ? 1 : 2;
        var red = Expectations.ModelsOnTable(projection, compiled, survivor);
        Assert.True(red > 0, "one of the two teams survives a one-on-one that was not a mutual wipe");
        var failures = await Check(
            new Expectation.EventOfKind(RoundStartedKind, AtLeast: 99),
            new Expectation.NoEventOfKind(RoundStartedKind),
            new Expectation.ReachedRound(99),
            new Expectation.TeamWiped(survivor),
            new Expectation.ModelsRemaining(survivor, red + 1),
            new Expectation.AskAsked(AskQuestions.EndRound, 99),
            new Expectation.Outcome(outcome.Kind == "ended" ? "round_limit" : "ended"));
        Assert.Equal(7, failures.Count);
        Assert.Contains(failures, f => f.Contains("expected outcome"));
    }

    [Fact]
    public async Task A_failed_expectation_fails_the_run_and_a_passing_one_does_not()
    {
        foreach (var (expectation, passes) in new (Expectation, bool)[]
                 { (new Expectation.ReachedRound(1), true), (new Expectation.ReachedRound(99), false) })
        {
            var scenario = ScenarioCatalog.Get("stalemate") with { Expectations = [expectation] };
            var compiled = ScenarioCompiler.Compile(scenario);
            await using var driver = await InProcessDriver.StartAsync(compiled, Ct);
            var report = await new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(1))), HandPlacement.ChooserFor(1)).RunAsync(Ct);
            Assert.Equal(passes, report.Passed);
        }
    }

    [Fact]
    public async Task A_run_that_ends_normally_but_fails_an_expectation_is_recorded_as_failed_with_the_first_failure()
    {
        var scenario = ScenarioCatalog.Get("stalemate") with { Expectations = [new Expectation.ReachedRound(99)] };
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, Ct);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(1))), HandPlacement.ChooserFor(1));
        var report = await loop.RunAsync(Ct);
        Assert.NotEqual("failed", report.Outcome.Kind);                 // the loop's own outcome is how the game ended
        var meta = loop.BuildRecording(report).Meta;
        Assert.Equal("failed", meta.Outcome);
        Assert.Equal(report.Failures[0], meta.OutcomeDetail);
        Assert.Contains("expected round 99", meta.OutcomeDetail);
    }
}
