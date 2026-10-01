# Task 3: The roll-off — started through the interpreter, read back from its values

**Files:**
- Create: `server/game/Scheduler/RollOff.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/ScriptedStarter.cs`, `tests/Tabletop.GameServer.Tests/RollOffTests.cs`

**Interfaces:**
- Consumes: `ISequenceStarter.Start` (interpreter plan Task 8); `SchedulerLog` (Task 1); `SequenceStarted`, `StepCompleted` (with `Values`), `SequenceFinished`, `SequenceAbandoned`, `SequenceContext` (Protocol); `GameInfo.Teams` (`Team.Ordinal`). The roll-off sequence itself is the interpreter plan's `GfRolls.RollOffSequence` (its Task 11): two steps, `GfNames.RollOffRollStep` (`"roll-off"`) — one die per team in ordinal order, ties rerolled among the tied until every team is ordered — and `GfNames.RollOffOrderStep` (`"order"`), which ranks them and carries `Values["winner_ordinal"]` and `Values["place_1"]..["place_n"]`, team ordinals best first. The confirm gate sits between the two. This plan rolls no dice and names neither step: it takes the frame's last completed step carrying `place_1`.
- Produces: `static class RollOff` with `abstract record Progress` and `Emit(IReadOnlyList<EventPayload> Events)`, `Waiting`, `Resolved(IReadOnlyList<Guid> Order)`; `static Progress Advance(GameState state, SchedulerLog log, string sequence, ISequenceStarter starter)`; `static IReadOnlyList<Guid> Order(GameState state, StepCompleted? step)`. Every branch is read from the effective log — `Waiting` is *no `sequence_finished` for the started frame*, not *the frame is on `Projection.Stack`* — so the reader answers the same way whoever calls it and however the engine's ladder routes a gated frame. Test infrastructure `ScriptedStarter : ISequenceStarter` with `List<(string Sequence, SequenceContext Context)> Started` (records and emits one `sequence_started`, validating nothing — for the `TurnCycle` and `RollOff` unit tests; the harness in Task 5 uses the real interpreter).

Spec: scheduler → Deployment ("The roll-off is the roll primitive with no threshold — one die per team, raw results compared, ties rerolled among the tied"; "an ordinary sequence on the roll primitive, logged"), Events (replay applies `round_started.Order`; the roll-off is never re-derived on replay). Page 6: "the players roll-off and the winner must start deploying"; page 50: "starting with the player that got the highest score, and then alternating in descending order". The scheduler starts the sequence `FirstTeamDecision.RollOffNeeded` names with no actor and reads its `place_k` values into the order on a later call. **The roll-off is gated like any other roll (ruling 7).** `GfRolls.RollOffSequence` is two steps, `roll-off` then `order`; the boundary between them is an unsealed `Confirm` Ask to every team, opened whatever the wait setting says, because the frame has no actor and no parent (interpreter plan Task 8, `GateTeams`; its Interfaces needed 4). So the roll-off spans three dispatches rather than one batch: the one that starts the sequence and rolls, the confirm — with any number of `SetDice` edits before it, which is the point of the gate — and the `Advance` after `sequence_finished` that writes `round_started`. The `order` step ranks the dice that stand at the confirm, so the edit decides the order without anything rewriting a logged `step_completed`. The reader carries nothing between the three and re-derives its answer from the log every time: `Advance` keys `Resolved` on `sequence_finished`, never on the presence of a `step_completed`, and hands `Order` the frame's last completed step **that carries `place_1`**, which is the `order` step and never the `roll-off` step. A roll-off abandoned from the menu is started again.

- [ ] **Step 1: Write `ScriptedStarter.cs`**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>An ISequenceStarter that records what was asked for and emits the sequence_started, checking nothing — the TurnCycle and RollOff unit tests drive the frame's life by hand.</summary>
public sealed class ScriptedStarter : ISequenceStarter
{
    public List<(string Sequence, SequenceContext Context)> Started { get; } = new();

    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context)
    {
        Started.Add((sequence, context));
        return [new SequenceStarted(Guid.CreateVersion7(), sequence, context)];
    }
}
```

- [ ] **Step 2: Write the failing tests `RollOffTests.cs`**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class RollOffTests
{
    static TestGames.Built Three() => TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)]);
    /// <summary>The ranking step, which runs after the gate and carries the values (GfNames.RollOffOrderStep).</summary>
    static StepCompleted Places(Guid frame, params int[] ordinals) =>
        new(frame, GfNames.RollOffOrderStep, null, [], []) { Values = ordinals.Select((o, i) => ($"place_{i + 1}", o)).Append(("winner_ordinal", ordinals[0])).ToDictionary(x => x.Item1, x => x.Item2) };
    /// <summary>The rolling step, which carries dice and no values: the reader must not mistake it for the ranking (GfNames.RollOffRollStep).</summary>
    static StepCompleted Rolled(Guid frame) => new(frame, GfNames.RollOffRollStep, null, [], []);
    static SchedulerLog Log(TestGames.Built g) => SchedulerLog.Of(g.State);

    [Fact]
    public void Order_reads_place_values_as_team_ordinals_best_first_and_fills_in_teams_the_values_miss()
    {
        var g = Three(); var t = g.Teams; var frame = Guid.CreateVersion7();
        Assert.Equal([t[1], t[2], t[0]], RollOff.Order(g.State, Places(frame, 2, 3, 1)));
        Assert.Equal([t[2], t[0], t[1]], RollOff.Order(g.State, Places(frame, 3)));                      // a hand-edited log: the rest follow in ordinal order
        Assert.Equal([t[0], t[1], t[2]], RollOff.Order(g.State, null));                                  // no step at all: ordinal order
        Assert.Equal([t[1], t[0], t[2]], RollOff.Order(g.State, Places(frame, 2, 2, 9)));                // duplicates and unknown ordinals are skipped
    }

    [Fact]
    public void With_no_roll_off_since_the_round_anchor_the_named_sequence_is_started_with_no_actor()
    {
        var g = Three(); var starter = new ScriptedStarter();
        var emit = Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));
        var started = Assert.IsType<SequenceStarted>(Assert.Single(emit.Events));
        Assert.Equal((GfNames.RollOffSeq, (Guid?)null, (string?)null), (started.Sequence, started.Context.ActorUnitId, started.Context.Action));
        Assert.Equal([(GfNames.RollOffSeq, started.Context)], starter.Started);
    }

    [Fact]
    public void A_live_frame_waits_a_finished_one_resolves_and_an_abandoned_one_is_started_again()
    {
        var g = Three(); var t = g.Teams; var starter = new ScriptedStarter();
        var frame = Guid.CreateVersion7();
        TestGames.Apply(g, new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));   // started and unfinished: the whole interval a confirm gate holds
        TestGames.Apply(g, Places(frame, 3, 1, 2), new SequenceFinished(frame));
        Assert.Equal([t[2], t[0], t[1]], Assert.IsType<RollOff.Resolved>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter)).Order);
        TestGames.Apply(g, new RoundStarted(0, [t[2], t[0], t[1]], []));
        Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));      // a new round anchor: the old roll-off does not count
        var again = Guid.CreateVersion7();
        TestGames.Apply(g, new SequenceStarted(again, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)), new SequenceAbandoned(again));
        Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));      // abandoned from the menu: start over
        Assert.Equal(2, starter.Started.Count);
    }

    [Fact]
    public void A_gated_roll_off_waits_behind_its_confirm_and_resolves_from_the_ranking_step_when_the_frame_finishes()
    {
        var g = Three(); var t = g.Teams; var starter = new ScriptedStarter();
        var frame = Guid.CreateVersion7();
        var gate = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, "Confirm", g.Teams, new Confirmation(), false, false, null, frame);
        TestGames.Apply(g, new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)), Rolled(frame), new AskOpened(gate));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));    // rolled, and the gate holds the frame with the dice editable
        TestGames.Apply(g, new AskClosed(gate.Id, [new TeamAnswer(t[0], new Confirmed())]), Places(frame, 3, 1, 2));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));    // confirmed and ranked, but the frame has not finished: still nothing to read
        TestGames.Apply(g, new SequenceFinished(frame));
        Assert.Equal([t[2], t[0], t[1]], Assert.IsType<RollOff.Resolved>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter)).Order);   // the ranking step, not the rolling one
        Assert.Empty(starter.Started);                                                                    // waiting starts nothing, so no second roll-off is ever begun
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `RollOff.cs`**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// The roll-off is a ruleset sequence (FirstTeamDecision.RollOffNeeded names it; OPR's is GfRolls.RollOffSequence) run by the
/// interpreter like any other. The scheduler starts it with no actor and, once its frame has finished, reads the descending order
/// from its step_completed.Values place_1..place_n — team ordinals, best first (scheduler → Deployment; p.50). Replay applies
/// round_started.Order and never reads the values again.
/// It is confirm-gated like any other roll: the sequence is two steps, and the boundary before the second is an
/// every-team Confirm the interpreter opens whatever the wait setting says, so the frame stays open with its dice editable and the
/// order is read only once sequence_finished stands. Waiting is that whole interval. The values live on the step that carries
/// place_1 — the ranking step, which runs after the gate — so an edited die decides the order and no logged step is ever rewritten.
/// </summary>
public static class RollOff
{
    /// <summary>The one key convention between a ruleset's roll-off and this reader: place_1..place_n, team ordinals best first (interfaces needed 5). It also identifies the ranking step among the frame's completed steps.</summary>
    static string Place(int k) => $"place_{k}";

    public abstract record Progress;
    public sealed record Emit(IReadOnlyList<EventPayload> Events) : Progress;
    public sealed record Waiting : Progress;
    public sealed record Resolved(IReadOnlyList<Guid> Order) : Progress;

    /// <summary>Since the last round_started (the whole log before the first): no roll-off → start one; abandoned → start again; started and unfinished → wait, which is where a confirm gate holds it; finished → its order.</summary>
    public static Progress Advance(GameState state, SchedulerLog log, string sequence, ISequenceStarter starter)
    {
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var started = since.OfType<SequenceStarted>().LastOrDefault(s => s.Sequence == sequence);
        if (started is null || since.Any(p => p is SequenceAbandoned a && a.FrameId == started.FrameId))
            return new Emit(starter.Start(state, sequence, new SequenceContext(null, [], [], null)));
        if (!since.Any(p => p is SequenceFinished f && f.FrameId == started.FrameId)) return new Waiting();
        return new Resolved(Order(state, since.OfType<StepCompleted>().LastOrDefault(s => s.FrameId == started.FrameId && s.Values?.ContainsKey(Place(1)) is true)));
    }

    /// <summary>place_1..place_n as team ids; ordinals the values do not name (a hand-edited log; the team list itself cannot change after the flip to playing) follow in ordinal order.
    /// The step handed in is the frame's last completed step carrying place_1 — the ranking step, which the interpreter runs after the confirm gate, over whatever dice stand then.</summary>
    public static IReadOnlyList<Guid> Order(GameState state, StepCompleted? step)
    {
        var teams = state.Info.Teams.OrderBy(t => t.Ordinal).ToList();
        var placed = new List<Guid>();
        for (var k = 1; step?.Values is { } values && values.TryGetValue(Place(k), out var ordinal); k++)
            if (teams.FirstOrDefault(t => t.Ordinal == ordinal) is { } team && !placed.Contains(team.Id)) placed.Add(team.Id);
        return placed.Concat(teams.Select(t => t.Id).Where(t => !placed.Contains(t))).ToList();
    }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter RollOff`; expect 4 passed. Commit: `Start the ruleset's roll-off through the interpreter and read the order back`.**
