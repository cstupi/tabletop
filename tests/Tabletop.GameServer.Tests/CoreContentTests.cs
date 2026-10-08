using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class CoreContentTests
{
    static readonly PropertySet Core = TestProperties.Core;

    [Fact]
    public void The_document_is_valid_and_names_the_rulebooks_thirty_three_rules()
    {
        Assert.Empty(PropertiesJson.Validate(TestProperties.RulebookDocument));
        string[] rules = ["Aircraft", "Ambush", "AP", "Artillery", "Bane", "Blast", "Caster", "Counter", "Deadly", "Fast", "Fear", "Fearless", "Flying", "Furious",
            "Hero", "Immobile", "Impact", "Indirect", "Limited", "Regeneration", "Relentless", "Reliable", "Rending", "Scout", "Slow", "Stealth", "Strider",
            "Surge", "Takedown", "Thrust", "Tough", "Transport", "Unstoppable"];
        Assert.Equal(rules.OrderBy(r => r), TestProperties.RulebookDocument.Entries.Select(e => e.Name).OrderBy(r => r));
        // Quality and Defense are declared at model level as well as unit level: a joined hero is a model of the unit it joined and its slot carries its own two.
        Assert.Equal([(StatLevel.Unit, "Defense"), (StatLevel.Unit, "Quality"), (StatLevel.Model, "Defense"), (StatLevel.Model, "Quality"), (StatLevel.Weapon, "Attacks"), (StatLevel.Weapon, "Range")],
            Core.Stats.OrderBy(s => s.Level).ThenBy(s => s.Name, StringComparer.Ordinal).Select(s => (s.Level, s.Name)));
        Assert.Equal(Tabletop.Content.StatKind.TargetNumber, Core.Stat(StatLevel.Model, "Quality")!.Kind);
        Assert.Equal(["Advance", "Charge", "Hold", "Rush"], Core.Actions.Select(a => a.Name).OrderBy(n => n));
        Assert.Equal([("Advance", 6.0), ("Charge", 12.0), ("Hold", 0.0), ("Rush", 12.0)], Core.Actions.OrderBy(a => a.Name).Select(a => (a.Name, a.Distance)));
        Assert.Equal(["casting", "defense", "hit", "morale"], Core.Rolls.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal("grimdark-future", Core.GameRules);
    }

    [Fact]
    public void The_four_worked_rows_match_schema_md()
    {
        var ap = Core.Entry("AP")!.Effects.Single();
        Assert.Equal((EffectSubject.Opponent, EffectKind.ModifyRoll, "defense", -1, true), (ap.Subject, ap.Kind, ap.TargetRoll, ap.Amount, ap.AmountFromParameter));
        var stealth = Core.Entry("Stealth")!;
        Assert.Equal(Aggregation.All, stealth.Aggregation);
        Assert.Equal(2, stealth.Effects.Single().Conditions.Count);
        Assert.Contains(stealth.Effects[0].Conditions, c => c.Kind == ConditionKind.Range && c.Comparison == Comparison.Gt && c.Number == 9 && c.Subject is null);
        var impact = Core.Entry("Impact")!.Effects.Single();
        Assert.Equal((EffectKind.Roll, true, 2, EffectKind.AddHit), (impact.Kind, impact.AmountFromParameter, impact.Threshold, impact.Children.Single().Kind));
        Assert.Contains(impact.Conditions, c => c.Kind == ConditionKind.ActorState && c.Comparison == Comparison.Neq && c.Values.Contains("fatigued"));
        var bane = Core.Entry("Bane")!.Effects;
        Assert.Equal(2, bane.Count);
        Assert.Contains(bane, e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == "defense" && e.Conditions.Single().Values.SequenceEqual(["defense", "raw"]));
        Assert.Contains(bane, e => e.Kind == EffectKind.SuppressProperty && e.TargetEntry == "Regeneration" && e.Subject == EffectSubject.Opponent && e.Conditions.Count == 0);
    }

    [Fact]
    public void Name_check_rules_carry_no_effects_and_blast_resolves_last()
    {
        foreach (var name in new[] { "Ambush", "Scout", "Flying", "Strider", "Immobile", "Limited", "Counter", "Caster", "Hero", "Tough", "Fear", "Transport" })
            Assert.Empty(Core.Entry(name)!.Effects);
        var blast = Core.Entry("Blast")!.Effects.Single(e => e.Kind == EffectKind.Multiply);
        Assert.Equal((TargetQuantity.Hits, CapKind.ModelsInTargetUnit), (blast.TargetQuantity, blast.CapKind));
        Assert.True(blast.Ordinal > Core.AllEffects().Where(e => e.Kind != EffectKind.Multiply).Max(e => e.Ordinal));
        Assert.Equal(0, Core.Entry("Deadly")!.Effects.Single().Ordinal);
        Assert.Equal(3, Core.Entry("Fast")!.Effects.Count);
        Assert.Equal(["Advance", "Charge", "Rush"], Core.Entry("Slow")!.Effects.Select(e => e.TargetAction).OrderBy(a => a));
    }
}
