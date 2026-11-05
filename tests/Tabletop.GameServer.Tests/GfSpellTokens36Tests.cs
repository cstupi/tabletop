using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// `remove_spell_tokens`: Burn the Heretic and Shifting Form, "Pick one enemy unit within 18" in line of sight, which loses 2 spell tokens"; Spell Drain,
/// "pick one enemy model within 18" in line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens."
/// </summary>
public class GfSpellTokens36Tests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);
    static EffectDocument Row(EffectKind kind, int? amount = null, string? roll = null, IReadOnlyList<ConditionDocument>? when = null, params EffectDocument[] children) =>
        new(EffectSubject.Self, 10, kind, roll, null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, when ?? [], children);

    static readonly EntryDocument Burn = new("Test Burn", CatalogKind.Spell, false, null, 1, "Test Book", "Pick one enemy unit within 18\" in line of sight, which loses 2 spell tokens.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18, LineOfSight: true), [Row(EffectKind.RemoveSpellTokens, 2)]);
    static readonly EntryDocument Drain = new("Test Drain", CatalogKind.Model, false, Aggregation.None, null, null,
        "Once per activation, before attacking, pick one enemy model within 18\" in line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Model, 1, 18), [Row(EffectKind.TakeTest, roll: "morale", when: [BeforeAttacking], children: Row(EffectKind.RemoveSpellTokens, 2))]);
    static readonly EntryDocument UnitDrain = new("Test Unit Drain", CatalogKind.Spell, false, null, 1, "Test Book", "Pick one enemy unit within 18\", which must take a Morale Test. If failed, it loses 2 spell tokens.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), [Row(EffectKind.TakeTest, roll: "morale", children: Row(EffectKind.RemoveSpellTokens, 2))]);

    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with
    {
        Entries = [.. TestProperties.CoreDocument36.Entries, Burn, Drain, UnitDrain],
        Books = [.. TestProperties.CoreDocument36.Books ?? [], new BookRecord(TestProperties.TestBook, "Test Book", null, [Burn.Name, UnitDrain.Name])],
    }, Guid.CreateVersion7);

    static void Put(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    /// <summary>A wizard at (0, 20) casts the spell (a 6) at the enemy unit 8" away, whose models hold the tokens given.</summary>
    static Sim Cast(string spell, Unit enemy, int[] tokens, params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = new Sim(State([wizard], [enemy], x1: -4, x2: 4).Under(RuleSets.Gf360), new WithSequences(GrimdarkFutureRules.V360, WithSequences.Parent("test-parent", "w")),
            Props, new ScriptedDice([6, .. dice]));
        Put(sim, wizard.Models[0].Id, 0, 20);
        for (var i = 0; i < enemy.Models.Count; i++) { Put(sim, enemy.Models[i].Id, 8 + 2 * i, 20); sim.Append([new SpellTokensSet(enemy.Models[i].Id, tokens[i])]); }
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id]));
        return sim;
    }

    [Fact]
    public void A_spell_takes_its_tokens_off_the_models_holding_the_most_first()
    {
        var enemy = Unit("E", "4+", "4+", [], 2, []);
        var sim = Cast(Burn.Name, enemy, [1, 2]);
        Assert.Equal((1, 0), (sim.Model(enemy.Models[0].Id).SpellTokens, sim.Model(enemy.Models[1].Id).SpellTokens));
        Assert.Contains(sim.Steps("effects"), s => s.Applied?.Contains(Burn.Name) ?? false);
    }

    [Fact]
    public void A_target_with_no_tokens_records_the_step_with_no_change()
    {
        var enemy = Unit("E", "4+", "4+", [], 1, []);
        var sim = Cast(Burn.Name, enemy, [0]);
        var effects = Assert.Single(sim.Steps("effects"));
        Assert.Empty(effects.Changes);
        Assert.Equal(0, sim.Model(enemy.Models[0].Id).SpellTokens);
    }

    [Theory]
    [InlineData(1, 1)]   // failed: the picked model loses 2 of its 3
    [InlineData(6, 3)]   // passed: nothing
    public void A_test_child_takes_its_tokens_off_the_picked_model_when_the_test_fails(int moraleDie, int left)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(Drain.Name)]);
        var enemy = Unit("E", "4+", "4+", [], 2, []);
        var sim = new Sim(State([a], [enemy], x1: -6, x2: 6).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props, new ScriptedDice(moraleDie, moraleDie));
        sim.Append([new SpellTokensSet(enemy.Models[0].Id, 3), new SpellTokensSet(enemy.Models[1].Id, 2)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key(Drain.Name, a.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Models[0].Id]));
        Assert.Equal((left, 2), (sim.Model(enemy.Models[0].Id).SpellTokens, sim.Model(enemy.Models[1].Id).SpellTokens));
    }

    [Fact]
    public void A_test_child_on_a_unit_pick_takes_its_tokens_off_the_unit_most_first()
    {
        var enemy = Unit("E", "4+", "4+", [], 2, []);
        var sim = Cast(UnitDrain.Name, enemy, [1, 2], 1);
        Assert.Equal((1, 0), (sim.Model(enemy.Models[0].Id).SpellTokens, sim.Model(enemy.Models[1].Id).SpellTokens));
    }

    [Fact]
    public void Remove_spell_tokens_needs_an_amount_and_names_no_target()
    {
        var bad = Burn with { Name = "Bad Burn", Effects = [Row(EffectKind.RemoveSpellTokens) with { TargetRoll = "hit" }] };
        var problems = PropertiesJson.Validate(TestProperties.CoreDocument36 with
        {
            Entries = [.. TestProperties.CoreDocument36.Entries, bad],
            Books = [.. TestProperties.CoreDocument36.Books ?? [], new BookRecord(TestProperties.TestBook, "Test Book", null, [bad.Name])],
        });
        Assert.Contains("Bad Burn: remove_spell_tokens needs an amount of 1 or more", problems);
        Assert.Contains("Bad Burn: remove_spell_tokens names no target", problems);
        Assert.DoesNotContain(problems, p => p.StartsWith("Bad Burn: a spell's effect"));
    }
}
