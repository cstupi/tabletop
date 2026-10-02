using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class StepAndWaitTests
{
    static CancellationToken Ct => CancellationToken.None;

    static async Task<(CompiledScenario, InProcessDriver, RunLoop)> Paused(ulong seed = 31)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        var driver = await InProcessDriver.StartAsync(compiled, Ct);
        driver.Mode = DriverMode.StepAndWait;
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(seed))), HandPlacement.ChooserFor(seed));
        return (compiled, driver, loop);
    }

    [Fact]
    public async Task Step_and_wait_stops_at_the_roll_off_gate_with_the_ask_open_and_answers_nothing()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        var report = await loop.RunAsync(Ct);
        var paused = Assert.IsType<RunOutcome.Paused>(report.Outcome);
        Assert.Equal(AskQuestions.Confirm, paused.Question);
        Assert.Equal(0, report.AsksAnswered);
        var open = Assert.Single(await driver.OpenAsksAsync(Ct));
        Assert.Equal(paused.AskId, open.Ask.Id);
        Assert.Equal(2, open.Ask.Teams.Count);
        Assert.Empty(open.Answers);
        Assert.True(report.Passed, string.Join("\n", report.Failures));   // a pause is a stop, not a failure
    }

    [Fact]
    public async Task Resuming_in_run_mode_finishes_the_game()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        await loop.RunAsync(Ct);
        driver.Mode = DriverMode.Run;
        var report = await loop.ResumeAsync(Ct);
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.AsksAnswered > 10);
    }

    [Fact]
    public async Task Answering_the_paused_ask_from_outside_advances_one_decision_and_pauses_again()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        var first = Assert.IsType<RunOutcome.Paused>((await loop.RunAsync(Ct)).Outcome);
        var gate = (await driver.OpenAsksAsync(Ct)).Single().Ask;

        var second = Assert.IsType<RunOutcome.Paused>((await loop.AnswerPausedAsync(gate.Teams[0], new Confirmed(), Ct)).Outcome);
        Assert.NotEqual(first.AskId, second.AskId);
        Assert.Equal(AskQuestions.Deploy, second.Question);

        var ask = (await driver.OpenAsksAsync(Ct)).Single().Ask;
        var unit = Assert.IsType<PieceSelection>(ask.Legal).Eligible[0];
        var report = await loop.AnswerPausedAsync(ask.Teams.Single(), new SelectedPieces([unit]), Ct);
        var third = Assert.IsType<RunOutcome.Paused>(report.Outcome);
        Assert.NotEqual(second.AskId, third.AskId);

        var projection = await driver.ProjectionAsync(Ct);
        Assert.All(projection.Units[unit].Definition.Models, m => Assert.IsType<OnTable>(projection.Models[m.Id].Location));
    }

    [Fact]
    public async Task Stepping_repeatedly_walks_the_whole_game_one_ask_at_a_time()
    {
        var (compiled, driver, loop) = await Paused(32);
        await using var _ = driver;
        var report = await loop.RunAsync(Ct);
        var steps = 0;
        while (report.Outcome is RunOutcome.Paused && steps++ < 2000)
        {
            var ask = (await driver.OpenAsksAsync(Ct)).First(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t)));
            var team = ask.Ask.Teams.First(t => !ask.Answers.ContainsKey(t));
            var view = new PolicyView(await driver.ProjectionAsync(Ct), driver.Board, null, driver.Info, compiled.Properties);
            report = await loop.AnswerPausedAsync(team, new AnswerPolicy(new Chooser((ulong)steps)).Answer(ask.Ask, view), Ct);
        }
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
    }

    [Fact]
    public async Task An_external_answer_is_recorded_once_and_consumes_its_ordinal()
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(33);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, Ct);
        driver.Mode = DriverMode.StepAndWait;
        var script = AnswerScript.Build().At(AskQuestions.Confirm, 1, new Confirmed()).Build();
        var answers = new ComposedAnswers(script, new AnswerPolicy(new Chooser(33)));
        var loop = new RunLoop(driver, compiled, answers, HandPlacement.ChooserFor(33));
        await loop.RunAsync(Ct);
        var gate = (await driver.OpenAsksAsync(Ct)).Single().Ask;

        var report = await loop.AnswerPausedAsync(gate.Teams[0], new Confirmed(), Ct);

        Assert.Equal(1, report.AsksAnswered);
        var recorded = Assert.Single(answers.History);
        Assert.Equal((AskQuestions.Confirm, 0, false), (recorded.Question, recorded.Ordinal, recorded.FromScript));

        // the next confirm Ask is ordinal 1, so the scripted entry is the one that applies
        var next = answers.Answer(gate, new PolicyView(await driver.ProjectionAsync(Ct), driver.Board, null, driver.Info, compiled.Properties));
        Assert.IsType<Confirmed>(next);
        Assert.Equal((1, true), (answers.History[1].Ordinal, answers.History[1].FromScript));
    }
}
