# Task 9: The step-and-wait seam

**Files:**
- Modify: `sim/Run/RunLoop.cs` (`ResumeAsync`, and the pause is already `RunOutcome.Paused`)
- Test: `tests/Tabletop.Sim.Tests/StepAndWaitTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `DriverMode` (Task 4); `RunLoop`, `RunOutcome`, `RunReport` (Task 6); `Ask`, `AskAnswer`, `Projection` (Protocol).
- Produces: on `RunLoop` — `Task<RunReport> ResumeAsync(CancellationToken ct)` (continue from a pause under the current `Mode`) and `Task<RunReport> AnswerPausedAsync(Guid teamId, AskAnswer answer, CancellationToken ct)` (answer the Ask the run paused on, from outside the answer source, and pause again).

Spec: spec → *Interactive debugging is a door, not a room* — "Playback is the whole of the first version. The viewer sends no command and answers no Ask. The design leaves the seam for it: … the driver interface has a step-and-wait mode … None of that is built yet." Module prompt item 8: "Build the seam and the mode. Do not build a control channel or anything in a browser."

**What the seam is and is not.** `DriverMode.StepAndWait` makes the loop stop at the first Ask it would have answered and report `RunOutcome.Paused`, having answered nothing. `ResumeAsync` continues; `AnswerPausedAsync` supplies one answer from outside. That is the whole seam: a caller with a socket, an HTTP endpoint or a REPL can drive a paused game one decision at a time. There is no control channel, no server, no browser and no viewer code here.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class StepAndWaitTests
{
    static async Task<(CompiledScenario, InProcessDriver, RunLoop)> Paused(ulong seed = 31)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        driver.Mode = DriverMode.StepAndWait;
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        return (compiled, driver, loop);
    }

    [Fact]
    public async Task Step_and_wait_stops_with_the_ask_open_and_answers_nothing()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
        var paused = Assert.IsType<RunOutcome.Paused>(report.Outcome);
        Assert.Equal(AskQuestions.Deploy, paused.Question);
        Assert.Equal(0, report.AsksAnswered);
        var open = Assert.Single(await driver.OpenAsksAsync(TestContext.Current.CancellationToken));
        Assert.Equal(paused.AskId, open.Ask.Id);
        Assert.Empty(open.Answers);
        Assert.True(report.Passed);                      // a pause is a stop, not a failure
    }

    [Fact]
    public async Task Resuming_in_run_mode_finishes_the_game()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        await loop.RunAsync(TestContext.Current.CancellationToken);
        driver.Mode = DriverMode.Run;
        var report = await loop.ResumeAsync(TestContext.Current.CancellationToken);
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.AsksAnswered > 10);
    }

    [Fact]
    public async Task Answering_the_paused_ask_from_outside_advances_one_decision_and_pauses_again()
    {
        var (compiled, driver, loop) = await Paused();
        await using var _ = driver;
        var first = Assert.IsType<RunOutcome.Paused>((await loop.RunAsync(TestContext.Current.CancellationToken)).Outcome);
        var ask = (await driver.OpenAsksAsync(TestContext.Current.CancellationToken)).Single().Ask;
        var unit = Assert.IsType<PieceSelection>(ask.Legal).Eligible[0];

        var report = await loop.AnswerPausedAsync(ask.Teams.Single(), new SelectedPieces([unit]), TestContext.Current.CancellationToken);
        var second = Assert.IsType<RunOutcome.Paused>(report.Outcome);
        Assert.NotEqual(first.AskId, second.AskId);

        var projection = await driver.ProjectionAsync(TestContext.Current.CancellationToken);
        Assert.All(projection.Units[unit].Definition.Models, m => Assert.IsType<OnTable>(projection.Models[m.Id].Location));
    }

    [Fact]
    public async Task Stepping_repeatedly_walks_the_whole_game_one_ask_at_a_time()
    {
        var (_, driver, loop) = await Paused(32);
        await using var _ = driver;
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
        var steps = 0;
        while (report.Outcome is RunOutcome.Paused && steps++ < 2000)
        {
            var ask = (await driver.OpenAsksAsync(TestContext.Current.CancellationToken)).First(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t)));
            var team = ask.Ask.Teams.First(t => !ask.Answers.ContainsKey(t));
            report = await loop.AnswerPausedAsync(team, new AnswerPolicy(new Chooser((ulong)steps)).Answer(ask.Ask,
                new PolicyView(await driver.ProjectionAsync(TestContext.Current.CancellationToken), driver.Board,
                    volumes: null, driver.Info)), TestContext.Current.CancellationToken);
        }
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
    }
}
```

- [ ] **Step 2: Run; expect failure, then add the two methods to `RunLoop`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter StepAndWaitTests`
Expected: `CS1061: 'RunLoop' does not contain a definition for 'ResumeAsync'`.

`RunAsync`'s body already ends in a report; extract the reporting so both entry points share it.

```csharp
    /// <summary>Continue after a pause, under whatever Mode the driver is in now.</summary>
    public Task<RunReport> ResumeAsync(CancellationToken ct) => RunAsync(ct);

    /// <summary>
    /// Answer the Ask the run paused on, from outside the answer source, then continue under the current
    /// Mode — so in StepAndWait this advances exactly one decision. This is the seam the spec leaves for
    /// answering an Ask from a debugger; nothing here is a control channel.
    /// </summary>
    public async Task<RunReport> AnswerPausedAsync(Guid teamId, AskAnswer answer, CancellationToken ct)
    {
        var open = (await driver.OpenAsksAsync(ct)).FirstOrDefault(a => a.Ask.Teams.Contains(teamId) && !a.Answers.ContainsKey(teamId))
            ?? throw new InvalidOperationException($"no ask is open for team {teamId:N}");
        var result = await driver.AnswerAsync(teamId, open.Ask.Id, answer, ct);
        if (result is SubmitResult.Rejected rejected)
            throw new InvalidOperationException($"'{open.Ask.Question}' refused: {rejected.Reason} {rejected.Detail}");
        Absorb(await driver.DrainEventsAsync(ct));

        if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
            foreach (var unitId in selected.PieceIds) await PlaceAsync(teamId, unitId, ct);

        return await RunAsync(ct);
    }
```

`RunAsync` is re-entrant because every piece of its state — the log, the projector, the fingerprint — accumulates rather than resets, so calling it again continues the same run. Reset `_lastAnswered` to `null` at the top of `DriveAsync` so a pause between an answer and its reopening is not mistaken for a hang.

- [ ] **Step 3: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter StepAndWaitTests
git add sim/Run tests/Tabletop.Sim.Tests
git commit -m "Pause with an Ask open, and answer it from outside the answer source"
```
