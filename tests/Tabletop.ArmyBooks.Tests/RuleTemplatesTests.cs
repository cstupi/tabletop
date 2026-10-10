using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class RuleTemplatesTests
{
    internal static AfBook Read(string path) => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", path)));

    [Fact]
    public void Names_in_scope_are_unit_rules_weapon_rules_item_contents_and_upgrade_gains()
    {
        var names = RuleWriter.NamesInScope(Read("books/rules-a.json"));
        Assert.Equal(["AP", "Bane when Shooting", "Banner", "Defense Debuff", "Fortified Aura", "Ignores Cover", "Precise", "Precision Shooter Aura", "Rending Mark", "Stealth Aura", "Stealth Buff"],
            names.Keys.Order(StringComparer.Ordinal));
        Assert.False(names["Ignores Cover"]);      // only on a weapon
        Assert.True(names["Precise"]);             // on a weapon and as a rule: a model's
    }

    [Fact]
    public void A_book_without_units_or_rule_texts_reads_empty()
    {
        var book = ArmyForgeJson.ReadBook("""{ "uid": "x", "name": "X", "spells": [] }""");
        Assert.Empty(RuleWriter.NamesInScope(book));
        Assert.Null(book.SpecialRules);
    }

    [Fact]
    public void Pattern_of_reads_the_last_word_exactly()
    {
        Assert.Equal(RulePattern.Aura, RuleTemplates.PatternOf("Stealth Aura"));
        Assert.Equal(RulePattern.Debuff, RuleTemplates.PatternOf("Defense Debuff"));
        Assert.Null(RuleTemplates.PatternOf("Aura"));
        Assert.Null(RuleTemplates.PatternOf("Rule 1"));
        Assert.Null(RuleTemplates.PatternOf("Stealth aura"));
    }

    [Fact]
    public void Base_is_x_only_when_the_text_is_the_template()
    {
        Assert.Equal("Stealth", RuleTemplates.Base(RulePattern.Aura, "Stealth Aura", "This model and its unit get Stealth."));
        Assert.Null(RuleTemplates.Base(RulePattern.Aura, "Precision Shooter Aura", "This model and its unit get +1 to hit rolls when shooting."));
        Assert.Equal("Stealth", RuleTemplates.Base(RulePattern.Buff, "Stealth Buff",
            "Once per activation, before attacking, pick one friendly unit within 12\", which gets Stealth once (next time the effect would apply)."));
        Assert.Equal("Rending", RuleTemplates.Base(RulePattern.Mark, "Rending Mark",
            "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Rending against once (next time the effect would apply)."));
        Assert.Null(RuleTemplates.Base(RulePattern.Debuff, "Defense Debuff", "anything"));
    }

    [Fact]
    public void The_wrappers_are_an_aura_entry_and_two_pick_entries()
    {
        var aura = RuleTemplates.Wrapper(RulePattern.Aura, "Stealth Aura", "Stealth", "This model and its unit get Stealth.");
        Assert.Equal((CatalogKind.Model, (Aggregation?)Aggregation.Any, (SpellTargeting?)null), (aura.Kind, aura.Aggregation, aura.Targeting));
        Assert.Equal((EffectSubject.Self, EffectKind.GrantProperty, "Stealth", EffectDuration.Interaction, 0),
            (aura.Effects.Single().Subject, aura.Effects.Single().Kind, aura.Effects.Single().TargetEntry, aura.Effects.Single().Duration, aura.Effects.Single().Conditions.Count));
        var buff = RuleTemplates.Wrapper(RulePattern.Buff, "Stealth Buff", "Stealth", "t");
        Assert.Equal(new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, false), buff.Targeting);
        Assert.Equal((EffectSubject.Self, "Stealth", EffectDuration.Once, "before-attacking"),
            (buff.Effects.Single().Subject, buff.Effects.Single().TargetEntry, buff.Effects.Single().Duration, buff.Effects.Single().Conditions.Single(c => c.Kind == ConditionKind.AtPoint).Values.Single()));
        var mark = RuleTemplates.Wrapper(RulePattern.Mark, "Rending Mark", "Rending", "t");
        Assert.Equal(new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), mark.Targeting);
        Assert.Equal((EffectSubject.Opponent, "Rending", EffectDuration.Once), (mark.Effects.Single().Subject, mark.Effects.Single().TargetEntry, mark.Effects.Single().Duration));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuleTemplates.Wrapper(RulePattern.Debuff, "Defense Debuff", "x", "t"));
    }
}
