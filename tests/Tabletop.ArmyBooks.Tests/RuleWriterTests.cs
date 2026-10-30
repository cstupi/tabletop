using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class RuleWriterTests
{
    static AfBook Read(string path) => RuleTemplatesTests.Read(path);
    static IReadOnlyList<AfBook> Books() => [Read("books/rules-a.json"), Read("books/rules-b.json")];

    static readonly PropertySetDocument Core = PropertiesJson.Parse("""
    { "name": "core", "gameRules": "grimdark-future", "rolls": [ "hit", "defense" ],
      "entries": [
        { "name": "Stealth", "kind": "model", "aggregation": "all", "ruleText": "S.", "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1 } ] },
        { "name": "Rending", "kind": "weapon", "ruleText": "R." },
        { "name": "AP", "kind": "weapon", "takesParameter": true, "ruleText": "AP." } ] }
    """);
    static readonly PropertySetDocument Hand = PropertiesJson.Parse("""
    { "name": "hand", "gameRules": "grimdark-future", "entries": [ { "name": "Bane when Shooting", "kind": "model", "aggregation": "all", "ruleText": "This model gets Bane when shooting." } ] }
    """);

    static PropertySetDocument Part() => RuleWriter.Build(Books(), Core, Hand, "grimdark-future").Part!;
    [Fact]
    public void The_part_names_the_rules_id_it_was_built_for()
    {
        Assert.Equal("grimdark-future-3.6", RuleWriter.Build(Books(), Core, Hand, "grimdark-future-3.6").Part!.GameRules);
    }

    static EntryDocument Entry(string name) => Part().Entries.Single(e => e.Name == name);

    [Fact]
    public void A_template_wrapper_is_generated_when_its_base_is_in_the_catalog()
    {
        var aura = Entry("Stealth Aura");
        Assert.Equal((CatalogKind.Model, Aggregation.Any, (bool?)null, "This model and its unit get Stealth."), (aura.Kind, aura.Aggregation, aura.Played, aura.RuleText));
        Assert.Equal((EffectSubject.Self, EffectKind.GrantProperty, "Stealth", EffectDuration.Interaction), (aura.Effects.Single().Subject, aura.Effects.Single().Kind, aura.Effects.Single().TargetEntry, aura.Effects.Single().Duration));
        var buff = Entry("Stealth Buff");
        Assert.Equal(new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, false), buff.Targeting);
        Assert.Equal((EffectSubject.Self, "Stealth", EffectDuration.Once, "before-attacking"),
            (buff.Effects.Single().Subject, buff.Effects.Single().TargetEntry, buff.Effects.Single().Duration, buff.Effects.Single().Conditions.Single(c => c.Kind == ConditionKind.AtPoint).Values.Single()));
        var mark = Entry("Rending Mark");
        Assert.Equal(new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), mark.Targeting);
        Assert.Equal((EffectSubject.Opponent, "Rending", EffectDuration.Once), (mark.Effects.Single().Subject, mark.Effects.Single().TargetEntry, mark.Effects.Single().Duration));
    }

    [Fact]
    public void Every_other_name_is_text_only_and_a_name_core_or_the_hand_part_defines_is_skipped()
    {
        foreach (var name in new[] { "Fortified Aura", "Precision Shooter Aura", "Defense Debuff", "Precise", "Swift Buff" })
        {
            var e = Entry(name);
            Assert.Equal((CatalogKind.Model, (Aggregation?)Aggregation.Any, (bool?)false, 0), (e.Kind, e.Aggregation, e.Played, e.Effects.Count));
        }
        Assert.Equal("Gets +1 to hit when attacking.", Entry("Precise").RuleText);      // trimmed
        Assert.DoesNotContain(Part().Entries, e => e.Name is "AP" or "Bane when Shooting");
    }

    [Fact]
    public void A_name_printed_only_on_weapons_is_a_weapon_entry()
    {
        var e = Entry("Ignores Cover");
        Assert.Equal((CatalogKind.Weapon, (Aggregation?)null, (bool?)false, "This weapon ignores cover."), (e.Kind, e.Aggregation, e.Played, e.RuleText));
    }

    [Fact]
    public void The_report_counts_the_groups_and_names_what_has_no_description()
    {
        var report = RuleWriter.Build(Books(), Core, Hand, "grimdark-future").Report!;
        Assert.Equal(3, report.Generated);
        var counts = report.Groups.ToDictionary(g => g.Group, g => g.Count);
        Assert.Equal(1, counts["Aura, template text, base in catalog"]);
        Assert.Equal(1, counts["Aura, template text, base missing"]);
        Assert.Equal(1, counts["Aura, effect written out"]);
        Assert.Equal(1, counts["Buff, template text, base in catalog"]);
        Assert.Equal(1, counts["Buff, template text, base missing"]);
        Assert.Equal(1, counts["Mark, template text, base in catalog"]);
        Assert.Equal(1, counts["Debuff, effect written out"]);
        Assert.Equal(3, counts["Standalone, own text"]);                            // Banner, Ignores Cover, Precise
        Assert.Equal(["Banner"], report.NoDescription);
        Assert.Equal("", Entry("Banner").RuleText);
    }

    [Fact]
    public void Two_descriptions_of_one_name_fail_and_nothing_is_built()
    {
        var (part, report, problems) = RuleWriter.Build([.. Books(), Read("conflict/rules-c.json")], Core, Hand, "grimdark-future");
        Assert.Null(part);
        Assert.Null(report);
        Assert.Equal(["'Precise' has two descriptions: Rules A and Rules C"], problems);
    }

    [Fact]
    public void The_part_composes_validates_and_serializes_the_same_whatever_the_book_order()
    {
        Assert.Empty(BookWriter.Check([], Core, Hand, Part()));
        var reversed = RuleWriter.Build(Books().Reverse().ToList(), Core, Hand, "grimdark-future").Part!;
        Assert.Equal(RuleWriter.Serialize(Part()), RuleWriter.Serialize(reversed));
        Assert.EndsWith("\n", RuleWriter.Serialize(Part()));
    }

    // A wrapper granting a text-only rule can never act: under 3.6 it is written text-only too, as a spell granting one is (DemoteUndefinedGrants).
    static readonly PropertySetDocument HandTextOnlySwift = PropertiesJson.Parse("""
    { "name": "hand", "gameRules": "grimdark-future", "entries": [ { "name": "Swift", "kind": "model", "aggregation": "none", "ruleText": "Sw.", "played": false } ] }
    """);

    [Fact]
    public void Under_3_6_a_wrapper_whose_base_is_text_only_is_text_only()
    {
        var swift36 = RuleWriter.Build(Books(), Core, HandTextOnlySwift, "grimdark-future-3.6").Part!.Entries.Single(e => e.Name == "Swift Buff");
        Assert.Equal(((bool?)false, 0, (SpellTargeting?)null), (swift36.Played, swift36.Effects.Count, swift36.Targeting));
        var report = RuleWriter.Build(Books(), Core, HandTextOnlySwift, "grimdark-future-3.6").Report!;
        Assert.Equal(1, report.Groups.Single(g => g.Group == "Buff, template text, base text-only").Count);
        var swift35 = RuleWriter.Build(Books(), Core, HandTextOnlySwift, "grimdark-future").Part!.Entries.Single(e => e.Name == "Swift Buff");
        Assert.Equal("Swift", swift35.Effects.Single().TargetEntry);                // 3.5 output unchanged
    }
}
