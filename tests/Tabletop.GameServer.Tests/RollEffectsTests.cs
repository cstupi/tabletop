using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class RollEffectsTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, "Quality"), Defense = new(StatLevel.Unit, "Defense");
    static readonly StatRef ModelQuality = new(StatLevel.Model, "Quality"), ModelDefense = new(StatLevel.Model, "Defense");

    static (LiveSet Live, Tabletop.Content.Unit A, Tabletop.Content.Unit D) Shooting(Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, double range, string action = "Hold")
    {
        var state = State([shooter], [target]);
        var weapons = shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        return (LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), weapons), Party.Whole(state, target.Id), AttackKind.Shooting, action, range, [])), shooter, target);
    }

    [Fact]
    public void Stealth_modifies_the_hit_roll_aircraft_cuts_range_and_unstoppable_ignores_the_penalties()
    {
        var (live, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("T", "4+", "4+", [P("Aircraft")], 2, [P("Stealth")]), 12);
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality).Value, [], D6, null, hitRollName: "hit");
        Assert.Equal(4, plan.Threshold); Assert.Equal(-1, plan.NetModifier);                                      // 3.5.1 Aircraft is -12" to range only, with no to-hit modifier
        Assert.Equal(["Stealth"], plan.Modifiers.Select(m => m.Source));
        Assert.Equal(12.0, StatReader.WeaponRange(live, Side.Attacker, live.Interaction.Attacker.WeaponIds[0]));
        var (unstoppable, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Unstoppable"))), Unit("T", "4+", "4+", [P("Aircraft")], 2, [P("Stealth")]), 12);
        Assert.Equal(0, RollEffects.Plan(unstoppable, Side.Attacker, "hit", 4, [], D6, null, "hit").NetModifier);
        Assert.Equal(24.0, StatReader.WeaponRange(unstoppable, Side.Attacker, unstoppable.Interaction.Attacker.WeaponIds[0]));   // -12" range penalty ignored
        var covered = RollEffects.Plan(unstoppable, Side.Defender, "defense", 4, [new RollModifier("cover", 1)], D6, null, "hit");
        Assert.Equal(1, covered.NetModifier);                                                                     // p.15 Unstoppable ignores modifiers to its own weapon, not the target's cover
        var (blast, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Bomb", "24\"", 1, P("Blast", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Empty(RollEffects.Plan(blast, Side.Defender, "defense", 4, [new RollModifier("cover", 1)], D6, null, "hit").Modifiers);   // p.13 Blast ignores cover
    }

    [Fact]
    public void Reliable_replaces_quality_ap_modifies_defense_and_bane_forces_a_reroll_of_sixes()
    {
        var (live, _, _) = Shooting(Unit("S", "5+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Reliable"), P("AP", 2), P("Bane"))), Unit("T", "4+", "3+", [], 2, [P("Regeneration")]), 6);
        Assert.Equal(2, StatReader.TargetNumber(live, Side.Attacker, Quality).Value);
        var defense = RollEffects.Plan(live, Side.Defender, "defense", StatReader.TargetNumber(live, Side.Defender, Defense).Value, [], D6, null, "hit");
        Assert.Equal((3, -2), (defense.Threshold, defense.NetModifier));
        Assert.Equal([6], defense.RerollRaw);
        Assert.Empty(RollEffects.OwnRolls(live, Side.Defender, c => c.Kind == EffectKind.OverrideResult && c.TargetResult == TargetResult.Wound));   // Regeneration suppressed
    }

    [Fact]
    public void A_slot_that_declares_its_own_target_number_is_read_at_model_level_and_reliable_still_replaces_it()
    {
        var squad = Unit("Squad", "5+", "4+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var (live, _, _) = Shooting(joined, Unit("T", "4+", "4+", [], 3, []), 12);
        Assert.Equal(3, StatReader.TargetNumber(live, Side.Attacker, ModelQuality, joined.Models[0].Id).Value);   // p.4: "the model's Quality value"
        Assert.Equal(5, StatReader.TargetNumber(live, Side.Attacker, ModelQuality, joined.Models[1].Id).Value);   // no slot value: the unit's
        Assert.Equal(2, StatReader.TargetNumber(live, Side.Attacker, ModelDefense, joined.Models[0].Id).Value);
        Assert.Equal(5, StatReader.TargetNumber(live, Side.Attacker, Quality).Value);                             // the unit-level ref is unchanged
        Assert.Throws<NotSupportedException>(() => StatReader.TargetNumber(live, Side.Attacker, ModelQuality));

        var elite = joined with { Models = joined.Models.Select(m => m with { Weapons = [Gun("Rifle", "24\"", 1, P("Reliable"))] }).ToList() };
        var (reliable, _, _) = Shooting(elite, Unit("T", "4+", "4+", [], 3, []), 12);
        Assert.Equal(2, StatReader.TargetNumber(reliable, Side.Attacker, ModelQuality, elite.Models[0].Id).Value);   // p.14 replaces the value the model brought, hero or not
        Assert.Equal(2, StatReader.TargetNumber(reliable, Side.Attacker, ModelQuality, elite.Models[1].Id).Value);
    }

    [Fact]
    public void Per_die_wounds_are_counted_apart_from_extra_hits()
    {
        var effects = new DieEffects(1, 2, []);
        Assert.Equal((1, 2), (effects.ExtraHits, effects.ExtraWounds));
    }

    [Fact]
    public void Per_die_effects_add_hits_and_grant_on_unmodified_sixes_and_blast_multiplies_with_a_cap()
    {
        var charger = Unit("C", "4+", "4+", [], 3, [P("Furious")], Melee("Blade", 1, P("Rending")));
        var state = State([charger], [Unit("T", "4+", "4+", [], 2, [])]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, charger.Id, charger.Models.Select(m => m.Id), charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id))), Party.Whole(state, state.Board.Armies[1].Snapshot.Units[0].Id), AttackKind.Melee, "Charge", 1, []));
        var six = RollEffects.PerDie(live, Side.Attacker, new Die(6, 6, true), "hit");
        Assert.Equal(1, six.ExtraHits); Assert.Equal([("AP", (int?)4)], six.Grants);
        var five = RollEffects.PerDie(live, Side.Attacker, new Die(5, 6, true), "hit");
        Assert.Equal((0, 0), (five.ExtraHits, five.Grants.Count));
        var (blast, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 1, P("Blast", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Equal(2, RollEffects.HitMultiplier(blast, Side.Attacker, defenderModels: 2));
        Assert.Equal(3, RollEffects.HitMultiplier(blast, Side.Attacker, defenderModels: 5));
        Assert.Equal(1, RollEffects.WoundMultiplier(blast, Side.Attacker));
        var (deadly, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Cannon", "30\"", 1, P("Deadly", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Equal(3, RollEffects.WoundMultiplier(deadly, Side.Attacker));
    }

    [Fact]
    public void Action_distances_carry_fast_and_slow()
    {
        var fast = Unit("F", "4+", "4+", [], 2, [P("Fast")]);
        var state = State([fast], [Unit("T", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, fast.Id), null, null, "Advance", null, []));
        Assert.Equal((8.0, 16.0, 0.0), (StatReader.ActionDistance(live, Side.Attacker, "Advance"), StatReader.ActionDistance(live, Side.Attacker, "Charge"), StatReader.ActionDistance(live, Side.Attacker, "Hold")));
    }
}
