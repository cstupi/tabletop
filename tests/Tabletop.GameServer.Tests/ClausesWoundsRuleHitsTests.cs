using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0 army rules (army-rules.part.json ruleText).
///
/// Deathstrike(X): "If this model is killed in melee, the attacking unit takes X hits."
///   D1 killed in melee: the attacking unit takes X hits (blocked as hits are).
///   D2 "killed": a wounded model that survives sets nothing off.
///   D3 "in melee": a model killed by shooting sets nothing off.
///   D4 "this model": only a carrier's death counts; one X per carrier killed (an upgrade on some models).
///   D5 "the attacking unit": a charger carrier killed by the strike back deals its hits to the defender.
///   D6 a Ravage wound at a strike turn kills in melee too.
///
/// Retaliate(X): "When this model takes wounds in melee, the attacker takes X hits per wound taken."
///   R1 X hits per wound taken, on the attacker.
///   R2 "in melee": wounds from shooting set nothing off.
///   R3 "this model": a wound on a model of its unit without the rule sets nothing off.
///   R4 "takes wounds": a wound ignored (Regeneration) is not taken.
///   R5 a Ravage wound at a strike turn is a wound taken in melee.
///
/// Self-Destruct(X): "If this model is killed in melee, the attacking unit takes X hits. If this model survives melee, after both sides have finished
/// attacking, it is immediately killed, and the enemy unit takes X hits."
///   S1 killed in melee: the attacking unit takes X hits, once (not again for surviving).
///   S2 survives melee: killed after both sides finished attacking, and the enemy unit takes X hits.
///   S3 a defending carrier that survives self-destructs too, the charger taking the hits; also when the defender did not strike back.
///   S4 "this model": only the carrier is killed; the other models of its unit stay.
///   S5 "melee": a carrier that survives shooting stays.
///
/// Dice: a constant face with the stats chosen around it (a 2 hits on 2+, misses on 6+; blocks on 2+, fails on 6+).
/// </summary>
public class ClausesWoundsRuleHitsTests
{
    /// <summary>A 3.6 charge of `charger` into `defender` 2" apart; the defender strikes back or not; every allocation goes to `allocate`'s pick (else the
    /// first offered model); every other Ask passes.</summary>
    static Sim Fight(Tabletop.Content.Unit charger, Tabletop.Content.Unit defender, int[] dice, bool strikeBack = true, Func<Allocation, Guid>? allocate = null)
    {
        var sim = Sim.Gf36(State([charger], [defender], x1: -1, x2: 1), dice);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        Drive(sim, strikeBack, allocate);
        return sim;
    }

    static void Drive(Sim sim, bool strikeBack = true, Func<Allocation, Guid>? allocate = null)
    {
        for (var i = 0; i < 40; i++)
        {
            var team = sim.AskForOrNull(Samples.Team2) is not null ? Samples.Team2 : sim.AskForOrNull(Samples.Team1) is not null ? Samples.Team1 : (Guid?)null;
            if (team is not { } t) return;
            var ask = sim.AskFor(t);
            sim.Answer(t, ask.Question == "strike_back" ? new Chosen(strikeBack ? "strike" : "pass")
                : ask.Legal is Allocation al ? new Allocated(new Dictionary<Guid, int> { [allocate?.Invoke(al) ?? al.Targets[0]] = al.Amount })
                : new Passed());
        }
    }

    static Sim Shoot(Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, params int[] dice)
    {
        var sim = Sim.Gf36(State([shooter], [target], x1: -6, x2: 6), dice);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        Drive(sim);
        return sim;
    }

    static IReadOnlyList<SequenceStarted> RuleHits(Sim sim) => sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.RuleHitsSeq).ToList();
    static IReadOnlyList<(string? Action, Guid By, Guid On)> Dealt(Sim sim) => RuleHits(sim).Select(s => (s.Context.Action, s.Context.ActorUnitId!.Value, s.Context.TargetUnitIds[0])).ToList();

    // ---- Deathstrike ----

    [Fact]
    public void D1_a_deathstrike_model_killed_in_melee_deals_x_hits_to_the_attacking_unit()
    {
        var charger = Unit("C", "2+", "6+", [], 1, [P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Deathstrike", 3)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.Equal([("Deathstrike|3", defender.Id, charger.Id)], Dealt(sim));
        Assert.Equal(3, sim.Model(charger.Models[0].Id).Wounds);                      // three hits, each block failed
    }

    [Fact]
    public void D2_a_deathstrike_model_wounded_but_not_killed_deals_nothing()
    {
        var charger = Unit("C", "2+", "6+", [], 1, [P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Deathstrike", 3), P("Tough", 3)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.Equal(1, sim.Model(defender.Models[0].Id).Wounds);
        Assert.Empty(RuleHits(sim));
    }

    [Fact]
    public void D3_a_deathstrike_model_killed_by_shooting_deals_nothing()
    {
        var shooter = Unit("S", "2+", "6+", [], 1, [], Gun("Rifle", "24\"", 1));
        var target = Unit("D", "6+", "6+", [], 1, [P("Deathstrike", 3)]);
        var sim = Shoot(shooter, target, 2);
        Assert.Equal(0, sim.Alive(target.Id));
        Assert.Empty(RuleHits(sim));
    }

    [Fact]
    public void D4_only_a_carriers_death_deals_hits_one_x_per_carrier_killed()
    {
        var charger = Unit("C", "2+", "2+", [], 1, [], Melee("Axe", 3));
        var defender = Unit("D", "6+", "6+", [], 3, [], Melee("Club", 1));
        defender = defender with { Models = [defender.Models[0], defender.Models[1] with { Properties = [P("Deathstrike", 2)] }, defender.Models[2] with { Properties = [P("Deathstrike", 2)] }] };
        var sim = Fight(charger, defender, [2]);
        Assert.Equal(0, sim.Alive(defender.Id));
        Assert.Equal([("Deathstrike|2", defender.Id, charger.Id), ("Deathstrike|2", defender.Id, charger.Id)], Dealt(sim));

        var one = Fight(charger with { Models = [charger.Models[0] with { Weapons = [Melee("Axe", 1)] }] }, defender, [2], allocate: al => defender.Models[0].Id);
        Assert.Equal(2, one.Alive(defender.Id));                                        // the model without the rule was killed
        Assert.Empty(RuleHits(one));
    }

    [Fact]
    public void D5_a_charging_carrier_killed_by_the_strike_back_deals_its_hits_to_the_defender()
    {
        var charger = Unit("C", "6+", "6+", [], 1, [P("Deathstrike", 2)], Melee("Axe", 1));
        var defender = Unit("D", "2+", "6+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.Equal(0, sim.Alive(charger.Id));
        Assert.Equal([("Deathstrike|2", charger.Id, defender.Id)], Dealt(sim));
        Assert.Equal(2, sim.Model(defender.Models[0].Id).Wounds);
    }

    /// <summary>Ravage's wound is dealt "when it's this model's turn to strike in melee": a Deathstrike model it kills is killed in melee.</summary>
    [Fact]
    public void D6_a_deathstrike_model_killed_by_a_ravage_wound_in_melee_deals_its_hits()
    {
        var charger = Unit("C", "6+", "6+", [], 1, [P("Ravage", 1), P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Deathstrike", 2)], Melee("Club", 1));
        var sim = Fight(charger, defender, [6]);                                        // Ravage's 6 wounds; every attack hits and every block holds
        Assert.Equal(0, sim.Alive(defender.Id));
        Assert.Contains(("Deathstrike|2", defender.Id, charger.Id), Dealt(sim));
    }

    // ---- Retaliate ----

    [Fact]
    public void R1_retaliate_deals_x_hits_per_wound_taken_to_the_attacker()
    {
        var charger = Unit("C", "2+", "6+", [], 1, [P("Tough", 12)], Melee("Axe", 3));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Retaliate", 2), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.Equal(3, sim.Model(defender.Models[0].Id).Wounds);
        Assert.Equal([("Retaliate|6", defender.Id, charger.Id)], Dealt(sim));
        Assert.Equal(6, sim.Model(charger.Models[0].Id).Wounds);
    }

    [Fact]
    public void R2_wounds_from_shooting_set_off_no_retaliate()
    {
        var shooter = Unit("S", "2+", "6+", [], 1, [], Gun("Rifle", "24\"", 2));
        var target = Unit("D", "6+", "6+", [], 1, [P("Retaliate", 2), P("Tough", 6)]);
        var sim = Shoot(shooter, target, 2);
        Assert.Equal(2, sim.Model(target.Models[0].Id).Wounds);
        Assert.Empty(RuleHits(sim));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void R3_only_wounds_on_the_carrier_set_off_retaliate(int wounded, bool retaliates)
    {
        var charger = Unit("C", "2+", "2+", [], 1, [], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 3, [], Melee("Club", 1));
        defender = defender with { Models = [defender.Models[0], defender.Models[1] with { Properties = [P("Retaliate", 1)] }, defender.Models[2]] };
        var sim = Fight(charger, defender, [2], allocate: _ => defender.Models[wounded].Id);
        Assert.Equal(2, sim.Alive(defender.Id));                                        // three models, so the lost melee's failed test does not rout
        Assert.Equal(retaliates ? [("Retaliate|1", defender.Id, charger.Id)] : [], Dealt(sim));
    }

    [Fact]
    public void R4_a_wound_ignored_is_not_taken()
    {
        var charger = Unit("C", "2+", "6+", [], 1, [P("Tough", 6)], Melee("Axe", 2));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Retaliate", 2), P("Regeneration"), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [5]);                                        // hits on 2+, blocks fail on 6+, Regeneration ignores on 5+
        Assert.Equal(0, sim.Model(defender.Models[0].Id).Wounds);
        Assert.Empty(RuleHits(sim));
    }

    [Fact]
    public void R5_a_ravage_wound_in_melee_sets_off_retaliate()
    {
        var charger = Unit("C", "6+", "6+", [], 1, [P("Ravage", 1), P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Retaliate", 2), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [6]);
        Assert.Equal(1, sim.Model(defender.Models[0].Id).Wounds);
        Assert.Contains(("Retaliate|2", defender.Id, charger.Id), Dealt(sim));
    }

    /// <summary>The hits Retaliate deals are no strike in melee: a Retaliate charger wounded by the defender's Retaliate hits deals nothing back.</summary>
    [Fact]
    public void R6_retaliate_hits_set_off_no_retaliate_in_turn()
    {
        var charger = Unit("C", "2+", "6+", [], 1, [P("Retaliate", 1), P("Tough", 12)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Retaliate", 1), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2], strikeBack: false);
        Assert.Equal(1, sim.Model(charger.Models[0].Id).Wounds);                        // the defender's Retaliate hit wounded it
        Assert.Equal([("Retaliate|1", defender.Id, charger.Id)], Dealt(sim));
    }

    // ---- Self-Destruct ----

    [Fact]
    public void S1_a_self_destruct_model_killed_in_melee_deals_x_hits_once()
    {
        var charger = Unit("C", "2+", "6+", [], 1, [P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "6+", [], 1, [P("Self-Destruct", 2)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.Equal([("Self-Destruct|2", defender.Id, charger.Id)], Dealt(sim));
        Assert.Equal(2, sim.Model(charger.Models[0].Id).Wounds);
    }

    [Fact]
    public void S2_a_surviving_charger_carrier_is_killed_after_both_sides_attacked_and_the_enemy_takes_x_hits()
    {
        var charger = Unit("C", "6+", "2+", [], 1, [P("Self-Destruct", 2)], Melee("Axe", 1));
        var defender = Unit("D", "2+", "6+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.IsType<OnShelf>(sim.Model(charger.Models[0].Id).Location);
        Assert.Equal([("Self-Destruct|2", charger.Id, defender.Id)], Dealt(sim));
        Assert.Equal(2, sim.Model(defender.Models[0].Id).Wounds);
        var strikeBack = sim.Emitted.FindIndex(e => e is SequenceStarted { Sequence: GfNames.AttackSequence } s && s.Context.ActorUnitId == defender.Id);
        var removed = sim.Emitted.FindIndex(e => e is StepCompleted { Step: "self-destruct" } c && c.Changes.OfType<ModelsMoved>().Any());
        Assert.InRange(strikeBack, 0, removed);                                         // after both sides finished attacking
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void S3_a_surviving_defending_carrier_self_destructs_on_the_charger(bool strikeBack)
    {
        var charger = Unit("C", "6+", "6+", [], 1, [P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "6+", "2+", [], 1, [P("Self-Destruct", 3)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2], strikeBack);
        Assert.IsType<OnShelf>(sim.Model(defender.Models[0].Id).Location);
        Assert.Equal([("Self-Destruct|3", defender.Id, charger.Id)], Dealt(sim));
        Assert.Equal(3, sim.Model(charger.Models[0].Id).Wounds);
    }

    [Fact]
    public void S4_only_the_carrier_is_killed_and_its_x_alone_is_dealt()
    {
        var charger = Unit("C", "6+", "2+", [], 3, [], Melee("Axe", 1));
        charger = charger with { Models = [charger.Models[0] with { Properties = [P("Self-Destruct", 1)] }, .. charger.Models.Skip(1)] };
        var defender = Unit("D", "6+", "6+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [2]);
        Assert.IsType<OnShelf>(sim.Model(charger.Models[0].Id).Location);
        Assert.Equal(2, sim.Alive(charger.Id));
        Assert.Equal([("Self-Destruct|1", charger.Id, defender.Id)], Dealt(sim));
    }

    [Fact]
    public void S5_a_carrier_that_survives_shooting_stays()
    {
        var shooter = Unit("S", "2+", "6+", [], 1, [P("Self-Destruct", 2)], Gun("Rifle", "24\"", 1));
        var target = Unit("T", "6+", "6+", [], 1, [P("Self-Destruct", 2), P("Tough", 6)]);
        var sim = Shoot(shooter, target, 2);
        Assert.IsType<OnTable>(sim.Model(shooter.Models[0].Id).Location);
        Assert.IsType<OnTable>(sim.Model(target.Models[0].Id).Location);
        Assert.Empty(RuleHits(sim));
    }
}
