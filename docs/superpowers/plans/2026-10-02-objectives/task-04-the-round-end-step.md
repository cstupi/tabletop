# Task 4: The round-end step

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfRules.Objectives.cs`
- Modify: `server/game/Rules/IGameRules.cs`, `server/game/Scheduler/SchedulerBase.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` (`WithSequences`)
- Test: `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs`

**Interfaces:**
- Consumes: `RoundEnded`, `ObjectiveControlSet`, `Projection.Control` (Task 1); `GfObjectives.Seize` (Task 3); `SchedulerHarness`, `TestArmies`, `FakeRules.Gf()`.
- Produces: `IGameRules` default member `IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => [];`; `GrimdarkFutureRules.OnRoundEnded` → `GfObjectives.Seize(state, properties)` for round ≥ 1; `SchedulerBase` proposes `round_ended` once per round, after the `end_round` answer and before `BeginRound`, only when the hook returns changes; `FakeRules.RoundEndedHook : Func<GameState, PropertySet, int, IReadOnlyList<StateChange>>?`; `WithSequences.OnRoundEnded` forwards.

Spec: objectives spec → *Where the seize runs*. The private `SchedulerBase.RoundEnded(...)` method is renamed `PlayerEndedRound(...)`, because the class now pattern-matches on the Protocol type `RoundEnded` and a method of the same name hides the type (CS0119).

- [ ] **Step 1: Give `FakeRules` the hook**

In `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, with the scheduler plan's members:

```csharp
    public Func<GameState, PropertySet, int, IReadOnlyList<StateChange>>? RoundEndedHook { get; set; }
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => RoundEndedHook?.Invoke(state, properties, round) ?? [];
```

`FakeRules.Gf()` leaves it unset: the scheduler tests' boundaries stay exactly as they are.

- [ ] **Step 2: Write the failing tests**

Append to `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs`, inside the class:

```csharp
    static SchedulerHarness Seizing(Enforcement enforcement = Enforcement.Warn, bool judge = false)
    {
        var rules = FakeRules.Gf();
        rules.RoundEndedHook = GrimdarkFutureRules.Instance.OnRoundEnded;
        if (judge) rules.JudgeAdvanceHook = GrimdarkFutureRules.Instance.JudgeAdvance;
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)], enforcement: enforcement, rules: rules);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        return h;
    }

    /// <summary>Every model of team 0's unit onto the setup objective's edge (36, 26.5): 2.5" from its centre, well inside 3" of its edge.</summary>
    static void OntoTheObjective(SchedulerHarness h)
    {
        var unit = h.Units(0)[0];
        h.Dispatch(new MoveModels(h.P.Units[unit].Definition.Models.Select(m =>
            new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(new Placement(new Point3(36, 26.5, 0), new Yaw(0))))).ToList()), team: 0);
    }

    static void PlayTurns(SchedulerHarness h)
    {
        for (var i = 0; i < 20 && h.Open.Question != AskQuestions.EndRound; i++)
            h.Answer(h.Teams.ToList().IndexOf(h.Open.Teams[0]), new Chosen(((Options)h.Open.Legal).Choices[0].Id));
    }

    [Fact]
    public void The_seize_runs_after_a_player_ends_the_round_and_before_the_next_round_starts()
    {
        var h = Seizing();
        OntoTheObjective(h);
        PlayTurns(h);
        Assert.Empty(h.P.Control);                                                       // nothing is seized until the round ends
        var seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_ended", "round_started"], h.Kinds(seq).Take(4));
        var ended = (RoundEnded)h.Log.Single(e => e.Payload is RoundEnded).Payload;
        var objective = h.State.Board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;
        Assert.Equal(1, ended.Round);
        Assert.Equal([new ObjectiveControlSet(objective, h.Teams[0])], ended.Changes);
        Assert.Equal(h.Teams[0], h.P.Control[objective]);

        PlayTurns(h);
        seq = h.P.LastSeq; h.EndRound();
        Assert.DoesNotContain("round_ended", h.Kinds(seq));                             // nothing changed: nothing written
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void A_round_with_nothing_near_a_marker_writes_no_round_end()
    {
        var h = Seizing();
        PlayTurns(h);
        var seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_started"], h.Kinds(seq).Take(3));
    }

    [Fact]
    public void A_wrapped_Grimdark_Future_still_seizes()
    {
        var h = Seizing();
        OntoTheObjective(h);
        Assert.NotEmpty(new WithSequences(GrimdarkFutureRules.Instance).OnRoundEnded(h.State, TestProperties.Core, 1));
        Assert.Empty(GrimdarkFutureRules.Instance.OnRoundEnded(h.State, TestProperties.Core, 0));   // the phases before round one are not a round
    }
```

Add `using Tabletop.GameServer.Scheduler;` to the file if `SchedulerHarness`'s namespace needs it (it is in `Tabletop.GameServer.Tests.Infrastructure`, already imported).

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter ObjectivesTests`
Expected: compile errors: `OnRoundEnded` is not a member of `GrimdarkFutureRules` or `WithSequences`.

- [ ] **Step 4: The hook, Grimdark Future's answer and the forward**

`server/game/Rules/IGameRules.cs`, at the end of the interface body, after the enforcement plan's `JudgeAdvance`:

```csharp
    // Added by the objectives plan.
    /// <summary>The ruleset's changes at the end of a round (round 1 on), after a player ended it and before the next round starts. Wrapping rulesets must forward it.</summary>
    IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => [];
```

Create `server/game/Rules/GrimdarkFuture/GfRules.Objectives.cs`:

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>p.6: "At the end of each round" objectives are seized, contested and kept (GfObjectives).</summary>
public sealed partial class GrimdarkFutureRules
{
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) =>
        round < 1 ? [] : GfObjectives.Seize(state, properties);
}
```

`tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`, inside `WithSequences`:

```csharp
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => inner.OnRoundEnded(state, properties, round);
```

- [ ] **Step 5: The step in `SchedulerBase`**

In `server/game/Scheduler/SchedulerBase.cs`:

1. Rename the private method `RoundEnded(GameState, SchedulerLog, IReadOnlyList<Guid>, out IReadOnlyList<EventPayload>)` to `PlayerEndedRound` (its summary is unchanged), and its one call site.
2. Replace the end of `Advance`:

```csharp
        // Round one on: the round is over, and it does not advance on its own — a player ends it.
        if (!PlayerEndedRound(state, log, teams, out var gate)) return gate;
        // Then the round's end, once (objectives spec → Where the seize runs): written only when the ruleset changes something.
        if (log.SinceRound.All(e => e.Payload is not RoundEnded) && state.Rules.OnRoundEnded(state, Properties, s.Round) is { Count: > 0 } changes)
            return [new RoundEnded(s.Round, changes)];
        return BeginRound(state, log, teams, s.Round + 1, starter);
```

3. Add a sentence to the class summary: "the round's end (the ruleset's changes, written as round_ended when there are any)".

- [ ] **Step 6: Run the scheduler and objectives tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "ObjectivesTests|RoundBoundaryTests|AlternatingSchedulerTests|PlayerTurnSchedulerTests|MissionLengthTests|DeploymentTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add server/game/Rules server/game/Scheduler/SchedulerBase.cs tests/Tabletop.GameServer.Tests
git commit -m "Seize objectives at the end of each round, between the end-round answer and the next round"
```
