using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>A spell's weapon rules (the child rules of its add_hit) belong to every hit of the spell and act as a weapon's rules do.</summary>
public class SpellWeaponRuleTests
{
    const string HumanDefenseForce = "z65fgu0l29i4lnlu", DarkWoundBook = "gk7me4sgn9s740kw";

    static EffectDocument Grant(string entry, int? amount = null) => new(EffectSubject.Self, 20, EffectKind.GrantProperty, null, null, entry, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, [], []);
    static EffectDocument Hits(int n, string? roll, params EffectDocument[] rules) => new(EffectSubject.Self, 10, EffectKind.AddHit, roll, null, null, null, null, null, null, n, false, null, null, null, EffectDuration.Interaction, [], rules);
    static EntryDocument Spell(string name, TargetGrain grain, EffectDocument effect) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(TargetSide.Enemy, grain, 1, 24), [effect]);
    static readonly PropertySet Props = TestProperties.WithSpells(
        Spell("Venom Bolt", TargetGrain.Unit, Hits(2, null, Grant("Bane"))),
        Spell("Burn Bolt", TargetGrain.Unit, Hits(2, null, Grant("Disintegrate"))),
        Spell("Overload", TargetGrain.Model, Hits(2, "hit", Grant("Hazardous"))),
        Spell("Plain Bolt", TargetGrain.Unit, Hits(2, null)),
        Spell("Rolled Bolt", TargetGrain.Unit, Hits(2, "hit")));

    static (Sim Sim, Tabletop.Content.Unit Wizard) Cast(PropertySet props, string spell, string? book, Tabletop.Content.Unit target, object pick, params int[] dice) =>
        Cast(props, spell, book, target, pick, [], dice);

    static (Sim Sim, Tabletop.Content.Unit Wizard) Cast(PropertySet props, string spell, string? book, Tabletop.Content.Unit target, object pick, IReadOnlyList<Tabletop.Content.PropertyInstance> holds, params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6), P("Tough", 3), .. holds]);
        if (book is not null) wizard = InBook(wizard, book);
        var sim = new Sim(State([wizard], [target], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), props, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([pick is Tabletop.Content.Unit u ? u.Id : (Guid)pick]));
        return (sim, wizard);
    }

    static int ApOf(RollRecord r) => r.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount);

    [Fact]
    public void A_spell_with_two_weapon_rules_rolls_each_hit_once_in_one_pool_with_the_whole_group_rule()
    {
        var t = Unit("T", "4+", "4+", [], 5, []);
        var (sim, _) = Cast(TestProperties.Core, "Electric Tempest (Human Defense Force)", HumanDefenseForce, t, t, 6, 2, 3, 4, 4);
        var roll = Assert.Single(sim.Steps("roll-to-block").Single().Rolls);   // Crack finds no six, so every hit is in the one pool
        Assert.Equal(2, roll.Dice.Count);
        Assert.Equal(-1, ApOf(roll));
    }

    [Fact]
    public void Shatter_on_a_spell_adds_its_AP_against_a_Tough_three_target()
    {
        var t = Unit("T", "4+", "4+", [], 3, [P("Tough", 3)]);
        var (sim, _) = Cast(TestProperties.Core, "Dark Wound", DarkWoundBook, t, t, 6, 4, 4);
        var rolls = sim.Steps("roll-to-block").Single().Rolls;
        Assert.Equal(2, rolls.Sum(r => r.Dice.Count));
        Assert.Equal(-2, ApOf(Assert.Single(rolls)));
    }

    [Fact]
    public void Crack_on_a_spell_keys_the_AP_of_a_six_and_applies_it_to_that_hit_only()
    {
        var t = Unit("T", "4+", "4+", [], 5, []);
        var (sim, _) = Cast(TestProperties.Core, "Electric Tempest (Human Defense Force)", HumanDefenseForce, t, t, 6, 6, 3, 4, 4);
        Assert.Equal(1, sim.Steps("hit").Single().Values!["granted_AP_2"]);
        var rolls = sim.Steps("roll-to-block").Single().Rolls;
        Assert.Equal(2, rolls.Sum(r => r.Dice.Count));
        Assert.Equal([-3, -1], rolls.Select(ApOf).Order().ToArray());   // AP(1)+AP(2) on the six, AP(1) on the other
    }

    [Fact]
    public void Bane_and_Disintegrate_on_a_spell_suppress_the_targets_Regeneration()
    {
        foreach (var spell in new[] { "Venom Bolt", "Burn Bolt" })
        {
            var t = Unit("T", "4+", "6+", [], 1, [P("Regeneration"), P("Tough", 6)]);
            var (sim, _) = Cast(Props, spell, null, t, t, 6, 1, 1, 5, 5);
            Assert.Empty(sim.Steps("assign").Single().Rolls);
            Assert.Equal(2, sim.Model(t.Models[0].Id).Wounds);
        }
    }

    [Fact]
    public void A_spell_hits_Hazardous_gives_AP_four_and_its_ones_wound_the_caster()
    {
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 3)]);
        var (sim, wizard) = Cast(Props, "Overload", null, t, t.Models[0].Id, 6, 1, 3, 4, 4);
        Assert.Equal(1, sim.Steps("hit").Single().Values!["self_wounds"]);
        Assert.Equal(1, sim.Model(wizard.Models[0].Id).Wounds);
        Assert.All(sim.Steps("roll-to-block").Single().Rolls, r => Assert.Equal(-4, ApOf(r)));
    }

    [Fact]
    public void Power_Surge_through_the_hit_step_applies_Hazardous_AP_four_and_wounds_the_caster_on_a_one()
    {
        var t = Unit("T", "4+", "4+", [], 3, [P("Tough", 3)]);
        var (sim, wizard) = Cast(TestProperties.Core, "Power Surge", "hk70l4d471plza00", t, t.Models[0].Id, 6, 1, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4);
        var hit = sim.Steps("hit").Single();
        Assert.Equal(1, hit.Values!["self_wounds"]);
        Assert.Equal(1, sim.Model(wizard.Models[0].Id).Wounds);
        var blocks = sim.Steps("roll-to-block").Single().Rolls;
        Assert.Equal(6, blocks.Sum(r => r.Dice.Count));
        Assert.All(blocks, r => Assert.Equal(-4, ApOf(r)));
    }

    [Fact]
    public void A_die_carrying_two_per_die_grants_is_blocked_once_in_one_group_with_both()
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Rending"), P("Crack")));
        var target = Unit("T", "4+", "2+", [], 1, [P("Tough", 3)]);
        var sim = new Sim(State([shooter], [target], x1: -4, x2: 4), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(6, 5, 5, 5));
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        var rolls = sim.Steps("roll-to-block").Single().Rolls;
        var one = Assert.Single(rolls);
        Assert.Single(one.Dice);
        Assert.Equal(-6, ApOf(one));
    }

    // Model rules worded "hits from this model's weapons" / "when attacking" are not read for the holder's spells (final review I4).
    [Theory]
    [InlineData("Warbound")]
    [InlineData("Infected")]
    public void A_defense_die_rule_of_the_caster_adds_no_wound_to_its_spells_hits(string rule)
    {
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 6)]);
        var (sim, _) = Cast(Props, "Plain Bolt", null, t, t, [P(rule)], 6, 1, 1);
        Assert.Equal(2, sim.Model(t.Models[0].Id).Wounds);                                  // two failed blocks, two wounds: no extra wound for the ones
    }

    [Fact]
    public void Warbound_still_adds_a_wound_per_defense_one_to_a_weapon_attack()
    {
        var shooter = Unit("S", "2+", "4+", [P("Warbound")], 1, [], Gun("Rifle", "24\"", 1)); var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 6)]);
        var sim = new Sim(State([shooter], [t], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Props, new ScriptedDice(5, 1, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = t.Id }));
        Assert.Equal(2, sim.Model(t.Models[0].Id).Wounds);
    }

    [Theory]
    [InlineData("Devout")]
    [InlineData("Ferocious")]
    public void A_when_attacking_rule_of_the_caster_adds_no_hit_to_its_spells_sixes(string rule)
    {
        var t = Unit("T", "4+", "4+", [], 2, []);
        var (sim, _) = Cast(Props, "Rolled Bolt", null, t, t, [P(rule)], 6, 6, 6, 5, 5, 5, 5);
        Assert.Equal(2, sim.Steps("roll-to-block").Single().Rolls.Sum(r => r.Dice.Count));
    }
}
