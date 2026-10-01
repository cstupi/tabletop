# Task 19: A destroyed container disgorges its occupants

**Numbered 19 but sequenced here, before Task 18**, so that Task 18's whole-tree check counts its tests; the other plans cite it
as Task 19, so it keeps that number.

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfContainers.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`DealWounds()` gains a second step; `assign` leaves an occupied container in place), `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (register `GfContainers.TransportDestroyed()`)
- Test: `tests/Tabletop.GameServer.Tests/GfContainersTests.cs`

**Interfaces:**
- Consumes: Tasks 6, 11–14; `Lookup.OccupantUnits`, `Lookup.ContainerOf`, `Lookup.AliveModels`, `Lookup.ModelParameter` (Task 6); `Battlefield.Terrain` (Task 11); `GfMoves.IsContainer`, `GfMoves.DangerousDice`, `GfMoves.DangerousTerrain()`, `GfMoves.ExitPlacement`, `GfNames.ExitWithin` (Task 14); `MoveRegion`, `MoveContainment`, `Destinations`, `Inside`, `OnShelf`, `ConditionSet` (Protocol).
- Produces: `static class GfContainers` with `Sequence TransportDestroyed()` (`transport-destroyed`: `wreck-test`, `place`) and `static IRL<Guid> Wrecks(GameState)`; `GfAttack.DealWounds()` becomes `[assign, disgorge]` with `sealed class DisgorgeStep`.
- **This is p.14's last clause and p.11's third dangerous-terrain trigger**: *"When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed fully within 6" of the transport before removal."* p.21 says the same of a solid building. [Action sequences](../specs/2026-09-03-action-sequences-design.md) → Dangerous terrain names the three invokers — *"moves through it, activating in it, and transport destruction"* — and Task 14 built the first two; this is the third, and it reuses the same `DangerousTestStep` by writing `Values["dangerous_dice"]` on its own frame and pushing `dangerous-terrain`, exactly as `ActivationTerrainStep` does.
- **"Before removal" is why this is two steps in two sequences.** The occupants have to be placed *relative to the container*, so the container must still have a placement while they are placed; and there may be several occupant units, each owing its own test and its own placement Ask. So `assign` (Task 12) stops shelving a container that is holding anybody, `disgorge` runs one `transport-destroyed` frame per occupant unit until none is left inside, and then shelves the wreck. A container destroyed with nobody inside never reaches `disgorge`: `assign` shelved it, and `Wrecks` only sees models still on the table.
- **A nested `deal-wounds` never disgorges.** The occupants' dangerous terrain test pushes `deal-wounds` of its own, whose `disgorge` would find the very wreck being emptied and push `transport-destroyed` for the unit already being processed. The guard is a walk up this frame's own ancestors (`FrameView.Parent`, not the stack, so the outer `disgorge` is unaffected while its child runs): a `deal-wounds` beneath a `transport-destroyed` frame completes `disgorge` at once. A container wrecked by that nested test is not lost — the outer `disgorge` re-reads `Wrecks` after each child and only shelves when no wreck holds anybody.
- **A survivor with nowhere legal to stand is destroyed** (ruling 12). p.15 says the survivors *"must be placed fully within 6" of the transport"* and says nothing about a model that cannot be, so this is a ruling and not a reading. `PlaceSurvivorsStep` treats it as a casualty rather than as a model parked off the table: wounds at capacity, the model to the shelf, and the wounds added to the unit's activation tally, which is what makes the removal count for p.10's end-of-activation morale test. The frame reports `Values["destroyed"]`.
- **Capacity is still not checked** ([game flow](../specs/2026-08-28-game-flow-design.md) → Inside a container: *"Capacity is not enforced"*). `GfMoves.IsContainer` asks whether `Transport` is on the model; its `(X)` is read nowhere in this plan.

Spec: game flow → Inside a container (a container is a model or a piece of terrain; when it is destroyed the occupants take a dangerous terrain test, are Shaken and are placed within 6"; capacity is not enforced); action sequences → Grimdark Future's sequences → Dangerous terrain (the third invoker); rulebook p.15 `Transport(X)`, p.11 dangerous terrain, p.21 solid buildings.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.Geometry;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfContainersTests
{
    /// <summary>Starts deal-wounds under a parent frame that recorded the wounds, the way resolve-hits does (Task 12's harness, R-F3). Riders, when given, are inside the container first.</summary>
    static Sim Wound(Tabletop.Content.Unit attacker, Tabletop.Content.Unit[] team2, Tabletop.Content.Unit target, int wounds, Tabletop.Content.Unit? riders, Guid container, params int[] dice)
    {
        var sim = new Sim(State([attacker], team2), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds")), TestProperties.Core, new ScriptedDice(dice));
        if (riders is not null) sim.Append([new ModelsMoved(riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [target.Id], [], GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], [], GfNames.Hold));
        return sim;
    }

    [Fact]
    public void A_destroyed_transport_tests_shakes_and_places_its_occupants_and_is_removed_last()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 3, []);
        // Three wounds kill the Tough(3) bus; the riders then roll three dangerous terrain dice (1, 4, 4) for one wound.
        var container = bus.Models[0].Id;
        var sim = Wound(gunners, [bus, riders], bus, 3, riders, container, 1, 4, 4);
        Assert.IsType<OnTable>(sim.Model(container).Location);                                        // p.15: not removed until the occupants are out
        Assert.Equal(3, sim.Steps("test").Single().Rolls.Single().Dice.Count);                        // one die per rider (p.11)
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [riders.Models[2].Id] = 1 }));
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (ask.Containment!.ExitingId!.Value, ask.Containment.ExitWithin!.Value));
        Assert.Equal(2, ask.ModelIds.Count);                                                          // the survivors
        sim.Answer(Samples.Team2, new Destinations([
            new ModelRelocation(riders.Models[0].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 4 }, new Yaw(0)))),
            new ModelRelocation(riders.Models[1].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 12 }, new Yaw(0)))),
        ]));
        Assert.Equal(seat.X + 4, ((OnTable)sim.Model(riders.Models[0].Id).Location).Placement.Position.X, 6);
        Assert.IsType<OnShelf>(sim.Model(riders.Models[1].Id).Location);                              // 12" out is not "fully within 6": nowhere legal to stand
        Assert.Equal(1, sim.Model(riders.Models[1].Id).Wounds);                                       // and so destroyed, not parked
        Assert.Equal(1, sim.Steps("place").Single().Values!["destroyed"]);
        Assert.Equal(2, sim.Unit(riders.Id).WoundsThisActivation);                                    // one from the test, one from the removal
        Assert.IsType<OnShelf>(sim.Model(riders.Models[2].Id).Location);                              // killed by the test
        Assert.IsType<OnShelf>(sim.Model(container).Location);                                        // removed after them
        Assert.Contains(GfNames.Shaken, sim.Unit(riders.Id).Conditions);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_container_destroyed_with_nobody_inside_is_an_ordinary_casualty()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var sim = Wound(gunners, [bus], bus, 1, null, Guid.Empty, 1);                              // one eligible model: no allocation is asked (R-N11)
        Assert.IsType<OnShelf>(sim.Model(bus.Models[0].Id).Location);                                 // assign shelved it; Wrecks never sees it
        Assert.Empty(sim.Steps("test"));
        Assert.Empty(GfContainers.Wrecks(sim.State));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_wreck_held_for_its_occupants_is_not_offered_a_carried_wound()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var buses = Unit("Buses", "4+", "4+", [], 2, [P("Transport", 11), P("Tough", 2)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, []);
        var (wreck, other) = (buses.Models[0].Id, buses.Models[1].Id);
        var sim = Wound(gunners, [buses, riders], buses, 3, riders, wreck, 4);
        Assert.Equal(new[] { wreck, other }.Order(), Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Targets.Order());   // two Tough models: the owner chooses
        // Three wounds on the first bus: two kill it, the third carries to the next segment. The wreck stays on the table holding the riders (p.15),
        // at capacity, and is the most-wounded Tough model, which is who the ordering would name if it were still a candidate.
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [wreck] = 3 }));
        Assert.Equal(1, sim.Model(other).Wounds);                                                     // the carried wound went to the other bus, unasked
        Assert.Equal(2, sim.Model(wreck).Wounds);
        Assert.IsType<OnTable>(sim.Model(wreck).Location);
        Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);                                   // the next question is the riders' placement, not another allocation
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfContainersTests`; expect compile errors on `GfContainers`. Write `GfContainers.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.15 Transport(X): "When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed
/// fully within 6" of the transport before it's removed." p.21 says the same of a solid building under BUILDING DESTRUCTION. One frame
/// per occupant unit, pushed by deal-wounds' disgorge step, which shelves the wreck once the last of them is out.
/// </summary>
public static class GfContainers
{
    public static Sequence TransportDestroyed() => new(GfNames.TransportDestroyedSeq, [new WreckTestStep(), new PlaceSurvivorsStep()]);

    /// <summary>
    /// Destroyed containers still on the table: `assign` leaves a container holding anybody in place at its Tough capacity, so this is the
    /// whole predicate. A container destroyed empty is an ordinary casualty and is already on the shelf.
    /// </summary>
    public static IReadOnlyList<Guid> Wrecks(GameState state) =>
        state.Projection.Models.Values.Where(m => m.Location is OnTable && GfMoves.IsContainer(state, m.Id)
            && GfAttack.AtCapacity(state, m.Id)).Select(m => m.Id).ToList();

    /// <summary>p.11's dangerous terrain test, invoked by a destroyed transport. Same shape as GfMoves.ActivationTerrainStep: settle the dice on this frame, then push, so the child reads a settled count.</summary>
    sealed class WreckTestStep : IStep
    {
        public string Name => "wreck-test";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0) return new Complete([]);                               // the test has been run
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var dice = GfMoves.DangerousDice(ctx.State, Lookup.AliveModels(ctx.State, unit));
            if (dice == 0) return new Complete([]);
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete([], new Dictionary<string, int> { ["dangerous_dice"] = dice }, Repeat: true);
        }
    }

    /// <summary>
    /// p.15: the survivors are Shaken and "placed fully within 6" of the transport". The Ask is a move_region whose containment names
    /// the container being exited, so the client draws the region a disembark move draws. A survivor with no placement in the answer,
    /// or one outside the radius, has nowhere legal to stand; the rulebook does not cover it (ruling 12), so it is destroyed.
    /// That removal is a casualty and writes what `assign` writes for one: wounds at capacity, the model to the shelf, and the wounds on the
    /// unit's activation tally, so p.10's end-of-activation morale test counts it. `Values["destroyed"]` is how many went that way.
    /// </summary>
    sealed class PlaceSurvivorsStep : IStep
    {
        public string Name => "place";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Lookup.ContainerOf(ctx.State, unit) is not { } container) return new Complete([]);     // the test killed everyone
            if (ctx.History.LastClosed() is not { } closed)
                return new OpenAsks([ctx.Ask("disembark", $"Place the survivors fully within {GfNames.ExitWithin:0.#}\" of the wreck", ctx.TeamOf(unit)!.Value,
                    // Allowance is unbound on a disembark (Task 14): ExitWithin is passed so the number is the exit distance. The client reads Containment.
                    new MoveRegion(Lookup.AliveModels(ctx.State, unit), GfNames.ExitWithin,
                        Battlefield.Terrain(ctx.State).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList(),
                        [], null, new MoveContainment([], container, GfNames.ExitWithin)), allowPass: false)]);
            IReadOnlyList<ModelRelocation> proposed = (closed.Single as Destinations)?.Moves ?? [];
            var changes = new List<StateChange> { new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true) };
            var moves = new List<ModelRelocation>(); var destroyed = 0; var fatal = 0;
            foreach (var m in Lookup.AliveModels(ctx.State, unit))
            {
                var to = proposed.FirstOrDefault(r => r.ModelId == m)?.To as OnTable;
                if (to is not null && GfMoves.ExitPlacement(ctx.State, m, container, to.Placement, GfNames.ExitWithin) is { } p)
                { moves.Add(new ModelRelocation(m, new Inside(container), new OnTable(p))); continue; }
                var capacity = Lookup.ModelParameter(ctx.State, m, GfNames.Tough) ?? 1;
                destroyed++; fatal += capacity - ctx.Projection.Models[m].Wounds;
                changes.Add(new WoundsSet(m, capacity));
                moves.Add(new ModelRelocation(m, new Inside(container), new OnShelf()));
            }
            changes.Add(new ModelsMoved(moves));
            if (fatal > 0) changes.Add(new WoundsThisActivationSet(unit, ctx.Projection.Units[unit].WoundsThisActivation + fatal));
            return new Complete(changes, new Dictionary<string, int> { ["destroyed"] = destroyed });
        }
    }
}
```

- [ ] **Step 3: Add `DisgorgeStep` to `GfAttack.cs`, extend `DealWounds()`, and stop `assign` shelving an occupied container**

```csharp
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep(), new DisgorgeStep()]);
```

`AssignStep` (Task 12) treats a model whose wounds have reached its capacity as taken out of play even while it stays on the table. The shared predicate lives in `GfAttack`:

```csharp
    /// <summary>Wounds have reached the model's Tough (1 without it). A dead model is removed at once, so on the table this is only a destroyed container held for disgorge.</summary>
    public static bool AtCapacity(GameState state, Guid modelId) =>
        state.Projection.Models[modelId].Wounds >= (Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1);
```

`EligibleForNextWound` starts from `Lookup.AliveModels(state, unit).Where(m => !AtCapacity(state, m))`, so a carried wound is never offered to a wreck, and `assign`'s survivor check is `Lookup.AliveModels(ctx.State, target).Any(m => !dead.Contains(m) && !AtCapacity(ctx.State, m))`, which excludes a retained wreck from the repeat. In `AssignStep.Run`, the casualty lines become:

```csharp
                // p.15: a container holding anybody stays on the table until disgorge has placed them, "before it's removed".
                if (model.Wounds + applied < capacity) continue;
                dead.Add(modelId);
                if (!(GfMoves.IsContainer(ctx.State, modelId) && Lookup.OccupantUnits(ctx.State, modelId).Count > 0)) moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
```

and the new step goes beside it:

```csharp
    /// <summary>
    /// p.15: a destroyed container is not removed until the units inside are out. `assign` left it on the table; this runs
    /// transport-destroyed once per occupant unit — each takes its dangerous terrain test, is Shaken and is placed — and then
    /// shelves every wreck. Nothing to do is the common case and costs one predicate.
    /// </summary>
    sealed class DisgorgeStep : IStep
    {
        public string Name => "disgorge";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0 && !(ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned)) return new OpenAsks([]);
            // The occupants' own dangerous terrain test pushes a deal-wounds of its own; that one must not disgorge the wreck the
            // frame above it is already emptying. The guard walks this frame's own ancestors, not the stack, so the outer disgorge
            // is unaffected while its child runs. A container wrecked by that nested test is still caught: the outer disgorge
            // re-reads Wrecks after every child and only shelves when no wreck holds anybody.
            for (var f = ctx.Frame.Parent; f is not null; f = f.Parent)
                if (f.Frame.Sequence == GfNames.TransportDestroyedSeq) return new Complete([]);
            var wrecks = GfContainers.Wrecks(ctx.State);
            if (wrecks.Count == 0) return new Complete([]);
            foreach (var wreck in wrecks)
                if (Lookup.OccupantUnits(ctx.State, wreck) is [var occupant, ..])
                    return new Push(GfNames.TransportDestroyedSeq, new SequenceContext(occupant, [], [], null));
            return new Complete([new ModelsMoved(wrecks.Select(w => new ModelRelocation(w, ctx.Projection.Models[w].Location, new OnShelf())).ToList())]);
        }
    }
```

`GfRules._sequences` gains `GfContainers.TransportDestroyed()`.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect `GfContainersTests` 3 passed and every earlier sequence test still green — `deal-wounds` has a second step after this task, so `GfDealWoundsTests` gains a `step_completed` named `disgorge` with no changes in every case that reaches `disgorge`. Commit: `Disgorge a destroyed container: the occupants test, are shaken, are placed within 6", and the wreck goes last`.**
