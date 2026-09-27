# Task 11: Grimdark Future's rolls, names, hooks and the battlefield reader

**Files:**
- Create: `server/game/Interpreter/Battlefield.cs`, `server/game/Rules/GrimdarkFuture/GfNames.cs`, `server/game/Rules/GrimdarkFuture/GfRolls.cs`, `server/game/Rules/GrimdarkFuture/GfHooks.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (Task 8's partial: the bodies)
- Test: `tests/Tabletop.GameServer.Tests/GfRollsTests.cs`

**Interfaces:**
- Consumes: Tasks 6–10; geometry plan's `Distance.EdgeToEdge/Vertical/InRange`, `LineOfSight.Check`, `LineOfSight.VisibleModels` (the geometry plan's per-unit visibility), `Containment.VolumesContaining`, `Bodies.AsBlockingVolume`, `Movement.Check`, `Settling.SurfaceHeightAt`; `TerrainPlacement.WorldVolumes()` (Content).
- Produces (namespace `Tabletop.GameServer.Interpreter`): `static class Battlefield` with `PlacedModel? Placed(GameState, Guid modelId)` (null off the table — on the shelf or inside a container), `PlacedModel At(GameState, Guid modelId, Placement)` (the body at a placement it does not hold yet: how a destination is judged, and the only way to place an occupant), `bool? InsideReach(GameState, Guid fromUnit, Guid toUnit)` (p.15: false when one is inside and the other out, true when both are in one container or the reaching unit is inside the other's container model, null when neither is inside and the answer is a measurement), `IRL<WorldVolume> Terrain(GameState)`, `IRL<WorldVolume> Blockers(GameState, Guid exceptUnitId)` (terrain plus every other unit's models as sight blockers — rulebook p.8 and p.11), `bool CanSee(GameState, Guid fromModel, Guid toModel)`, `double Distance(GameState, Guid modelA, Guid modelB)`, `double Vertical(GameState, Guid modelA, Guid modelB)`, `double? UnitDistance(GameState, Guid unitA, Guid unitB)` (closest pair of living models), `int ModelsInCoverTerrain(GameState, Guid unitId)` (living models whose base centre is inside a `Cover` volume) and `int ModelsVisibleFrom(GameState, Guid targetUnit, IRL<Guid> fromModels)` (how many of the unit's living models at least one of those models can see) — two counts, with the fraction they are compared against left to the ruleset, `IRL<string> TerrainUnder(GameState, Guid unitId)` (declaration names of volumes containing any living model — the `position_terrain` predicate's values), `Placement Settle(GameState, Placement clientPlacement)` (the server's Z from `Settling.SurfaceHeightAt`).
- Produces (namespace `Tabletop.GameServer.Rules.GrimdarkFuture`): `static class GfNames` (`Quality`, `Defense`, `Range`, `Attacks`; actions `Hold`, `Advance`, `Rush`, `Charge`; rolls `Hit = "hit"`, `DefenseRoll = "defense"`, `Morale = "morale"`, `Casting = "casting"`, `RollOff = "roll-off"`; conditions `Shaken = "shaken"`, `Fatigued = "fatigued"`, `Moved = "moved"`; window point `BeforeAttacking = "before-attacking"`; sequence names `HoldSeq = "hold"`, `AdvanceSeq = "advance"`, `RushSeq = "rush"`, `ChargeSeq = "charge"`, `Shooting = "shooting"`, `Melee = "melee"`, `AttackSequence = "attack-sequence"`, `ResolveHits = "resolve-hits"`, `DealWounds = "deal-wounds"`, `MoraleTest = "morale-test"`, `DangerousTerrain = "dangerous-terrain"`, `CastingSeq = "casting"`, `RollOffSeq = "roll-off"`; roll-off step names `RollOffRollStep = "roll-off"`, `RollOffOrderStep = "order"`; melee reach `MeleeHorizontal = 2`, `MeleeVertical = 4`; `ChargeCloseIn = 3`, `Consolidate = 3`, `FallBack = 1`, `Separation = 1`; `DifficultCap = 6`; `ExitWithin = 6` (p.15's *"fully within 6\" of it"*); `TokenCap = 6`; `CastRange = 18`; `AmbushDistance = 9`; and the scheduler plan's names, so there is one names class: phases `Deployment = "deployment"`, `Scouts = "scouts"`, `Rounds = Scheduler.SchedulerBase.RoundsPhase`; `Deploy = "Deploy"` (the `Activation.Action` of a deployment answer); rules `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `Transport`, `Takedown`, `Hero`; condition `SetAside = "set-aside"`; sequence name `TransportDestroyedSeq = "transport-destroyed"` (Task 19)); `GrimdarkFutureRules` (the partial in `GfRules.Sequences.cs`) implementing `Sequence`, `ActionSequence` (`Hold→hold`, …, null otherwise), `RollSpec`, `HitRoll`, `ReactionsAt`, `EndOfTurnSequences`, `OnRoundStarted`, `OnTurnEnded`; `static class GfRolls` with `RollSpec Spec(string roll)`, `bool Fatigued(StepContext, LiveSet, AttackKind)`, `int HitQuality(StepContext, LiveSet, AttackKind, Guid modelId)` (p.4's *"the model's Quality value"*: the model's slot `Quality` where it declares one, the unit's otherwise, with the target-number effects folded in; 6 for a fatigued melee, which p.9 replaces rather than modifies), `RollPlan HitPlan(StepContext, LiveSet, AttackKind, int quality)` (one pooled roll for one Quality value; fatigued in melee → `RequiredRaw = 6` and threshold 6 with no modifiers, p.9), `RollPlan DefensePlan(StepContext, LiveSet, bool inCover, int defenderModels)` (base modifier `cover +1` when shooting into cover, p.11; the model's own slot `Defense` when `defenderModels` is 1, which is p.14's *"must use the unit's Defense until all other models have been killed"*, with the count settled by `fire-groups` before any group fired), `bool InCover(GameState, Guid targetUnit, IRL<Guid> attackerModels)` (p.11's majority over the unit's models, strictly more than half), `RollPlan MoralePlan(StepContext, LiveSet)` (threshold the best of the unit's Quality and every living `Hero` model's own, which is p.14's *"may take morale tests on behalf of the unit"* resolved always-when-it-helps, with no Ask), `RollPlan CastingPlan(StepContext, int netTokens)` (4+, modifier `tokens` ±), `Sequence RollOffSequence()` (**the roll-off, owned here and consumed by the scheduler**: two steps, `roll-off` then `order`. `roll-off` rolls one die per team in ordinal order and any teams still tied roll again among themselves until every team is ordered — p.50 needs the whole descending order, not just a winner. `order` ranks them from the recorded dice and writes `Values["winner_ordinal"]` and `Values["place_1"]..["place_n"]` = team ordinals best first, which the scheduler's `RollOff.Order` reads into `round_started.Order`. The boundary between the two is the every-team confirm gate that makes the dice editable, Task 8); `static class GfHooks` with the three hook bodies — `OnRoundStarted(GameState, PropertySet)`, `OnTurnEnded(GameState, PropertySet, TurnContext)`, `EndOfTurnSequences(GameState, PropertySet, Guid? actor)` — and `IRL<Guid> UnitsToTestAtEndOfActivation(GameState, PropertySet, Guid? actor)` (took wounds this activation, now at half or less of starting size — or Tough value for a single model — and did not fight a melee this activation; p.10), `bool AtHalfOrLess(GameState, PropertySet, Guid unitId)`, `int StartingSize(GameState, Guid unitId)` (models in the snapshot), `bool FoughtMeleeThisActivation(GameState, Guid? actor, Guid unitId)` (the actor bounds the activation; no activation frame means false).
- One flagged interpretation is a constant here, visibly: `GfRules.OneTryPerSpellScope = "activation"` (ruling 2). Ruling 3 has no constant: attack counts are settled unconditionally when the action starts, so nothing branches on a flag (Task 13).

Spec: action sequences → One roll primitive (the split), Grimdark Future's sequences (fatigue read by the melee hit roll; the End-of-activation hook; the token economy: +X a round, capped at 6, even off the table), Rulings; scheduler → The round boundary (clear round-scoped conditions, spell tokens), The line (end-of-turn hook), Deployment (roll-off: null threshold, ties rerolled); rulebook p.7–11, p.13 (`Caster`, cover).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfRollsTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly IGameRules Gf = GrimdarkFutureRules.Instance;

    [Fact]
    public void Roll_specs_are_per_roll_and_thresholds_are_functions()
    {
        var a = Unit("A", "3+", "5+", [], 1, [], Melee("Fist", 1)); var b = Unit("B", "4+", "2+", [], 1, []);
        var state = State([a], [b]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, a.Id), Party.Whole(state, b.Id), AttackKind.Melee, "Charge", 1, []));
        Assert.Equal(3, Gf.RollSpec(GfNames.Hit).Threshold(live, Side.Attacker));
        Assert.Equal(2, Gf.RollSpec(GfNames.DefenseRoll).Threshold(live, Side.Defender));
        Assert.Equal(4, Gf.RollSpec(GfNames.Morale).Threshold(live, Side.Defender));
        Assert.Equal(4, Gf.RollSpec(GfNames.Casting).Threshold(live, Side.Attacker));
        Assert.Null(Gf.RollSpec(GfNames.RollOff).Threshold(live, Side.Attacker));
        Assert.Equal(new RollConvention(6, null), Gf.RollSpec(GfNames.Hit).Convention);
        Assert.Equal(GfNames.Hit, Gf.HitRoll);
        Assert.Equal(GfNames.ChargeSeq, Gf.ActionSequence(GfNames.Charge));
        Assert.Throws<UnknownSequenceException>(() => Gf.Sequence("nope"));
    }

    [Fact]
    public void Fatigue_makes_melee_hit_only_on_unmodified_sixes_and_cover_adds_one_to_defense_against_shooting()
    {
        var a = Unit("A", "3+", "5+", [], 1, [], Melee("Fist", 1, P("Thrust"))); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([a], [b]), 6, 5);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, true)]);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(a.Id, [b.Id], [], GfNames.Charge)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        var ctx = new StepContext(sim.State, Core, Gf, frame, new Roller(new ScriptedDice(6, 5), sim.State.RngSeed, 0));
        var live = LiveProperties.Settle(new Interaction(sim.State, Core, Party.Of(sim.State, a.Id, a.Models.Select(m => m.Id), a.Models[0].Weapons.Select(w => w.Id)), Party.Whole(sim.State, b.Id), AttackKind.Melee, GfNames.Charge, 1, []));
        Assert.Equal(6, GfRolls.HitQuality(ctx, live, AttackKind.Melee, a.Models[0].Id));        // p.9 replaces the value, so fatigue collapses every Quality to one
        var plan = GfRolls.HitPlan(ctx, live, AttackKind.Melee, 6);
        Assert.Equal((6, 6, 0), (plan.RequiredRaw, plan.Threshold, plan.Modifiers.Count));       // Thrust's +1 does not help a fatigued unit
        Assert.Equal([true, false], ctx.Roll(plan, 2).Dice.Select(d => d.Success));
        var cover = GfRolls.DefensePlan(ctx, live with { }, inCover: true, defenderModels: 1);
        Assert.Contains(cover.Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.DoesNotContain(GfRolls.DefensePlan(ctx, LiveProperties.Settle(live.Interaction with { Attack = AttackKind.Shooting }), inCover: false, defenderModels: 1).Modifiers, m => m.Source == "cover");
    }

    [Fact]
    public void A_joined_hero_hits_at_its_own_quality_blocks_on_the_units_until_it_is_alone_and_lends_its_quality_to_morale()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var (captain, trooper) = (joined.Models[0].Id, joined.Models[1].Id);
        var enemy = Unit("Enemy", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var state = State([joined], [enemy], x1: -6, x2: 6);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(joined.Id, [enemy.Id], [], GfNames.Hold)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        var ctx = new StepContext(state, Core, Gf, frame, new Roller(new ScriptedDice(4), state.RngSeed, 0));
        var shooting = LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, joined.Id, joined.Models.Select(m => m.Id), joined.Models.SelectMany(m => m.Weapons.Select(w => w.Id))), Party.Whole(state, enemy.Id), AttackKind.Shooting, GfNames.Hold, 12, []));
        Assert.Equal(3, GfRolls.HitQuality(ctx, shooting, AttackKind.Shooting, captain));                 // p.4: the model's Quality value
        Assert.Equal(5, GfRolls.HitQuality(ctx, shooting, AttackKind.Shooting, trooper));
        Assert.Equal(3, GfRolls.HitPlan(ctx, shooting, AttackKind.Shooting, 3).Threshold);

        // p.14: the hero must use the unit's Defense until all other models have been killed.
        var incoming = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, enemy.Id), Party.Whole(state, joined.Id), AttackKind.Shooting, GfNames.Hold, 12, []));
        Assert.Equal(5, GfRolls.DefensePlan(ctx, incoming, inCover: false, defenderModels: 2).Threshold);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(trooper, state.Projection.Models[trooper].Location, new OnShelf())]), null));
        var alone = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, enemy.Id), Party.Whole(state, joined.Id), AttackKind.Shooting, GfNames.Hold, 12, []));
        Assert.Equal(2, GfRolls.DefensePlan(ctx, alone, inCover: false, defenderModels: 1).Threshold);
        Assert.Equal(5, GfRolls.DefensePlan(ctx, alone, inCover: false, defenderModels: 2).Threshold);   // the count fire-groups settled, not the live one

        // p.14: "may take morale tests on behalf of the unit", resolved always-when-it-helps.
        var morale = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, joined.Id), null, null, GfNames.Hold, null, []));
        Assert.Equal(3, GfRolls.MoralePlan(ctx, morale).Threshold);
        var leaderless = State([squad], [enemy], x1: -6, x2: 6);
        var plain = LiveProperties.Settle(new Interaction(leaderless, Core, Party.Whole(leaderless, squad.Id), null, null, GfNames.Hold, null, []));
        Assert.Equal(5, GfRolls.MoralePlan(new StepContext(leaderless, Core, Gf, frame, new Roller(new ScriptedDice(4), leaderless.RngSeed, 0)), plain).Threshold);
    }

    [Fact]
    public void Round_start_clears_fatigue_and_grants_capped_tokens_and_turn_end_clears_wounds_this_activation()
    {
        var caster = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 2)]); var grunts = Unit("Grunts", "4+", "4+", [], 3, []);
        var state = State([caster], [grunts]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Fatigued, true), null));
        state.Applier.Apply(new GameEvent(3, null, new SpellTokensSet(caster.Models[0].Id, 5), null));
        state.Applier.Apply(new GameEvent(4, null, new WoundsThisActivationSet(grunts.Id, 2), null));
        var round = Gf.OnRoundStarted(state, Core, 2);
        Assert.Contains(round, c => c is ConditionSet { Condition: GfNames.Fatigued, Present: false } cs && cs.Subject.Id == grunts.Id);
        Assert.Contains(round, c => c is SpellTokensSet { Tokens: 6 } t && t.ModelId == caster.Models[0].Id);
        var turn = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, null, 0, 0));
        Assert.Contains(turn, c => c is WoundsThisActivationSet { Wounds: 0 } w && w.UnitId == grunts.Id);
        Assert.Empty(Gf.OnRoundStarted(state, Core, 0));                                                          // the phases before round one carry none
        state.Applier.Apply(new GameEvent(5, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Shaken, true), null));
        var idle = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, new Activation(grunts.Id, ActivationOption.Idle, false, []), 0, 0));
        Assert.Contains(idle, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false } cs && cs.Subject.Id == grunts.Id);   // p.10: idling stops Shaken
        var moved = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, new Activation(grunts.Id, GfNames.Advance, true, []), 0, 0));
        Assert.DoesNotContain(moved, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false });                             // an ordinary Advance does not
    }

    [Fact]
    public void A_shaken_aircrafts_mandatory_advance_still_stops_it_being_shaken()
    {
        var jet = Unit("Jet", "4+", "4+", [P("Aircraft")], 1, []);
        var state = State([jet], [Unit("T", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, jet.Id), GfNames.Shaken, true), null));
        // p.13: an Aircraft may only use Advance actions, even if Shaken, and "their mandatory move doesn't break the staying idle requirement to stop being Shaken".
        var advanced = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team1, new Activation(jet.Id, GfNames.Advance, true, []), 0, 0));
        Assert.Contains(advanced, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false } cs && cs.Subject.Id == jet.Id);
    }

    [Fact]
    public void End_of_activation_tests_units_at_half_or_less_that_took_wounds_and_did_not_fight_a_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 4, []); var big = Unit("Big", "4+", "4+", [], 1, [P("Tough", 6)]);
        var state = State([a], [b, big]);
        void Kill(Guid model) => state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(model, state.Projection.Models[model].Location, new OnShelf())]), null));
        Kill(b.Models[0].Id); Kill(b.Models[1].Id);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(b.Id, 2), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(big.Models[0].Id, 3), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(big.Id, 3), null));
        var tests = Gf.EndOfTurnSequences(state, Core, new TurnContext(Samples.Team1, new Activation(a.Id, GfNames.Hold, true, []), 0, 0));
        Assert.Equal(2, tests.Count);
        Assert.All(tests, t => Assert.Equal((GfNames.MoraleTest, "wounds"), (t.Sequence, t.Context.Action)));
        Assert.Equal(new HashSet<Guid> { b.Id, big.Id }, tests.Select(t => t.Context.ActorUnitId!.Value).ToHashSet());
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(b.Id, 1), null));
        Assert.True(GfHooks.AtHalfOrLess(state, Core, b.Id));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(big.Models[0].Id, 2), null));
        Assert.False(GfHooks.AtHalfOrLess(state, Core, big.Id));                                   // 4 of Tough(6) left
    }

    [Fact]
    public void Containment_settles_reach_without_measuring_and_carried_models_still_count()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 4, []);
        var stowaways = Unit("Stowaways", "4+", "4+", [], 2, []);
        var outside = Unit("Outside", "4+", "4+", [], 1, []);
        var state = State([bus, riders], [stowaways, outside]);
        var container = bus.Models[0].Id;
        void Embark(Tabletop.Content.Unit u) => state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
            new ModelsMoved(u.Models.Select(m => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new Inside(container))).ToList()), null));
        Embark(riders); Embark(stowaways);
        Assert.Null(Battlefield.InsideReach(state, outside.Id, bus.Id));                           // both on the table: measure it
        Assert.False(Battlefield.InsideReach(state, outside.Id, riders.Id));                       // p.14: no sight into a transport
        Assert.False(Battlefield.InsideReach(state, riders.Id, outside.Id));                       // nor out of one
        Assert.True(Battlefield.InsideReach(state, riders.Id, stowaways.Id));                      // p.14: units inside can target other units inside
        Assert.True(Battlefield.InsideReach(state, riders.Id, bus.Id));                            // p.15: and the Transport itself
        // p.10 counts the models a unit still has, and being carried has cost it none.
        Assert.False(GfHooks.AtHalfOrLess(state, Core, riders.Id));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
            new ModelsMoved(riders.Models.Take(2).Select(m => new ModelRelocation(m.Id, new Inside(container), new OnShelf())).ToList()), null));
        Assert.True(GfHooks.AtHalfOrLess(state, Core, riders.Id));                                 // two of four dead inside it
    }

    [Fact]
    public void The_roll_off_records_one_die_per_team_rerolls_ties_and_stops_at_a_gate_every_team_may_release()
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [Unit("B", "4+", "4+", [], 1, [])]), 4, 4, 2, 5);
        sim.Append([new RoundStarted(0, [Samples.Team1, Samples.Team2], [])]);
        var events = sim.Start(GfNames.RollOffSeq, new SequenceContext(null, [], [], null));
        Assert.Equal(["step_completed", "ask_opened"], events.Select(EventKinds.Of));                // the frame stays open behind the gate
        var roll = events.OfType<StepCompleted>().Single();
        Assert.Equal(2, roll.Rolls.Count);                                                          // a tie, then the decider
        Assert.All(roll.Rolls, r => Assert.Null(r.Threshold));
        Assert.Equal([4, 4], roll.Rolls[0].Dice.Select(d => d.Raw)); Assert.Equal([2, 5], roll.Rolls[1].Dice.Select(d => d.Raw));
        Assert.Null(roll.Values);                                                                   // the rolling step settles nothing: `order` derives it
        var confirm = sim.AskFor(Samples.Team2);
        Assert.Equal((AskQuestions.Confirm, 2), (confirm.Question, confirm.Teams.Count));            // no actor, no parent: every team
        var rest = sim.Answer(Samples.Team2, new Confirmed());
        var order = rest.OfType<StepCompleted>().Single();
        Assert.Equal((2, 2, 1), (order.Values!["winner_ordinal"], order.Values["place_1"], order.Values["place_2"]));   // what the scheduler reads into round_started.Order
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));
    }

    /// <summary>
    /// The point of the gate: the frame is on the stack while it is open, so SetDice is legal
    /// against the roll-off's own record, and `order` derives the places from the edited dice rather than from a settled value.
    /// </summary>
    [Fact]
    public void Editing_the_roll_offs_dice_at_the_gate_changes_the_order()
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [Unit("B", "4+", "4+", [], 1, [])]), 2, 5);
        sim.Append([new RoundStarted(0, [Samples.Team1, Samples.Team2], [])]);
        sim.Start(GfNames.RollOffSeq, new SequenceContext(null, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var record = sim.Steps(GfNames.RollOffRollStep).Single().Rolls.Single();
        Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, record.Id, [new DieValue(0, 6)]), Samples.P1, Samples.Team1));
        var order = sim.Answer(Samples.Team1, new Confirmed()).OfType<StepCompleted>().Single();
        Assert.Equal((1, 1, 2), (order.Values!["winner_ordinal"], order.Values["place_1"], order.Values["place_2"]));   // team 1's 2 edited to a 6 wins
        Assert.Equal(2, DiceCursor.DrawnSoFar(sim.State));                                          // an edit draws nothing
    }

    [Fact]
    public void The_battlefield_reads_range_sight_and_cover_from_geometry()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 2, []);
        var state = State([a], [b], x1: -6, x2: 6);
        Assert.True(Battlefield.CanSee(state, a.Models[0].Id, b.Models[0].Id));
        Assert.InRange(Battlefield.Distance(state, a.Models[0].Id, b.Models[0].Id), 10.0, 12.0);   // 12" centre to centre, less the two bases
        Assert.Equal((0, 2), (Battlefield.ModelsInCoverTerrain(state, b.Id), Battlefield.ModelsVisibleFrom(state, b.Id, [a.Models[0].Id])));
        Assert.False(GfRolls.InCover(state, b.Id, [a.Models[0].Id]));                                 // nobody in terrain, both models seen: neither clause of p.11's majority
        Assert.Empty(Battlefield.TerrainUnder(state, b.Id));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Battlefield.cs`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Geometry questions over the projection. Every answer is a measurement; what it means is the ruleset's.</summary>
public static class Battlefield
{
    public static PlacedModel? Placed(GameState state, Guid modelId) =>
        state.Projection.Models[modelId].Location is OnTable t ? At(state, modelId, t.Placement) : null;

    /// <summary>The model's body at a placement it does not hold yet — how a destination is judged before it is applied, and the only way to place an occupant, which has no position of its own.</summary>
    public static PlacedModel At(GameState state, Guid modelId, Placement placement) =>
        new(new ModelBody(state.Projection.Models[modelId].Slot.Base, state.Projection.Models[modelId].Slot.Height), placement);

    /// <summary>
    /// What containment settles about one unit reaching another, before anything is measured (p.15: "Units inside/outside of
    /// Transports don't have line of sight to the outside/inside … but units inside can target other units inside, and the
    /// Transport itself"). False: one is inside and the other cannot see in or out. True: both are inside the same container,
    /// or the reaching unit is inside and the other owns the container — they are in the same box, so there is no distance and
    /// no sight line to take. Null: neither is inside anything, so measure it the usual way.
    /// </summary>
    public static bool? InsideReach(GameState state, Guid fromUnit, Guid toUnit)
    {
        var (from, to) = (Effects.Lookup.ContainerOf(state, fromUnit), Effects.Lookup.ContainerOf(state, toUnit));
        if (from is null && to is null) return null;
        if (from is not { } container) return false;                                              // outside looking in
        return to == container || state.Projection.Models[container].UnitId == toUnit;
    }

    public static IReadOnlyList<WorldVolume> Terrain(GameState state) => state.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();

    /// <summary>Terrain plus every model not in the given unit, as sight blockers (p.8: a unit ignores its own models; p.11: other units block).</summary>
    public static IReadOnlyList<WorldVolume> Blockers(GameState state, Guid exceptUnitId) =>
        Terrain(state).Concat(state.Projection.Models.Values.Where(m => m.UnitId != exceptUnitId && m.Location is OnTable)
            .Select(m => Bodies.AsBlockingVolume(m.Id, Placed(state, m.Id)!.Value))).ToList();

    public static bool CanSee(GameState state, Guid fromModel, Guid toModel)
    {
        var (from, to) = (Placed(state, fromModel), Placed(state, toModel));
        if (from is null || to is null) return false;
        var blockers = Blockers(state, state.Projection.Models[fromModel].UnitId).Where(v => v.Id != toModel && v.Id != fromModel).ToList();
        return LineOfSight.Check(from.Value, to.Value, blockers).Clear;
    }

    public static double Distance(GameState state, Guid a, Guid b) => Geometry.Distance.EdgeToEdge(Placed(state, a)!.Value, Placed(state, b)!.Value);
    public static double Vertical(GameState state, Guid a, Guid b) => Geometry.Distance.Vertical(Placed(state, a)!.Value, Placed(state, b)!.Value);

    public static double? UnitDistance(GameState state, Guid unitA, Guid unitB)
    {
        var pairs = from a in Effects.Lookup.LivingModels(state, unitA) from b in Effects.Lookup.LivingModels(state, unitB) select Distance(state, a, b);
        return pairs.Any() ? pairs.Min() : null;
    }

    /// <summary>
    /// p.11's terrain half, counted and not judged: living models whose base centre is inside a volume declared `Cover`. The test
    /// is the base centre inside the volume, so this is `Containment.VolumesContaining` rather than a whole-base test, and it reads
    /// the same way for a single-model unit as for a squad. What fraction of the unit has to be in cover is the ruleset's number and
    /// lives in `GfRolls.InCover`.
    /// </summary>
    public static int ModelsInCoverTerrain(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        return Effects.Lookup.LivingModels(state, unitId).Count(id =>
            Containment.VolumesContaining(Placed(state, id)!.Value, terrain).Any(v => terrain.First(t => t.Id == v).Volume.Declarations.HasFlag(TerrainDeclarations.Cover)));
    }

    /// <summary>
    /// p.11's sight half, counted and not judged: how many of the target unit's living models at least one of these models can
    /// see. `LineOfSight.VisibleModels` takes every observer, so the attacking unit sees a model when any of its models does, and
    /// no single vantage point has to be chosen. It is called once per target model rather than once for the unit, because the
    /// blockers must lose the target's own body and keep the unit's other models (p.11); the ray count is the same either way.
    /// </summary>
    public static int ModelsVisibleFrom(GameState state, Guid targetUnit, IReadOnlyList<Guid> fromModels)
    {
        var observers = fromModels.Select(m => Placed(state, m)).Where(p => p is not null).Select(p => p!.Value).ToList();
        if (observers.Count == 0) return 0;
        var blockers = Blockers(state, state.Projection.Models[fromModels[0]].UnitId);
        return Effects.Lookup.LivingModels(state, targetUnit)
            .Count(id => LineOfSight.VisibleModels(observers, [Placed(state, id)!.Value], blockers.Where(v => v.Id != id).ToList())[0]);
    }

    public static IReadOnlyList<string> TerrainUnder(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        var flags = Effects.Lookup.LivingModels(state, unitId).SelectMany(id => Containment.VolumesContaining(Placed(state, id)!.Value, terrain)).Distinct()
            .Select(id => terrain.First(v => v.Id == id).Volume.Declarations).Aggregate(TerrainDeclarations.None, (acc, d) => acc | d);
        return Enum.GetValues<TerrainDeclarations>().Where(f => f != TerrainDeclarations.None && flags.HasFlag(f)).Select(f => Tabletop.Content.ContentJson.Serialize(f).Trim('"')).ToList();
    }

    public static Placement Settle(GameState state, Placement client) =>
        new(client.Position with { Z = Settling.SurfaceHeightAt(client.Position, Terrain(state)) }, client.Yaw);
}
```

- [ ] **Step 3: Write `GfNames.cs` and fill in `GfRules.Sequences.cs`**

```csharp
namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Every Grimdark Future name the C# half knows. The data half (Content/grimdark-future-core.properties.json) declares the same names.</summary>
public static class GfNames
{
    public const string Quality = "Quality", Defense = "Defense", Range = "Range", Attacks = "Attacks";
    public const string Hold = "Hold", Advance = "Advance", Rush = "Rush", Charge = "Charge";
    public const string Hit = "hit", DefenseRoll = "defense", Morale = "morale", Casting = "casting", RollOff = "roll-off";
    public const string Shaken = "shaken", Fatigued = "fatigued", Moved = "moved";
    public const string BeforeAttacking = "before-attacking";
    public const string HoldSeq = "hold", AdvanceSeq = "advance", RushSeq = "rush", ChargeSeq = "charge", Shooting = "shooting", Melee = "melee";
    public const string AttackSequence = "attack-sequence", ResolveHits = "resolve-hits", DealWounds = "deal-wounds", MoraleTest = "morale-test";
    public const string DangerousTerrain = "dangerous-terrain", CastingSeq = "casting", RollOffSeq = "roll-off";
    /// <summary>The roll-off's two step names. The scheduler reads the order off the step_completed named by the second.</summary>
    public const string RollOffRollStep = "roll-off", RollOffOrderStep = "order";
    public const string TransportDestroyedSeq = "transport-destroyed";                                                  // p.15: a wrecked container disgorges (Task 19)
    public const double MeleeHorizontal = 2, MeleeVertical = 4, ChargeCloseIn = 3, Consolidate = 3, FallBack = 1, Separation = 1, CastRange = 18, DifficultCap = 6;
    /// <summary>p.15 and p.21: a unit leaving a container "must stay fully within 6" of it".</summary>
    public const double ExitWithin = 6;
    public const int TokenCap = 6;

    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts", Rounds = Scheduler.SchedulerBase.RoundsPhase;   // phases
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough";
    public const string Transport = "Transport";                                                                        // p.15; its (X) is carried and never read — game flow: capacity is not enforced
    public const string Takedown = "Takedown";                                                                              // p.14; a weapon property, read by GfAttack.IsTakedownGroup
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
    public const double AmbushDistance = 9;
}
```

`GfNames.Rounds` is `SchedulerBase.RoundsPhase` so the two spellings cannot drift; land the scheduler plan's `SchedulerBase.cs` constant first (it has no other dependency) or write `"rounds"` and switch when it lands.

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The interpreter plan's partial of GrimdarkFutureRules (identity is in the skeleton's GfRules.cs; the scheduler's hooks in GfRules.Scheduling.cs and GfRules.Deployment.cs). The sequence catalog fills in over Tasks 12–17.</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>A flagged interpretation, not a silent guess (action sequences → Rulings): the scope is the activation, so a caster that failed a spell may try it again in a later activation.</summary>
    public const string OneTryPerSpellScope = "activation";

    private readonly Lazy<IReadOnlyDictionary<string, Sequence>> _sequences = new(() => new Sequence[]
    {
        GfRolls.RollOffSequence(),
        // Tasks 12–17 add: GfAttack.DealWounds(), GfAttack.ResolveHits(), GfAttack.AttackSequence(), GfShooting.Shooting(), GfMoves.Hold(), GfMoves.Advance(),
        // GfMoves.Rush(), GfMoves.DangerousTerrain(), GfMelee.Charge(), GfMelee.Melee(), GfMorale.MoraleTest(), GfCasting.Casting()
    }.ToDictionary(s => s.Name));

    public Sequence Sequence(string name) => _sequences.Value.TryGetValue(name, out var s) ? s : throw new UnknownSequenceException(name);
    public string? ActionSequence(string actionName) => actionName switch
    {
        GfNames.Hold => GfNames.HoldSeq, GfNames.Advance => GfNames.AdvanceSeq, GfNames.Rush => GfNames.RushSeq, GfNames.Charge => GfNames.ChargeSeq,
        _ => null,   // an out-of-set action accepted under warn/silent: recorded, no sequence
    };
    public RollSpec RollSpec(string roll) => GfRolls.Spec(roll);
    public string HitRoll => GfNames.Hit;
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => GfHooks.ReactionsAt(state, properties, point, frame);
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn) => GfHooks.EndOfTurnSequences(state, properties, turn.Activation?.UnitId);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => round == 0 ? [] : GfHooks.OnRoundStarted(state, properties);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn) => GfHooks.OnTurnEnded(state, properties, turn);
}
```

- [ ] **Step 4: Write `GfRolls.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>OPR's dice: 1D6 against a target number, never clamped. Thresholds are functions of the live set; fatigue and cover are read here.</summary>
public static class GfRolls
{
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, GfNames.Quality), Defense = new(StatLevel.Unit, GfNames.Defense);
    static readonly StatRef ModelQuality = new(StatLevel.Model, GfNames.Quality), ModelDefense = new(StatLevel.Model, GfNames.Defense);

    /// <summary>
    /// The default threshold for each roll, which is the unit's number. Three of them are overridden per plan where the rulebook
    /// makes the target number a model's: HitQuality for the attacking model (p.4), DefensePlan for a lone joined hero (p.14) and
    /// MoralePlan for a hero testing on the unit's behalf (p.14). A caller that names no threshold gets what is here.
    /// </summary>
    public static RollSpec Spec(string roll) => roll switch
    {
        GfNames.Hit => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.DefenseRoll => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Defense)),
        GfNames.Morale => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.Casting => new(roll, D6, (_, _) => 4),
        GfNames.RollOff => new(roll, D6, (_, _) => null),
        _ => new(roll, D6, (_, _) => null),   // a property's own roll (Regeneration, Impact, Fearless) carries its threshold in the effect
    };

    /// <summary>p.9: after attacking in melee for the first time in a round, a unit hits only on unmodified 6s until the round ends.</summary>
    public static bool Fatigued(StepContext ctx, LiveSet live, AttackKind kind) =>
        kind == AttackKind.Melee && ctx.Projection.Units[live.Interaction.Attacker.UnitId].Conditions.Contains(GfNames.Fatigued);

    /// <summary>
    /// The Quality value one attacking model tests against. p.4: a Quality test succeeds on "the model's Quality value or higher",
    /// so the value is the model's, and a slot that declares its own is a joined hero (p.14). Every other slot
    /// falls back to the unit's, so an ordinary squad has one value. Effects on the roll's target number are already folded in,
    /// which is what puts Reliable's 2+ over a hero's own 3+.
    ///
    /// p.9's fatigue replaces the value rather than modifying it, and it is the whole unit's condition, so a fatigued melee has one
    /// value for every model and one pool however many Qualities the unit holds.
    /// </summary>
    public static int HitQuality(StepContext ctx, LiveSet live, AttackKind kind, Guid modelId) =>
        Fatigued(ctx, live, kind) ? 6 : StatReader.TargetNumber(live, Side.Attacker, ModelQuality, modelId);

    /// <summary>
    /// One pooled hit roll for one Quality value. `determine-attacks` splits the group's attacks by `HitQuality` and `roll-to-hit`
    /// asks for a plan per distinct value, which is p.4's *"we recommend having dice of multiple colors ... and then roll them all
    /// at once"* for the case a joined hero creates: a hero and its squad carrying one weapon type that `fire-groups`
    /// merged into a single group.
    ///
    /// A fatigued melee is a flat 6+ with no modifiers and `RequiredRaw = 6`, because p.9 says *"only hit on unmodified rolls of 6"*.
    /// </summary>
    public static RollPlan HitPlan(StepContext ctx, LiveSet live, AttackKind kind, int quality) =>
        Fatigued(ctx, live, kind)
            ? new RollPlan(GfNames.Hit, 6, [], D6, RequiredRaw: 6, RerollRaw: new HashSet<int>())
            : ctx.Plan(live, Side.Attacker, GfNames.Hit, [], threshold: quality);

    /// <summary>
    /// p.11's *"If the majority of models in a unit are fully inside a piece of cover terrain or behind a sight blocker"*, one
    /// majority over the target unit's models applied to two facts. A majority is strictly more than half, so five of ten is not
    /// one: the book's word governs, and half or more is not a majority.
    /// Terrain cover is a majority of the unit's models with a base centre inside a `Cover` volume, which for a single-model unit
    /// is that model's own centre. Sight cover is the attacker seeing fewer than half of them, which is the same majority read
    /// from the other side: fewer than half seen is more than half hidden. Either clause is enough. The fraction is a ruleset
    /// number, so it is here and `Battlefield` only counts.
    /// </summary>
    public static bool InCover(GameState state, Guid targetUnit, IReadOnlyList<Guid> attackerModels)
    {
        var models = Lookup.LivingModels(state, targetUnit).Count;
        if (models == 0) return false;
        return Battlefield.ModelsInCoverTerrain(state, targetUnit) * 2 > models
            || Battlefield.ModelsVisibleFrom(state, targetUnit, attackerModels) * 2 < models;
    }

    /// <summary>
    /// p.11: +1 to Defense against shooting when in cover. Blast and Indirect suppress it (pass one).
    ///
    /// p.14's Hero clause is the other half of the threshold. A joined hero *"must use the unit's Defense until all other models have
    /// been killed"*, so the unit's value stands while anything else lives, and once the hero is alone its own slot Defense is read.
    /// `defenderModels` is that clause's *all other models*, and it is the count `fire-groups` settled before any group fired rather
    /// than a live one: p.8 fires a unit's weapons simultaneously, so a second group must not find the hero alone that
    /// the first group left it. A model without a slot Defense reads the unit's through the same call, so nothing branches on `Hero`.
    /// </summary>
    public static RollPlan DefensePlan(StepContext ctx, LiveSet live, bool inCover, int defenderModels)
    {
        var living = Lookup.LivingModels(ctx.State, live.Interaction.Defender!.UnitId);
        var alone = defenderModels == 1 && living.Count == 1 ? living[0] : (Guid?)null;
        return ctx.Plan(live, Side.Defender, GfNames.DefenseRoll,
            live.Interaction.Attack == AttackKind.Shooting && inCover ? [new RollModifier("cover", 1)] : [],
            threshold: alone is { } id ? StatReader.TargetNumber(live, Side.Defender, ModelDefense, id) : null);
    }

    /// <summary>
    /// p.10: one Quality test. p.14 lets a joined hero *"take morale tests on behalf of the unit"*, and that clause is resolved
    /// always-when-it-helps, so the threshold is the best of the unit's Quality and every living Hero's own and no Ask
    /// is opened. Nothing else of the test moves: p.14's *"even if the hero is the last model remaining, it takes morale tests as the
    /// unit"* keeps the starting-size comparison that decides Shaken from Rout on the unit.
    /// </summary>
    public static RollPlan MoralePlan(StepContext ctx, LiveSet live)
    {
        var unit = live.Interaction.Attacker.UnitId;
        var best = Lookup.LivingModels(ctx.State, unit)
            .Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero))
            .Select(m => StatReader.TargetNumber(live, Side.Attacker, ModelQuality, m))
            .Append(StatReader.TargetNumber(live, Side.Attacker, Quality)).Min();
        return ctx.Plan(live, Side.Attacker, GfNames.Morale, [], threshold: best);
    }

    /// <summary>p.13: one die on 4+, ±1 per token spent for or against.</summary>
    public static RollPlan CastingPlan(StepContext ctx, int netTokens) =>
        new(GfNames.Casting, 4, netTokens == 0 ? [] : [new RollModifier("tokens", netTokens)], D6, null, new HashSet<int>());

    /// <summary>
    /// The roll-off the scheduler starts (scheduler → Deployment; p.6, p.50). Two steps, because the boundary between them is the confirm
    /// gate that keeps the frame on the stack while a player edits the dice, and because
    /// dice_edited and dice_rerolled reach back into a RollRecord and never rewrite a step_completed's Values — so the ranking has to be
    /// re-derived after the gate rather than written by the step that rolled.
    /// </summary>
    public static Sequence RollOffSequence() => new(GfNames.RollOffSeq, [new RollOffStep(), new RollOffOrderStep()]);

    /// <summary>
    /// One die per team in ordinal order, no threshold; teams whose results are still tied roll again among themselves — roll k's dice
    /// belong, in ordinal order, to the teams tied after rolls 0..k-1 — until every team is ordered, because free-for-all activates
    /// "in descending order" and needs the whole order, not a winner.
    /// </summary>
    sealed class RollOffStep : IStep
    {
        public string Name => GfNames.RollOffRollStep;
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var plan = new RollPlan(GfNames.RollOff, null, [], D6, null, new HashSet<int>());
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            for (var round = 0; round < 100; round++)
            {
                var tied = Tied(teams, keys);
                if (tied.Count == 0) break;
                var record = ctx.Roll(plan, tied.Count);
                for (var i = 0; i < tied.Count; i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            return new Complete([]);
        }
    }

    /// <summary>
    /// Ranks the teams from the dice the previous step recorded, replaying the same tie loop over them instead of rolling, so a die
    /// edited or rerolled at the gate is the one that counts. It never rolls: a residual tie an edit created falls to team ordinal, which
    /// is the tolerance Scheduler.RollOff.Order already has for a hand-edited log.
    /// Values: winner_ordinal, and place_1..place_n — the team ordinals best first — which Scheduler.RollOff.Order reads into round_started.Order.
    /// </summary>
    sealed class RollOffOrderStep : IStep
    {
        public string Name => GfNames.RollOffOrderStep;
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            foreach (var record in ctx.Frame.Frame.Steps.Single(s => s.Step == GfNames.RollOffRollStep).Rolls)
            {
                var tied = Tied(teams, keys);
                for (var i = 0; i < Math.Min(tied.Count, record.Dice.Count); i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            var ranked = teams.OrderByDescending(t => keys[t.Id], KeyComparer.Instance).ThenBy(t => t.Ordinal).ToList();
            var values = new Dictionary<string, int>();
            if (ranked.Count > 0) values["winner_ordinal"] = ranked[0].Ordinal;
            for (var k = 0; k < ranked.Count; k++) values[$"place_{k + 1}"] = ranked[k].Ordinal;
            return new Complete([], values);
        }
    }

    /// <summary>The teams still sharing a result key with another team, in ordinal order: whose dice the next roll-off roll holds.</summary>
    static List<Team> Tied(List<Team> teams, Dictionary<Guid, List<int>> keys) =>
        teams.Where(t => teams.Any(o => o.Id != t.Id && keys[o.Id].SequenceEqual(keys[t.Id]))).ToList();

    /// <summary>Lexicographic over each team's raw results; a longer key (more tie-break rolls) ranks above its prefix.</summary>
    sealed class KeyComparer : IComparer<List<int>>
    {
        public static readonly KeyComparer Instance = new();
        public int Compare(List<int>? a, List<int>? b)
        {
            for (var i = 0; i < Math.Min(a!.Count, b!.Count); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }
    }
}
```

- [ ] **Step 5: Write `GfHooks.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The hooks the scheduler calls, and who may react at a window. Every reading is of the projection and the log, never of a flag.</summary>
public static class GfHooks
{
    /// <summary>Round boundary: fatigue clears (action sequences → Fatigue); every Caster(X) gets X tokens, capped at 6, on the table or not (p.13).</summary>
    public static IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet props)
    {
        var changes = new List<StateChange>();
        foreach (var u in state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Fatigued)))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Fatigued, false));
        foreach (var m in state.Projection.Models.Values)
            if (Lookup.ModelParameter(state, m.Id, GfNames.Caster) is { } x && x > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, m.SpellTokens + x)));
        return changes;
    }

    /// <summary>turn_ended: the wounds-this-activation counters clear (whole-design review → Gaps filled); an activation spent idle stops the unit being Shaken (p.10).</summary>
    public static IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet props, Scheduler.TurnContext turn)
    {
        var changes = state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0).Select(u => (StateChange)new WoundsThisActivationSet(u.Id, 0)).ToList();
        // p.10: "Shaken units must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation."
        // p.13: an Aircraft "may only use Advance actions … (even if Shaken)" and "their mandatory move doesn't break the staying idle requirement
        // to stop being Shaken" — so a Shaken Aircraft's Advance counts as having idled.
        if (turn.Activation is { } act && state.Projection.Units[act.UnitId].Conditions.Contains(GfNames.Shaken)
            && (act.Action == Scheduler.ActivationOption.Idle || (act.Action == GfNames.Advance && Lookup.UnitHas(state, props, act.UnitId, GfNames.Aircraft))))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, act.UnitId), GfNames.Shaken, false));
        return changes;
    }

    /// <summary>p.10: at the end of an activation, a unit that took wounds leaving it at half or less tests; units that fought this melee compared wounds instead. The actor bounds "this activation".</summary>
    public static IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet props, Guid? actor) =>
        UnitsToTestAtEndOfActivation(state, props, actor).Select(u => new SequenceStart(GfNames.MoraleTest, new SequenceContext(u, [], [], "wounds"))).ToList();

    public static IReadOnlyList<Guid> UnitsToTestAtEndOfActivation(GameState state, PropertySet props, Guid? actor) =>
        state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0 && AtHalfOrLess(state, props, u.Id) && !FoughtMeleeThisActivation(state, actor, u.Id) && Lookup.AliveModels(state, u.Id).Count > 0)
            .Select(u => u.Id).ToList();

    public static int StartingSize(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models.Count;

    /// <summary>Half or less of starting size, or of the Tough value for a single-model unit (p.10). Counted over the models still in play: a unit inside a transport has not lost a model by being carried.</summary>
    public static bool AtHalfOrLess(GameState state, PropertySet props, Guid unitId)
    {
        var start = StartingSize(state, unitId);
        if (start == 1)
        {
            var model = state.Projection.Units[unitId].Definition.Models[0];
            var tough = Lookup.ModelParameter(state, model.Id, "Tough") ?? 1;
            var remaining = state.Projection.Models[model.Id].Location is not OnShelf ? tough - state.Projection.Models[model.Id].Wounds : 0;
            return remaining * 2 <= tough;
        }
        return Lookup.AliveModels(state, unitId).Count * 2 <= start;
    }

    /// <summary>A melee sequence started since the actor's activation began, with this unit as charger or target. No activation frame in the log means no melee this activation — never a scan of the whole log, which would report a melee from an earlier round.</summary>
    public static bool FoughtMeleeThisActivation(GameState state, Guid? actor, Guid unitId)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted s
            && s.Context.Action is GfNames.Hold or GfNames.Advance or GfNames.Rush or GfNames.Charge
            && (actor is null || s.Context.ActorUnitId == actor));
        if (activation < 0) return false;
        return log.Skip(activation).Any(e => e.Payload is SequenceStarted { Sequence: GfNames.Melee } m && (m.Context.ActorUnitId == unitId || m.Context.TargetUnitIds.Contains(unitId)));
    }

    /// <summary>
    /// The one core window: before attacking, the acting unit's own team may cast if a model in it has tokens (p.13). Task 17 supplies the sequence.
    /// A Shaken unit casts nothing: 3.5.1 p.10 spells out that being idle means it "can't take any actions, or use any active special
    /// rules, such as casting spells, using buffs/debuffs on units, re-positioning, etc.".
    /// </summary>
    public static IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet props, string point, FrameView frame)
    {
        if (point != GfNames.BeforeAttacking || frame.Context.ActorUnitId is not { } unit) return [];
        if (state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return [];   // p.10: no active special rules while Shaken
        var casters = Lookup.AliveModels(state, unit).Where(m => state.Projection.Models[m].SpellTokens > 0 && Lookup.ModelParameter(state, m, "Caster") is not null).ToList();   // p.15: a caster inside a container may still cast at what is inside with it
        if (casters.Count == 0 || Teams.Of(state, unit) is not { } team) return [];
        return [new Reaction(team, "cast", "Cast a spell", GfNames.CastingSeq, new SequenceContext(unit, [], [], null))];
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfRollsTests`; expect 10 passed. Commit: `Add Grimdark Future's roll specs, hooks, names and the battlefield reader`.**
