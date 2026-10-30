using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The 3.6 hand part's unit, defensive and utility rules that reach the engine through a shape new to their name.</summary>
public class ArmyRules36BTests
{
    static readonly PropertySet Core36 = TestProperties.Core36;

    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    /// <summary>The weapons of the defender's attacks (one per weapon group) before the charger's first.</summary>
    static IEnumerable<Guid> StruckFirst(Sim sim, Guid defender) =>
        sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).TakeWhile(s => s.Context.ActorUnitId == defender).SelectMany(s => s.Context.WeaponIds);

    /// <summary>"Counter in Melee Aura": "This model and its unit get Counter on its weapons in melee." Every model's weapons strike first, not only the carrier's.</summary>
    [Fact]
    public void Counter_in_melee_aura_gives_every_model_of_the_unit_counter()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var squad = Unit("Squad", "4+", "4+", [], 2, [], Melee("Knife", 1));
        var carrier = Unit("C", "4+", "4+", [], 1, [P("Counter in Melee Aura")], Melee("Spear", 1)).Models[0] with { Ordinal = 3 };
        var defender = squad with { Models = [.. squad.Models, carrier] };
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        Assert.Equal("counter", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal(defender.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).Order(), StruckFirst(sim, defender.Id).Order());
    }

    /// <summary>"Counter in Melee Buff": "pick one friendly unit within 12", which gets Counter in Melee." The picked unit's every weapon strikes first, and the
    /// grant is spent by that strike.</summary>
    [Fact]
    public void Counter_in_melee_buff_grants_the_picked_unit_counter_in_melee_once()
    {
        var buff = Core36.Entry("Counter in Melee Buff")!;
        Assert.NotNull(buff.Targeting);
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 2, [], Melee("Spear", 1));
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        sim.Append([new GrantAdded(defender.Id, new Grant(Guid.CreateVersion7(), buff.Effects[0].Id, buff.Name, defender.Id, 1))]);
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        Assert.Equal("counter", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Empty(sim.Unit(defender.Id).Grants);
        Assert.Equal(defender.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).Order(), StruckFirst(sim, defender.Id).Order());
    }

    /// <summary>"Spell Resistance": "If the wound was from a spell, then it is ignored on a 2+. If the wound was not from a spell, then it is ignored on a 6+
    /// instead." "Most models" is the entry's majority aggregation.</summary>
    [Fact]
    public void Spell_resistance_ignores_spell_wounds_on_2_and_others_on_6_for_most_models()
    {
        var e = Core36.Entry("Spell Resistance")!;
        Assert.Equal(Aggregation.Majority, e.Aggregation);
        var rolls = e.Effects.Where(r => r.Kind == EffectKind.Roll).ToList();
        Assert.Equal(2, rolls.Single(r => r.Conditions.Any(c => c is { Kind: ConditionKind.AttackKind, Comparison: Comparison.Eq, Values: ["spell"] })).Threshold);
        Assert.Equal(6, rolls.Single(r => r.Conditions.Any(c => c is { Kind: ConditionKind.AttackKind, Comparison: Comparison.Neq, Values: ["spell"] })).Threshold);
        Assert.All(rolls, r => Assert.Contains(r.Children, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound }));
    }

    /// <summary>"Shatter" (3.6.0): "For each unmodified result of 6 to hit, those hits get AP(+3)." A per-die AP grant, as Rending's.</summary>
    [Fact]
    public void Shatter_gives_ap_3_to_hits_of_an_unmodified_6()
    {
        var row = Assert.Single(Core36.Entry("Shatter")!.Effects);
        Assert.Equal((EffectKind.GrantProperty, "AP", 3), (row.Kind, row.TargetEntry, row.Amount));
        Assert.Contains(row.Conditions, c => c is { Kind: ConditionKind.RollResult, Number: 6, Values: ["hit", "raw"] });
    }

}
