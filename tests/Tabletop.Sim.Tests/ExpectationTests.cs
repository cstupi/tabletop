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

    [Fact]
    public async Task Warnings_counts_warned_events_of_a_kind()
    {
        var failures = await Check(new Expectation.Warnings("round_started", 1));
        Assert.Contains(failures, f => f.Contains("expected 1 warned 'round_started' events, saw 0"));
        Assert.Empty(await Check(new Expectation.Warnings("round_started", 0)));
    }

    [Fact]
    public async Task Won_needs_a_game_ended_naming_exactly_that_team()
    {
        var failures = await Check(new Expectation.Won(1));                              // the known run is a capped stalemate: no game_ended
        Assert.Contains(failures, f => f.Contains("expected team 1 to win"));
    }

    /// <summary>The objectives board played by the uniform policy, not the catalog's goal-seeking one: seed 3 is then a game nobody seizes in.</summary>
    static async Task<(CompiledScenario Compiled, Projection Projection, IReadOnlyList<GameEvent> Log, RunOutcome Outcome)> Objectives(ulong seed)
    {
        var scenario = ScenarioCatalog.Get("objectives").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, Ct);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), HandPlacement.ChooserFor(seed));
        var report = await loop.RunAsync(Ct);
        return (compiled, await driver.ProjectionAsync(Ct), loop.Log.ToList(), report.Outcome);
    }

    [Theory]
    [InlineData(2UL)]   // team 2 holds one marker at the end: a win
    [InlineData(3UL)]   // nobody holds a marker: every team tied at none
    public async Task MostMarkersWin_holds_for_the_engines_own_end_win_or_tie(ulong seed)
    {
        var (compiled, projection, log, outcome) = await Objectives(seed);
        Assert.Empty(Expectations.Check([new Expectation.MostMarkersWin()], projection, log, outcome, compiled));
    }

    [Fact]
    public async Task MostMarkersWin_fails_when_game_ended_names_other_teams_than_hold_the_most_markers()
    {
        var (compiled, projection, log, outcome) = await Objectives(2);
        Assert.Single(projection.Control);                                              // seed 2: team 2 holds one marker
        var team1 = compiled.Info.Teams.Single(t => t.Ordinal == 1).Id;
        var team2 = compiled.Info.Teams.Single(t => t.Ordinal == 2).Id;
        foreach (var winners in new[] { [team1], [team1, team2], Array.Empty<Guid>() })
        {
            var forged = log.Append(new GameEvent(log[^1].Seq + 1, null, new GameEnded("forged", winners), null)).ToList();
            var failures = Expectations.Check([new Expectation.MostMarkersWin()], projection, forged, outcome, compiled);
            Assert.Contains(failures, f => f.Contains("expected winners [2]"));
        }
        var (stalemate, stalemateProjection, stalemateLog, stalemateOutcome) = await Known();  // capped: no game_ended
        Assert.Contains(Expectations.Check([new Expectation.MostMarkersWin()], stalemateProjection, stalemateLog, stalemateOutcome, stalemate),
            f => f.Contains("no game_ended"));
    }

    [Fact]
    public async Task MostMarkersWin_counts_only_objective_markers()
    {
        var (compiled, _, log, outcome) = await Objectives(2);                         // seed 2: team 2 holds one marker and wins
        var applier = new EventApplier(compiled.Board, compiled.Info);
        foreach (var e in log) applier.Apply(e);
        var team1 = compiled.Info.Teams.Single(t => t.Ordinal == 1).Id;
        var seq = log[^1].Seq;
        foreach (var stray in new[] { Guid.CreateVersion7(), Guid.CreateVersion7() })     // control of ids that name no objective marker
            applier.Apply(new GameEvent(++seq, null, new ObjectiveControlSet(stray, team1), null));
        Assert.Equal(3, applier.Projection.Control.Count);
        Assert.Empty(Expectations.Check([new Expectation.MostMarkersWin()], applier.Projection, log, outcome, compiled));
    }

    [Fact]
    public async Task A_tie_is_recorded_without_a_winner_and_a_win_names_the_team()
    {
        var scenario = ScenarioCatalog.Get("stalemate");
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, Ct);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(1))), HandPlacement.ChooserFor(1));
        var report = await loop.RunAsync(Ct) with { Failures = [] };
        var (one, two) = (compiled.TeamIds[0], compiled.TeamIds[1]);

        var tie = loop.BuildRecording(report with { Outcome = new RunOutcome.Ended(4, [one, two]) }).Meta;
        Assert.Equal("tied between Team 1, Team 2", tie.OutcomeDetail);
        Assert.Null(tie.WinningTeamId);
        var win = loop.BuildRecording(report with { Outcome = new RunOutcome.Ended(4, [two]) }).Meta;
        Assert.Equal(two, win.WinningTeamId);
        var stray = Guid.CreateVersion7();
        Assert.Contains(stray.ToString("N")[..8], loop.BuildRecording(report with { Outcome = new RunOutcome.Ended(4, [one, stray]) }).Meta.OutcomeDetail);

        var json = System.Text.Json.JsonDocument.Parse(Summary.Of("in-process", [report with { Outcome = new RunOutcome.Ended(4, [one, two]) }]).ToJson());
        var ended = json.RootElement.GetProperty("reports")[0].GetProperty("outcome");
        Assert.Equal("ended", ended.GetProperty("kind").GetString());
        Assert.Equal(4, ended.GetProperty("rounds").GetInt32());
        Assert.Equal(2, ended.GetProperty("winners").GetArrayLength());
    }
}
