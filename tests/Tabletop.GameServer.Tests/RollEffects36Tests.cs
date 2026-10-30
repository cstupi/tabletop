using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.15 Unstoppable: "ignores all negative modifiers to this weapon which are not from this model's special rules." 3.5.1 p.15 ignores
/// them all.
/// </summary>
public class RollEffects36Tests
{
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, "Quality");

    static RollPlan HitPlan(string rulesId, Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, string action = "Hold")
    {
        var properties = rulesId == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        var state = State([shooter], [target]);
        var weapons = shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        var live = LiveProperties.Settle(new Interaction(state, properties, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), weapons),
            Party.Whole(state, target.Id), AttackKind.Shooting, action, 12, []));
        return RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality).Value, [], D6, null, "hit");
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 0)]
    [InlineData(RuleSets.Gf360, -1)]
    public void Unstoppable_ignores_stealth_but_keeps_its_own_units_minus_one_under_3_6(string rulesId, int expected)
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [P("Bad Shot")], Gun("Beam", "24\"", 1, P("Unstoppable")));
        var plan = HitPlan(rulesId, shooter, Unit("T", "4+", "4+", [], 2, [P("Stealth")]));   // shot from 12": Stealth's -1 applies
        Assert.Equal(expected, plan.NetModifier);
        Assert.Contains(new IgnoredEffect("Stealth", "Unstoppable", -1), plan.Ignored!);
        Assert.Equal(expected == 0, plan.Ignored!.Any(i => i.Source == "Bad Shot"));
        Assert.Equal(expected == 0 ? [] : ["Bad Shot"], plan.Modifiers.Select(m => m.Source));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 0)]
    [InlineData(RuleSets.Gf360, -1)]
    public void Unstoppable_keeps_its_own_weapons_indirect_penalty_under_3_6(string rulesId, int expected)
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Mortar", "24\"", 1, P("Indirect"), P("Unstoppable")));
        Assert.Equal(expected, HitPlan(rulesId, shooter, Unit("T", "4+", "4+", [], 2, []), "Advance").NetModifier);
    }
    /// <summary>A Precision Debuff's once row granted to the shooter: an enemy's pick is not the shooter's rule; one its own side placed is.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, true, 0)]
    [InlineData(RuleSets.Gf360, true, 0)]
    [InlineData(RuleSets.Gf351, false, 0)]
    [InlineData(RuleSets.Gf360, false, -1)]
    public void Unstoppable_ignores_an_enemys_precision_debuff_and_keeps_its_own_sides_under_3_6(string rulesId, bool enemyCaster, int expected)
    {
        var properties = rulesId == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Unstoppable")));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        foreach (var row in properties.Entry("Precision Debuff")!.Effects.Where(e => e.Duration == EffectDuration.Once))
            state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new GrantAdded(a.Id, new Grant(Guid.CreateVersion7(), row.Id, "Precision Debuff", enemyCaster ? b.Id : a.Id, 1)), null));
        var live = LiveProperties.Settle(new Interaction(state, properties, Party.Of(state, a.Id, a.Models.Select(m => m.Id), a.Models.SelectMany(m => m.Weapons.Select(w => w.Id))), Party.Whole(state, b.Id), AttackKind.Shooting, "Hold", 12, []));
        Assert.Equal(-1, live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter)));   // the debuff acts
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", 4, [], D6, null, "hit");
        Assert.Equal(expected, plan.NetModifier);
        Assert.Equal(expected == 0, plan.Ignored?.Contains(new IgnoredEffect("Precision Debuff", "Unstoppable", -1)) ?? false);
    }
}
