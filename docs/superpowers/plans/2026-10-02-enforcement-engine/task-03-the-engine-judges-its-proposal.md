# Task 3: The engine judges its own proposal

**Files:**
- Modify: `server/game/Rules/IRulesEngine.cs`, `server/game/Rules/IGameRules.cs`, `server/game/Rules/InertEngine.cs`, `server/game/Engine/IScheduler.cs`, `server/game/Engine/RulesEngine.cs`, `server/game/Session/GameSession.cs`
- Modify (tests' engines and harness): `tests/Tabletop.GameServer.Tests/Infrastructure/FixedAskEngine.cs`, `tests/Tabletop.GameServer.Tests/GameSessionTests.cs` (`DesyncingEngine`, `ThrowingEngine`), `tests/Tabletop.GameServer.Tests/Infrastructure/Sim.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`, `tests/Tabletop.GameServer.Tests/SchedulerFactoryTests.cs`
- Test: `tests/Tabletop.GameServer.Tests/RulesEngineTests.cs`

**Interfaces:**
- Consumes: `Breach`, `StrictEndsGame`, `Accept`, `Refuse`, `EndGame`, `EnforcementPolicy.Decide(Enforcement, Breach?)`, `EnforcementPolicy.EndGamePayloads` (Task 2); `Projection.Ended`, `GameEnded` (Task 1).
- Produces:
  - `public sealed record Emit(IReadOnlyList<EventPayload> Events, string? Warning = null) : EngineResult` with `public static Emit None { get; }`.
  - `IRulesEngine.Advance(GameState) : Emit`; default member `IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => [];`.
  - `IScheduler`: default member `IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => [];`.
  - `public sealed record GameEndingBreach(string Rule, string Detail, IReadOnlyList<Guid> Winners)` in `Tabletop.GameServer.Rules`; `IGameRules` default member `GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => null;`.
  - `RulesEngine.Advance`: `Emit.None` while `Projection.Ended` is set; otherwise the proposal, judged. `RulesEngine.RoundStartChanges` delegates to the scheduler.
  - `StubRules.Judge : Func<IReadOnlyList<EventPayload>, GameEndingBreach?>?` (init), behind `StubRules.JudgeAdvance`.
  - `Sim.Append(IEnumerable<EventPayload>, Guid? actor = null, string? warning = null)`: the warning rides on the first event, as `GameSession.AppendAsync` does.

Spec: enforcement spec → *Who raises a breach* (the engine), *How a warning is recorded and shown*, *The `game_ended` event* (silence while ended).

- [ ] **Step 1: Write the failing tests**

Append to `tests/Tabletop.GameServer.Tests/RulesEngineTests.cs`, inside the class:

```csharp
    /// <summary>A scheduler that proposes one given event until the log holds it.</summary>
    sealed class Proposes(EventPayload payload) : IScheduler
    {
        public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) =>
            state.Applier.Log.Any(e => ReferenceEquals(e.Payload, payload)) ? [] : [payload];
    }

    static readonly RoundStarted Fifth = new(5, [Samples.Team1, Samples.Team2], []);

    static Sim Judged(Enforcement enforcement, bool judge = true) => new(State([A], [B], enforcement: enforcement),
        new StubRules(Roller()) { Judge = judge ? p => p.OfType<RoundStarted>().Any() ? new GameEndingBreach("test_rule", "past the end", [Samples.Team1]) : null : null },
        TestProperties.Core, new ScriptedDice(1), new Proposes(Fifth));

    [Fact]
    public void An_unjudged_proposal_passes_through_with_no_warning()
    {
        var sim = Judged(Enforcement.Strict, judge: false);
        var emit = sim.Engine.Advance(sim.State);
        Assert.Equal([Fifth], emit.Events);
        Assert.Null(emit.Warning);
    }

    [Fact]
    public void Silent_lets_the_breach_through_and_warn_marks_it()
    {
        var silent = Judged(Enforcement.Silent);
        var quiet = silent.Engine.Advance(silent.State);
        Assert.Equal([Fifth], quiet.Events);
        Assert.Null(quiet.Warning);

        var warn = Judged(Enforcement.Warn);
        var marked = warn.Engine.Advance(warn.State);
        Assert.Equal([Fifth], marked.Events);
        Assert.Equal("past the end", marked.Warning);
        warn.Append(marked.Events, warning: marked.Warning);
        Assert.Equal("past the end", warn.State.Applier.Log[^1].Warning);
        Assert.Empty(warn.Engine.Advance(warn.State).Events);                 // the act is in the log: no second warning
    }

    [Fact]
    public void Strict_ends_the_game_in_place_of_the_proposal_and_the_engine_falls_silent()
    {
        var sim = Judged(Enforcement.Strict);
        var emit = sim.Engine.Advance(sim.State);
        var end = Assert.IsType<GameEnded>(Assert.Single(emit.Events));
        Assert.Equal("past the end", end.Reason);
        Assert.Equal([Samples.Team1], end.Winners);
        Assert.Null(emit.Warning);
        sim.Append(emit.Events);
        Assert.Same(end, sim.State.Projection.Ended);
        Assert.Empty(sim.Engine.Advance(sim.State).Events);                  // still proposing round 5, but ended: nothing
        Assert.DoesNotContain(sim.State.Applier.Log, e => e.Payload is RoundStarted);
    }
```

`Fifth` is one shared instance, so `Proposes` recognises it in the log by reference.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter RulesEngineTests`
Expected: compile errors: `GameEndingBreach` and `StubRules.Judge` do not exist, and `Advance` returns a list with no `Events`.

- [ ] **Step 3: Change the seams**

`server/game/Rules/IRulesEngine.cs`: replace the `Emit` record and the interface's `Advance` line:

```csharp
/// <summary>Events to append, in order, and the warning the first of them carries (enforcement spec → How a warning is recorded). Empty is a valid answer.</summary>
public sealed record Emit(IReadOnlyList<EventPayload> Events, string? Warning = null) : EngineResult
{
    public static Emit None { get; } = new([]);
}
```

```csharp
    Emit Advance(GameState state);
    /// <summary>The changes a round start carries for this game's scheduler and ruleset; what a menu set_round writes (enforcement spec → A menu round start).</summary>
    IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => [];
```

`server/game/Engine/IScheduler.cs`: give the interface a body:

```csharp
public interface IScheduler
{
    IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter);
    /// <summary>The shape's round-start changes then the ruleset's OnRoundStarted; BeginRound and a menu set_round both write them.</summary>
    IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => [];
}
```

`server/game/Rules/IGameRules.cs`: beside `SequenceStart`, add

```csharp
/// <summary>A breach found in the engine's own proposal (enforcement spec → Who raises a breach). Under strict it always ends the game, with these winners.</summary>
public sealed record GameEndingBreach(string Rule, string Detail, IReadOnlyList<Guid> Winners);
```

and at the end of the interface body

```csharp
    // Added by the enforcement plan.
    /// <summary>Judges what the interpreter or scheduler proposes before RulesEngine returns it. Null: nothing broken. Wrapping rulesets must forward it.</summary>
    GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => null;
```

`server/game/Rules/InertEngine.cs`: `public Emit Advance(GameState state) => Emit.None;`

- [ ] **Step 4: Judge in `RulesEngine.Advance`**

In `server/game/Engine/RulesEngine.cs` add `using System.Diagnostics;` and replace `Advance`:

```csharp
    /// <summary>
    /// Nothing while the game has ended. A framed Ask blocks everything; an unframed one is the scheduler's to wait on or close.
    /// The proposal — the interpreter's while frames exist, the scheduler's otherwise — is judged by the ruleset and decided by the
    /// game's enforcement: through as it is, through with a warning, or replaced by game_ended.
    /// </summary>
    public Emit Advance(GameState state)
    {
        if (state.Projection.Ended is not null) return Emit.None;
        if (state.Projection.OpenAsks.Values.Any(a => a.Ask.FrameId is not null)) return Emit.None;
        var proposed = state.Projection.Stack.Count > 0 ? Interpreter.Advance(state) : scheduler.Advance(state, Interpreter);
        if (proposed.Count == 0) return Emit.None;
        var found = rules.JudgeAdvance(state, properties, proposed);
        var breach = found is null ? null : new Breach(found.Rule, found.Detail, new StrictEndsGame(found.Winners));
        return EnforcementPolicy.Decide(state.Info.Enforcement, breach) switch
        {
            Accept a => new Emit(proposed, a.Warning),
            EndGame e => new Emit(EnforcementPolicy.EndGamePayloads(state.Projection, e.Reason, e.Winners)),
            Refuse r => throw new InvalidOperationException($"a breach in the engine's own proposal cannot refuse it: {r.Detail}"),
            var v => throw new UnreachableException(v.GetType().Name),
        };
    }

    public IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => scheduler.RoundStartChanges(state, round);
```

`RulesEngine` already has `using Tabletop.GameServer.Dispatch;`.

- [ ] **Step 5: Pass the warning through the session**

In `server/game/Session/GameSession.cs`, `AdvanceAsync`, replace the loop body's engine call and append:

```csharp
            Emit emit;
            try { emit = Engine.Advance(Play); }
            catch (Exception ex) { await FaultAsync($"the engine threw in Advance: {ex.Message}"); return; }
            if (emit.Events.Count == 0) return;
            await AppendAsync(emit.Events, null, emit.Warning);
            if (Faulted) return;
```

- [ ] **Step 6: Bring the test engines and harness along**

- `Infrastructure/FixedAskEngine.cs`: `public Emit Advance(GameState state) => state.Applier.Log.Any(e => e.Payload is AskOpened o && o.Ask.Id == ask.Id) ? Emit.None : new Emit([new AskOpened(ask)]);`
- `GameSessionTests.cs`, `DesyncingEngine`: `public Emit Advance(GameState state) => state.Applier.Log.Count == 1 ? new Emit([new WoundsSet(Guid.CreateVersion7(), 1)]) : Emit.None;`
- `GameSessionTests.cs`, `ThrowingEngine`: `public Emit Advance(GameState state) => always || state.Applier.Log.Count > 0 ? throw new InvalidOperationException("the engine broke") : Emit.None;`
- `SchedulerFactoryTests.cs`: `var first = engine.Advance(alternating.State).Events;`
- `Infrastructure/Sim.cs`: `Append` gains the warning, and `Run` passes it:

```csharp
    public void Append(IEnumerable<EventPayload> events, Guid? actor = null, string? warning = null)
    {
        var first = true;
        foreach (var e in events)
        {
            State.Applier.Apply(new GameEvent(State.Projection.LastSeq + 1, actor, e, first ? warning : null));
            Emitted.Add(e); first = false;
        }
    }

    /// <summary>Advance until the engine returns nothing (the session's AdvanceAsync loop).</summary>
    public IReadOnlyList<EventPayload> Run()
    {
        var from = Emitted.Count;
        for (var i = 0; i < 1000; i++)
        {
            var emit = Engine.Advance(State);
            if (emit.Events.Count == 0) return Emitted.Skip(from).ToList();
            Append(emit.Events, warning: emit.Warning);
        }
        throw new InvalidOperationException("the engine did not rest in 1000 steps");
    }
```

  `Dispatch` keeps calling `Append(a.Events, participant)`; add the dispatcher's warning there too: `if (outcome is Applied a) Append(a.Events, participant, a.Warning);`.
- `Infrastructure/StubRules.cs`, inside `StubRules`:

```csharp
    public Func<IReadOnlyList<EventPayload>, GameEndingBreach?>? Judge { get; init; }
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => Judge?.Invoke(proposed);
```

  and inside `WithSequences`, forward it, so a wrapped Grimdark Future keeps its judge (Task 5 tests it):

```csharp
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => inner.JudgeAdvance(state, properties, proposed);
```

- [ ] **Step 7: Build and run the server and sim suites**

Run: `docker compose up -d && dotnet build && dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: 0 build errors; PASS. `sim/` compiles unchanged: it calls no engine directly.

- [ ] **Step 8: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests
git commit -m "Judge the engine's own proposal and carry its warning on Emit"
```
