# Task 14: Shooting, the move step with its casting checkpoint, embarking and disembarking, dangerous terrain, and the four actions

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfShooting.cs`, `server/game/Rules/GrimdarkFuture/GfMoves.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`, `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`PushStep` gains a context factory), `tests/Tabletop.GameServer.Tests/Infrastructure/Armies.cs` (`State` gains `terrain`)
- Test: `tests/Tabletop.GameServer.Tests/GfShootingTests.cs`

**Interfaces:**
- Consumes: Tasks 9, 11–13; geometry plan's `Movement.Check`, `Containment.VolumesContaining`, `Containment.BaseFullyWithin`; `MoveRegion`, `MoveContainment`, `Destinations`, `Inside`, `Assignment`, `Assigned`, `Options`, `Chosen` (Protocol).
- Produces: `GfShooting.Shooting()` (`shooting`: `declare-targets`, `fire-groups`); `static IRL<WeaponGroup> Groups(GameState, PropertySet, Guid unit)` with `sealed record WeaponGroup(string Name, IRL<Guid> WeaponIds, bool Takedown)` (one per ranged weapon type across living models, `Limited` weapons already used this game excluded); `static IRL<Guid> ValidTargets(StepContext, Guid unit, WeaponGroup)` (enemy units with a model still in play, `EligibleModels` deciding — so a unit inside a container is a candidate and is ruled out, or, for a fellow occupant, in); `GfMoves.Hold()`, `Advance()`, `Rush()` (`hold`: `activation-terrain`, `casting-window`, `shoot-or-not`; `advance`: `activation-terrain`, `move`, `shoot-or-not`; `rush`: `activation-terrain`, `move`), `GfMoves.DangerousTerrain()` (`dangerous-terrain`: `test`, `deal`); `sealed class MoveStep(string action, bool ignoreSeparation)`, `sealed class ShootOrNotStep`, `sealed class CastingWindowStep`, `sealed class ActivationTerrainStep`, `sealed class DangerousTestStep`; `static int DangerousDice(GameState, IEnumerable<Guid> models)` (one die per model, or the model's `Tough` value in dice); `static IRL<Guid> StandingInDangerous(GameState, Guid unit)`; `static bool IsContainer(GameState, Guid modelId)`; `static (bool Embarked, bool Disembarked) ContainerMovesThisActivation(GameState, Guid unit)`; `static IRL<Guid> ContainersInReach(StepContext, Guid unit, double remaining, IRL<WorldVolume> impassable)`; `static Placement? ExitPlacement(GameState, Guid modelId, Guid container, Placement proposed, double within)`; `GfAttack.PushStep(string name, string sequence, Func<StepContext, SequenceContext>? context = null)`.
- **p.11 has three triggers for a dangerous terrain test — *moves in*, *moves through*, *is activated in* — and it is a **model's** test, not the unit's.** `activation-terrain` is the first step of all four action sequences and pushes `dangerous-terrain` for the models standing in a `Dangerous` volume when the activation begins; the move step pushes it for the models that ended in one *or* whose path crossed one (`Movement.Check` with the dangerous volumes passed in its `difficult` slot, reading `EntersDifficult` — the same swept test, asked of a different set). Either way the pushing step writes `Values["dangerous_dice"]` and `test` rolls exactly that many, so only the affected models are tested. Transport destruction, the third invoker [action sequences](../specs/2026-09-03-action-sequences-design.md) names, reuses `DangerousTestStep` the same way from Task 19.
- The move step is a phase machine over `Values["phase"]`, one segment per `Complete(Repeat: true)`: phase 0 opens a `MoveRegion` Ask (the unit's models still in play, `allowance − used`, the impassable volume ids, the difficult volume ids, `GfNames.DifficultCap` — `null` for a `Strider`/`Flying` unit, which the cap does not bind — and `MoveStep.Containers(…)`) and, when it closes with `Destinations`, applies them with the server's settled Z (`Battlefield.Settle`), sets the unit's `moved` condition and adds the segment's `distance_used_tenths` (the largest `Movement.Check(...).Distance` over the moved models, in tenths of an inch); phase 1 pushes `dangerous-terrain` when a moved model entered or crossed a `Dangerous` volume (p.11); phase 2 is the casting checkpoint — `Windows.Run(ctx, BeforeAttacking)`, a react-or-pass Ask to the actor's own team when `GfHooks.ReactionsAt` finds a caster with tokens, nothing otherwise. **A finished cast returns the step to phase 0 when budget remains and to phase 2 otherwise, in both cases with a fresh history, so the window reopens: p.13 is *"spend as many tokens as the spell's value to try casting **one or more spells** (only one try per spell)"*, and one window per activation would allow only one** (action sequences → Windows: "checkpoint a move mid-path … then resume the move with its remaining budget"). A `Passed` answer to the move Ask is a zero move. The `distance_used_tenths` value in the test below is the segment total. The 1" separation and coherency are not enforced here (flagged by the client; `architecture.md` → Rules enforcement) — `ignoreSeparation` only changes the Ask's prompt for a charge.
- **Embarking and disembarking are this step's, because p.14 makes them a move**: *"units may enter/exit by using any move action"*. `MoveStep.Containers` decides what the Ask offers, and `Apply` handles the three destination shapes a `Destinations` answer can now carry:
  - **`To is Inside`** — embarking. Nothing is measured and no path is swept: p.15 says *"they ignore the 1" move restriction when embarking (only one model needs to reach the Transport for everyone to embark)"*, so the whole unit may name the container even though `ContainersInReach` found it in reach of a single model, and the segment's `distance_used_tenths` does not grow. The 1" restriction itself is not enforced anywhere in this plan (the client flags it; `architecture.md` → Rules enforcement), so *ignoring* it needs no code — only the Ask's prompt and the client's decision not to flag it. The one guard is state integrity: an `Inside` whose container is not a model on the table is dropped.
  - **`From is Inside`, `To is OnTable`** — disembarking. An occupant has no start position, so there is again no distance and no swept path; the whole constraint is p.15's *"must stay fully within 6" of it when exiting"*, which is `ExitPlacement` over the geometry plan's `Containment.BaseFullyWithin`. It is checked on the server rather than trusted from the client because it is the one containment rule that is geometry; a placement outside the radius is not applied and that model stays inside.
  - **table to table** — unchanged.
- **"Never both in one activation" (p.14) lives in `Containers`**, which reads `ContainerMovesThisActivation` and returns `null` for the exit after an embark and `null` for the enterable list after a disembark. It is the legal set that enforces it, the way every other rule here does — the answer is applied as sent (`architecture.md` → Rules enforcement), and `LegalSet.Contains` under `strict` enforcement checks a `move_region` answer's model ids only (game server plan, Task 8). A **Charge** offers the exit but never the enterable list: p.15 says *"Units may use Charge actions to disembark"*, and charging a transport is an attack on it rather than a way into it.
- **Capacity is never checked.** [Game flow](../specs/2026-08-28-game-flow-design.md) → Inside a container rules it explicitly: *"Capacity is not enforced."* `Transport(X)`'s parameter is carried by the catalog entry and read by nothing; `GfMoves.IsContainer` asks only whether the name is there.
- **`Hold` from inside a container is legal and changes nothing** — the unit stays inside, and `shoot-or-not` finds a target only among the container's other occupants and the container itself (`EligibleModels`, Task 13). A unit inside cannot be *charged*: `charge-target` measures to the target's living models and an occupant has none (Task 16).

- `shoot-or-not`: a Shaken unit spends the activation idle and stops being Shaken — `Finish([ConditionSet(unit, shaken, false)])` (p.10); no valid target → `Complete`; else `Options([shoot, pass])` and `shoot` pushes `shooting`. `declare-targets`: `Assignment(Items = groups, Targets = valid target units, MaxDistinctTargets = 2)` (p.8: "up to two different targets"); `fire-groups` reads the previous step's `Answer` (`Assigned`) from `Frame.Frame.Steps[0].Answer`, records every group's attack counts in its own `Values` as `attacks_<i>` and one `attacks_<i>_q<n>` per Quality value, plus `defender_models_<i>`, before pushing anything (ruling 3: p.8 fires the weapons simultaneously, so no group is shortened, capped lower, or left blocking a lone joined hero by an earlier group's casualties), then pushes one `attack-sequence` per group, `Takedown` groups first (p.14), and completes when the last has finished.

Spec: action sequences → Grimdark Future's sequences (Shooting; the four actions; Dangerous terrain — "invoked by moves through it, activating in it"), Windows (invoked, mid-move); game flow → Inside a container (enter or exit with any move action, never both in one activation, exit within 6", capacity not enforced); whole-design review → Gaps filled (the four OPR actions are sequences); `architecture.md` → How the client stays responsive (parameters, not an answer); rulebook p.15 `Transport(X)`, p.21 solid buildings; rulebook p.7 (Hold/Advance/Rush distances, the difficult-terrain cap on p.11), p.8 (targets declared before rolling; weapon groups; two targets), p.10 (Shaken idle), p.11 (dangerous terrain: a model that moves in or through it, or is activated in it, rolls one die or its Tough value in dice; each 1 is a wound), p.13 (`Caster`: one or more spells, one try per spell).

- [ ] **Step 1: Extend `Armies.State` with `IReadOnlyList<TerrainPlacement>? terrain = null` (placed on the sample table) and write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfShootingTests
{
    static Destinations MoveAll(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location, new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    [Fact]
    public void Advance_moves_six_inches_then_offers_shooting_which_declares_groups_and_targets()
    {
        var squad = Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        var a = Unit("A", "4+", "4+", [], 2, []); var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([squad], [a, b], x1: -10, x2: 10), 6, 6, 6, 1, 1, 1);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(squad.Id, [], [], GfNames.Advance));
        var move = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((6.0, 3), (move.Allowance, move.ModelIds.Count));
        sim.Answer(Samples.Team1, MoveAll(sim, squad, 2));
        Assert.Contains(GfNames.Moved, sim.Unit(squad.Id).Conditions);
        Assert.Equal(20, sim.Steps("move").First().Values!["distance_used_tenths"]);                           // the segment; the step completes again after the checkpoint
        var shoot = sim.AskFor(Samples.Team1);
        Assert.Equal(["shoot", "pass"], Assert.IsType<Options>(shoot.Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        var declare = Assert.IsType<Assignment>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((["Rifle"], 2, 2), (declare.Items.Select(i => i.Id).ToArray(), declare.Targets.Count, declare.MaxDistinctTargets));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = a.Id }));
        var attacks = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(a.Id, Assert.Single(attacks).Context.TargetUnitIds[0]);
        Assert.Equal(GfNames.Advance, attacks[0].Context.Action);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [a.Models[0].Id] = 3 }));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_takedown_weapon_type_splits_into_one_group_per_model()
    {
        // 3.5.1 p.14 gives the pick to "This model", so two models carrying one takedown weapon type are two groups, each
        // naming its own target and rolling its own attack.
        var pair = Unit("Pair", "4+", "4+", [], 2, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([pair], [t], x1: -6, x2: 6), 6, 1, 6, 1);                                    // each group hits once, and each block fails
        sim.Start(GfNames.Shooting, new SequenceContext(pair.Id, [], [], GfNames.Hold));
        var declare = Assert.IsType<Assignment>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(["Long Rifle (1)", "Long Rifle (2)"], declare.Items.Select(i => i.Id).OrderBy(x => x));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Long Rifle (1)"] = t.Id, ["Long Rifle (2)"] = t.Id }));
        Assert.Equal(1, sim.Steps("determine-attacks")[0].Values!["attacks"]);                              // one model's own attack, not the weapon type's two
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[2].Id]));                                    // the first takedown model names the third model
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[0].Id]));                                    // the second makes its own, different choice
        Assert.IsType<OnShelf>(sim.Model(t.Models[2].Id).Location);
        Assert.IsType<OnShelf>(sim.Model(t.Models[0].Id).Location);
        Assert.Equal(1, sim.Alive(t.Id));                                                                   // the model neither named is untouched
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void The_second_group_rolls_the_count_settled_before_the_first_group_fired()
    {
        // p.8 fires a unit's weapons simultaneously, so the Carbine rolls its two
        // attacks even though the only model within its 12" was killed by the Blaster group that was pushed first.
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Blaster", "24\"", 1), Gun("Carbine", "12\"", 2));
        var pair = Unit("Pair", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(State([shooter], [pair], x1: -5, x2: 5), 6, 1, 6, 6, 1, 1);                       // Blaster hits and wounds; the Carbine hits twice and wounds twice
        var far = pair.Models[1].Id;
        var seat = ((OnTable)sim.Model(far).Location).Placement.Position;
        sim.Append([new ModelsMoved([new ModelRelocation(far, sim.Model(far).Location,
            new OnTable(new Placement(seat with { X = seat.X + 10 }, new Yaw(0))))])]);                    // 20" out: inside the Blaster's range, outside the Carbine's
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Blaster"] = pair.Id, ["Carbine"] = pair.Id }));
        var fire = sim.Steps("fire-groups").First();
        Assert.Equal((1, 2), (fire.Values!["attacks_0"], fire.Values["attacks_1"]));                       // both counts settled before either group fired
        Assert.Equal((1, 2), (fire.Values["attacks_0_q4"], fire.Values["attacks_1_q4"]));                    // one Quality in the unit, so one key each
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));    // the near model dies to the Blaster
        var determine = sim.Steps("determine-attacks");
        Assert.Equal(2, determine.Count);
        Assert.Equal((2, 0), (determine[1].Values!["attacks"], determine[1].Values["eligible_models"]));   // the recorded count stands over an empty eligible set
        Assert.Equal(2, sim.Steps("roll-to-hit")[1].Rolls.Single().Dice.Count);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [far] = 2 }));
        Assert.Equal(0, sim.Alive(pair.Id));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void The_second_groups_blast_cap_is_the_defender_model_count_settled_before_the_first_group_fired()
    {
        // p.13's cap of "as many hits as models in the target unit" is fixed at the same moment as the attack counts (p.8: all
        // weapons fire simultaneously), so the Mortar's Blast(3) is still capped at 2 after the Blaster killed one of the two
        // models. It is one number under one name, because p.14's "until all other models have been killed" counts the same
        // models at the same moment.
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Blaster", "24\"", 1), Gun("Mortar", "30\"", 1, P("Blast", 3)));
        var pair = Unit("Pair", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(State([shooter], [pair], x1: -5, x2: 5), 6, 1, 6, 1, 1);                          // Blaster hits and wounds; the Mortar hits, then two blocks fail
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Blaster"] = pair.Id, ["Mortar"] = pair.Id }));
        var fire = sim.Steps("fire-groups").First();
        Assert.Equal((2, 2), (fire.Values!["defender_models_0"], fire.Values["defender_models_1"]));       // both counts settled before either group fired
        Assert.Equal((1, 1), (fire.Values["in_cover_0"], fire.Values["in_cover_1"]));                      // p.11's majority frozen with them, so no group can shoot a unit out of cover
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));    // one model dies to the Blaster
        Assert.Equal(2, sim.Steps("roll-to-hit")[1].Values!["hit_multiplier"]);                            // the recorded 2, not the one model still standing
        Assert.Equal(2, sim.Steps("roll-to-block")[1].Rolls.Single().Dice.Count);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pair.Models[1].Id] = 2 }));
        Assert.Equal(0, sim.Alive(pair.Id));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Fast_and_slow_change_the_allowance_and_rush_never_offers_shooting()
    {
        var fast = Unit("Fast", "4+", "4+", [], 2, [P("Fast")], Gun("Rifle", "24\"", 1));
        var sim = Sim.Gf(State([fast], [Unit("T", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 1);
        sim.Start(GfNames.RushSeq, new SequenceContext(fast.Id, [], [], GfNames.Rush));
        Assert.Equal(16.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        sim.Answer(Samples.Team1, new Passed());
        Assert.Null(sim.AskForOrNull(Samples.Team1)); Assert.Empty(sim.State.Projection.Stack);
        var slow = Unit("Slow", "4+", "4+", [], 2, [P("Slow")], Gun("Rifle", "24\"", 1));
        var sim2 = Sim.Gf(State([slow], [Unit("T", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 1);
        sim2.Start(GfNames.AdvanceSeq, new SequenceContext(slow.Id, [], [], GfNames.Advance));
        Assert.Equal(4.0, Assert.IsType<MoveRegion>(sim2.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void Hold_with_a_shaken_unit_is_idle_and_clears_shaken_and_hold_without_a_target_asks_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 2, [], Gun("Pistol", "6\"", 1));
        var sim = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions); Assert.Empty(sim.State.Projection.Stack);
        var sim2 = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        sim2.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.Null(sim2.AskForOrNull(Samples.Team1)); Assert.Empty(sim2.State.Projection.Stack);           // pistols out of range: nothing to ask
    }

    [Fact]
    public void Moving_into_dangerous_terrain_tests_only_the_models_that_entered_it_and_wounds_on_ones()
    {
        var swamp = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("swamp"), new Point3(-4, -18, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(8, 8), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);
        var pair = Unit("Pair", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([pair], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [swamp]), 1, 3);
        sim.Start(GfNames.RushSeq, new SequenceContext(pair.Id, [], [], GfNames.Rush));
        Assert.Empty(sim.Steps("test"));                                                                   // p.11: nobody was activated in it
        sim.Answer(Samples.Team1, MoveAll(sim, pair, 8));
        var test = sim.Steps("test").Single().Rolls.Single();
        Assert.Equal((2, 1), (test.Dice.Count, test.Dice.Count(d => !d.Success)));                          // both models entered: two dice
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team1).Legal).Amount);                 // the unit's own team allocates its wound
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(pair.Id));
    }

    [Fact]
    public void Cover_terrain_needs_a_majority_of_the_units_models_not_half()
    {
        // p.11: "If the majority of models in a unit are fully inside a piece of cover terrain". A majority is more than half,
        // so one of two is not one. The test is the base centre inside the volume.
        var wood = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(6, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(1, 1), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var pair = Unit("Pair", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(State([shooter], [pair], x1: -6, x2: 6, terrain: [wood]), 6, 5);                  // a hit, then a 5 that blocks only with the cover bonus
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [pair.Id], shooter.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var block = sim.Steps("roll-to-block").Single();
        Assert.DoesNotContain(block.Rolls.Single().Modifiers, m => m.Source == "cover");                   // Armies.State puts only Pair's first model at (6, -18.5), inside the wood
        Assert.False(block.Rolls.Single().Dice[0].Success);
        Assert.Equal(1, block.Values!["wounds"]);

        var both = Sim.Gf(State([shooter], [pair], x1: -6, x2: 6, terrain: [wood]), 6, 5);
        var (first, second) = (pair.Models[0].Id, pair.Models[1].Id);
        var inside = ((OnTable)both.Model(first).Location).Placement.Position;
        both.Append([new ModelsMoved([new ModelRelocation(second, both.Model(second).Location,
            new OnTable(new Placement(inside with { X = inside.X + 0.2 }, new Yaw(0))))])]);               // both centres inside the 1"x1" wood
        both.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [pair.Id], shooter.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var covered = both.Steps("roll-to-block").Single();
        Assert.Contains(covered.Rolls.Single().Modifiers, m => m.Source == "cover" && m.Amount == 1);      // two of two is a majority
        Assert.True(covered.Rolls.Single().Dice[0].Success);
        Assert.Equal(0, covered.Values!["wounds"]);
        Assert.Equal(2, both.Alive(pair.Id));
        Assert.Empty(both.State.Projection.Stack);
    }

    [Fact]
    public void A_move_action_embarks_the_whole_unit_when_one_model_reaches_the_transport()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 3, []);
        var sim = Sim.Gf(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 1);
        // Armies.State puts each unit in its own row 12" away, so park the bus 3" from the nearest trooper.
        var near = ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position;
        sim.Append([new ModelsMoved([new ModelRelocation(bus.Models[0].Id, sim.Model(bus.Models[0].Id).Location,
            new OnTable(new Placement(near with { X = near.X + 3 }, new Yaw(0))))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(troops.Id, [], [], GfNames.Advance));
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([bus.Models[0].Id], ask.Containment!.Enterable);                                     // p.14: within 6" of one model, by a clear path
        Assert.Null(ask.Containment.ExitingId);
        Assert.Equal(3, ask.ModelIds.Count);
        sim.Answer(Samples.Team1, new Destinations(troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(bus.Models[0].Id))).ToList()));
        Assert.All(troops.Models, m => Assert.Equal(bus.Models[0].Id, Assert.IsType<Inside>(sim.Model(m.Id).Location).ContainerId));
        Assert.Equal(0, sim.Steps("move").First().Values!["distance_used_tenths"]);                        // p.14: embarking measures nothing
        Assert.Empty(Lookup.LivingModels(sim.State, troops.Id));
        Assert.Equal(3, Lookup.AliveModels(sim.State, troops.Id).Count);
    }

    [Fact]
    public void A_unit_inside_disembarks_within_six_inches_and_may_not_embark_again_this_activation()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 1);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved(troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(troops.Id, [], [], GfNames.Advance));
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (ask.Containment!.ExitingId!.Value, ask.Containment.ExitWithin!.Value));
        Assert.Empty(ask.Containment.Enterable);                                                          // p.14: never both in one activation
        Assert.Equal(2, ask.ModelIds.Count);                                                              // named although neither has a position
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        Placement Out(double dx) => new(seat with { X = seat.X + dx }, new Yaw(0));
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(troops.Models[0].Id, new Inside(container), new OnTable(Out(4))),          // inside the radius
            new ModelRelocation(troops.Models[1].Id, new Inside(container), new OnTable(Out(9))),          // outside it: refused
        ]));
        Assert.Equal(seat.X + 4, ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position.X, 6);
        Assert.Equal(container, Assert.IsType<Inside>(sim.Model(troops.Models[1].Id).Location).ContainerId);
        Assert.Contains(GfNames.Moved, sim.Unit(troops.Id).Conditions);
        Assert.Equal(0, sim.Steps("move").First().Values!["distance_used_tenths"]);                        // no start position, so nothing is measured
        Assert.Equal((false, true), GfMoves.ContainerMovesThisActivation(sim.State, troops.Id));      // p.15: it disembarked, so it may not embark again
    }

    [Fact]
    public void One_model_standing_in_dangerous_terrain_tests_at_activation_and_only_that_model()
    {
        var lava = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("lava"), new Point3(-12, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);
        var pair = Unit("Pair", "4+", "4+", [], 2, []);   // model 1 at y −18.5, model 2 at y −17: only the first is inside
        var sim = Sim.Gf(State([pair], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [lava]), 1);
        sim.Start(GfNames.HoldSeq, new SequenceContext(pair.Id, [], [], GfNames.Hold));
        Assert.Single(sim.Steps("test").Single().Rolls.Single().Dice);                                      // p.11: activated in it, one model, one die
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(pair.Id));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfShooting.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed record WeaponGroup(string Name, IReadOnlyList<Guid> WeaponIds, bool Takedown);

public static class GfShooting
{
    public static Sequence Shooting() => new(GfNames.Shooting, [new DeclareTargetsStep(), new FireGroupsStep()]);

    /// <summary>
    /// One group per ranged weapon type over the living models (p.8). A Limited weapon already fired this game is left out (p.14).
    /// A Takedown weapon type splits into one group per model, because p.14 gives the pick to "This model": each takedown model
    /// names its own target and rolls its own dice. The split group's name carries the model's ordinal, since the name is the key
    /// the target declaration and fire-groups use, and two groups sharing one name would collapse into a single target.
    /// </summary>
    public static IReadOnlyList<WeaponGroup> Groups(GameState state, PropertySet props, Guid unit)
    {
        var used = EventApplier.EffectiveLog(state.Applier.Log).Where(e => e.Payload is SequenceStarted).SelectMany(e => ((SequenceStarted)e.Payload).Context.WeaponIds).ToHashSet();
        var groups = new List<WeaponGroup>();
        foreach (var g in Lookup.AliveModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => w.Stats.ContainsKey(GfNames.Range))
                     .Where(w => !(w.Properties.Any(p => p.Name == "Limited") && used.Contains(w.Id))).GroupBy(w => w.Name))
        {
            var ids = g.Select(w => w.Id).ToList();
            if (!GfAttack.IsTakedownGroup(state, ids)) { groups.Add(new WeaponGroup(g.Key, ids, false)); continue; }
            groups.AddRange(ids.Select((id, i) => new WeaponGroup(ids.Count == 1 ? g.Key : $"{g.Key} ({i + 1})", [id], true)));
        }
        return groups;
    }

    public static IReadOnlyList<Guid> ValidTargets(StepContext ctx, Guid unit, WeaponGroup group)
    {
        var team = ctx.TeamOf(unit);
        // The in-play reading: an enemy unit inside a container is still a candidate, and EligibleModels is what rules it out (or, for a fellow occupant, in).
        return ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team && Lookup.AliveModels(ctx.State, u.Id).Count > 0).Select(u => u.Id).Where(target =>
        {
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, unit, target, group.WeaponIds, AttackKind.Shooting, ctx.Frame.Context.Action));
            return GfAttack.EligibleModels(ctx.State, live, unit, target, group.WeaponIds, AttackKind.Shooting).Count > 0;
        }).ToList();
    }

    sealed class DeclareTargetsStep : IStep
    {
        public string Name => "declare-targets";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit).Select(g => (Group: g, Targets: ValidTargets(ctx, unit, g))).Where(x => x.Targets.Count > 0).ToList();
            if (groups.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("declare_targets", "Declare weapon groups and up to two targets", ctx.ActorTeam!.Value,
                new Assignment(groups.Select(x => new AskOption(x.Group.Name, x.Group.Name, x.Group.WeaponIds)).ToList(), groups.SelectMany(x => x.Targets).Distinct().ToList(), 2))]);
        }
    }

    /// <summary>
    /// p.8: a unit's weapons are fired simultaneously, so every group's attack counts and the defending unit's model count are settled
    /// on this frame before the first group is pushed, and a later group is neither shortened, nor capped lower, nor left blocking a
    /// joined hero on its own Defense by an earlier group's casualties. Two phases over
    /// `attacks_0`: record, then push one `attack-sequence` at a time, Takedown groups first (p.14).
    ///
    /// A group's attacks are recorded per Quality value as `attacks_&lt;i&gt;_q&lt;n&gt;`, since p.4 tests each against *"the model's Quality
    /// value"* and a joined hero brings its own, with `attacks_&lt;i&gt;` kept as the group's total for a reader that wants one number.
    /// `defender_models_&lt;i&gt;` is read by p.13's `Blast` cap and by p.14's *"until all other models have been killed"*.
    /// `in_cover_&lt;i&gt;` is p.11's majority, settled here for the same reason and read by `roll-to-block`.
    /// </summary>
    sealed class FireGroupsStep : IStep
    {
        public string Name => "fire-groups";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Frame.Frame.Steps[0].Answer is not Assigned assigned) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit).Where(g => assigned.Targets.ContainsKey(g.Name)).OrderByDescending(g => g.Takedown).ThenBy(g => g.Name).ToList();
            if (groups.Count == 0) return new Complete([]);                                   // nothing was assigned a target: also the guard that bounds the recording phase
            if (!ctx.Frame.Values.ContainsKey("attacks_0"))
            {
                var settled = new Dictionary<string, int>();
                foreach (var (g, i) in groups.Select((g, i) => (g, i)))
                {
                    var byQuality = GfAttack.AttacksAgainst(ctx, unit, assigned.Targets[g.Name], g.WeaponIds, AttackKind.Shooting);
                    settled[$"attacks_{i}"] = byQuality.Sum(x => x.Attacks);
                    foreach (var (quality, attacks) in byQuality) settled[$"attacks_{i}_q{quality}"] = attacks;
                    settled[$"defender_models_{i}"] = Lookup.LivingModels(ctx.State, assigned.Targets[g.Name]).Count;   // p.13's Blast cap and p.14's Hero sunset, frozen with the counts
                    settled[$"in_cover_{i}"] = GfAttack.InCoverAgainst(ctx, unit, assigned.Targets[g.Name], g.WeaponIds) ? 1 : 0;   // p.11's majority, frozen with them
                }
                return new Complete([], settled, Repeat: true);
            }
            var pushed = ctx.History.Children.Count;
            if (pushed < groups.Count && (pushed == 0 || ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned))
                return new Push(GfNames.AttackSequence, new SequenceContext(unit, [assigned.Targets[groups[pushed].Name]], groups[pushed].WeaponIds, ctx.Frame.Context.Action));
            return new Complete([]);
        }
    }
}
```

- [ ] **Step 3: Write `GfMoves.cs` and widen `Windows.Run`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMoves
{
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new CastingWindowStep(), new ShootOrNotStep()]);
    public static Sequence Advance() => new(GfNames.AdvanceSeq, [new ActivationTerrainStep(), new MoveStep(GfNames.Advance, false), new ShootOrNotStep()]);
    public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new MoveStep(GfNames.Rush, false)]);
    public static Sequence DangerousTerrain() => new(GfNames.DangerousTerrain, [new DangerousTestStep(), new GfAttack.PushStep("deal", GfNames.DealWounds, ctx => new SequenceContext(null, [ctx.Frame.Context.ActorUnitId!.Value], [], null))]);

    static LiveSet Lone(StepContext ctx, Guid unit, string? action) => LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, null, action, null, Battlefield.TerrainUnder(ctx.State, unit)));

    static IReadOnlyList<WorldVolume> Declared(IReadOnlyList<WorldVolume> terrain, TerrainDeclarations flag) => terrain.Where(v => v.Volume.Declarations.HasFlag(flag)).ToList();

    /// <summary>p.11: one die per model, or the model's Tough value in dice.</summary>
    public static int DangerousDice(GameState state, IEnumerable<Guid> models) => models.Sum(m => Lookup.ModelParameter(state, m, "Tough") ?? 1);

    /// <summary>
    /// p.15: "units can't both embark/disembark as part of the same activation". Read from the log, bounded by the activation the
    /// way GfHooks.FoughtMeleeThisActivation is — every relocation into or out of a container is a models_moved, so this is a scan,
    /// never a flag. A hand move from the menu counts, which is what a player doing it by hand should expect.
    /// </summary>
    public static (bool Embarked, bool Disembarked) ContainerMovesThisActivation(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted s
            && s.Context.Action is GfNames.Hold or GfNames.Advance or GfNames.Rush or GfNames.Charge && s.Context.ActorUnitId == unit);
        if (activation < 0) return (false, false);
        var mine = Lookup.AliveModels(state, unit).ToHashSet();
        var moves = log.Skip(activation).Select(e => e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(r => mine.Contains(r.ModelId)).ToList();
        return (moves.Any(r => r.To is Inside), moves.Any(r => r.From is Inside));
    }

    /// <summary>
    /// p.15: the containers a model of this unit could reach — a friendly model with Transport(X) on the table, within the move's
    /// remaining allowance of one of the unit's models, by a straight path crossing no impassable volume (the same reading as
    /// charge-target's). Only one model has to reach it, so the answer is per container, not per model. Capacity is not checked:
    /// game flow rules it unenforced and Transport's (X) is carried unread.
    /// </summary>
    /// <summary>p.15: a model that can hold others — `Transport` is on it. The (X) is carried and never read; game flow rules capacity unenforced. The name lives here because `Effects/` may not name an OPR rule (constraint 1, Task 18).</summary>
    public static bool IsContainer(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Transport) is not null;

    public static IReadOnlyList<Guid> ContainersInReach(StepContext ctx, Guid unit, double remaining, IReadOnlyList<WorldVolume> impassable)
    {
        var team = ctx.TeamOf(unit);
        var movers = Lookup.LivingModels(ctx.State, unit);
        if (movers.Count == 0) return [];
        return ctx.Projection.Models.Values.Where(c => c.Location is OnTable && c.UnitId != unit && IsContainer(ctx.State, c.Id) && ctx.TeamOf(c.UnitId) == team)
            .Where(c => movers.Any(m => Battlefield.Distance(ctx.State, m, c.Id) <= remaining
                && !Movement.Check(Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, c.Id)!.Value.Placement.Position.Horizontal,
                    remaining, impassable, [], null, ctx.State.Board.Table.Size).CrossesImpassable))
            .Select(c => c.Id).ToList();
    }

    /// <summary>
    /// p.15 and p.21: "must stay fully within 6" of it when exiting". The settled placement when the whole base is inside the
    /// radius of the container's base, null otherwise — the one containment constraint that is geometry, so it is checked on the
    /// server rather than trusted from the client, and a model whose placement is refused simply stays inside.
    /// </summary>
    public static Placement? ExitPlacement(GameState state, Guid modelId, Guid container, Placement proposed, double within)
    {
        var settled = Battlefield.Settle(state, proposed);
        return Battlefield.Placed(state, container) is { } c && Containment.BaseFullyWithin(Battlefield.At(state, modelId, settled), c, within) ? settled : null;
    }

    /// <summary>p.11 "is activated in it": the unit's living models whose base centre is inside a Dangerous volume right now.</summary>
    public static IReadOnlyList<Guid> StandingInDangerous(GameState state, Guid unit)
    {
        var dangerous = Declared(Battlefield.Terrain(state), TerrainDeclarations.Dangerous);
        return dangerous.Count == 0 ? [] : Lookup.LivingModels(state, unit)
            .Where(m => Containment.VolumesContaining(Battlefield.Placed(state, m)!.Value, dangerous).Count > 0).ToList();
    }

    /// <summary>p.11's third trigger, the first step of every action sequence: a model activated in dangerous terrain tests before it does anything else.</summary>
    public sealed class ActivationTerrainStep : IStep
    {
        public string Name => "activation-terrain";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0) return new Complete([]);                               // the test has been run
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Lone(ctx, unit, ctx.Frame.Context.Action).Has(Side.Attacker, "Flying")) return new Complete([]);   // p.13: ignores terrain effects
            var standing = StandingInDangerous(ctx.State, unit);
            if (standing.Count == 0) return new Complete([]);
            // Write the dice count first, then push: the child reads dangerous_dice off this frame, so it must be settled before the push.
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete([], new Dictionary<string, int> { ["dangerous_dice"] = DangerousDice(ctx.State, standing) }, Repeat: true);
        }
    }

    /// <summary>The casting checkpoint on its own: Hold has no move to hang it on. p.13 allows more than one spell, so a finished cast reopens the window with a fresh history.</summary>
    public sealed class CastingWindowStep : IStep
    {
        public string Name => "casting-window";
        public StepOutcome Run(StepContext ctx) =>
            Windows.Run(ctx, GfNames.BeforeAttacking) ?? new Complete([], null, Repeat: ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq));
    }

    /// <summary>
    /// One segment per run of its phase machine, carried in Values["phase"]: 0 ask for a move → apply it (Repeat); 1 dangerous terrain test (Push) → (Repeat);
    /// 2 the casting checkpoint: react-or-pass to the actor's own team; a cast with budget left goes back to phase 0 (Repeat), otherwise the step completes.
    /// </summary>
    public sealed class MoveStep(string action, bool ignoreSeparation) : IStep
    {
        public string Name => "move";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var live = Lone(ctx, unit, action);
            var allowance = StatReader.ActionDistance(live, Side.Attacker, action);
            var used = ctx.Frame.Value("distance_used_tenths") / 10.0;
            var terrain = Battlefield.Terrain(ctx.State);
            switch (ctx.Frame.Value("phase"))
            {
                case 0 when ctx.History.LastClosed("move") is not { } moved:
                    var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList();
                    var difficult = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Difficult)).Select(v => v.Id).ToList();
                    var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");
                    var containment = Containers(ctx, unit, action, Math.Max(0, allowance - used), Declared(terrain, TerrainDeclarations.Impassable));
                    var prompt = containment?.ExitingId is not null ? $"Disembark: place every model fully within {GfNames.ExitWithin:0.#}\" of the transport"
                        : ignoreSeparation ? "Charge: move into contact" : $"Move up to {allowance - used:0.#}\"";
                    return new OpenAsks([ctx.Ask("move", prompt, ctx.ActorTeam!.Value,
                        new MoveRegion(Lookup.AliveModels(ctx.State, unit), Math.Max(0, allowance - used), impassable, difficult, strider ? null : GfNames.DifficultCap, containment), allowPass: true)]);
                case 0:
                {
                    var (changes, segment, dangerousDice) = Apply(ctx, unit, live, allowance, ctx.History.LastClosed("move")!.Single as Destinations, terrain);
                    return new Complete(changes, new Dictionary<string, int>
                        { ["distance_used_tenths"] = ctx.Frame.Value("distance_used_tenths") + segment, ["dangerous_dice"] = dangerousDice, ["phase"] = dangerousDice > 0 ? 1 : 2 }, Repeat: true);
                }
                case 1 when ctx.History.Children.Count == 0: return new Push(GfNames.DangerousTerrain, new SequenceContext(unit, [], [], action));
                case 1: return new Complete([], new Dictionary<string, int> { ["phase"] = 2 }, Repeat: true);
                default:
                    if (Windows.Run(ctx, GfNames.BeforeAttacking) is { } window) return window;
                    // p.13: a caster may try more than one spell in an activation, so a finished cast reopens the window — back to the
                    // move when budget remains, otherwise round again on the checkpoint. A pass leaves no casting child and the step ends.
                    var cast = ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq);
                    return new Complete([], new Dictionary<string, int> { ["phase"] = allowance - used > 0 ? 0 : 2 }, Repeat: cast);
            }
        }

        /// <summary>
        /// p.15: what containment this segment allows. Inside a container → the exit, unless the unit embarked earlier in this
        /// activation. Outside → the containers in reach, unless it disembarked earlier in this activation, and never on a Charge —
        /// p.15 lets a Charge disembark, and charging a transport is an attack on it, not a way into it. Null when containment plays
        /// no part in this move, which is every move on open table. This is where "never both in one activation" is enforced.
        /// </summary>
        static MoveContainment? Containers(StepContext ctx, Guid unit, string action, double remaining, IReadOnlyList<WorldVolume> impassable)
        {
            var (embarked, disembarked) = ContainerMovesThisActivation(ctx.State, unit);
            if (Lookup.ContainerOf(ctx.State, unit) is { } inside)
                return embarked ? null : new MoveContainment([], inside, GfNames.ExitWithin);
            if (disembarked || action == GfNames.Charge) return null;
            var enterable = ContainersInReach(ctx, unit, remaining, impassable);
            return enterable.Count > 0 ? new MoveContainment(enterable, null, null) : null;
        }
        // Allowance, DifficultVolumeIds and DifficultCap are unbound whenever Containment.ExitingId is set:
        // an occupant has no position, so nothing is swept and no distance is measured from a start. The
        // region is the container's base grown by ExitWithin. The three are still populated from the action
        // so the record has one shape, and the client ignores them on a disembark (client shell -> MoveRuler).

        /// <summary>The destinations settled onto the surface, the moved condition, the segment's distance in tenths, and the dangerous-terrain dice the models that moved in or through a Dangerous volume owe (p.11).</summary>
        static (List<StateChange> Changes, int SegmentTenths, int DangerousDice) Apply(StepContext ctx, Guid unit, LiveSet live, double allowance, Destinations? d, IReadOnlyList<WorldVolume> terrain)
        {
            var changes = new List<StateChange>(); var used = 0.0; var moves = new List<ModelRelocation>(); var exposed = new List<Guid>();
            var impassable = Declared(terrain, TerrainDeclarations.Impassable);
            var difficult = Declared(terrain, TerrainDeclarations.Difficult);
            var dangerous = Declared(terrain, TerrainDeclarations.Dangerous);
            var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");   // p.14: Strider ignores difficult terrain, Flying all terrain effects
            var flying = live.Has(Side.Attacker, "Flying");                                          // Strider does *not* skip a dangerous terrain test; only Flying does
            foreach (var m in d?.Moves ?? [])
            {
                // p.15: embarking costs no measured distance and sweeps no path — "only one model needs to reach the Transport for
                // everyone to embark", and the 1" restriction is ignored (it is client-flagged here anyway; architecture.md → Rules enforcement).
                if (m.To is Inside into)
                {
                    if (ctx.Projection.Models[into.ContainerId].Location is OnTable) moves.Add(m);          // never point Inside at something that is not on the table
                    continue;
                }
                if (m.To is not OnTable to) continue;
                // p.14: a disembarking model has no start position, so nothing is measured or swept; the radius is the whole constraint.
                if (m.From is Inside from)
                {
                    if (ExitPlacement(ctx.State, m.ModelId, from.ContainerId, to.Placement, GfNames.ExitWithin) is { } exit)
                        moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(exit)));
                    continue;
                }
                if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
                var settled = Battlefield.Settle(ctx.State, to.Placement);
                used = Math.Max(used, Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, strider ? null : GfNames.DifficultCap, ctx.State.Board.Table.Size).Distance);
                // p.11 is "moves in or through", which the landing position alone does not answer (geometry plan → VolumesCrossed is the in-or-through test).
                if (Movement.VolumesCrossed(start, settled.Position.Horizontal, dangerous).Count > 0) exposed.Add(m.ModelId);
                moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
            }
            if (moves.Count > 0) { changes.Add(new ModelsMoved(moves)); changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Moved, true)); }
            return (changes, (int)Math.Round(used * 10), flying ? 0 : DangerousDice(ctx.State, exposed));
        }
    }

    /// <summary>p.7–8: Hold and Advance may shoot. p.10: a Shaken unit spends the activation idle and stops being Shaken.</summary>
    public sealed class ShootOrNotStep : IStep
    {
        public string Name => "shoot-or-not";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return new Finish([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, false)]);
            if (ctx.History.Children.Count > 0) return ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete([]) : new OpenAsks([]);
            if (ctx.History.LastClosed() is { } closed) return closed.Single is Chosen { OptionId: "shoot" } ? new Push(GfNames.Shooting, ctx.Frame.Context) : new Complete([]);
            var canShoot = GfShooting.Groups(ctx.State, ctx.Properties, unit).Any(g => GfShooting.ValidTargets(ctx, unit, g).Count > 0);
            if (!canShoot) return new Complete([]);
            return new OpenAsks([ctx.Ask("shoot_or_not", "Shoot?", ctx.ActorTeam!.Value, new Options([new AskOption("shoot", "Shoot", [unit]), new AskOption("pass", "Don't shoot", [])]))]);
        }
    }

    /// <summary>p.11: one die per *affected* model (or that model's Tough value in dice); each 1 is a wound. The pushing step settled how many in Values["dangerous_dice"]; the fallback is the models standing in it now. Values["wounds"] feeds deal-wounds with no attacker.</summary>
    public sealed class DangerousTestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var owed = ctx.Frame.Parent?.Value("dangerous_dice") ?? 0;
            var dice = owed > 0 ? owed : DangerousDice(ctx.State, StandingInDangerous(ctx.State, unit));
            if (dice == 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            var record = ctx.Roll(new Dice.RollPlan(GfNames.DangerousTerrain, 2, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice);
            var wounds = Dice.RollOutcome.Failures(record);
            return wounds == 0 ? new Finish([], new Dictionary<string, int> { ["wounds"] = 0 }) : new Complete([], new Dictionary<string, int> { ["wounds"] = wounds });
        }
    }
}
```

`GfAttack.PushStep` gains an optional `Func<StepContext, SequenceContext>? context` and pushes `context?.Invoke(ctx) ?? ctx.Frame.Context`. `GfRules._sequences` gains `GfShooting.Shooting()`, `GfMoves.Hold()`, `GfMoves.Advance()`, `GfMoves.Rush()`, `GfMoves.DangerousTerrain()`. `Windows.Run` is unchanged: a repeating step's history is only its current segment, which is what both the checkpoint and the second spell need. `Takedown` resolves first, at the shooting model's own Quality (3.5.1's `Takedown` carries no *"Shoots at Quality 2+"*), and its group — one per takedown model, which is how `Groups` splits such a type — names the model its wounds land on in `attack-sequence`'s `pick-target-model` step (Task 13; ruling 7). `WeaponGroup.Takedown` and that step read one predicate, `GfAttack.IsTakedownGroup`, so a group cannot be ordered first without also being offered the pick.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfShootingTests`; expect 11 passed. Commit: `Add Hold, Advance and Rush, shooting with weapon groups, the casting checkpoint, dangerous terrain, and embarking and disembarking as part of a move`.**
