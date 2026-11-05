using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
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
        return RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality).Value, [], D6, null, "hit", "defense");
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

    /// <summary>No shipped weapon rule carries a hit penalty, so the test adds one: -1 to hit on its own weapon, beside Unstoppable.</summary>
    static readonly EntryDocument ClumsyBarrel = new("Clumsy Barrel", CatalogKind.Weapon, false, null, null, null, "-1 to hit rolls with this weapon.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, -1, false, null, null, null, EffectDuration.Interaction, [], [])]);

    static PropertySet WithClumsyBarrel(PropertySetDocument document) =>
        PropertiesJson.ToSet(document with { Entries = [.. document.Entries, ClumsyBarrel] }, Guid.CreateVersion7);

    [Theory]
    [InlineData(RuleSets.Gf351, 0)]
    [InlineData(RuleSets.Gf360, -1)]
    public void Unstoppable_keeps_a_penalty_from_its_own_weapon_rule_under_3_6(string rulesId, int expected)
    {
        var properties = WithClumsyBarrel(rulesId == RuleSets.Gf360 ? TestProperties.CoreDocument36 : TestProperties.CoreDocument);
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Clumsy Barrel"), P("Unstoppable")));
        var target = Unit("T", "4+", "4+", [], 2, [P("Stealth")]);
        var state = State([shooter], [target]);
        var live = LiveProperties.Settle(new Interaction(state, properties, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id))),
            Party.Whole(state, target.Id), AttackKind.Shooting, "Hold", 12, []));
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality).Value, [], D6, null, "hit", "defense");
        Assert.Equal(expected, plan.NetModifier);
        Assert.Contains(new IgnoredEffect("Stealth", "Unstoppable", -1), plan.Ignored!);
        Assert.Equal(expected == 0, plan.Ignored!.Any(i => i.Source == "Clumsy Barrel"));
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
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", 4, [], D6, null, "hit", "defense");
        Assert.Equal(expected, plan.NetModifier);
        Assert.Equal(expected == 0, plan.Ignored?.Contains(new IgnoredEffect("Precision Debuff", "Unstoppable", -1)) ?? false);
    }
    /// <summary>3.6.0 p.15 Spell Amplifier: "Gets +1 to casting rolls."</summary>
    [Fact]
    public void Spell_amplifier_adds_one_to_the_casting_roll()
    {
        var plain = Unit("W", "4+", "4+", [], 1, []);
        var amplified = Unit("A", "4+", "4+", [], 1, [P("Spell Amplifier")]);
        static int Net(Tabletop.Content.Unit caster)
        {
            var state = State([caster], [Unit("E", "4+", "4+", [], 1, [])]);
            var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(caster.Id, [], [], GfNames.Advance)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
            var ctx = new StepContext(state, TestProperties.Core36, GrimdarkFutureRules.Instance, frame, new Roller(new ScriptedDice(4), state.RngSeed, 0));
            var live = LiveProperties.Settle(new Interaction(state, TestProperties.Core36, Party.Whole(state, caster.Id), null, AttackKind.Shooting, GfNames.Advance, null, []));
            return GfRolls.CastingPlan(ctx, live, 0).NetModifier;
        }
        Assert.Equal((0, 1), (Net(plain), Net(amplified)));
    }

    /// <summary>3.6.0 p.15 Shred is a core entry; 3.5.1 keeps it in its army-rules part.</summary>
    [Fact]
    public void Shred_is_a_core_entry_under_3_6()
    {
        Assert.Equal(CatalogKind.Weapon, TestProperties.Core36.Entry("Shred")!.Kind);
        var part36 = PropertiesSeeder.EmbeddedPart("grimdark-future-3.6", "army-rules.part.json")!;
        var part351 = PropertiesSeeder.EmbeddedPart("grimdark-future-3.5.1", "army-rules.part.json")!;
        Assert.DoesNotContain(part36.Entries, e => e.Name == "Shred");   // so the composed 3.6 Shred comes from its core file
        Assert.Contains(part351.Entries, e => e.Name == "Shred");        // 3.5.1 keeps it in its part, not its core
    }

    /// <summary>3.6.0 p.13 Aircraft: "units targeting them get -12" range and -1 to hit rolls." 3.5.1 takes the range only.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 0)]
    [InlineData(RuleSets.Gf360, -1)]
    public void Shooting_at_an_aircraft_is_minus_one_to_hit_under_3_6_and_unmodified_under_3_5_1(string rulesId, int expected)
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var plan = HitPlan(rulesId, shooter, Unit("T", "4+", "4+", [P("Aircraft")], 2, []));
        Assert.Equal(expected, plan.NetModifier);
        Assert.Equal(expected == 0 ? [] : ["Aircraft"], plan.Modifiers.Select(m => m.Source));
    }
}
