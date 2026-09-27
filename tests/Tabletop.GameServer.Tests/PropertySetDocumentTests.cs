using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests;

public class PropertySetDocumentTests
{
    public const string ApJson = """
    { "name": "Doc", "gameRules": "grimdark-future",
      "stats": [ { "level": "unit", "name": "Quality", "kind": "target_number" }, { "level": "unit", "name": "Defense", "kind": "target_number" } ],
      "actions": [ { "name": "Hold", "distance": 0 }, { "name": "Advance", "distance": 6 } ],
      "rolls": [ "hit", "defense" ],
      "entries": [
        { "name": "Regeneration", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 5+ it is ignored.",
          "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5,
                         "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound" } ] } ] },
        { "name": "Bane", "kind": "weapon", "ruleText": "Ignores Regeneration; re-roll unmodified Defense rolls of 6.",
          "effects": [ { "subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense",
                         "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "defense", "raw" ] } ] },
                       { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" } ] }
      ] }
    """;

    [Fact]
    public void Parses_hashes_canonically_and_builds_a_set()
    {
        var doc = PropertiesJson.Parse(ApJson);
        var reordered = doc with { Entries = doc.Entries.Reverse().ToList(), Rolls = doc.Rolls.Reverse().ToList() };
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(reordered));
        Assert.Matches("^sha256:[0-9a-f]{64}$", PropertiesJson.Hash(doc));
        Assert.NotEqual(PropertiesJson.Hash(doc), PropertiesJson.Hash(doc with { Name = "Other" }));
        var set = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(PropertiesJson.Hash(doc), set.ContentHash);
        var bane = set.Entry("Bane")!;
        Assert.Equal(2, bane.Effects.Count);
        Assert.Equal(["defense", "raw"], bane.Effects[0].Conditions[0].Values);
        Assert.Equal(EffectKind.OverrideResult, set.Entry("Regeneration")!.Effects[0].Children[0].Kind);
        Assert.Equal(4, set.AllEffects().Count());
        Assert.NotEqual(Guid.Empty, bane.Effects[0].Id);
        Assert.All(set.AllEffects(), e => Assert.Equal(EffectDuration.Interaction, e.Duration));
    }

    [Fact]
    public void Validation_checks_every_reference_and_per_kind_rule()
    {
        var doc = PropertiesJson.Parse(ApJson);
        Assert.Empty(PropertiesJson.Validate(doc));
        var e0 = doc.Entries[1].Effects[0];
        var bad = doc with { Entries = [
            doc.Entries[0] with { Aggregation = null },                                                    // model needs aggregation
            doc.Entries[1] with { Effects = [ e0 with { TargetRoll = "save" }, e0 with { Kind = EffectKind.Multiply, Threshold = 2 } ] },
            new EntryDocument("Bolt", CatalogKind.Spell, true, null, null, "Book", "x", null, []),        // spell: no parameter, needs cost + targeting
            new EntryDocument("Ghost", CatalogKind.Weapon, false, null, null, null, "x", null,
                [ new EffectDocument(EffectSubject.Self, 0, EffectKind.SuppressProperty, null, null, "Nobody", null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], []) ]) ] };
        var problems = PropertiesJson.Validate(bad);
        Assert.Contains(problems, p => p.Contains("Regeneration") && p.Contains("aggregation"));
        Assert.Contains(problems, p => p.Contains("save"));
        Assert.Contains(problems, p => p.Contains("threshold") && p.Contains("Bane"));
        Assert.Contains(problems, p => p.Contains("Bolt") && p.Contains("cost"));
        Assert.Contains(problems, p => p.Contains("Bolt") && p.Contains("parameter"));
        Assert.Contains(problems, p => p.Contains("Nobody"));
        Assert.Throws<InvalidDataException>(() => PropertiesJson.ToSet(bad, Guid.CreateVersion7));
    }

    [Fact]
    public void A_spells_effect_is_hits_or_a_once_grant_and_nothing_else()
    {
        var doc = PropertiesJson.Parse(ApJson);
        EntryDocument Spell(EffectKind kind, EffectDuration duration) => new("Bolt", CatalogKind.Spell, false, null, 2, "Book", "x",
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
            [new EffectDocument(EffectSubject.Self, 10, kind, null, null, kind == EffectKind.GrantProperty ? "Regeneration" : null, null, null, null, null, 3, false, null, null, null, duration, [], [])]);
        Assert.Empty(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.AddHit, EffectDuration.Interaction)] }));
        Assert.Empty(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.GrantProperty, EffectDuration.Once)] }));
        Assert.Contains(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.GrantProperty, EffectDuration.Interaction)] }),
            p => p.Contains("Bolt") && p.Contains("duration"));
    }

    [Fact]
    public void Roll_result_condition_must_have_exactly_two_values_with_second_raw_or_modified()
    {
        var doc = PropertiesJson.Parse(ApJson);
        // Well-formed: ["defense", "raw"] validates clean
        var wellFormed = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["hit", "raw"])], [])
            ])
        ]};
        Assert.Empty(PropertiesJson.Validate(wellFormed));

        // One value only: ["hit"]
        var oneValue = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["hit"])], [])
            ])
        ]};
        var problems = PropertiesJson.Validate(oneValue);
        Assert.Contains(problems, p => p.Contains("Test") && p.Contains("roll_result") && p.Contains("values"));

        // Invalid second value: ["hit", "modified_invalid"]
        var badModifier = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["hit", "modified_invalid"])], [])
            ])
        ]};
        var badProblems = PropertiesJson.Validate(badModifier);
        Assert.Contains(badProblems, p => p.Contains("Test") && p.Contains("modifier"));

        // Undeclared roll: ["unknown", "raw"]
        var undeclaredRoll = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["unknown", "raw"])], [])
            ])
        ]};
        var undeclaredProblems = PropertiesJson.Validate(undeclaredRoll);
        Assert.Contains(undeclaredProblems, p => p.Contains("Test") && p.Contains("unknown"));
    }

    [Fact]
    public void Rolls_named_raw_or_modified_are_reserved()
    {
        var doc = PropertiesJson.Parse(ApJson);
        var withRawRoll = doc with { Rolls = [.. doc.Rolls, "raw"] };
        var problems = PropertiesJson.Validate(withRawRoll);
        Assert.Contains(problems, p => p.Contains("raw") && p.Contains("reserved"));

        var withModifiedRoll = doc with { Rolls = [.. doc.Rolls, "modified"] };
        var modifiedProblems = PropertiesJson.Validate(withModifiedRoll);
        Assert.Contains(modifiedProblems, p => p.Contains("modified") && p.Contains("reserved"));
    }
}
