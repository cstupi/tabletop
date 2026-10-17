using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class PickGrammarTests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);
    static EffectDocument Grant(string entry, EffectSubject subject, EffectDuration duration = EffectDuration.Once, params ConditionDocument[] when) =>
        new(subject, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, duration, when, []);
    static EntryDocument Buff(string name = "Test Buff", params EffectDocument[] rows) =>
        new(name, CatalogKind.Model, false, Aggregation.None, null, null, "Test.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, LineOfSight: false),
            rows.Length == 0 ? [Grant("Stealth", EffectSubject.Self, EffectDuration.Once, BeforeAttacking)] : rows);
    static IReadOnlyList<string> Problems(params EntryDocument[] entries) =>
        PropertiesJson.Validate(TestProperties.CoreDocument with { Entries = [.. TestProperties.CoreDocument.Entries, .. entries] });

    [Fact]
    public void Line_of_sight_false_is_read_and_true_reads_as_omitted()
    {
        const string json = """
        { "name": "D", "gameRules": "grimdark-future", "entries": [
          { "name": "A", "kind": "model", "aggregation": "none", "ruleText": "A.", "targeting": { "side": "friendly", "grain": "unit", "count": 1, "range": 12, "lineOfSight": false } },
          { "name": "B", "kind": "model", "aggregation": "none", "ruleText": "B.", "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 18, "lineOfSight": true } } ] }
        """;
        var doc = PropertiesJson.Parse(json);
        Assert.False(doc.Entries[0].Targeting!.LineOfSight);
        Assert.Null(doc.Entries[1].Targeting!.LineOfSight);
        Assert.DoesNotContain("lineOfSight", PropertiesJson.EntryCanonical(doc.Entries[1]));
    }

    [Fact]
    public void A_pick_entry_is_admitted()
    {
        Assert.Empty(Problems(Buff()));
    }

    [Fact]
    public void A_pick_entry_is_a_model_entry_of_once_rows_at_before_attacking_that_picks_one()
    {
        Assert.Contains("Unit Pick: targeting is set on a spell or a pick entry (kind model)", Problems(Buff("Unit Pick") with { Kind = CatalogKind.Unit, Aggregation = null }));
        Assert.Contains("No Point: a pick entry's row has an at_point naming before-attacking", Problems(Buff("No Point", Grant("Stealth", EffectSubject.Self))));
        Assert.Contains("Lasting: a pick entry's rows have duration once or are take_test, roll, add_hit hits or remove_wounds", Problems(Buff("Lasting", Grant("Stealth", EffectSubject.Self, EffectDuration.Interaction, BeforeAttacking))));
        Assert.Contains("Two: a pick entry picks one", Problems(Buff("Two") with { Targeting = new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 12) }));
    }

    [Fact]
    public void A_once_row_off_a_spell_or_pick_entry_is_refused_and_a_spell_is_always_in_sight()
    {
        var loose = new EntryDocument("Loose", CatalogKind.Model, false, Aggregation.All, null, null, "Loose.", null, [Grant("Stealth", EffectSubject.Self)]);
        Assert.Contains("Loose: a once row belongs to a spell or a pick entry", Problems(loose));
        var blind = new EntryDocument("Blind", CatalogKind.Spell, false, null, 1, null, "Blind.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12, LineOfSight: false),
            [Grant("Stealth", EffectSubject.Self)]);
        Assert.Contains("Blind: a spell's target is in line of sight (p.13)", PropertiesJson.Validate(TestProperties.CoreDocument with
        {
            Entries = [.. TestProperties.CoreDocument.Entries, blind], Books = [.. TestProperties.CoreDocument.Books ?? [], new BookRecord("blind-book", "Blind", null, ["Blind"])],
        }));
    }

    [Fact]
    public void A_carried_pick_entry_exposes_no_rows()
    {
        var props = TestProperties.WithSpells(Buff());
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], GfNames.BeforeAttacking));
        Assert.False(live.Has(Side.Attacker, "Stealth"));
        Assert.Empty(live.AtPoint(Side.Attacker, GfNames.BeforeAttacking));
    }

    [Fact]
    public void A_granted_rows_at_point_holds_wherever_the_grant_is_read()
    {
        var props = TestProperties.WithSpells(Buff());
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a, b], [Unit("C", "4+", "4+", [], 1, [])]);
        var row = props.Entry("Test Buff")!.Effects.Single();
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new GrantAdded(b.Id, new Grant(Guid.CreateVersion7(), row.Id, "Test Buff", a.Id, 1)), null));
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, b.Id), null, null, null, null, []));
        Assert.True(live.Has(Side.Attacker, "Stealth"));
    }
}
