# Task 16: Charge and melee

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfMelee.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfMeleeTests.cs`, `tests/Tabletop.GameServer.Tests/GfGroupsTests.cs`

**Interfaces:**
- Consumes: Tasks 11–15; `PieceSelection`, `SelectedPieces` (Protocol).
- Produces: `GfMelee.Charge()` (`charge`: `activation-terrain` (`GfMoves.ActivationTerrainStep`, p.11's *activated in it*, Task 14), `charge-target`, `move` (`MoveStep(Charge, ignoreSeparation: true)`), `defender-closes`, `fight`), `GfMelee.Melee()` (`melee`: `melee-start`, `counter`, `impact`, `charger-attacks`, `strike-back`, `compare`, `consolidate`); `static IRL<Guid> MeleeWeapons(GameState, Guid unit, Func<Weapon,bool>? filter = null)`; `static IRL<Guid> ChargeTargets(GameState, PropertySet, Guid unit)` (the enemy units `charge-target` offers; the scheduler's `ActionsFor` offers Charge only when it is non-empty, p.7 *"Units may only take a Charge action if at least one model is within charge distance of the target unit"*); `static int WoundsCaused(StepContext, Guid by, Guid to, string beforeKey, bool attacked)` (the target's `WoundsThisActivation` less the value recorded at `melee-start`, plus `Fear(X)` summed over the attacker's models when it attacked).
- `charge-target`: `PieceSelection(min 1, max 1)` over `ChargeTargets`: enemy units with a model within charge distance (`StatReader.ActionDistance(Charge)`, `Fast`/`Slow` included) of a charger model whose straight path crosses no impassable volume (`Movement.Check(...).CrossesImpassable`); none → `Finish` (p.7: a charge needs a target in distance). An answer that names no unit the Ask offered (`GfAnswers.Pieces`) is no target: `Finish`. Later steps read the target through `GfAnswers.Pieces(state, frame, "charge-target", "charge_target")`, and `defender-closes` and `fight` `Finish` when it is empty. **A charger inside a container measures from the container instead** — p.15 says *"Units may use Charge actions to disembark"*, and an occupant has no position of its own, so its reach is `GfNames.ExitWithin` from the container it is inside: an enemy within that is one it could be placed fully within 6" of and then be in contact with. The step's own `move` (`MoveStep(Charge, ignoreSeparation: true)`) is then the disembark, since `MoveStep.Containers` offers the exit on a Charge and never the enterable list (Task 14). The mirror also falls out: **a unit inside a container cannot be charged**, because the predicate measures to the target's *living* models and an occupant has none — which is p.15's no line of sight in. `defender-closes`: `MoveRegion(3")` to the defender's team for its models not in base contact (edge-to-edge distance above `Touching = 1e-6`, float noise) — `Passed` moves nobody, and its moves and `consolidate`'s are read through `GfAnswers.Moves`. `fight`: no charger model in melee reach → `Finish` (p.7: "the unit may complete its move in any direction and ends its activation"); else `Push(melee, (charger, [target], charger's melee weapons, Charge))`.
- `melee`: `melee-start` records both units' `WoundsThisActivation` (`charger_before`, `defender_before`). Every striking step (`counter`, `charger-attacks`, `strike-back`) is `Strike`, two phases as `fire-groups`: the first run records each melee weapon group's attacks per Quality value and the defending unit's living models (`attacks_<i>_q<n>`, `attacks_<i>`, `defender_models_<i>`) and repeats; the repeat pushes one `attack-sequence` per group in `GfGroups.Of`'s order, and `determine-attacks` reads the recorded counts. p.9 sums *"how many attacks the unit has in total for this melee"*, so a group that strikes after an earlier group's casualties is not shortened. A target with no model left ends the pushing. `counter` strikes the defender's `Counter` weapons before anything else (the 09-03 ruling; p.13 moves that weapon's strike earlier and does not add a second one) and records `Values["struck"]`; the defender is not fatigued here. `impact` — charger models with `Impact(X)`, unless fatigued: `Σ X − (defender models with a Counter weapon)` dice on the effect's threshold, recorded as roll `Impact`, `Values["hits"]`, then `Push(resolve-hits)` when hits > 0. `charger-attacks` strikes the charger's weapons and, when the last group has finished, sets `fatigued` on the charger. `strike-back` — a living defender with a non-`Counter` melee weapon is asked `Options([strike, pass])`, and a destroyed charger skips the Ask; `strike` strikes with the non-`Counter` weapons under a null `Action`, so the `Charge`-keyed rules (`Furious`, `Impact`, `Thrust`) apply only to the charger (p.9), and the defender's fatigue is set once, at the end of `strike-back`, when it struck at all, `counter` included, even if its strike destroyed the charger. `compare` — wounds caused each way (`Fear` counting for a unit that attacked, which includes a defender whose `Counter` weapon struck), a destroyed unit always loses, the loser pushes `morale-test` with `Action = "melee"`, a tie tests nobody; it completes twice when the loser tests, so a reader takes the first completion's values. `consolidate` — one unit destroyed or routed → the other's team gets `MoveRegion(3")`; otherwise the charger's team gets `MoveRegion(1")` to fall back (p.10). A Shaken defender may strike back, counting as fatigued (p.10, `GfRolls.Fatigued`).

Spec: action sequences → Grimdark Future's sequences (Charge, melee's seven bullets), Rulings (`Counter` before `Impact`); rulebook p.7 (charge distance, path), p.9 (melee reach, striking back, fatigue), p.10 (who won, consolidation), p.13 `Counter`, `Fear`, p.14 `Impact`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfMeleeTests
{
    static Destinations Into(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Models.Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location,
            new OnTable(new Placement(((OnTable)sim.Model(target.Models[Math.Min(i, target.Models.Count - 1)].Id).Location).Placement.Position with { X = ((OnTable)sim.Model(target.Models[0].Id).Location).Placement.Position.X - 1.3 }, new Yaw(0))))).ToList());

    /// <summary>A single-model charger ends its move with the given edge-to-edge gap to a single-model target.</summary>
    static Destinations Stop(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target, double gap)
    {
        var (from, to) = (sim.Model(charger.Models[0].Id).Location, (OnTable)sim.Model(target.Models[0].Id).Location);
        return new([new ModelRelocation(charger.Models[0].Id, from, new OnTable(new Placement(to.Placement.Position with { X = to.Placement.Position.X - 1.26 - gap }, new Yaw(0))))]);
    }

    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    [Fact]
    public void Pages_9_and_10_warriors_charge_dwarves_who_lose_the_melee_and_are_shaken()
    {
        var warriors = Unit("Dynasty Warriors", "4+", "4+", [], 3, [], Melee("CCW", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 4, [], Melee("Hammer", 1));
        // The charger's half is page 9's worked example verbatim — 3 attacks at Quality 4+ rolling 3,4,5 for two hits, Defense 4+ rolling 3,4 for one wound.
        // The rest is constructed. In draw order: charger hits 3,4,5; blocks 3,4; strike-back hits 6,1,1 (one hit); block 5 (blocked); morale 2 (failed).
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -8, x2: 0), 3, 4, 5, 3, 4, 6, 1, 1, 5, 2);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(warriors.Id, [], [], GfNames.Charge));
        var target = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([dwarves.Id], target.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        Assert.Equal(12.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        sim.Answer(Samples.Team1, Into(sim, warriors, dwarves));
        sim.Answer(Samples.Team2, new Passed());                                                    // no caster, so no checkpoint; the defender declines its 3" close
        Assert.Contains(sim.State.Projection.Stack, f => f.Sequence == GfNames.Melee);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dwarves.Models[0].Id] = 1 }));   // one dwarf falls
        Assert.Contains(GfNames.Fatigued, sim.Unit(warriors.Id).Conditions);
        Assert.Equal(["strike", "pass"], Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team2, new Chosen("strike"));                                            // one hit, blocked: no wound to allocate
        var compare = sim.Steps("compare").First();                                                 // the loser's morale test pushes, so compare completes twice
        Assert.Equal((1, 0, 2), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.Contains(GfNames.Shaken, sim.Unit(dwarves.Id).Conditions);                           // 3 of 4 alive: Shaken, not routed
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);      // nobody destroyed: the charger falls back 1"
        sim.Answer(Samples.Team1, new Passed());
        Assert.Empty(sim.State.Projection.Stack);
        Assert.True(GfHooks.FoughtMeleeThisActivation(sim.State, warriors.Id, dwarves.Id));         // anchored on the charge sequence, not on the last nested frame
        Assert.True(GfHooks.FoughtMeleeThisActivation(sim.State, warriors.Id, warriors.Id));
    }

    [Fact]
    public void Counter_strikes_before_impact_and_reduces_impact_dice_and_fatigue_leaves_only_sixes()
    {
        var riders = Unit("Riders", "4+", "4+", [], 2, [P("Impact", 2), P("Tough", 2)], Melee("Thrust", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([riders], [pikes], x1: -3, x2: 0), 6, 1, 1, 5, 3, 1, 1);           // pike hits 6, rider blocks 1; Impact 1,5,3; pike blocks 1,1
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([pikes.Id]));
        sim.Answer(Samples.Team1, Into(sim, riders, pikes));
        sim.Answer(Samples.Team2, new Passed());
        var started = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(pikes.Id, started[0].Context.ActorUnitId);                                      // Counter first
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [riders.Models[0].Id] = 1 }));  // the pike's hit (6) got through (block 1)
        var impact = sim.Steps("impact").First().Rolls.Single();                                     // the step completes again after pushing resolve-hits
        Assert.Equal(("Impact", 2, 3), (impact.Roll, impact.Threshold, impact.Dice.Count));         // 2 × Impact(2) = 4, −1 for the Counter model
        Assert.Equal(2, sim.Steps("impact").First().Values!["hits"]);                               // 1,5,3 → two at 2+
        // Both hits wound (1,1) and the pike is the only model that can take them, so no Allocation Ask opens: it dies.
        Assert.Equal(0, sim.Alive(pikes.Id));
        Assert.Equal(2, sim.Steps("compare").Single().Values!["loser"]);                            // a destroyed unit always loses; nobody left to test
        Assert.Equal(3.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);    // consolidate 3"
        sim.Answer(Samples.Team1, new Passed());
        Assert.Contains(GfNames.Fatigued, sim.Unit(riders.Id).Conditions);
        // A second melee this round: fatigued, the riders hit only on unmodified 6s.
        var again = Unit("Again", "4+", "4+", [], 2, [], Melee("Club", 1));
        var sim2 = Sim.Gf(State([riders], [again], x1: -3, x2: 0), 5, 6, 1, 1, 1);
        sim2.Append([new ConditionSet(new Subject(SubjectKind.Unit, riders.Id), GfNames.Fatigued, true)]);
        sim2.Start(GfNames.Melee, new SequenceContext(riders.Id, [again.Id], riders.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        var hit = sim2.Steps("roll-to-hit").First().Rolls.Single();
        Assert.Equal(6, hit.Threshold);
        Assert.Equal([false, true], hit.Dice.Select(d => d.Success));
        Assert.Empty(sim2.Steps("impact").Single().Rolls);                                          // Impact does not roll when fatigued
    }

    [Fact]
    public void No_target_in_charge_distance_ends_the_activation()
    {
        var u = Unit("U", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        // activation-terrain (no dangerous terrain), then charge-target with nothing in range: Finish.
        Assert.Equal(["step_completed", "step_completed", "sequence_finished"], sim.Start(GfNames.ChargeSeq, new SequenceContext(u.Id, [], [], GfNames.Charge)).Select(EventKinds.Of));
    }

    [Fact]
    public void A_charge_disembarks_measuring_from_the_transport_and_a_unit_inside_one_cannot_be_charged()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, [], Melee("Axe", 1));
        var enemy = Unit("Enemy", "4+", "4+", [], 1, [], Melee("Club", 1));
        var truck = Unit("Truck", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var hidden = Unit("Hidden", "4+", "4+", [], 1, [], Melee("Knife", 1));
        // Armies.State rows: team1's bus and riders and team2's enemy, truck and hidden each 12" apart in y; the enemy is the
        // only enemy model within 6" of the bus, and the truck's row is 12" away.
        var sim = Sim.Gf(State([bus, riders], [enemy, truck, hidden], x1: -4, x2: 0), 1, 1, 1, 1);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved([
            new ModelRelocation(riders.Models[0].Id, sim.Model(riders.Models[0].Id).Location, new Inside(container)),
            new ModelRelocation(hidden.Models[0].Id, sim.Model(hidden.Models[0].Id).Location, new Inside(truck.Models[0].Id))])]);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        // p.15: the reach is 6" from the bus, and a unit inside a container of its own is not a target at all.
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([enemy.Id], pick.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id]));
        var move = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (move.Containment!.ExitingId!.Value, move.Containment.ExitWithin!.Value));
        Assert.Empty(move.Containment.Enterable);                                                   // p.14: a Charge disembarks; it never embarks
    }

    [Fact]
    public void A_shaken_unit_strikes_back_counting_as_fatigued()
    {
        var charger = Unit("Charger", "4+", "4+", [], 2, [], Melee("Sword", 1));
        var shaken = Unit("Shaken", "4+", "4+", [], 2, [], Melee("Club", 1));
        var sim = Sim.Gf(State([charger], [shaken], x1: -3, x2: 0), 1, 1, 5, 6, 1);                // charger misses twice; strike-back 5,6; block 1
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, shaken.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, shaken));
        sim.Answer(Samples.Team2, new Chosen("strike"));                                            // p.10: Shaken units "may strike back counting as fatigued"
        var hit = sim.Steps("roll-to-hit").Last().Rolls.Single();
        Assert.Equal(6, hit.Threshold);
        Assert.Equal([false, true], hit.Dice.Select(d => d.Success));
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [charger.Models[0].Id] = 1 }));   // the one hit that got through
        Assert.Contains(GfNames.Fatigued, sim.Unit(shaken.Id).Conditions);                          // and striking back fatigued it as well
    }

    [Fact]
    public void The_fight_needs_a_charger_model_within_two_inches_of_the_target()
    {
        static Sim Charge(double gap, out Tabletop.Content.Unit charger)
        {
            charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
            var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
            var sim = Sim.Gf(State([charger], [target], x1: -8, x2: 0), 1);
            sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
            sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
            sim.Answer(Samples.Team1, Stop(sim, charger, target, gap));
            sim.Answer(Samples.Team2, new Passed());
            return sim;
        }
        var short2 = Charge(2.4, out _);                                                            // p.9: 2" horizontally; 2.4" is out of reach
        Assert.DoesNotContain(short2.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.Melee);
        Assert.Empty(short2.State.Projection.Stack);                                                // p.7: the unit ends its activation
        var close = Charge(1.9, out _);
        Assert.Contains(close.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.Melee);
    }

    [Fact]
    public void Fear_counts_as_wounds_dealt_when_the_melee_is_compared()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Fear", 1)], Melee("Claw", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], x1: -3, x2: 0), 1, 6);                          // the charger misses; the target passes its morale test
        sim.Start(GfNames.Melee, MeleeOf(charger, target));
        sim.Answer(Samples.Team2, new Chosen("pass"));
        var compare = sim.Steps("compare").First();
        Assert.Equal((1, 0, 2), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));   // no wound landed, Fear(1) won it
    }

    [Fact]
    public void A_single_tough_model_at_half_or_less_that_fails_the_melee_test_routs()
    {
        var brute = Unit("Brute", "4+", "4+", [], 1, [P("Tough", 3)]);
        var routed = Sim.Gf(State([brute], [Unit("E", "4+", "4+", [], 1, [])]), 1);
        routed.Append([new WoundsSet(brute.Models[0].Id, 2)]);                                       // 1 of 3 left: half or less
        routed.Start(GfNames.MoraleTest, new SequenceContext(brute.Id, [], [], "melee"));
        Assert.Equal(0, routed.Alive(brute.Id));
        var shaken = Sim.Gf(State([brute], [Unit("E", "4+", "4+", [], 1, [])]), 1);
        shaken.Append([new WoundsSet(brute.Models[0].Id, 1)]);                                       // 2 of 3 left: over half
        shaken.Start(GfNames.MoraleTest, new SequenceContext(brute.Id, [], [], "melee"));
        Assert.Equal(1, shaken.Alive(brute.Id));
        Assert.Contains(GfNames.Shaken, shaken.Unit(brute.Id).Conditions);
    }

    [Fact]
    public void A_tie_tests_nobody_and_the_charger_falls_back()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], x1: -3, x2: 0), 1, 1);                          // both miss: 0-0
        sim.Start(GfNames.Melee, MeleeOf(charger, target));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var compare = sim.Steps("compare").Single();                                                // no morale test pushed, so compare completes once
        Assert.Equal((0, 0, 0), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.MoraleTest);
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);      // p.10: the charger moves back 1"
    }

    [Fact]
    public void A_charge_with_waits_between_steps_asks_for_the_target_and_then_the_move()
    {
        var warriors = Unit("Warriors", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 1, [], Melee("Hammer", 1));
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -8, x2: 0, waits: true), 1);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(warriors.Id, [], [], GfNames.Charge));
        sim.Confirm();
        Assert.Equal([dwarves.Id], Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal).Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        sim.Confirm();
        Assert.Equal(12.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void A_melee_with_waits_between_steps_asks_for_the_consolidation_move()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], x1: -3, x2: 0, waits: true), 1, 1);              // both miss: 0-0
        sim.Start(GfNames.Melee, MeleeOf(charger, target));
        sim.Confirm();
        sim.Answer(Samples.Team2, new Chosen("strike"));
        sim.Confirm();
        var move = sim.AskFor(Samples.Team1);
        Assert.Equal(("move", 1.0), (move.Question, Assert.IsType<MoveRegion>(move.Legal).Allowance));   // p.10: the charger moves back 1"
    }

    [Fact]
    public void Fear_counts_for_the_defender_when_it_strikes_back()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [P("Fear", 1)], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], x1: -3, x2: 0), 1, 1, 6);                       // both miss; the charger passes its morale test
        sim.Start(GfNames.Melee, MeleeOf(charger, target));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var compare = sim.Steps("compare").First();
        Assert.Equal((0, 1, 1), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));   // Fear(1) on the striking-back side
    }

    [Fact]
    public void A_counter_defender_strikes_back_only_with_its_other_weapons_and_is_not_fatigued_by_its_counter_strike()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")), Melee("Dagger", 1));
        var dagger = pikes.Models[0].Weapons.Single(w => w.Name == "Dagger");
        var sim = Sim.Gf(State([charger], [pikes], x1: -3, x2: 0), 1, 1, 4, 6);                     // Counter strike misses; charger misses; dagger hits 4; the block 6 stops it
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        Assert.Equal(["strike", "pass"], Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal).Choices.Select(c => c.Id));
        Assert.DoesNotContain(GfNames.Fatigued, sim.Unit(pikes.Id).Conditions);                     // p.13: Counter moves the strike earlier, it does not add one
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var attacks = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == pikes.Id).ToList();
        Assert.Equal(2, attacks.Count);                                                             // the Counter strike, then the strike back
        Assert.Equal([dagger.Id], attacks[1].Context.WeaponIds);
        Assert.Equal(4, sim.Steps("roll-to-hit").Last().Rolls.Single().Threshold);                  // not fatigued when it strikes back
        Assert.Contains(GfNames.Fatigued, sim.Unit(pikes.Id).Conditions);
    }

    [Fact]
    public void A_counter_defender_with_no_other_weapon_has_nothing_to_strike_back_with_and_ends_fatigued()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([charger], [pikes], x1: -3, x2: 0), 1, 1);
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);      // no strike-back Ask: straight to compare and consolidation
        Assert.Contains(GfNames.Fatigued, sim.Unit(pikes.Id).Conditions);                           // its Counter strike fatigued it, once
    }

    [Fact]
    public void A_counter_strike_that_changes_nothing_still_counts_as_the_defender_striking()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [P("Fear", 1)], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([charger], [pikes], x1: -3, x2: 0), 1, 1, 6);                        // Counter strike misses; charger misses; charger passes its morale test
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        var compare = sim.Steps("compare").First();
        Assert.Equal((0, 1, 1), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));   // Fear(1): it struck, though nothing changed
    }

    [Fact]
    public void A_counter_strike_that_destroys_the_charger_still_fatigues_the_defender()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Tough", 1)], Melee("Sword", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        // Counter hits 6, charger blocks 1 (fails), charger dies (Tough-1 with 1 wound)
        var sim = Sim.Gf(State([charger], [defender], x1: -3, x2: 0), 6, 1);
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        // After Counter step, charger should be dead
        Assert.Equal(0, sim.Alive(charger.Id));
        // Defender should still be fatigued because it struck via Counter
        Assert.Contains(GfNames.Fatigued, sim.Unit(defender.Id).Conditions);
    }
    [Fact]
    public void Melee_groups_strike_with_the_attacks_and_defender_count_the_melee_started_with()
    {
        // p.9: "Sum the Attack value from the weapons of all models that can strike ... how many attacks the unit has in total for this
        // melee." Two chargers, each with an Axe and a Zed, each opposite its own defender. The Axe group kills the defender facing
        // charger 2, which leaves charger 2 with nobody within 2"; the Zed group still rolls both chargers' attacks against both models.
        var chargers = Unit("Chargers", "4+", "4+", [], 2, [], Melee("Axe", 1), Melee("Zed", 1));
        var defenders = Unit("Defenders", "4+", "6+", [], 2, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([chargers], [defenders], x1: -3, x2: 0), 6, 1, 1, 1, 1);          // Axe hits 6, misses 1; block 1; Zed 1,1
        static ModelRelocation To(Sim sim, Guid model, double x, double y) => new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
        sim.Append([new ModelsMoved([To(sim, chargers.Models[0].Id, -1.3, -18.5), To(sim, defenders.Models[0].Id, 0, -18.5),
            To(sim, chargers.Models[1].Id, -1.3, -8), To(sim, defenders.Models[1].Id, 0, -8)])]);
        sim.Start(GfNames.Melee, MeleeOf(chargers, defenders));
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [defenders.Models[1].Id] = 1 }));   // the defender facing charger 2 falls
        Assert.Equal(1, sim.Alive(defenders.Id));
        var zed = sim.Steps("determine-attacks").Last();
        Assert.Equal((2, 1, 2), (zed.Values!["attacks"], zed.Values["eligible_models"], zed.Values["defender_models"]));
        Assert.Equal(2, sim.Steps("roll-to-hit").Last().Rolls.Single().Dice.Count);
    }

    [Fact]
    public void A_strike_back_that_destroys_the_charger_fatigues_the_defender()
    {
        // p.9: fatigue follows "attacking in melee for the first time during a round, either by charging or by striking back".
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [defender], x1: -3, x2: 0), 1, 6, 1);                    // the charger misses; the strike back hits 6; the block 1 fails
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal(0, sim.Alive(charger.Id));
        Assert.Contains(GfNames.Fatigued, sim.Unit(defender.Id).Conditions);
    }
}
```

