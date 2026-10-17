using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 2 (army-book rules spec → the pick): a pick entry's use rows, its targeting's required rule, and remove_wounds.</summary>
public class PickUseRowsGrammarTests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);
    static readonly ConditionDocument OncePerGame = new(ConditionKind.UsageCount, null, Comparison.Lt, 1, ["game"]);
    static EffectDocument Row(EffectKind kind, int? amount = null, string? targetRoll = null, IReadOnlyList<EffectDocument>? children = null, int? threshold = null,
        string? dice = null, EffectDuration duration = EffectDuration.Interaction, IReadOnlyList<ConditionDocument>? when = null, string? entry = null) =>
        new(EffectSubject.Self, 10, kind, targetRoll, null, entry, null, null, null, null, amount, false, threshold, null, null, duration, when ?? [BeforeAttacking], children ?? [], AmountDice: dice);
    static EntryDocument Pick(string name, SpellTargeting t, params EffectDocument[] rows) => new(name, CatalogKind.Model, false, Aggregation.None, null, null, name + ".", t, rows);
    static readonly SpellTargeting Enemy = new(TargetSide.Enemy, TargetGrain.Unit, 1, 12, LineOfSight: false);
    static IReadOnlyList<string> Problems(params EntryDocument[] entries) =>
        PropertiesJson.Validate(TestProperties.CoreDocument with { Entries = [.. TestProperties.CoreDocument.Entries, .. entries] });

    [Fact]
    public void A_pick_entry_admits_a_test_dice_with_hits_hits_and_a_heal_used_on_its_target()
    {
        var hit = Row(EffectKind.AddHit, 3, children: [Row(EffectKind.GrantProperty, entry: "AP", amount: 1, when: [])], when: []);
        Assert.Empty(Problems(
            Pick("Storm", Enemy, Row(EffectKind.Roll, 3, threshold: 2, children: [hit], when: [BeforeAttacking, OncePerGame])),
            Pick("Breath", Enemy, Row(EffectKind.AddHit, 1, children: [Row(EffectKind.GrantProperty, entry: "Blast", amount: 3, when: [])])),
            Pick("Test", Enemy, Row(EffectKind.TakeTest, targetRoll: "morale", children: [new(EffectSubject.Self, 20, EffectKind.SetCondition, null, null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], [], TargetCondition: "fatigued")])),
            Pick("Heal", new SpellTargeting(TargetSide.Friendly, TargetGrain.Model, 1, 3, LineOfSight: false, Requires: "Tough"), Row(EffectKind.RemoveWounds, 1, dice: "d3"))));
    }

    [Fact]
    public void A_pick_entrys_interaction_row_of_another_kind_and_a_missing_required_rule_are_refused()
    {
        Assert.Contains("Lasting: a pick entry's rows have duration once or are take_test, roll, add_hit hits or remove_wounds",
            Problems(Pick("Lasting", Enemy, Row(EffectKind.ModifyRoll, -1, targetRoll: "hit"))));
        Assert.Contains("Nobody: targeting requires no entry 'Nobody Rule'",
            Problems(Pick("Nobody", Enemy with { Requires = "Nobody Rule" }, Row(EffectKind.ModifyRoll, -1, targetRoll: "hit", duration: EffectDuration.Once))));
        var loose = new EntryDocument("Loose Heal", CatalogKind.Model, false, Aggregation.All, null, null, "Loose.", null, [Row(EffectKind.RemoveWounds, 1, when: [])]);
        Assert.Contains("Loose Heal: remove_wounds belongs to a pick entry", Problems(loose));
    }

    [Fact]
    public void Requires_round_trips_through_json_and_is_omitted_when_null()
    {
        var doc = PropertiesJson.Parse("""
        { "name": "D", "gameRules": "grimdark-future", "entries": [
          { "name": "A", "kind": "model", "aggregation": "none", "ruleText": "A.", "targeting": { "side": "friendly", "grain": "model", "count": 1, "range": 3, "lineOfSight": false, "requires": "Tough" } } ] }
        """);
        Assert.Equal("Tough", doc.Entries[0].Targeting!.Requires);
        Assert.DoesNotContain("requires", PropertiesJson.EntryCanonical(doc.Entries[0] with { Targeting = doc.Entries[0].Targeting! with { Requires = null } }));
    }
}
