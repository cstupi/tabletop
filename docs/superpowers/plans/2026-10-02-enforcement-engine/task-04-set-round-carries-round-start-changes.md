# Task 4: A menu round start carries the round-start changes

**Files:**
- Modify: `server/game/Scheduler/SchedulerBase.cs`, `server/game/Dispatch/CommandDispatcher.cs`
- Test: `tests/Tabletop.GameServer.Tests/RoundBoundaryTests.cs`

**Interfaces:**
- Consumes: `IScheduler.RoundStartChanges` and `IRulesEngine.RoundStartChanges` default members, `RulesEngine.RoundStartChanges` delegating to the scheduler (Task 3); `MenuTranslation.Translate` (unchanged).
- Produces: `public IReadOnlyList<StateChange> SchedulerBase.RoundStartChanges(GameState state, int round)`, which implements `IScheduler`'s default member; `CommandDispatcher` writes `set_round`'s `round_started` with `engine.RoundStartChanges(state, round)`.

Spec: enforcement spec → *A menu round start carries the round-start changes*. Today `MenuTranslation` writes `new RoundStarted(r.Round, p.Scheduler.Order.ToList(), [])`, so a round started from the menu grants no spell tokens and clears no fatigue or action records; it is also how a table plays on after a `strict` end.

- [ ] **Step 1: Write the failing test**

Append to `tests/Tabletop.GameServer.Tests/RoundBoundaryTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_menu_round_start_carries_the_changes_the_scheduler_would()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1, modelRules: [(1, Caster, 2)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1); var caster = h.P.Units[a[0]].Definition.Models[0].Id;
        h.Dispatch(new SetCondition(TestGames.UnitSubject(b[0]), Fatigued, true));
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(2, h.P.Models[caster].SpellTokens);

        var seq = h.P.LastSeq;
        h.Dispatch(new SetRound(2), team: 0);
        var started = h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted);
        var round = (RoundStarted)started.Payload;
        Assert.NotNull(started.Actor);                                                                  // still the menu's override, with an actor
        Assert.Contains(round.Changes, c => c is ActionRecordCleared x && x.UnitId == a[0]);           // the shape's record clear
        Assert.Contains(round.Changes, c => c is ConditionSet { Condition: Fatigued, Present: false }); // the ruleset's fatigue
        Assert.Contains(round.Changes, c => c is SpellTokensSet s && s.ModelId == caster);             // and its tokens
        Assert.Equal(4, h.P.Models[caster].SpellTokens);
        Assert.Empty(h.P.Units[a[0]].Actions);
    }
```

`RoundBoundaryTests` already has `using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;`, which supplies `Caster` and `Fatigued`.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter A_menu_round_start_carries`
Expected: FAIL at the first `Assert.Contains`: `round_started` has no changes.

- [ ] **Step 3: Name the round-start changes once, in `SchedulerBase`**

In `server/game/Scheduler/SchedulerBase.cs`, add:

```csharp
    /// <summary>What a round start carries: the shape's changes, then the ruleset's (scheduler → Round start). BeginRound writes them, and so does a menu set_round.</summary>
    public IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round)
    {
        var changes = new List<StateChange>(ShapeRoundStartChanges(state, round));
        changes.AddRange(state.Rules.OnRoundStarted(state, Properties, round));
        return changes;
    }
```

and in `BeginRound` replace

```csharp
        var changes = new List<StateChange>(ShapeRoundStartChanges(state, round));
        changes.AddRange(state.Rules.OnRoundStarted(state, Properties, round));
        var events = new List<EventPayload> { new RoundStarted(round, order, changes) };
```

with

```csharp
        var events = new List<EventPayload> { new RoundStarted(round, order, RoundStartChanges(state, round)) };
```

- [ ] **Step 4: Fill `set_round`'s changes in the dispatcher**

In `server/game/Dispatch/CommandDispatcher.cs`, replace the `CommandClass.Override` arm of `Dispatch` with `CommandClass.Override => Override(state, command),` and add:

```csharp
    /// <summary>The menu's one event. A set_round carries the round-start changes the scheduler would write, so a round started by hand is a whole round (enforcement spec → A menu round start).</summary>
    private DispatchOutcome Override(GameState state, Command command)
    {
        if (MenuTranslation.Translate(state, command, out var missing) is not { } payload)
            return new Refused(RefusalReason.Unknown, missing ?? "unknown command");
        if (payload is RoundStarted r) payload = r with { Changes = engine.RoundStartChanges(state, r.Round) };
        return new Applied([payload], null);
    }
```

The changes are computed against the state before the round starts, exactly as `BeginRound` computes them.

- [ ] **Step 5: Run the boundary and dispatcher tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "RoundBoundaryTests|CommandDispatcherTests|AlternatingSchedulerTests|PlayerTurnSchedulerTests"`
Expected: PASS. A test that asserted an empty `Changes` on a menu `set_round` is wrong now; if one fails on that alone, change its expectation to the round-start changes and say so in the commit body.

- [ ] **Step 6: Commit**

```bash
git add server/game/Scheduler/SchedulerBase.cs server/game/Dispatch/CommandDispatcher.cs tests/Tabletop.GameServer.Tests/RoundBoundaryTests.cs
git commit -m "Start a menu round with the changes the scheduler's round start carries"
```
