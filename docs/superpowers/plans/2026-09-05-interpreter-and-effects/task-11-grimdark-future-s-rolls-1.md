# Task 11: Grimdark Future's rolls, names, hooks and the battlefield reader

**Files:**
- Create: `server/game/Interpreter/Battlefield.cs`, `server/game/Interpreter/Sight.cs`, `server/game/Rules/GrimdarkFuture/GfNames.cs`, `server/game/Rules/GrimdarkFuture/GfRolls.cs`, `server/game/Rules/GrimdarkFuture/GfHooks.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (Task 8's partial: the bodies)
- Test: `tests/Tabletop.GameServer.Tests/GfRollsTests.cs`
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/Armies.cs` (`State` gains `IReadOnlyList<TerrainPlacement>? terrain = null`, placed on the sample table: `Sample() with { Terrain = terrain ?? [] }`; the six-parameter form of Task 6 stays valid)

**Interfaces:**
- Consumes: Tasks 6–10; geometry plan's `Distance.EdgeToEdge/Vertical/InRange`, `LineOfSight.Check`, `Cover.InsideCoverVolumes`, `Cover.SightBlockersBetween`, `Containment.VolumesContaining`, `Bodies.AsBlockingVolume`, `Movement.Check`, `Settling.SurfaceHeightAt`; `TerrainPlacement.WorldVolumes()` (Content).
- Produces (namespace `Tabletop.GameServer.Interpreter`): `static class Battlefield` with `PlacedModel? Placed(GameState, Guid modelId)` (null off the table — on the shelf or inside a container), `PlacedModel At(GameState, Guid modelId, Placement)` (the body at a placement it does not hold yet: how a destination is judged, and the only way to place an occupant), `bool? InsideReach(GameState, Guid fromUnit, Guid toUnit)` (p.15: false when one is inside and the other out, true when both are in one container or the reaching unit is inside the other's container model, null when neither is inside and the answer is a measurement), `IRL<WorldVolume> Terrain(GameState)`, `bool CanSee(GameState, Guid fromModel, Guid toModel)`, `sealed class Sight` (`static Sight From(GameState, Guid observerUnit, Guid? alsoIgnoring = null)`, `bool CanSee(Guid fromModel, Guid toModel)`, `(bool Clear, bool Screened)? Look(Guid fromModel, Guid toModel)`: one `Sight` holds the blockers built once for the unit that looks, which are the sight-blocking terrain and every model on the table outside that unit — rulebook p.8 and p.11), `double Distance(GameState, Guid modelA, Guid modelB)`, `double Vertical(GameState, Guid modelA, Guid modelB)`, `double? UnitDistance(GameState, Guid unitA, Guid unitB)` (closest pair of living models), `IRL<Guid> ModelsInCoverTerrain(GameState, Guid unitId, bool wholeBase)` (living models inside a `Cover` volume: with `wholeBase` the whole base, otherwise the base centre) and `IRL<Guid> ModelsBehindSightBlockers(GameState, Guid targetUnit, IRL<Guid> fromModels)` (living models of the target unit to which each of those models has no clear line or a clear line that passes a sight blocker, which is every model when none of them has a clear line; the target's own unit never screens it) — two sets, which the ruleset unions and compares against its fraction, `IRL<string> TerrainUnder(GameState, Guid unitId)` (declaration names of volumes containing any living model — the `position_terrain` predicate's values), `Placement Settle(GameState, Placement clientPlacement)` (the server's Z from `Settling.SurfaceHeightAt`).
- Produces (namespace `Tabletop.GameServer.Rules.GrimdarkFuture`): `static class GfNames` (`Quality`, `Defense`, `Range`, `Attacks`; actions `Hold`, `Advance`, `Rush`, `Charge`; rolls `Hit = "hit"`, `DefenseRoll = "defense"`, `Morale = "morale"`, `Casting = "casting"`, `RollOff = "roll-off"`; conditions `Shaken = "shaken"`, `Fatigued = "fatigued"`, `Moved = "moved"`; window point `BeforeAttacking = "before-attacking"`; sequence names `HoldSeq = "hold"`, `AdvanceSeq = "advance"`, `RushSeq = "rush"`, `ChargeSeq = "charge"`, `Shooting = "shooting"`, `Melee = "melee"`, `AttackSequence = "attack-sequence"`, `ResolveHits = "resolve-hits"`, `DealWounds = "deal-wounds"`, `MoraleTest = "morale-test"`, `DangerousTerrain = "dangerous-terrain"`, `CastingSeq = "casting"`, `RollOffSeq = "roll-off"`; roll-off step names `RollOffRollStep = "roll-off"`, `RollOffOrderStep = "order"`; melee reach `MeleeHorizontal = 2`, `MeleeVertical = 4`; `ChargeCloseIn = 3`, `Consolidate = 3`, `FallBack = 1`, `Separation = 1`; `DifficultCap = 6`; `ExitWithin = 6` (p.15's *"fully within 6\" of it"*); `TokenCap = 6`; `CastRange = 18`; `AmbushDistance = 9`; and the scheduler plan's names, so there is one names class: phases `Deployment = "deployment"`, `Scouts = "scouts"`, `Rounds = Scheduler.SchedulerBase.RoundsPhase`; `Deploy = "Deploy"` (the `Activation.Action` of a deployment answer); rules `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `Transport`, `Indirect`, `Takedown`, `Hero`; condition `SetAside = "set-aside"`; sequence name `TransportDestroyedSeq = "transport-destroyed"` (Task 19)); `GrimdarkFutureRules` (the partial in `GfRules.Sequences.cs`) implementing `Sequence`, `ActionSequence` (`Hold→hold`, …, null otherwise), `RollSpec`, `HitRoll`, `ReactionsAt`, `EndOfTurnSequences`, `OnRoundStarted`, `OnTurnEnded`; `static class GfRolls` with `RollSpec Spec(string roll)`, `bool Fatigued(StepContext, LiveSet, AttackKind)` (p.9's fatigue, and p.10's Shaken unit striking back *"counting as fatigued"*), `int HitQuality(StepContext, LiveSet, AttackKind, Guid modelId)` (p.4's *"the model's Quality value"*: the model's slot `Quality` where it declares one, the unit's otherwise, with the target-number effects folded in; 6 for a fatigued melee, which p.9 replaces rather than modifies), `RollPlan HitPlan(StepContext, LiveSet, AttackKind, int quality)` (one pooled roll for one Quality value; fatigued in melee → `RequiredRaw = 6` and threshold 6 with no modifiers, p.9), `RollPlan DefensePlan(StepContext, LiveSet, bool inCover, int defenderModels)` (base modifier `cover +1` when shooting into cover, p.11; the model's own slot `Defense` when `defenderModels` is 1, which is p.14's *"must use the unit's Defense until all other models have been killed"*, with the count settled by `fire-groups` before any group fired), `bool InCover(GameState, Guid targetUnit, IRL<Guid> attackerModels, bool ignoresSightObstructions)` (p.11's one majority, strictly more than half of the unit's living models, over the union of the models in cover terrain and the models behind a sight blocker; `ignoresSightObstructions` is Indirect's p.14 clause and drops the sight half only), `RollPlan MoralePlan(StepContext, LiveSet)` (threshold the best of the unit's Quality and every living `Hero` model's own, which is p.14's *"may take morale tests on behalf of the unit"* resolved always-when-it-helps, with no Ask), `RollPlan CastingPlan(StepContext, int netTokens)` (4+, modifier `tokens` ±), `Sequence RollOffSequence()` (**the roll-off, owned here and consumed by the scheduler**: two steps, `roll-off` then `order`. `roll-off` rolls one die per team in ordinal order and any teams still tied roll again among themselves until every team is ordered — p.50 needs the whole descending order, not just a winner. `order` ranks them from the recorded dice and writes `Values["winner_ordinal"]` and `Values["place_1"]..["place_n"]` = team ordinals best first, which the scheduler's `RollOff.Order` reads into `round_started.Order`. The boundary between the two is the every-team confirm gate that makes the dice editable, Task 8); `static class GfHooks` with the three hook bodies — `OnRoundStarted(GameState, PropertySet)`, `OnTurnEnded(GameState, PropertySet, TurnContext)`, `EndOfTurnSequences(GameState, PropertySet, Guid? actor)` — and `IRL<Guid> UnitsToTestAtEndOfActivation(GameState, PropertySet, Guid? actor)` (took wounds this activation, now at half or less of starting size — or Tough value for a single model — and did not fight a melee this activation; p.10), `bool AtHalfOrLess(GameState, PropertySet, Guid unitId)`, `int StartingSize(GameState, Guid unitId)` (models in the snapshot), `bool FoughtMeleeThisActivation(GameState, Guid? actor, Guid unitId)` (the actor bounds the activation; no activation frame means false).
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
        var shot = LiveProperties.Settle(live.Interaction with { Attack = AttackKind.Shooting });
        Assert.Contains(GfRolls.DefensePlan(ctx, shot, inCover: true, defenderModels: 1).Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.DoesNotContain(GfRolls.DefensePlan(ctx, shot, inCover: false, defenderModels: 1).Modifiers, m => m.Source == "cover");
        Assert.DoesNotContain(GfRolls.DefensePlan(ctx, live, inCover: true, defenderModels: 1).Modifiers, m => m.Source == "cover");   // melee: no cover bonus
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
    public void Turn_end_clears_moved_so_it_means_this_activation()
    {
        var grunts = Unit("Grunts", "4+", "4+", [], 3, []);
        var state = State([grunts], [Unit("T", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Moved, true), null));
        // p.14 Indirect: "-1 to hit rolls when shooting after moving" — a move this activation, not one in an earlier round.
        var turn = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team1, new Activation(grunts.Id, GfNames.Advance, true, []), 0, 0));
        Assert.Contains(turn, c => c is ConditionSet { Condition: GfNames.Moved, Present: false } cs && cs.Subject.Id == grunts.Id);
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
        Assert.Equal((0, 0), (Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: false).Count, Battlefield.ModelsBehindSightBlockers(state, b.Id, [a.Models[0].Id]).Count));
        Assert.False(GfRolls.InCover(state, b.Id, [a.Models[0].Id], ignoresSightObstructions: false));   // nobody in terrain, neither model behind a blocker: no majority of p.11's union
        Assert.Empty(Battlefield.TerrainUnder(state, b.Id));
    }

    /// <summary>A Cover area spanning x -5..5 and y -30..edgeY, so a model in the x = 0 column is fully inside when its base clears edgeY.</summary>
    static Tabletop.Content.TerrainPlacement CoverUpTo(double edgeY) =>
        new(Guid.CreateVersion7(), Tabletop.Content.VisualRef.OfBuiltin("forest"), new Tabletop.Geometry.Point3(0, (edgeY - 30) / 2, 0), new Tabletop.Geometry.Yaw(0), 1.0,
            [new Tabletop.Content.TerrainVolume(Guid.CreateVersion7(), new Tabletop.Geometry.Volume(Tabletop.Geometry.VolumeKind.Area, Tabletop.Geometry.Footprint.Rect(10, edgeY + 30), new Tabletop.Geometry.Point2(0, 0), new Tabletop.Geometry.Yaw(0), 0, 3, Tabletop.Geometry.TerrainDeclarations.Cover, null))]);

    // Models of a unit sit at y = -18.5, -17, -15.5, -14 with a 0.63 radius, so an edge at -15.2 has three centres inside and two whole bases.
    [Theory]
    [InlineData(-15.2, 3, 2, false)]   // third model straddles: centre in, base not fully in
    [InlineData(-13.8, 4, 3, true)]
    [InlineData(-13.0, 4, 4, true)]
    [InlineData(-17.5, 1, 1, false)]
    public void Cover_terrain_is_counted_by_centre_or_by_whole_base_and_in_cover_needs_a_strict_majority(double edgeY, int byCentre, int byBase, bool inCover)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 4, []);
        var state = State([a], [b], x1: -12, x2: 0, terrain: [CoverUpTo(edgeY)]);
        Assert.Equal((byCentre, byBase), (Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: false).Count, Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: true).Count));
        Assert.Equal(inCover, GfRolls.InCover(state, b.Id, [a.Models[0].Id], ignoresSightObstructions: false));   // a multi-model unit is judged by whole bases: 2 of 4 is not a majority, 3 of 4 is
    }

    [Fact]
    public void A_single_model_straddling_cover_terrain_is_in_cover_by_its_centre()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b], x1: -12, x2: 4.9, terrain: [CoverUpTo(-13)]);                 // x = 4.9: centre inside x -5..5, base past it
        Assert.Equal((1, 0), (Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: false).Count, Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: true).Count));
        Assert.True(GfRolls.InCover(state, b.Id, [a.Models[0].Id], ignoresSightObstructions: false));   // p.11: single-model units need only be "mostly inside"
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Battlefield.cs` and `Sight.cs`**

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
    /// What containment settles about one unit reaching another, before anything is measured (p.15: a unit inside a container and a
    /// unit outside it cannot reach each other; occupants of one container reach each other and the container model itself).
    /// False: one is inside and the other cannot see in or out. True: both are inside the same container, or the reaching unit is
    /// inside and the other owns the container — they are in the same box, so there is no distance and no sight line to take.
    /// Null: neither is inside anything, so measure it the usual way.
    /// </summary>
    public static bool? InsideReach(GameState state, Guid fromUnit, Guid toUnit)
    {
        var (from, to) = (Effects.Lookup.ContainerOf(state, fromUnit), Effects.Lookup.ContainerOf(state, toUnit));
        if (from is null && to is null) return null;
        if (from is not { } container) return false;                                              // outside looking in
        return to == container || state.Projection.Models[container].UnitId == toUnit;
    }

    public static IReadOnlyList<WorldVolume> Terrain(GameState state) => state.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();

    /// <summary>One pair, one sight line. A caller checking many pairs builds a <see cref="Sight"/> once and asks it instead.</summary>
    public static bool CanSee(GameState state, Guid fromModel, Guid toModel) =>
        Sight.From(state, state.Projection.Models[fromModel].UnitId).CanSee(fromModel, toModel);

    public static double Distance(GameState state, Guid a, Guid b) => Geometry.Distance.EdgeToEdge(Placed(state, a)!.Value, Placed(state, b)!.Value);
    public static double Vertical(GameState state, Guid a, Guid b) => Geometry.Distance.Vertical(Placed(state, a)!.Value, Placed(state, b)!.Value);

    public static double? UnitDistance(GameState state, Guid unitA, Guid unitB)
    {
        var pairs = (from a in Effects.Lookup.LivingModels(state, unitA) from b in Effects.Lookup.LivingModels(state, unitB) select Distance(state, a, b)).ToList();
        return pairs.Count > 0 ? pairs.Min() : null;
    }

    /// <summary>
    /// p.11's terrain test, per model and not judged: the living models inside a volume declared `Cover`. Which test a model has to
    /// pass is the ruleset's call, because the book words it differently by unit size: `wholeBase` takes models whose whole base is
    /// inside (`Cover.InsideCoverVolumes`, the multi-model wording "fully inside"), otherwise a model is taken when its base centre is
    /// inside (`Containment.VolumesContaining`, standing for the single-model wording "mostly inside"). The majority is the ruleset's.
    /// </summary>
    public static IReadOnlyList<Guid> ModelsInCoverTerrain(GameState state, Guid unitId, bool wholeBase)
    {
        var terrain = Terrain(state);
        return Effects.Lookup.LivingModels(state, unitId).Where(id =>
            wholeBase
                ? Cover.InsideCoverVolumes(Placed(state, id)!.Value, terrain).Count > 0
                : Containment.VolumesContaining(Placed(state, id)!.Value, terrain).Any(v => terrain.First(t => t.Id == v).Volume.Declarations.HasFlag(TerrainDeclarations.Cover))).ToList();
    }

    /// <summary>
    /// p.11's sight test, per model and not judged: the target unit's living models that are "behind a sight blocker" from these
    /// attacking models. A model is when every attacking model with a clear line to it has a blocker along that line
    /// (`Cover.SightBlockersBetween` non-empty while `LineOfSight.Check` is clear), which includes no attacking model seeing it at all.
    /// The blockers are terrain and every unit but the two: p.11's other units "are sight blockers that can provide other units
    /// with cover", so a model does not take cover behind its own unit, and the attackers ignore their own (p.8).
    /// </summary>
    public static IReadOnlyList<Guid> ModelsBehindSightBlockers(GameState state, Guid targetUnit, IReadOnlyList<Guid> fromModels)
    {
        var living = Effects.Lookup.LivingModels(state, targetUnit);
        if (fromModels.Count == 0) return living;
        var sight = Sight.From(state, state.Projection.Models[fromModels[0]].UnitId, alsoIgnoring: targetUnit);
        return living.Where(id => fromModels.All(o => sight.Look(o, id) is not { Clear: true, Screened: false })).ToList();
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

`Sight.cs`:

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>
/// Sight lines drawn from one unit's models, with the blocking volumes built once: the terrain that declares `BlocksLineOfSight`
/// and the body of every model on the table outside the observing unit (p.8: a unit ignores its own models; p.11: other units
/// block). A caller that checks many pairs builds one `Sight` and asks it each pair, so no pair rebuilds the table.
/// </summary>
public sealed class Sight
{
    readonly GameState state;
    readonly IReadOnlyList<WorldVolume> blockers;
    readonly HashSet<Guid> bodies;

    Sight(GameState state, IReadOnlyList<WorldVolume> blockers, HashSet<Guid> bodies) => (this.state, this.blockers, this.bodies) = (state, blockers, bodies);

    /// <summary>`alsoIgnoring` leaves a second unit's bodies out as well, for a question that must not count that unit as a blocker.</summary>
    public static Sight From(GameState state, Guid observerUnit, Guid? alsoIgnoring = null)
    {
        var terrain = Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight));
        var models = state.Projection.Models.Values.Where(m => m.UnitId != observerUnit && m.UnitId != alsoIgnoring && m.Location is OnTable).ToList();
        var volumes = terrain.Concat(models.Select(m => Bodies.AsBlockingVolume(m.Id, Battlefield.Placed(state, m.Id)!.Value))).ToList();
        return new(state, volumes, models.Select(m => m.Id).ToHashSet());
    }

    public bool CanSee(Guid fromModel, Guid toModel) => Look(fromModel, toModel) is { Clear: true };

    /// <summary>
    /// One sight line measured: `Clear` is `LineOfSight.Check`, and `Screened` is `Cover.SightBlockersBetween` non-empty along a
    /// clear line, so it is false whenever `Clear` is. The target's own body is never its own blocker. Null when either model is off the table.
    /// </summary>
    public (bool Clear, bool Screened)? Look(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        if (from is null || to is null) return null;
        var volumes = bodies.Contains(toModel) ? blockers.Where(v => v.Id != toModel).ToList() : blockers;
        if (!LineOfSight.Check(from.Value, to.Value, volumes).Clear) return (false, false);
        return (true, Cover.SightBlockersBetween(from.Value, to.Value, volumes).Count > 0);
    }
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
    public const string DangerousTerrain = "dangerous-terrain", CastingSeq = "casting", SpellHitsSeq = "spell-hits", RollOffSeq = "roll-off";
    /// <summary>The roll-off's two step names. The scheduler reads the order off the step_completed named by the second.</summary>
    public const string RollOffRollStep = "roll-off", RollOffOrderStep = "order";
    public const string TransportDestroyedSeq = "transport-destroyed";                                                  // p.15: a wrecked container disgorges (Task 19)
    public const double MeleeHorizontal = 2, MeleeVertical = 4, ChargeCloseIn = 3, Consolidate = 3, FallBack = 1, Separation = 1, CastRange = 18, DifficultCap = 6;
    /// <summary>p.15 and p.21: a unit leaving a container "must stay fully within 6" of it".</summary>
    public const double ExitWithin = 6;
    public const int TokenCap = 6;

    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts", Rounds = "rounds";   // phases
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough", Hero = "Hero";
    public const string Transport = "Transport";                                                                        // p.15; its (X) is carried and never read — game flow: capacity is not enforced
    public const string Indirect = "Indirect";                                                                          // p.14; a weapon property, read by GfAttack for sight and cover
    public const string Takedown = "Takedown";                                                                          // p.14; a weapon property, read by GfAttack.IsTakedownGroup
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
    public const double AmbushDistance = 9;
}
```

`GfNames.Rounds` is the literal `"rounds"`; the scheduler plan's Task 5 makes it a reference to `SchedulerBase.RoundsPhase`.

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
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams) => null;
}
```

