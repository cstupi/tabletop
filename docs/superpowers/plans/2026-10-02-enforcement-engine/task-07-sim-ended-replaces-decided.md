# Task 7: `sim/`: a wipe-out is not a win

**Files:**
- Modify: `sim/Run/RunOutcome.cs`, `sim/Run/RunLoop.cs`, `sim/Run/RunLoop.Recording.cs`, `sim/Run/RunReport.cs`, `sim/Summary.cs`, `sim/Invariants/ProjectionComparer.cs`, `sim/Recording/Recording.cs` (doc comment)
- Modify: `viewer/tests/feed.test.ts` (the fixture's outcome string)
- Test: `tests/Tabletop.Sim.Tests/RunLoopFailureTests.cs`, `RunLoopTests.cs`, `StepAndWaitTests.cs`, `DriverAgreementTests.cs`, `ExpectationTests.cs`, `InvariantTests.cs`

**Interfaces:**
- Consumes: `Projection.Ended`, `GameEnded` (Task 1); the round-4 end under `strict` (Task 5); `ScenarioRows.EndGameAsync` (Task 6).
- Produces: `RunOutcome.Ended(int Round, IReadOnlyList<Guid> Winners)`, kind `ended`; `RunOutcome.Decided` removed; `RunReport.Passed` true for `Ended`, `RoundLimit`, `Paused` with no failures; `RecordingMeta.Outcome` writes `ended`; `RecordingMeta.WinningTeamId` is the sole winner or null; `RecordingMeta.OutcomeDetail` is `"tied between Team 1, Team 2"` for a tie; `Summary`'s outcome JSON carries `rounds` and `winners` for `ended`; `ProjectionComparer` compares `Ended`.

Spec: enforcement spec → *sim*. p.6: "unless stated otherwise, players don't ever win the game by fully destroying their opponent's army." The wipe check goes; a run with one army gone plays on, because the empty team has no legal turn and the boundary question still reaches every team with a participant. Under `strict` every run that is not stuck or failed ends `ended` at round 4; under `warn` and `silent` runs end `round_limit` as today.

- [ ] **Step 1: Write the failing tests**

In `tests/Tabletop.Sim.Tests/RunLoopFailureTests.cs`, replace `One_team_left_with_models_after_round_one_is_decided` with:

```csharp
    [Fact]
    public async Task A_game_ended_event_is_the_run_ending_with_its_winners()
    {
        var driver = new ScriptedDriver(Compiled, d =>
        {
            var e = d.Emit(new GameEnded("4 rounds have been played", [d.FirstTeam]));
            return Task.FromResult(new StepResult([e], [], false));
        });
        var report = await Play(driver);
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        var ended = Assert.IsType<RunOutcome.Ended>(report.Outcome);
        Assert.Equal([driver.FirstTeam], ended.Winners);
        Assert.Equal("ended", ended.Kind);
    }

    [Fact]
    public async Task One_team_left_with_models_is_not_an_end()
    {
        var driver = new ScriptedDriver(Compiled, async d =>
        {
            var projection = await d.ProjectionAsync(Ct);
            var moves = projection.Models.Values
                .Where(m => Compiled.TeamOfParticipant[projection.Units[m.UnitId].ParticipantId] == d.FirstTeam)
                .Select(m => new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0)))))
                .ToList();
            var e = d.Emit(new RoundStarted(1, [], [new ModelsMoved(moves)]));
            return new StepResult([e], [], false);
        });
        var report = await Play(driver);
        Assert.IsType<RunOutcome.Stuck>(report.Outcome);           // past the script nothing happens: the wipe decided nothing
    }
```

In `tests/Tabletop.Sim.Tests/RunLoopTests.cs`:

- In `RunTwice`, replace the `winner` computation with

```csharp
        var winner = report.Outcome is RunOutcome.Ended { Winners.Count: 1 } e
            ? compiled.Info.Teams.Single(t => t.Id == e.Winners[0]).Ordinal
            : (int?)null;
```

  and in the comment above it read `RunOutcome.Ended carries team *ids*`.
- Every `Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);` becomes `Assert.Contains(report.Outcome.Kind, (string[])["ended", "round_limit"]);`, and the `meta.Outcome` one likewise.
- Add:

```csharp
    [Fact]
    public async Task A_default_strict_skirmish_ends_after_four_rounds()
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(1);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(1))), HandPlacement.ChooserFor(1));
        var report = await loop.RunAsync(CancellationToken.None);
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Equal((4, "ended"), (report.Rounds, report.Outcome.Kind));
        Assert.Equal(1, loop.Log.Count(e => e.Payload is GameEnded));
        Assert.Equal(GameStatus.Ended, await driver.Session.RunAsync(() => Task.FromResult(driver.Session.Info.Status)));   // ScenarioRows took the row write
    }
```

In `tests/Tabletop.Sim.Tests/StepAndWaitTests.cs` and `DriverAgreementTests.cs`: `"decided"` becomes `"ended"` in each `(string[])[...]`, and `report.Outcome is RunOutcome.Decided or RunOutcome.RoundLimit` becomes `report.Outcome is RunOutcome.Ended or RunOutcome.RoundLimit`.

In `tests/Tabletop.Sim.Tests/ExpectationTests.cs`: `new Expectation.Outcome(outcome.Kind == "decided" ? "round_limit" : "decided")` becomes `new Expectation.Outcome(outcome.Kind == "ended" ? "round_limit" : "ended")`.

In `tests/Tabletop.Sim.Tests/InvariantTests.cs`, add a comparer case:

```csharp
    [Fact]
    public void The_comparer_sees_a_difference_in_the_end()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
        var ended = new EventApplier(c.Board, c.Info);
        ended.Apply(new GameEvent(1, null, new GameEnded("over", [c.TeamIds[0]]), null));
        var open = new EventApplier(c.Board, c.Info);
        open.Apply(new GameEvent(1, null, new ScoreSet(c.TeamIds[0], 0), null));
        Assert.Contains(ProjectionComparer.Differences(ended.Projection, open.Projection), d => d.StartsWith("Ended"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Sim.Tests`
Expected: compile error, `RunOutcome.Ended` does not exist.

- [ ] **Step 3: The outcome**

`sim/Run/RunOutcome.cs`: replace the summary and the records' `Decided` line, and the `Kind` arm:

```csharp
/// <summary>
/// Five ends, as spec → What a full game means tabulates. Ended (the game logged game_ended, with its winners), RoundLimit and Stuck
/// are the game ending; Failed is an assertion breach — an unscripted Ask in a script-only run, a failed expectation or a failed
/// invariant — and Paused is the step-and-wait seam. A wipe-out is not an end (p.6).
/// </summary>
public abstract record RunOutcome
{
    /// <summary>One winner is a win, several a tie, none no result.</summary>
    public sealed record Ended(int Round, IReadOnlyList<Guid> Winners) : RunOutcome;
    public sealed record RoundLimit(int Rounds) : RunOutcome;
    public sealed record Stuck(StuckReason Reason, string Detail) : RunOutcome;
    public sealed record Failed(string Detail) : RunOutcome;
    /// <summary>StepAndWait: the loop stopped with this Ask open and answered nothing (Task 9).</summary>
    public sealed record Paused(Guid AskId, string Question) : RunOutcome;

    public string Kind => this switch
    {
        Ended => "ended",
        RoundLimit => "round_limit",
        Stuck => "stuck",
        Paused => "paused",
        _ => "failed",
    };
}
```

`sim/Run/RunReport.cs`: `public bool Passed => Failures.Count == 0 && Outcome is RunOutcome.Ended or RunOutcome.RoundLimit or RunOutcome.Paused;`

- [ ] **Step 4: The loop reads the end, not the wipe**

In `sim/Run/RunLoop.cs`, `DriveAsync`, replace

```csharp
            if (Decided(projection) is { } winner) return new RunOutcome.Decided(winner);
```

with

```csharp
            if (projection.Ended is { } end) return new RunOutcome.Ended(projection.Scheduler.Round, end.Winners);
```

and delete the private `Decided(Projection)` method. Update the class summary's first sentence to "Drive a game to one of the three ends spec → What a full game means lists (ended, round-capped, stuck)".

- [ ] **Step 5: The recording and the summary**

`sim/Run/RunLoop.Recording.cs`: make `OutcomeOf` an instance method and teach it the end:

```csharp
    private (string Kind, string? Detail) OutcomeOf(RunReport report) => report switch
    {
        { Failures.Count: > 0, Outcome: RunOutcome.Ended or RunOutcome.RoundLimit } => ("failed", report.Failures[0]),
        { Outcome: RunOutcome.Ended { Winners.Count: > 1 } e } => (report.Outcome.Kind,
            $"tied between {string.Join(", ", e.Winners.Select(w => $"Team {scenario.Info.Teams.Single(t => t.Id == w).Ordinal}"))}"),
        _ => (report.Outcome.Kind, report.Outcome switch { RunOutcome.Stuck s => s.Detail, RunOutcome.Failed f => f.Detail, _ => null }),
    };
```

and in `BuildRecording` replace `report.Outcome is RunOutcome.Decided d ? d.WinningTeamId : null,` with

```csharp
        report.Outcome is RunOutcome.Ended { Winners.Count: 1 } e ? e.Winners[0] : null,
```

`sim/Summary.cs`, `OutcomeJson`: replace the `Guid? WinningTeamId = null` parameter with `IReadOnlyList<Guid>? Winners = null` and the `Decided` arm with

```csharp
            RunOutcome.Ended e => new(outcome.Kind, Rounds: e.Round, Winners: e.Winners),
```

`sim/Recording/Recording.cs`: in `RecordingMeta`'s `Outcome` doc comment, the finished-run values read `` `ended`, `round_limit`, `stuck` or `failed` ``, and `WinningTeamId` is documented as "the sole winner of an `ended` run; null for a tie, no result, or any other outcome".

- [ ] **Step 6: The comparer sees the end**

In `sim/Invariants/ProjectionComparer.cs`, after the `Scores` check:

```csharp
        static string? EndOf(Projection p) => p.Ended is { } e ? $"{e.Reason}|{string.Join(",", e.Winners)}" : null;
        Check("Ended", EndOf(expected), EndOf(actual));
```

- [ ] **Step 7: The viewer fixture's outcome**

In `viewer/tests/feed.test.ts`, the inline recording's `outcome: 'decided'` becomes `outcome: 'ended'`. Nothing in the viewer reads the value.

- [ ] **Step 8: Run the suites**

Run: `docker compose up -d && dotnet test tests/Tabletop.Sim.Tests && dotnet test tests/Tabletop.Viewer.Tests && cd viewer && npm test`
Expected: PASS. `DriverAgreementTests` (Postgres, the socket driver against a hosted game server) passes too: the socket server's `PostgresGameRows.EndGameAsync` writes the row the scenario seeded.

- [ ] **Step 9: Commit**

```bash
git add sim tests/Tabletop.Sim.Tests viewer/tests/feed.test.ts
git commit -m "End a sim run on game_ended with its winners; a wipe-out is not a win"
```
