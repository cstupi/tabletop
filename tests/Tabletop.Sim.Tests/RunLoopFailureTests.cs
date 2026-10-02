using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

/// <summary>The run loop's failure paths, against a driver that does exactly what each test scripts. No engine, no Postgres.</summary>
public class RunLoopFailureTests
{
    static CancellationToken Ct => CancellationToken.None;

    static CompiledScenario Compiled { get; } = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));

    static AnswerScript Confirms => AnswerScript.Build().At(AskQuestions.Confirm, 0, new Confirmed()).Build();

    static async Task<RunReport> Play(ScriptedDriver driver, AnswerScript? script = null, bool withPolicy = false)
    {
        var answers = new ComposedAnswers(script ?? AnswerScript.Empty, withPolicy ? new AnswerPolicy(new Chooser(1)) : null);
        return await new RunLoop(driver, Compiled, answers, new Chooser(2)).RunAsync(Ct);
    }

    static void AssertFailed(RunReport report, string kind)
    {
        Assert.False(report.Passed);
        Assert.Equal(kind, report.Outcome.Kind);
        Assert.Contains(report.Failures, f => f.Contains("last frames:"));
    }

    [Fact]
    public async Task No_ask_and_no_events_is_stuck_and_dumps_the_frames()
    {
        var driver = new ScriptedDriver(Compiled, d => Task.FromResult(d.Open(d.Confirmation(AskQuestions.EndRound))),
            _ => Task.FromResult(ScriptedDriver.Nothing));
        // The first Ask is answered by the script, which then leaves nothing open and nothing emitted.
        var report = await Play(driver, AnswerScript.Build().At(AskQuestions.EndRound, 0, new Confirmed()).Build());
        AssertFailed(report, "stuck");
        Assert.Equal(StuckReason.NoAskNoEvents, Assert.IsType<RunOutcome.Stuck>(report.Outcome).Reason);
        Assert.Contains(report.Failures, f => f.Contains("ask_opened"));        // the dump names the frames that did happen
    }

    [Fact]
    public async Task An_answered_ask_that_reopens_unchanged_is_stuck()
    {
        var driver = new ScriptedDriver(Compiled,
            d => Task.FromResult(d.Open(d.Confirmation())),
            d => Task.FromResult(d.Open(d.Confirmation())));            // a new id, the same question, teams and legal set
        var report = await Play(driver, Confirms);
        AssertFailed(report, "stuck");
        Assert.Equal(StuckReason.AskReopenedUnchanged, Assert.IsType<RunOutcome.Stuck>(report.Outcome).Reason);
    }

    [Fact]
    public async Task A_refused_answer_is_stuck_with_the_servers_reason()
    {
        var driver = new ScriptedDriver(Compiled, d => Task.FromResult(d.Open(d.Confirmation())))
        {
            OnSubmit = _ => new SubmitResult.Rejected(RefusalReason.Illegal, "not like that"),
        };
        var report = await Play(driver, Confirms);
        AssertFailed(report, "stuck");
        var stuck = Assert.IsType<RunOutcome.Stuck>(report.Outcome);
        Assert.Equal(StuckReason.SubmitRefused, stuck.Reason);
        Assert.Contains("not like that", stuck.Detail);
    }

    [Fact]
    public async Task An_ask_with_no_legal_answer_and_no_pass_is_stuck()
    {
        var driver = new ScriptedDriver(Compiled,
            d => Task.FromResult(d.Open(d.Confirmation(AskQuestions.Activate, new Options([]), allowPass: false))));
        var report = await Play(driver, withPolicy: true);
        AssertFailed(report, "stuck");
        Assert.Equal(StuckReason.NoLegalAnswer, Assert.IsType<RunOutcome.Stuck>(report.Outcome).Reason);
        Assert.Empty(driver.Submitted);                                  // never narrowed, never guessed
    }

    [Fact]
    public async Task A_rest_timeout_is_a_failed_run_not_a_stuck_game()
    {
        var driver = new ScriptedDriver(Compiled,
            _ => throw new RestTimeoutException(TimeSpan.FromSeconds(30), 7, new IOException("the server closed the socket: InternalServerError boom")));
        var report = await Play(driver);
        AssertFailed(report, "failed");
        Assert.Contains("did not reach rest", ((RunOutcome.Failed)report.Outcome).Detail);
        Assert.Contains("InternalServerError boom", ((RunOutcome.Failed)report.Outcome).Detail);
    }

    [Fact]
    public async Task Any_other_exception_is_this_seeds_failure_with_the_frame_dump()
    {
        var driver = new ScriptedDriver(Compiled,
            d => Task.FromResult(d.Open(d.Confirmation())),
            _ => throw new KeyNotFoundException("a unit the projection lacks"));
        var report = await Play(driver, Confirms);
        AssertFailed(report, "failed");
        Assert.Contains("a unit the projection lacks", ((RunOutcome.Failed)report.Outcome).Detail);
    }

    [Fact]
    public async Task One_team_left_with_models_after_round_one_is_decided()
    {
        var driver = new ScriptedDriver(Compiled, async d =>
        {
            var projection = await d.ProjectionAsync(Ct);
            var winner = d.FirstTeam;
            var moves = projection.Models.Values
                .Where(m => Compiled.TeamOfParticipant[projection.Units[m.UnitId].ParticipantId] == winner)
                .Select(m => new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0)))))
                .ToList();
            var e = d.Emit(new RoundStarted(1, [], [new ModelsMoved(moves)]));
            return new StepResult([e], [], false);
        });
        var report = await Play(driver);
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Equal(driver.FirstTeam, Assert.IsType<RunOutcome.Decided>(report.Outcome).WinningTeamId);
    }

    [Fact]
    public async Task A_closed_recorder_is_a_driver_fault_that_names_the_close()
    {
        await using var driver = await InProcessDriver.StartAsync(ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate")), Ct);
        await driver.Session.DrainAsync("the engine threw in Advance: boom");
        var fault = await Assert.ThrowsAsync<DriverFaultException>(() => driver.DrainEventsAsync(Ct));
        Assert.Contains("boom", fault.Message);
    }
}
