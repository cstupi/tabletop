# Task 3: Step outline

**Wave:** 2 · **Depends on:** 1 · **Model:** sonnet

**Files:**
- Modify: `server/game/Interpreter/Steps.cs` (`IStep.Label`, `LabelledStep`, `StepLabels.Labelled`)
- Modify: `server/game/Interpreter/SequenceInterpreter.cs` (`Start` and a `Push` write the outline into `SequenceStarted`)
- Modify: `server/game/Rules/GrimdarkFuture/GfMoves.cs` (`Hold`, `Advance`, `Rush` labels)
- Modify: `server/game/Rules/GrimdarkFuture/GfMelee.cs` (`Charge`, `Melee` labels)
- Modify: `server/game/Rules/GrimdarkFuture/GfShooting.cs` (`Shooting` labels)
- Test: `tests/Tabletop.GameServer.Tests/StepOutlineTests.cs` (new)

**Interfaces:**
- Consumes: `SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null)`,
  `OutlineStep(string Step, string Label)` (Task 1).
- Produces: `public interface IStep { string Name { get; } string? Label => null; StepOutcome Run(StepContext ctx); }`;
  `public sealed class LabelledStep(IStep step, string label) : IStep`; `public static IStep Labelled(this IStep step, string label)`
  (`StepLabels`). Every `SequenceStarted` the interpreter writes carries the sequence's labelled steps in order, or
  `null` when it has none.

Spec: *Server and protocol changes → Step outline*, *Header* (the strip reads the action frame's outline; nested
frames light the outline step they run under). A step without a label is internal: reaction windows, the dangerous
terrain test every action opens with (p.11), the casting checkpoint, attack-start, the special-rule strike steps
(Counter, Impact, Unwieldy), the after-shooting and after-melee windows. `OutlineStep.Step` is the step's `Name`, the
same string `StepCompleted.Step` carries, so the client ticks a step off when its `StepCompleted` arrives.

A label goes on the step instance in the sequence's definition (`.Labelled("Move")`), not on the step class: one
class serves several sequences (`ShootOrNotStep` ends Hold, Advance and Rush; `MoveStep` serves Advance, Rush and
Charge), and Rush's `shoot-or-not` is not a player step (below).

## The labels

The words are the rulebook's. Page 7, *Activating units*:

> • Hold - Doesn't move, can shoot.
> • Advance - Moves 6” and can shoot after moving.
> • Rush - Moves 12” but can’t shoot.
> • Charge - Moves 12” into melee.

Page 8: "When shooting, a unit must pick one valid target" (*Picking targets*); "the target for each weapon group must
be declared before rolling, and all weapons are fired simultaneously" (*Multiple weapon types*).

Page 9: "When charging, a unit must pick one valid target" (*Picking targets*); *Charge moves*: "Charging models must
move by up to 12” … Once all charging models have moved, all models from the target unit that are not in base contact
with a charging model must move by up to 3” to get into base contact"; *Return strikes*: "Once all charging models
have attacked, the defending unit may choose to strike back".

Page 10: *Melee resolution*, "you need to determine who won the melee"; *Consolidation moves*, "the units must make
consolidation moves"; *Shaken units*: "Shaken units must spend their activation being idle (can’t take any actions …)".

| Sequence (`GfNames`) | Step `Name` | Label | Source |
| --- | --- | --- | --- |
| `HoldSeq` | `shoot-or-not` | Shoot | p.7 Hold "can shoot" |
| `AdvanceSeq` | `move` | Move | p.7 Advance "Moves 6”" |
| `AdvanceSeq` | `shoot-or-not` | Shoot | p.7 Advance "can shoot after moving" |
| `RushSeq` | `move` | Move | p.7 Rush "Moves 12”" |
| `ChargeSeq` | `charge-target` | Pick target | p.9 "must pick one valid target" |
| `ChargeSeq` | `move` | Charge move | p.9 *Charge moves* |
| `ChargeSeq` | `defender-closes` | Target moves | p.9 "all models from the target unit … must move by up to 3”" |
| `ChargeSeq` | `fight` | Melee | p.9 *The melee sequence* |
| `IdleSeq` | — | — | p.10 "can’t take any actions": the header shows "Idle" with no strip |
| `Shooting` | `declare-targets` | Pick targets | p.8 *Picking targets*, groups "declared before rolling" |
| `Shooting` | `fire-groups` | Fire | p.8 "all weapons are fired simultaneously" |
| `Melee` | `charger-attacks` | Charger attacks | p.9 "Once all charging models have attacked" |
| `Melee` | `strike-back` | Strike back | p.9 *Return strikes* |
| `Melee` | `compare` | Melee resolution | p.10 *Melee resolution* |
| `Melee` | `consolidate` | Consolidation | p.10 *Consolidation moves* |

Rush's `shoot-or-not` stays unlabelled: p.7 says a Rush "can’t shoot", and the step offers shooting only through Quick
Shot. Hold's `casting-window` stays unlabelled: it is a decision only for a Caster, and a strip step that ticks itself
for every other unit tells the player nothing. Nested sequences other than shooting and melee (attack-sequence,
resolve-hits, deal-wounds, morale-test, casting, dangerous-terrain, the roll-off) carry no outline: the header never
shows their frames as the strip.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/StepOutlineTests.cs`:

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>SequenceStarted carries the sequence's labelled steps (in-game UX spec → Step outline).</summary>
public class StepOutlineTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);

    static Sequence Outer() => new("outer", [
        new LambdaStep("window", _ => new Complete([])),
        new LambdaStep("move", _ => new Complete([])).Labelled("Move"),
        new LambdaStep("push", ctx => ctx.History.Children.Any(ch => ch.Finished) ? new Complete([]) : new Push("inner", ctx.Frame.Context)).Labelled("Shoot")]);
    static Sequence Inner() => new("inner", [new LambdaStep("fire", _ => new Complete([])).Labelled("Fire"), new LambdaStep("after", _ => new Complete([]))]);
    static Sequence Bare() => new("bare", [new LambdaStep("only", _ => new Complete([]))]);

    static SequenceInterpreter Interpreter(IGameRules rules) => new(rules, TestProperties.Core, new ScriptedDice(4));
    static void Append(GameState state, IEnumerable<EventPayload> events) { foreach (var e in events) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, e, null)); }
    static List<EventPayload> Run(GameState state, SequenceInterpreter interpreter)
    {
        var all = new List<EventPayload>();
        for (var guard = 0; guard < 100; guard++)
        {
            var events = interpreter.Advance(state);
            if (events.Count == 0) return all;
            Append(state, events); all.AddRange(events);
        }
        throw new InvalidOperationException("did not rest");
    }
    static IEnumerable<(string, string)> Pairs(IReadOnlyList<OutlineStep>? outline) => outline!.Select(o => (o.Step, o.Label));

    [Fact]
    public void A_started_sequence_carries_its_labelled_steps_in_order()
    {
        var state = State([A], [B]);
        var started = Assert.IsType<SequenceStarted>(Assert.Single(Interpreter(new StubRules(Outer(), Inner())).Start(state, "outer", new SequenceContext(A.Id, [], [], null))));
        Assert.Equal([("move", "Move"), ("push", "Shoot")], Pairs(started.Outline));
    }

    [Fact]
    public void A_sequence_with_no_label_carries_no_outline()
    {
        var state = State([A], [B]);
        var started = Assert.IsType<SequenceStarted>(Assert.Single(Interpreter(new StubRules(Bare())).Start(state, "bare", new SequenceContext(A.Id, [], [], null))));
        Assert.Null(started.Outline);
    }

    [Fact]
    public void A_pushed_sequence_carries_its_own_outline_and_every_outline_step_completes_under_its_name()
    {
        var state = State([A], [B]);
        var interpreter = Interpreter(new StubRules(Outer(), Inner()));
        Append(state, interpreter.Start(state, "outer", new SequenceContext(A.Id, [], [], null)));
        var outer = state.Projection.Stack.Single().Outline;
        var run = Run(state, interpreter);
        var inner = run.OfType<SequenceStarted>().Single();
        Assert.Equal([("fire", "Fire")], Pairs(inner.Outline));
        var completed = run.OfType<StepCompleted>().Select(s => s.Step).ToHashSet();
        Assert.All(outer.Concat(inner.Outline!), o => Assert.Contains(o.Step, completed));
    }

    [Fact]
    public void A_frame_started_before_outlines_existed_runs_to_the_end()
    {
        var state = State([A], [B]);
        var interpreter = Interpreter(new StubRules(Outer(), Inner()));
        Append(state, [new SequenceStarted(Guid.CreateVersion7(), "outer", new SequenceContext(A.Id, [], [], null))]);   // as an old log has it
        Assert.Empty(state.Projection.Stack.Single().Outline);
        var run = Run(state, interpreter);
        Assert.Equal(2, run.OfType<SequenceFinished>().Count());
        Assert.Empty(state.Projection.Stack);
    }

    [Fact]
    public void A_label_leaves_the_step_as_it_was()
    {
        var bare = new LambdaStep("move", _ => new Complete([]));
        var labelled = bare.Labelled("Move");
        Assert.Equal(("move", "Move"), (labelled.Name, labelled.Label));
        Assert.Null(((IStep)bare).Label);
    }

    public static IEnumerable<object[]> GrimdarkFutureOutlines() =>
    [
        [GfNames.HoldSeq, new[] { "shoot-or-not:Shoot" }],
        [GfNames.AdvanceSeq, new[] { "move:Move", "shoot-or-not:Shoot" }],
        [GfNames.RushSeq, new[] { "move:Move" }],
        [GfNames.ChargeSeq, new[] { "charge-target:Pick target", "move:Charge move", "defender-closes:Target moves", "fight:Melee" }],
        [GfNames.IdleSeq, Array.Empty<string>()],
        [GfNames.Shooting, new[] { "declare-targets:Pick targets", "fire-groups:Fire" }],
        [GfNames.Melee, new[] { "charger-attacks:Charger attacks", "strike-back:Strike back", "compare:Melee resolution", "consolidate:Consolidation" }],
    ];

    [Theory, MemberData(nameof(GrimdarkFutureOutlines))]
    public void Grimdark_Future_labels_the_action_and_attack_steps(string sequence, string[] expected)
    {
        var state = State([A], [B]);
        var started = Assert.IsType<SequenceStarted>(Assert.Single(Interpreter(GrimdarkFutureRules.Instance).Start(state, sequence, new SequenceContext(A.Id, [], [], null))));
        Assert.Equal(expected, (started.Outline ?? []).Select(o => $"{o.Step}:{o.Label}"));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter StepOutlineTests`
Expected: compile errors: `Labelled` is not defined on `LambdaStep`; `IStep` has no `Label`.

- [ ] **Step 3: Give a step its label**

In `server/game/Interpreter/Steps.cs`, replace

```csharp
/// <summary>A step is ruleset C#. It reads game state and its frame and ends one of four ways. It holds nothing between runs.</summary>
public interface IStep { string Name { get; } StepOutcome Run(StepContext ctx); }
```

with

```csharp
/// <summary>A step is ruleset C#. It reads game state and its frame and ends one of four ways. It holds nothing between runs.
/// Label: the player-facing name in the sequence's outline (SequenceStarted.Outline); null for an internal step, which the outline leaves out.</summary>
public interface IStep { string Name { get; } string? Label => null; StepOutcome Run(StepContext ctx); }

/// <summary>A step under a player-facing label. The same name and the same run: a label is a property of the step's place in a sequence, not of its class.</summary>
public sealed class LabelledStep(IStep step, string label) : IStep
{
    public string Name => step.Name;
    public string? Label => label;
    public StepOutcome Run(StepContext ctx) => step.Run(ctx);
}

public static class StepLabels
{
    public static IStep Labelled(this IStep step, string label) => new LabelledStep(step, label);
}
```

- [ ] **Step 4: Write the outline into SequenceStarted**

In `server/game/Interpreter/SequenceInterpreter.cs`, replace `Start`:

```csharp
    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context)
    {
        rules.Sequence(sequence);   // throws UnknownSequenceException
        return [new SequenceStarted(Guid.CreateVersion7(), sequence, context)];
    }
```

with

```csharp
    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context) =>
        [new SequenceStarted(Guid.CreateVersion7(), sequence, context, Outline(rules.Sequence(sequence)))];   // Sequence throws UnknownSequenceException
```

replace the `Push` arm of `Advance`'s switch:

```csharp
            case Push p: rules.Sequence(p.Sequence); events.Add(new SequenceStarted(Guid.CreateVersion7(), p.Sequence, p.Context)); break;
```

with

```csharp
            case Push p: events.Add(new SequenceStarted(Guid.CreateVersion7(), p.Sequence, p.Context, Outline(rules.Sequence(p.Sequence)))); break;
```

and add, after `RepeatValues`:

```csharp
    /// <summary>The sequence's labelled steps in order (in-game UX spec → Step outline), or null when it has none, so an unlabelled
    /// sequence's sequence_started is written as it was before outlines existed.</summary>
    static IReadOnlyList<OutlineStep>? Outline(Sequence sequence)
    {
        var outline = sequence.Steps.Where(s => s.Label is not null).Select(s => new OutlineStep(s.Name, s.Label!)).ToList();
        return outline.Count == 0 ? null : outline;
    }
```

- [ ] **Step 5: Run the generic tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter StepOutlineTests`
Expected: every test passes except `Grimdark_Future_labels_the_action_and_attack_steps`, which fails for every row but
`IdleSeq` (no labels yet).

- [ ] **Step 6: Label the Grimdark Future sequences**

In `server/game/Rules/GrimdarkFuture/GfMoves.cs`, replace the `Hold`, `Advance` and `Rush` definitions with:

```csharp
    /// <summary>Outline labels: p.7's words for the action ("Hold - Doesn't move, can shoot"; "Advance - Moves 6” and can shoot after moving";
    /// "Rush - Moves 12” but can’t shoot", so Rush's shoot-or-not, reached only through Quick Shot, is not a player step). The dangerous terrain
    /// test, the reaction window and Hold's casting checkpoint are internal.</summary>
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new CastingWindowStep(), new ShootOrNotStep().Labelled("Shoot")]);
    public static Sequence Advance() => new(GfNames.AdvanceSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Advance, false).Labelled("Move"), new ShootOrNotStep().Labelled("Shoot")]);
    public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Rush, false).Labelled("Move"), new ShootOrNotStep()]);
```

`Idle` and `DangerousTerrain` are unchanged.

In `server/game/Rules/GrimdarkFuture/GfMelee.cs`, replace the `Charge` and `Melee` definitions with:

```csharp
    /// <summary>Outline labels: p.9 ("must pick one valid target"; Charge moves; the target's models "must move by up to 3”"; The melee sequence).</summary>
    public static Sequence Charge() => new(GfNames.ChargeSeq, [new GfMoves.ActivationTerrainStep(), new GfMoves.PointWindowStep(GfNames.ActivationStart, "activation-start"), new ChargeTargetStep().Labelled("Pick target"), new GfMoves.MoveStep(GfNames.Charge, ignoreSeparation: true).Labelled("Charge move"), new DefenderClosesStep().Labelled("Target moves"), new FightStep().Labelled("Melee")]);
    /// <summary>Outline labels: p.9 ("Once all charging models have attacked"; Return strikes) and p.10 (Melee resolution; Consolidation moves). The Counter, Impact and Unwieldy strikes and the after-melee window are internal.</summary>
    public static Sequence Melee() => new(GfNames.Melee, [new MeleeStartStep(), new CounterStep(), new ImpactStep(), new ChargerAttacksStep().Labelled("Charger attacks"), new StrikeBackStep().Labelled("Strike back"), new UnwieldyStep(), new CompareStep().Labelled("Melee resolution"), new ConsolidateStep().Labelled("Consolidation"), new GfMoves.PointWindowStep(GfNames.AfterMelee, "after-melee")]);
```

In `server/game/Rules/GrimdarkFuture/GfShooting.cs`, replace the `Shooting` definition with:

```csharp
    /// <summary>Outline labels: p.8 (Picking targets, each group's target "declared before rolling"; "all weapons are fired simultaneously").</summary>
    public static Sequence Shooting() => new(GfNames.Shooting, [new DeclareTargetsStep().Labelled("Pick targets"), new GfAttack.AttackStartStep(), new FireGroupsStep().Labelled("Fire"), new GfMoves.PointWindowStep(GfNames.AfterShooting, "after-shooting")]);
```

`GfMoves.cs`, `GfMelee.cs` and `GfShooting.cs` already have `using Tabletop.GameServer.Interpreter;`, where
`StepLabels` lives.

- [ ] **Step 7: Run them to see them pass, then the whole server suite**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter StepOutlineTests`
Expected: PASS.

Run: `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS. Nothing in the server reads `SequenceStarted.Outline`; a failure here is a step whose type was
inspected by a caller (none today: no `is MoveStep`-style check exists under `server/game/`).

- [ ] **Step 8: Check the viewer still reads the log**

The viewer's fixtures are sim recordings; their `sequence_started` rows gain `outline` the next time they are
regenerated, and the generated type already has the optional field (Task 1).

Run: `cd viewer && npm test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add server/game/Interpreter/Steps.cs server/game/Interpreter/SequenceInterpreter.cs \
  server/game/Rules/GrimdarkFuture/GfMoves.cs server/game/Rules/GrimdarkFuture/GfMelee.cs server/game/Rules/GrimdarkFuture/GfShooting.cs \
  tests/Tabletop.GameServer.Tests/StepOutlineTests.cs
git commit -m "Write each sequence's labelled steps into sequence_started, with Grimdark Future's labels from pages 7 to 10" \
  -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
