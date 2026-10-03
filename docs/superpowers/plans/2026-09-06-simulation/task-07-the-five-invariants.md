# Task 7: The invariants

**Files:**
- Create: `sim/Invariants/ProjectionComparer.cs`, `sim/Invariants/Invariants.cs`
- Modify: `sim/Run/RunLoop.cs` (run the invariants at the end when `RunSettings.CheckInvariants`)
- Test: `tests/Tabletop.Sim.Tests/InvariantTests.cs`

**Interfaces:**
- Consumes: `Projection`, `UnitInPlay`, `ModelInPlay`, `SchedulerPosition`, `Frame`, `OpenAsk`, `EventApplier`, `GameEvent`, `EventPayload` members, `Location` (`OnTable`, `OnShelf`, `Inside`), `ModelRelocation`, `AskQuestions`, `Board`, `GameInfo`, `Undo` (Protocol); `Replay` (game server skeleton); `Distance`, `TableSize` (Geometry); `IGameDriver`, `InProcessDriver` (Task 4); `CompiledScenario` (Task 1); `RunOutcome` (Task 6).
- Produces: `static class ProjectionComparer` with `IReadOnlyList<string> Differences(Projection expected, Projection actual)`; `sealed record InvariantResult(string Name, bool Held, string? Detail)`; `static class Invariants` (namespace `Tabletop.Sim.Invariants`, so callers outside it write `Invariants.Invariants.CheckAll`) with `IReadOnlyList<InvariantResult> CheckAll(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)`, and one method per invariant — `InvariantResult ReplayReproducesTheProjection(...)`, `InvariantResult UndoAndRedoReproduceTheDice(...)`, `InvariantResult EveryAskAnsweredByAnAddressedTeam(...)`, `InvariantResult NoModelInTwoPlacesOrOffTheSlab(...)`, `InvariantResult TheRoundAdvancedOnlyThroughTheBoundaryAsk(...)`; `static class MidRun` with `void Assert(Projection, IReadOnlyList<GameEvent>, CompiledScenario)` (throws `InvariantViolatedException` with every failure); `sealed class InvariantViolatedException(IReadOnlyList<InvariantResult>) : Exception`.

Spec: spec → *What it asserts*, all five bullets verbatim:
- Replaying the log from the initial state reproduces the final projection.
- Undoing to an arbitrary sequence and redoing reproduces the same dice, because the seed is stored on the game and never changes.
- Every Ask is answered by a team that Ask addressed.
- No model is in two places, and no model on the table is outside the slab.
- The round only advances through the boundary Ask (scheduler design → *The final round is the ruleset's*).

**Each one, as this task computes it.**

1. **Replay.** `Replay.To(board, info, log, log[^1].Seq)` against the driver's own projection, through `ProjectionComparer`. In process the same comparison also runs against `InProcessDriver.ServerProjectionAsync`, which is the stronger form: the server's live projection, the driver's incrementally-applied one, and a cold replay all three agree.
2. **Undo and the dice.** The check reads `EventApplier.EffectiveLog` and not the raw log: an `Undone` already in the log removes dice from the game, and the raw log reports a false failure when an `Undone` is followed by fresh dice. `Undone(N)` shortens the effective log, and the dice are counter-based on `(GameState.RngSeed, cursor)` (interpreter plan Task 5) where `RngSeed` is the value stored on the game row and read at hydration, so the *k*th die of the game is a pure function of that seed and *k*. The check is therefore over the log alone and needs no second run: the effective log after an appended `Undone(N)` must contain the same dice, in the same order, at the same cursor positions as the effective log's first *N* sequences. Doing it for every `N` at which a `step_completed` carrying dice appears covers "an arbitrary sequence" exhaustively rather than by sampling. The invariant therefore checks only the effective-log prefix, and its doc comment says so. The engine-level check is `DriverAgreementTests` (Task 8), which submits `Undo` to the socket driver and re-answers.
3. **Addressed teams.** For each `AskAnswered`, the `AskOpened` with that id must have the answering team in `Ask.Teams`. An `AskClosed` with an empty answer list is a superseded scheduler Ask and is not an answer (scheduler design → *A superseded scheduler Ask closes with no answers*).
4. **One place, on the slab.** Walk the log: for each `ModelRelocation`, `From` must equal the model's location before the event, so no move invents a location; and every `OnTable` position must satisfy `0 ≤ x ≤ width`, `0 ≤ y ≤ length` on the final projection and on every intermediate one.
5. **The round boundary.** For every `RoundStarted` with `Round ≥ 2`, either the nearest preceding `AskClosed` closed an Ask whose question is `AskQuestions.EndRound`, or the `GameEvent.Actor` is non-null — which is the menu's `set_round`, an override that is "visible to opponents and recorded in the event log, never silent" (`architecture.md` → Rules enforcement). Rounds 0 and 1 are deployment's flip into round one and are exempt.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class InvariantTests
{
    static async Task<(CompiledScenario Scenario, RunLoop Loop, RunReport Report, IGameDriver Driver)> Played(ulong seed = 1)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), HandPlacement.ChooserFor(seed));
        var report = await loop.RunAsync(CancellationToken.None);
        return (compiled, loop, report, driver);
    }

    [Fact]
    public async Task Every_invariant_holds_over_a_whole_game()
    {
        var (scenario, loop, report, driver) = await Played();
        await using var _ = driver;
        var results = Invariants.Invariants.CheckAll(await driver.ProjectionAsync(CancellationToken.None), loop.Log, scenario);
        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Held, $"{r.Name}: {r.Detail}"));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task The_run_loop_reports_a_broken_invariant_as_a_failure()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        // A round that started with no boundary Ask and no actor: the round advanced on its own, which the scheduler forbids.
        var forged = loop.Log.Append(new GameEvent(loop.Log[^1].Seq + 1, null, new RoundStarted(9, scenario.TeamIds, []), null)).ToList();
        var result = Invariants.Invariants.TheRoundAdvancedOnlyThroughTheBoundaryAsk(forged);
        Assert.False(result.Held);
        Assert.Contains("round 9", result.Detail);
    }

    [Fact]
    public async Task A_cold_replay_the_incremental_projection_and_the_servers_all_agree()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var mine = await driver.ProjectionAsync(CancellationToken.None);
        var cold = Replay.To(scenario.Board, scenario.Info, loop.Log, loop.Log[^1].Seq);
        Assert.Empty(ProjectionComparer.Differences(cold, mine));
        var server = await ((InProcessDriver)driver).ServerProjectionAsync(CancellationToken.None);
        Assert.Empty(ProjectionComparer.Differences(server, mine));
    }

    [Fact]
    public async Task An_undo_to_any_dice_bearing_sequence_reproduces_the_dice()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var result = Invariants.Invariants.UndoAndRedoReproduceTheDice(loop.Log, scenario);
        Assert.True(result.Held, result.Detail);
    }

    [Fact]
    public void The_comparer_names_what_differs()
    {
        var scenario = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        var a = Projection.Initial(scenario.Board, scenario.Info);
        var applier = new EventApplier(scenario.Board, scenario.Info);
        var model = scenario.Board.Armies[0].Snapshot.Units[0].Models[0].Id;
        applier.Apply(new GameEvent(1, null, new WoundsSet(model, 2), null));
        var differences = ProjectionComparer.Differences(a, applier.Projection);
        Assert.Contains(differences, d => d.Contains("LastSeq"));
        Assert.Contains(differences, d => d.Contains("Wounds"));
    }

    [Fact]
    public async Task Mid_run_assertion_throws_with_every_failure_named()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        MidRun.Assert(await driver.ProjectionAsync(CancellationToken.None), loop.Log, scenario);   // does not throw
        var forged = loop.Log.Append(new GameEvent(loop.Log[^1].Seq + 1, null, new RoundStarted(9, scenario.TeamIds, []), null)).ToList();
        var thrown = Assert.Throws<InvariantViolatedException>(() =>
            MidRun.Assert(Replay.To(scenario.Board, scenario.Info, forged, forged[^1].Seq), forged, scenario));
        Assert.Contains("round", thrown.Message);
    }
}
```

`RunLoop` therefore exposes its log: add `public IReadOnlyList<GameEvent> Log => _log;` beside `Projector`.
- [ ] **Step 2: Run; expect failure, then write `ProjectionComparer.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter InvariantTests`
Expected: `CS0246: ... 'Invariants' could not be found`.

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Invariants;

/// <summary>
/// Projection is a class, not a record, so it has no structural equality. This is that equality, field by
/// field, reporting every difference rather than the first — a wrong projection usually differs in a pattern.
/// </summary>
public static class ProjectionComparer
{
    public static IReadOnlyList<string> Differences(Projection expected, Projection actual)
    {
        var differences = new List<string>();
        void Check(string what, object? a, object? b)
        {
            if (!Equals(a, b)) differences.Add($"{what}: expected {a}, got {b}");
        }

        Check("LastSeq", expected.LastSeq, actual.LastSeq);
        Check("Scheduler.Round", expected.Scheduler.Round, actual.Scheduler.Round);
        Check("Scheduler.Phase", expected.Scheduler.Phase, actual.Scheduler.Phase);
        Check("Scheduler.ActiveTeamId", expected.Scheduler.ActiveTeamId, actual.Scheduler.ActiveTeamId);
        Check("Scheduler.Order", string.Join(",", expected.Scheduler.Order), string.Join(",", actual.Scheduler.Order));
        Check("Scheduler.Passed", string.Join(",", expected.Scheduler.Passed.Order()), string.Join(",", actual.Scheduler.Passed.Order()));
        Check("Stack", string.Join(",", expected.Stack.Select(f => $"{f.Id:N}/{f.Sequence}/{f.Steps.Count}")),
                        string.Join(",", actual.Stack.Select(f => $"{f.Id:N}/{f.Sequence}/{f.Steps.Count}")));
        Check("OpenAsks", string.Join(",", expected.OpenAsks.Keys.Order()), string.Join(",", actual.OpenAsks.Keys.Order()));
        Check("Markers", string.Join(",", expected.Markers.Keys.Order()), string.Join(",", actual.Markers.Keys.Order()));
        Check("Scores", string.Join(",", expected.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}")),
                        string.Join(",", actual.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}")));

        foreach (var id in expected.Models.Keys.Union(actual.Models.Keys).Order())
        {
            if (!expected.Models.TryGetValue(id, out var a) || !actual.Models.TryGetValue(id, out var b))
            { differences.Add($"model {id:N} is in one projection only"); continue; }
            Check($"model {id:N} Location", a.Location, b.Location);
            Check($"model {id:N} Wounds", a.Wounds, b.Wounds);
            Check($"model {id:N} SpellTokens", a.SpellTokens, b.SpellTokens);
            Check($"model {id:N} Conditions", string.Join(",", a.Conditions.Order()), string.Join(",", b.Conditions.Order()));
        }

        foreach (var id in expected.Units.Keys.Union(actual.Units.Keys).Order())
        {
            if (!expected.Units.TryGetValue(id, out var a) || !actual.Units.TryGetValue(id, out var b))
            { differences.Add($"unit {id:N} is in one projection only"); continue; }
            Check($"unit {id:N} Actions", string.Join(",", a.Actions.Order()), string.Join(",", b.Actions.Order()));
            Check($"unit {id:N} Conditions", string.Join(",", a.Conditions.Order()), string.Join(",", b.Conditions.Order()));
            Check($"unit {id:N} Grants", string.Join(",", a.Grants.Select(g => g.Id).Order()), string.Join(",", b.Grants.Select(g => g.Id).Order()));
            Check($"unit {id:N} WoundsThisActivation", a.WoundsThisActivation, b.WoundsThisActivation);
        }
        return differences;
    }
}
```

- [ ] **Step 3: Write `Invariants.cs`**

```csharp
using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Invariants;

public sealed record InvariantResult(string Name, bool Held, string? Detail);

public sealed class InvariantViolatedException(IReadOnlyList<InvariantResult> failures)
    : Exception("invariants violated:\n" + string.Join("\n", failures.Select(f => $"  {f.Name}: {f.Detail}")))
{
    public IReadOnlyList<InvariantResult> Failures { get; } = failures;
}

/// <summary>
/// The invariants that hold for any game, whatever the ruleset (spec → What it asserts). These read state
/// freely — wounds, conditions, positions, the scheduler position — which spec → Two ways to answer an Ask allows.
/// What they contain is no second implementation of the rules: each one is a statement about the log, the
/// projection and the slab, and none of them recomputes an outcome the engine produced.
/// </summary>
public static class Invariants
{
    public static IReadOnlyList<InvariantResult> CheckAll(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario) =>
    [
        ReplayReproducesTheProjection(driverProjection, log, scenario),
        UndoAndRedoReproduceTheDice(log, scenario),
        EveryAskAnsweredByAnAddressedTeam(log),
        NoModelInTwoPlacesOrOffTheSlab(log, scenario),
        TheRoundAdvancedOnlyThroughTheBoundaryAsk(log),
    ];

    public static InvariantResult ReplayReproducesTheProjection(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        if (log.Count == 0) return new InvariantResult(nameof(ReplayReproducesTheProjection), true, null);
        var cold = Replay.To(scenario.Board, scenario.Info, log, log[^1].Seq);
        var differences = ProjectionComparer.Differences(cold, driverProjection);
        return new InvariantResult(nameof(ReplayReproducesTheProjection), differences.Count == 0, string.Join("; ", differences.Take(10)));
    }

    /// <summary>
    /// The dice are counter-based on (RngSeed, cursor), and RngSeed is the seed stored on the game row, so it
    /// never changes for a game: die k is a pure function of that seed and k, and Undone(N) shortens the
    /// effective log rather than rewriting it. Checked at every sequence that carries dice, which is
    /// "an arbitrary sequence" exhaustively. This checks the effective-log prefix only: the engine-level undo and
    /// redo check, re-answering after a real Undo, is Task 8's socket test (DriverAgreementTests).
    /// </summary>
    public static InvariantResult UndoAndRedoReproduceTheDice(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        // The effective log: an undone range's dice are no longer the game's, so a log that already holds an
        // Undone is measured as it stands, not as every event ever appended.
        var live = EventApplier.EffectiveLog(log);
        var original = DiceOf(live);
        foreach (var target in live.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).Select(e => e.Seq))
        {
            var undone = log.Append(new GameEvent(log[^1].Seq + 1, null, new Undone(target), null)).ToList();
            var effective = EventApplier.EffectiveLog(undone);
            var kept = DiceOf(effective);
            var expected = original.Take(kept.Count).ToList();
            if (!kept.SequenceEqual(expected))
                return new InvariantResult(nameof(UndoAndRedoReproduceTheDice), false,
                    $"undoing to {target} changed the dice: kept [{string.Join(",", kept)}] against [{string.Join(",", expected)}]");
        }
        return new InvariantResult(nameof(UndoAndRedoReproduceTheDice), true, null);
    }

    private static IReadOnlyList<int> DiceOf(IReadOnlyList<GameEvent> log) => log
        .SelectMany(e => e.Payload switch
        {
            StepCompleted s => s.Rolls.SelectMany(r => r.Dice.Select(d => d.Raw)),
            DiceRerolled d => d.Replaced.Select(r => r.New.Raw),
            _ => [],
        })
        .ToList();

    public static InvariantResult EveryAskAnsweredByAnAddressedTeam(IReadOnlyList<GameEvent> log)
    {
        var asked = log.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
        foreach (var e in log)
        {
            if (e.Payload is not AskAnswered answered) continue;
            if (!asked.TryGetValue(answered.AskId, out var ask))
                return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), false, $"seq {e.Seq} answers ask {answered.AskId:N}, which was never opened");
            if (!ask.Teams.Contains(answered.TeamId))
                return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), false,
                    $"seq {e.Seq}: team {answered.TeamId:N} answered '{ask.Question}', addressed to {string.Join(",", ask.Teams.Select(t => t.ToString("N")))}");
        }
        return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), true, null);
    }

    public static InvariantResult NoModelInTwoPlacesOrOffTheSlab(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var where = new Dictionary<Guid, Location>();
        foreach (var model in scenario.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models)) where[model.Id] = new OnShelf();
        var size = scenario.Board.Table.Size;

        foreach (var e in EventApplier.EffectiveLog(log))
        {
            foreach (var move in Relocations(e.Payload))
            {
                if (!where.TryGetValue(move.ModelId, out var was))
                    return Fail($"seq {e.Seq} moves model {move.ModelId:N}, which is not in any army");
                if (!Equals(was, move.From))
                    return Fail($"seq {e.Seq} moves model {move.ModelId:N} from {move.From}, but it was at {was}");
                where[move.ModelId] = move.To;
                if (move.To is OnTable table)
                {
                    var p = table.Placement.Position;
                    if (p.X < 0 || p.X > size.Width || p.Y < 0 || p.Y > size.Length)
                        return Fail($"seq {e.Seq} puts model {move.ModelId:N} at {p.X},{p.Y}, off a {size.Width}x{size.Length} slab");
                }
            }
        }
        return new InvariantResult(nameof(NoModelInTwoPlacesOrOffTheSlab), true, null);

        InvariantResult Fail(string detail) => new(nameof(NoModelInTwoPlacesOrOffTheSlab), false, detail);
    }

    private static IEnumerable<ModelRelocation> Relocations(EventPayload payload) => payload switch
    {
        ModelsMoved m => m.Moves,
        RoundStarted r => r.Changes.SelectMany(Relocations),
        TurnEnded t => t.Changes.SelectMany(Relocations),
        StepCompleted s => s.Changes.SelectMany(Relocations),
        _ => [],
    };

    /// <summary>The round does not advance on its own: the next round's RoundStarted follows the first answer to the end_round Ask, or carries the actor of a menu override (scheduler design → The final round is the ruleset's).</summary>
    public static InvariantResult TheRoundAdvancedOnlyThroughTheBoundaryAsk(IReadOnlyList<GameEvent> log)
    {
        var effective = EventApplier.EffectiveLog(log);
        var asked = effective.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
        for (var i = 0; i < effective.Count; i++)
        {
            if (effective[i].Payload is not RoundStarted { Round: >= 2 } started) continue;
            if (effective[i].Actor is not null) continue;                                   // the menu's set_round: an override, and logged
            var closed = effective.Take(i).LastOrDefault(e => e.Payload is AskClosed);
            var question = closed?.Payload is AskClosed c && asked.TryGetValue(c.AskId, out var ask) ? ask.Question : null;
            if (question != AskQuestions.EndRound)
                return new InvariantResult(nameof(TheRoundAdvancedOnlyThroughTheBoundaryAsk), false,
                    $"seq {effective[i].Seq} started round {started.Round} without a closed '{AskQuestions.EndRound}' ask (nearest closed ask: {question ?? "none"})");
        }
        return new InvariantResult(nameof(TheRoundAdvancedOnlyThroughTheBoundaryAsk), true, null);
    }
}

/// <summary>The mid-run entry point the module prompt asks for: assert now, and throw with every failure named.</summary>
public static class MidRun
{
    public static void Assert(Projection projection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var failures = Invariants.CheckAll(projection, log, scenario).Where(r => !r.Held).ToList();
        if (failures.Count > 0) throw new InvariantViolatedException(failures);
    }
}
```

- [ ] **Step 4: Wire the invariants into `RunLoop.RunAsync`**

Insert after the expectation check, before the `RunReport` is built. A paused run skips the expectations and the invariants, because both describe a finished game:

```csharp
        if (_settings.CheckInvariants && outcome is not RunOutcome.Paused)   // a paused run is mid-game
        {
            failures.AddRange(Invariants.Invariants.CheckAll(projection, _log, scenario)
                .Where(r => !r.Held).Select(r => $"{r.Name}: {r.Detail}"));

            if (driver is InProcessDriver inProcess)
            {
                var server = await inProcess.ServerProjectionAsync(ct);
                failures.AddRange(ProjectionComparer.Differences(server, projection)
                    .Select(d => $"the driver's projection differs from the server's — {d}"));
            }
        }
```

The invariants run **after** the recording is complete, because the undo invariant reads the log rather than writing to it and the projection comparison reads state; neither appends an event, so the recording is unaffected either way. `DriverAgreementTests` (Task 8) is the one place a real `Undo` command is submitted, and it runs after its own recording is written.

- [ ] **Step 5: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter InvariantTests
git add sim/Invariants sim/Run tests/Tabletop.Sim.Tests
git commit -m "Assert the five invariants that hold for any game"
```
