using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0, on the shipped 3.6 set (Core36), written from the rulebook and army-book text.
///
/// Counter (p.13): "May strike first with this weapon when charged, unless this unit is Fatigued."
///   1. "May": asked, and a pass strikes with the rest (GfMelee36Tests).  2. first: before the charger, and only that weapon first.  3. "when charged":
///   a charger's Counter weapon is not asked and strikes in turn.  4. not when Fatigued, a Shaken unit counting as Fatigued (GfMelee36Tests).
/// Counter in Melee (army rule): "This model gets Counter on its weapons in melee." Asked, the model's own weapons only, lost when Fatigued
/// (ArmyRules36Tests); a charger holding it strikes in turn (Counter's "when charged").
/// Unwieldy (army rule): "Strikes last when charging."  1. the charger's Unwieldy weapon strikes after the strike back.  2. a charged unit's strikes in
///   its strike back as usual.
/// Brace (army rule): "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued."  1. -X total on the shipped entry (one
///   weapon type: one X).  2. Fatigued/Shaken: none (GfBrace36Tests).
/// Parry (army rule): "When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many dice as hits taken. For each 6+ one
/// hit is ignored (defender picks which)."  1. X per model, capped at the hits (GfParry36Tests, here on the shipped entry).  2. a charging unit taking
///   the strike back's hits parries too.  3. not shooting (GfParry36Tests).  4. "defender picks which": the engine ignores the hits with the most
///   per-die rules first and asks nothing (not tested: no Ask exists to drive).
/// Heavy Impact (army rule): "Counts as having Impact(X) with hits that have AP(1)." With Impact(X) (p.14): "When charging, before either unit attacks,
/// roll X dice, unless this unit is Fatigued. For each 2+ the target takes 1 hit."
///   1. X dice when charging, 2+ a hit.  2. those hits block at AP(1).  3. none when charged.  4. none when Fatigued.
/// </summary>
public class ClausesAttackMeleeOrderTests
{
    static Sim Fight(Unit charger, Unit defender, params int[] dice)
    {
        var sim = Sim.Gf36(State([charger], [defender], x1: -1.3, x2: 0), dice.Length > 0 ? dice : Enumerable.Repeat(1, 40).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        return sim;
    }

    static List<(Guid Unit, string Weapon)> Strikes(Sim sim, params Unit[] units) =>
        [.. sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence)
            .Select(s => (s.Context.ActorUnitId!.Value, string.Join(",", s.Context.WeaponIds.Select(w => units.SelectMany(u => u.Models).SelectMany(m => m.Weapons).Single(x => x.Id == w).Name).Distinct())))];

    static List<RollRecord> Rolls(Sim sim, string roll) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == roll)];

    // ---- Counter --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Counter_chosen_strikes_that_weapon_before_the_charger_and_the_rest_strike_back()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var pikes = Unit("P", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Pike", 1, P("Counter")), Melee("Dagger", 1));
        var sim = Fight(charger, pikes);
        Assert.Equal("counter", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal([(pikes.Id, "Pike"), (charger.Id, "Claw"), (pikes.Id, "Dagger")], Strikes(sim, charger, pikes));
    }

    [Fact]
    public void A_chargers_counter_weapon_is_not_asked_and_strikes_in_turn()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Pike", 1, P("Counter")));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var sim = Fight(charger, d);
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);
        Assert.Equal([(charger.Id, "Pike")], Strikes(sim, charger, d));
    }

    [Fact]
    public void A_charger_with_counter_in_melee_is_not_asked_and_strikes_in_turn()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P(GfNames.CounterInMelee), P("Tough", 9)], Melee("CCW", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var sim = Fight(charger, d);
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);
        Assert.Equal([(charger.Id, "CCW")], Strikes(sim, charger, d));
    }

    // ---- Unwieldy -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_chargers_unwieldy_weapon_strikes_after_the_strike_back()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Maul", 1, P(GfNames.Unwieldy)), Melee("Knife", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Spear", 1));
        var sim = Fight(charger, d);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal([(charger.Id, "Knife"), (d.Id, "Spear"), (charger.Id, "Maul")], Strikes(sim, charger, d));
        Assert.Contains(GfNames.Fatigued, sim.Unit(charger.Id).Conditions);
    }

    [Fact]
    public void A_charged_units_unwieldy_weapon_strikes_back_in_its_turn()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Maul", 1, P(GfNames.Unwieldy)));
        var sim = Fight(charger, d);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal([(charger.Id, "Claw"), (d.Id, "Maul")], Strikes(sim, charger, d));
    }

    // ---- Brace ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Brace_takes_x_off_the_chargers_total_impact_dice_on_the_shipped_entry()
    {
        var riders = Unit("Riders", "4+", "4+", [], 3, [P("Impact", 2), P("Tough", 3)], Melee("Thrust", 1));
        var spears = Unit("Spears", "4+", "4+", [], 5, [], Melee("Spear", 1, P(GfNames.Brace, 1)));
        var sim = Fight(riders, spears);
        var impact = sim.Steps("impact").First();
        Assert.Equal(5, impact.Rolls.Sum(r => r.Dice.Count));                                          // 3 × 2 less 1
        Assert.Contains(GfNames.Brace, impact.Applied ?? []);
    }

    // ---- Parry ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Parry_rolls_x_per_model_on_the_shipped_entry()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claws", 4));
        var d = Unit("D", "4+", "4+", [], 2, [P("Tough", 3)], Melee("Hand Weapon", 1, P(GfNames.Parry, 1)));
        var sim = Fight(charger, d, [4, 4, 4, 4, .. Enumerable.Repeat(1, 40)]);
        Assert.Equal(2, Rolls(sim, GfNames.Parry).First().Dice.Count);
    }

    [Fact]
    public void A_charging_unit_with_parry_parries_the_strike_backs_hits()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Hand Weapon", 1, P(GfNames.Parry, 2)));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claws", 3));
        var sim = Fight(charger, d, [1, 4, 4, 4, 6, 1, .. Enumerable.Repeat(1, 20)]);                  // the charger misses; three hits back; Parry 6, 1
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var parry = Assert.Single(Rolls(sim, GfNames.Parry));
        Assert.Equal(2, parry.Dice.Count);
        Assert.Equal(2, Rolls(sim, GfNames.DefenseRoll).Sum(r => r.Dice.Count));                      // one of three hits ignored
    }

    // ---- Heavy Impact ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Heavy_impact_rolls_x_dice_when_charging_hitting_on_two_plus_with_ap_one()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P(GfNames.HeavyImpact, 2), P("Tough", 9)], Melee("CCW", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var sim = Fight(charger, d, [2, 1, 4, .. Enumerable.Repeat(1, 20)]);                           // impact 2, 1: one hit; its block 4 - 1 = 3 fails
        var impact = sim.Steps("impact").First().Rolls.Single();
        Assert.Equal((GfNames.HeavyImpact, 2, 1), (impact.Roll, impact.Dice.Count, impact.Dice.Count(d => d.Success)));
        var block = Rolls(sim, GfNames.DefenseRoll).First();
        Assert.Equal(-1, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.False(block.Dice.Single().Success);
        Assert.Equal(1, sim.Model(d.Models[0].Id).Wounds);
    }

    [Fact]
    public void Heavy_impact_rolls_nothing_when_charged()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("CCW", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P(GfNames.HeavyImpact, 2), P("Tough", 9)], Melee("Claw", 1));
        var sim = Fight(charger, d);
        Assert.Empty(sim.Steps("impact").SelectMany(s => s.Rolls));
    }

    [Fact]
    public void Heavy_impact_rolls_nothing_when_the_charger_is_fatigued()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P(GfNames.HeavyImpact, 2), P("Tough", 9)], Melee("CCW", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var sim = Sim.Gf36(State([charger], [d], x1: -1.3, x2: 0), Enumerable.Repeat(6, 40).ToArray());
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, charger.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        Assert.Empty(sim.Steps("impact").SelectMany(s => s.Rolls));
    }
}
