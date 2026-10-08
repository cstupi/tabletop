using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class SpellShapeTests
{
    static EffectDocument Grant(string entry, int? amount = null) => new(EffectSubject.Self, 20, EffectKind.GrantProperty, null, null, entry, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, [], []);
    static EffectDocument Hits(int n, string? roll, params EffectDocument[] rules) => new(EffectSubject.Self, 10, EffectKind.AddHit, roll, null, null, null, null, null, null, n, false, null, null, null, EffectDuration.Interaction, [], rules);
    static EntryDocument Spell(string name, TargetGrain grain, EffectDocument effect) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(TargetSide.Enemy, grain, 1, 24), [effect]);
    static readonly EntryDocument MeleeGuard = new("Melee Guard", CatalogKind.Model, false, Aggregation.All, null, null, "+3 to defense rolls in melee.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, "defense", null, null, null, null, null, null, 3, false, null, null, null, EffectDuration.Interaction,
            [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["melee"])], [])]);
    static readonly EffectDocument CastingMinus3 = new(EffectSubject.Self, 10, EffectKind.ModifyRoll, "casting", null, null, null, null, null, null, -3, false, null, null, null, EffectDuration.Once, [], []);

    static readonly PropertySet Props = TestProperties.WithSpells(MeleeGuard,
        Spell("Quiet Bolt", TargetGrain.Unit, Hits(3, null)),
        Spell("Loud Bolt", TargetGrain.Unit, Hits(2, "hit", Grant("Surge"))),
        Spell("Lance", TargetGrain.Model, Hits(2, null, Grant("AP", 4))),
        Spell("Boom", TargetGrain.Unit, Hits(2, null, Grant("Blast", 3))),
        Spell("Pin Boom", TargetGrain.Model, Hits(2, null, Grant("Blast", 3))),
        Spell("Amp", TargetGrain.Unit, Grant("Blast", 3) with { Duration = EffectDuration.Once }),
        Spell("Smite", TargetGrain.Unit, Hits(1, null, Grant("Deadly", 3))),
        new EntryDocument("Burst", CatalogKind.Weapon, false, null, null, null, "Burst.", null,
            [new EffectDocument(EffectSubject.Self, 30, EffectKind.GrantProperty, null, null, "Surge", null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 6, [GfNames.Hit, "raw"])], [])]),
        new EntryDocument("Hex", CatalogKind.Spell, false, null, 1, null, "Hex.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24), [CastingMinus3]));

    static (Sim Sim, Tabletop.Content.Unit Wizard, Tabletop.Content.Unit Target) Cast(string spell, Tabletop.Content.Unit target, params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]);
        var sim = new Sim(State([wizard], [target], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        return (sim, wizard, target);
    }

    [Fact]
    public void A_spell_that_prints_no_roll_sentence_rolls_no_hit_dice()
    {
        var (sim, _, t) = Cast("Quiet Bolt", Unit("T", "4+", "6+", [], 5, []), 5, 1, 1, 1);   // cast 5; three blocks of 1
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Empty(sim.Steps("hit").Single().Rolls);
        Assert.Equal(3, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
    }

    [Fact]
    public void A_spell_that_prints_the_roll_sentence_rolls_one_die_per_hit_and_surge_triggers()
    {
        var (sim, _, t) = Cast("Loud Bolt", Unit("T", "4+", "6+", [], 5, []), 5, 6, 2, 1, 1, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Equal(2, sim.Steps("hit").Single().Rolls.Single().Dice.Count);
        Assert.Equal(3, sim.Steps("roll-to-block").Single().Rolls.Sum(r => r.Dice.Count));              // 2 hits, a 6 surges one more
    }

    [Fact]
    public void Spell_hits_are_not_melee_so_an_in_melee_rule_does_not_reach_them()
    {
        var (sim, _, t) = Cast("Quiet Bolt", Unit("T", "4+", "6+", [], 5, [P("Melee Guard")]), 5, 1, 1, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.DoesNotContain(sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers, m => m.Source == "Melee Guard");
    }

    [Fact]
    public void A_model_target_takes_its_wounds_as_a_unit_of_one_with_no_allocation()
    {
        var (sim, _, t) = Cast("Lance", Unit("T", "4+", "4+", [], 3, [P("Tough", 3)]), 5, 1, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[1].Id]));
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal((0, 2, 0), (sim.Model(t.Models[0].Id).Wounds, sim.Model(t.Models[1].Id).Wounds, sim.Model(t.Models[2].Id).Wounds));
    }

    [Fact]
    public void Blast_on_a_spell_multiplies_its_hits_and_a_model_target_caps_it_at_one()
    {
        var (unit, _, t1) = Cast("Boom", Unit("T", "4+", "6+", [], 5, []), [5, .. Enumerable.Repeat(6, 6)]);
        unit.Answer(Samples.Team1, new SelectedPieces([t1.Id]));
        Assert.Equal(6, unit.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
        var (pin, _, t2) = Cast("Pin Boom", Unit("T", "4+", "6+", [], 5, []), 5, 6, 6);
        pin.Answer(Samples.Team1, new SelectedPieces([t2.Models[0].Id]));
        Assert.Equal(2, pin.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
    }

    [Fact]
    public void A_casting_modifier_grant_applies_to_the_units_next_casting_roll_and_is_spent()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]);
        var sim = new Sim(State([wizard], [Unit("T", "4+", "4+", [], 1, [])], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new GrantAdded(wizard.Id, new Grant(Guid.CreateVersion7(), Props.Entry("Hex")!.Effects[0].Id, "Hex", wizard.Id, 1))]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Quiet Bolt|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Contains(roll.Modifiers, m => m.Source == "Hex" && m.Amount == -3);
        Assert.Equal(0, sim.Steps("roll").Single().Values!["passed"]);                                   // 6 − 3 = 3, short of 4
        Assert.Empty(sim.Unit(wizard.Id).Grants);
    }

    [Fact]
    public void A_granted_hit_multiplier_on_the_caster_applies_to_the_next_spells_hits_and_is_spent()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]);
        var t = Unit("T", "4+", "6+", [], 5, []);
        var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice([5, .. Enumerable.Repeat(6, 9)]));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new GrantAdded(wizard.Id, new Grant(Guid.CreateVersion7(), Props.Entry("Amp")!.Effects[0].Id, "Amp", wizard.Id, 1))]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Quiet Bolt|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Equal(9, sim.Steps("roll-to-block").Single().Rolls.Sum(r => r.Dice.Count));                 // 3 hits x Blast(3)
        Assert.Empty(sim.Unit(wizard.Id).Grants);
    }

    [Fact]
    public void A_spell_that_grants_Deadly_multiplies_the_wounds_it_deals()
    {
        var (sim, _, t) = Cast("Smite", Unit("T", "4+", "6+", [], 1, [P("Tough", 3)]), 5, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Equal(3, sim.Model(t.Models[0].Id).Wounds);                                                 // one failed block, x3
    }

    [Fact]
    public void A_per_die_grant_of_a_rule_with_no_amount_does_not_throw_on_a_weapon_attack()
    {
        var gun = Gun("Rifle", "24\"", 1, P("Burst"));
        var a = Unit("A", "4+", "4+", [], 1, [], gun); var t = Unit("T", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [t], x1: -4, x2: 4), GrimdarkFutureRules.Instance, Props, new ScriptedDice(6, 1));
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [t.Id], a.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        Assert.Equal(1, sim.Steps("roll-to-block").Single().Rolls.Sum(r => r.Dice.Count));
    }

    static ConditionDocument Raw(string roll, int face) => new(ConditionKind.RollResult, null, Comparison.Eq, face, [roll, "raw"]);
    static readonly EntryDocument TestShred = new("Test Shred", CatalogKind.Weapon, false, null, null, null, "Defense 1s take an extra wound.", null,
        [new EffectDocument(EffectSubject.Opponent, 10, EffectKind.AddHit, null, null, null, null, TargetQuantity.Wounds, null, null, 1, false, null, null, null, EffectDuration.Interaction, [Raw("defense", 1)], [])]);
    static readonly EntryDocument TestHazard = new("Test Hazard", CatalogKind.Weapon, false, null, null, null, "Hit 1s wound this unit.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, TargetQuantity.Wounds, null, null, 1, false, null, null, null, EffectDuration.Interaction, [Raw("hit", 1)], [])]);
    static readonly PropertySet Weapons = TestProperties.WithSpells(TestShred, TestHazard);

    [Fact]
    public void A_defense_one_against_a_shred_weapon_is_one_more_wound()
    {
        var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Test Shred"))); var b = Unit("B", "4+", "4+", [], 5, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Weapons, new ScriptedDice(5, 5, 1, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);               // one failed block, plus one for its 1
    }

    [Fact]
    public void A_hit_one_with_a_hazardous_weapon_wounds_the_attackers_own_unit()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Gun("Pistol", "12\"", 1, P("Test Hazard"))); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Weapons, new ScriptedDice(1, 5, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Pistol"] = b.Id }));
        Assert.Equal(1, sim.Steps("roll-to-hit").Single().Values!["self_wounds"]);
        var removed = a.Models.Count(m => sim.Model(m.Id).Location is OnShelf);
        Assert.Equal(1, removed + (sim.AskForOrNull(Samples.Team1) is { Legal: Allocation } ? 1 : 0));   // removed, or awaiting its own allocation
    }
}
