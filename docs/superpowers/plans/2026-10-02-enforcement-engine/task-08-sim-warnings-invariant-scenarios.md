# Task 8: `sim/`: warnings, invariant 6 and the final-round scenarios

**Files:**
- Modify: `sim/Drivers/InProcessDriver.cs`, `sim/Drivers/SocketDriver.cs`, `sim/Invariants/Invariants.cs`, `sim/Scenarios/Expectation.cs`, `sim/Scenarios/ScenarioCatalog.cs`
- Test: `tests/Tabletop.Sim.Tests/InProcessDriverTests.cs`, `InvariantTests.cs`, `ExpectationTests.cs`, `ScenarioTests.cs`

**Interfaces:**
- Consumes: `RunOutcome.Ended` (Task 7); the mission-length judge (Task 5); `GameEvent.Warning`; `Expectations.Check`; `ScenarioBuilder.Enforcing`, `.RoundCap`, `.Expect`.
- Produces: `SubmitResult.Accepted.Warning` populated by both drivers (the `Warning` of the first event of the submitted command's own batch: seq above the head at submit, actor the submitting participant; null when the command appended nothing); `Invariants.NothingAdvancesAfterTheEnd(IReadOnlyList<GameEvent> log) : InvariantResult`, sixth in `CheckAll`; `Expectation.Warnings(string Kind, int Count)`; catalog scenarios `final-round`, `final-round-warn`, `final-round-silent`.

Spec: enforcement spec → *sim*. Invariant 6: in the effective log, every event between a `game_ended` and the next `round_started` has an actor. It reads the log only and names no rule. Invariant numbering is load-bearing for the spell tree, which merges after this plan: 6 is this one, 7 is the objectives plan's.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Tabletop.Sim.Tests/InProcessDriverTests.cs`, inside the class (add `using Tabletop.Protocol;` if absent):

```csharp
    [Theory]
    [InlineData(Enforcement.Warn, true)]
    [InlineData(Enforcement.Silent, false)]
    public async Task An_answer_outside_the_legal_set_comes_back_with_the_events_warning(Enforcement enforcement, bool warned)
    {
        var scenario = ScenarioCatalog.Get("stalemate") with { Enforcement = enforcement };
        await using var driver = await InProcessDriver.StartAsync(ScenarioCompiler.Compile(scenario), CancellationToken.None);
        var open = (await driver.StepAsync(CancellationToken.None)).OpenAsks[0];                 // the roll-off's confirm gate
        var result = Assert.IsType<SubmitResult.Accepted>(await driver.SubmitAsync(driver.ParticipantFor(open.Ask.Teams[0]), new AnswerAsk(open.Ask.Id, new Chosen("not-an-option")), CancellationToken.None));
        var answered = (await driver.DrainEventsAsync(CancellationToken.None)).First(e => e.Payload is AskAnswered);
        Assert.Equal(warned, result.Warning is not null);
        Assert.Equal(answered.Warning, result.Warning);
    }

    [Fact]
    public async Task A_legal_answer_carries_no_warning()
    {
        var scenario = ScenarioCatalog.Get("stalemate") with { Enforcement = Enforcement.Warn };
        await using var driver = await InProcessDriver.StartAsync(ScenarioCompiler.Compile(scenario), CancellationToken.None);
        var open = (await driver.StepAsync(CancellationToken.None)).OpenAsks[0];
        Assert.Equal(new SubmitResult.Accepted(null), await driver.SubmitAsync(driver.ParticipantFor(open.Ask.Teams[0]), new AnswerAsk(open.Ask.Id, new Confirmed()), CancellationToken.None));
    }
```

Append to `tests/Tabletop.Sim.Tests/InvariantTests.cs`, inside the class:

```csharp
    static GameEvent E(long seq, EventPayload p, Guid? actor = null) => new(seq, actor, p, null);

    [Fact]
    public void Nothing_the_engine_writes_follows_the_end_until_a_round_is_started_by_hand()
    {
        var team = Guid.CreateVersion7(); var player = Guid.CreateVersion7();
        var held = Invariants.Invariants.NothingAdvancesAfterTheEnd([
            E(1, new GameEnded("over", [team])), E(2, new ScoreSet(team, 3), player),
            E(3, new RoundStarted(5, [team], []), player), E(4, new TurnStarted(team))]);
        Assert.True(held.Held, held.Detail);

        var broken = Invariants.Invariants.NothingAdvancesAfterTheEnd([E(1, new GameEnded("over", [team])), E(2, new TurnStarted(team))]);
        Assert.False(broken.Held);
        Assert.Contains("seq 2", broken.Detail);

        var undone = Invariants.Invariants.NothingAdvancesAfterTheEnd([E(1, new ScoreSet(team, 0), player), E(2, new GameEnded("over", [])), E(3, new Undone(1), player), E(4, new TurnStarted(team))]);
        Assert.True(undone.Held, undone.Detail);                                                 // an undone end ends nothing
    }
```

and in `Every_invariant_holds_over_a_whole_game` change `Assert.Equal(5, results.Count);` to `Assert.Equal(6, results.Count);`.

Append to `tests/Tabletop.Sim.Tests/ScenarioTests.cs`, inside the class (add `using Tabletop.Sim.Answers; using Tabletop.Sim.Drivers; using Tabletop.Sim.Run;` if absent):

```csharp
    [Theory]
    [InlineData("final-round")]
    [InlineData("final-round-warn")]
    [InlineData("final-round-silent")]
    public async Task The_final_round_scenarios_meet_their_expectations(string name)
    {
        var scenario = ScenarioCatalog.Get(name);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(scenario.Seed))), HandPlacement.ChooserFor(scenario.Seed));
        var report = await loop.RunAsync(CancellationToken.None);
        Assert.True(report.Passed, string.Join("\n", report.Failures));
    }
```

Append to `tests/Tabletop.Sim.Tests/ExpectationTests.cs`, inside the class (it has a `Known()` helper returning a played `stalemate` run and a `Check(params Expectation[])` helper over it):

```csharp
    [Fact]
    public async Task Warnings_counts_warned_events_of_a_kind()
    {
        var failures = await Check(new Expectation.Warnings("round_started", 1));
        Assert.Contains(failures, f => f.Contains("expected 1 warned 'round_started' events, saw 0"));
        Assert.Empty(await Check(new Expectation.Warnings("round_started", 0)));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "InProcessDriverTests|InvariantTests|ExpectationTests|ScenarioTests"`
Expected: compile errors: `NothingAdvancesAfterTheEnd`, `Expectation.Warnings` do not exist; after those, `KeyNotFoundException: no scenario 'final-round'`, and the warn case returns `Accepted(null)`.

- [ ] **Step 3: Both drivers report the warning**

`sim/Drivers/InProcessDriver.cs`, `SubmitAsync`:

```csharp
    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var client = _players.FirstOrDefault(c => c.ParticipantId == participantId)
            ?? throw new ArgumentException($"no connected client for participant {participantId}", nameof(participantId));
        var head = _applier.Projection.LastSeq;
        await Session.ReceiveAsync(client, new CommandMessage(Guid.CreateVersion7(), command));
        var refused = client.TakeFrames().OfType<Refused>().LastOrDefault();
        Absorb();
        return refused is null
            ? new SubmitResult.Accepted(_applier.Log.FirstOrDefault(e => e.Seq > head && e.Actor == participantId)?.Warning)
            : new SubmitResult.Rejected(refused.Reason, refused.Detail);
    }
```

`sim/Drivers/SocketDriver.cs`, `SubmitAsync`: the submitting socket receives its own batch before its `AtRest`, so read the warning off it in the wait loop. Declare `string? warning = null;` beside `Refused? refusal = null;`, add a branch to the loop:

```csharp
            if (frame is Refused r && r.MessageId == commandId) refusal = r;
            else if (frame is EventsMessage batch) warning ??= batch.Events.FirstOrDefault(e => e.Seq > head && e.Actor == participantId)?.Warning;
            else if (frame is AtRest rest && (refusal is not null || rest.Seq > head)) { restSeq = rest.Seq; break; }
```

and return `new SubmitResult.Accepted(warning)` in place of `new SubmitResult.Accepted(null)`.

- [ ] **Step 4: Invariant 6**

In `sim/Invariants/Invariants.cs`, add `NothingAdvancesAfterTheEnd(log),` as the sixth entry of `CheckAll`, and the method:

```csharp
    /// <summary>
    /// After game_ended the engine is silent until a round is started by hand (enforcement spec → The game_ended event): every event
    /// between a live game_ended and the next round_started has an actor. Read from the effective log, so an undone end ends nothing.
    /// </summary>
    public static InvariantResult NothingAdvancesAfterTheEnd(IReadOnlyList<GameEvent> log)
    {
        GameEvent? end = null;
        foreach (var e in EventApplier.EffectiveLog(log))
        {
            if (end is not null && e.Actor is null)
                return new InvariantResult(nameof(NothingAdvancesAfterTheEnd), false,
                    $"seq {e.Seq} ({EventKinds.Of(e.Payload)}) has no actor, after the game ended at seq {end.Seq}");
            if (e.Payload is GameEnded) end = e;
            else if (e.Payload is RoundStarted) end = null;
        }
        return new InvariantResult(nameof(NothingAdvancesAfterTheEnd), true, null);
    }
```

Update the class summary's count if it names five.

- [ ] **Step 5: The expectation**

In `sim/Scenarios/Expectation.cs`, add the record:

```csharp
    /// <summary>Exactly Count effective-log events of Kind carry a warning.</summary>
    public sealed record Warnings(string Kind, int Count) : Expectation;
```

and its case in `Expectations.Check`:

```csharp
                case Expectation.Warnings(var kind, var expected):
                    var warned = EventApplier.EffectiveLog(log).Count(e => EventKinds.Of(e.Payload) == kind && e.Warning is not null);
                    if (warned != expected) failures.Add($"expected {expected} warned '{kind}' events, saw {warned}");
                    break;
```

- [ ] **Step 6: The three scenarios**

In `sim/Scenarios/ScenarioCatalog.cs`, add to the factory dictionary (add `using Tabletop.Protocol;` if `Enforcement` is not in scope):

```csharp
        // The round-4 end (GF p.7) under each setting. One unit a side keeps six rounds cheap; a wipe-out no longer ends a run (p.6),
        // so the outcome does not depend on the seed.
        ["final-round"] = () => new ScenarioBuilder("final-round", seed: 1)
            .RoundCap(6).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Expect(new Expectation.Outcome("ended"), new Expectation.ReachedRound(4), new Expectation.EventOfKind("game_ended"))
            .Build(),

        ["final-round-warn"] = () => new ScenarioBuilder("final-round-warn", seed: 1)
            .Enforcing(Enforcement.Warn).RoundCap(6).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Expect(new Expectation.Outcome("round_limit"), new Expectation.NoEventOfKind("game_ended"), new Expectation.Warnings("round_started", 2))
            .Build(),

        ["final-round-silent"] = () => new ScenarioBuilder("final-round-silent", seed: 1)
            .Enforcing(Enforcement.Silent).RoundCap(6).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Expect(new Expectation.Outcome("round_limit"), new Expectation.NoEventOfKind("game_ended"), new Expectation.Warnings("round_started", 0))
            .Build(),
```

A cap of 6 ends the run on round 6's boundary Ask, so under `warn` exactly rounds 5 and 6 start with the warning.

- [ ] **Step 7: Run the sim suite, then the console**

Run: `dotnet test tests/Tabletop.Sim.Tests && dotnet run --project sim -- final-round --seeds 1..3 --no-recording && dotnet run --project sim -- final-round-warn --seeds 1..3 --no-recording`
Expected: tests PASS; each console run prints three passing rows, `ended` at round 4 and `round_limit` at round 6, and exits 0.

- [ ] **Step 8: Commit**

```bash
git add sim tests/Tabletop.Sim.Tests
git commit -m "Report a command's warning from both drivers, assert nothing advances after the end, and add the final-round scenarios"
```
