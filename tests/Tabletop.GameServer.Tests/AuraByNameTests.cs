using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class AuraByNameTests
{
    static EffectDocument Grant(string entry, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, when, []);
    static EntryDocument Aura(string name, string grants, Aggregation aggregation = Aggregation.Any, params ConditionDocument[] when) =>
        new(name, CatalogKind.Model, false, aggregation, null, null, "This model and its unit get it.", null, [Grant(grants, when)]);

    static readonly PropertySet Props = TestProperties.WithSpells(
        Aura("Test Scout Aura", "Scout"),
        Aura("Test Shooting Scout Aura", "Scout", Aggregation.Any, new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"])),
        Aura("Test All Scout", "Scout", Aggregation.All),
        new EntryDocument("Test Text Aura", CatalogKind.Model, false, Aggregation.Any, null, null, "This model and its unit get Scout.", null, [], Played: false));

    /// <summary>A three-model unit whose first model alone carries the rule.</summary>
    static Tabletop.Content.Unit OneCarrier(string rule)
    {
        var u = Unit("A", "4+", "4+", [], 3, []);
        return u with { Models = [u.Models[0] with { Properties = [P(rule)] }, .. u.Models.Skip(1)] };
    }

    [Fact]
    public void A_carried_aura_gives_its_unit_a_rule_read_by_name()
    {
        var a = OneCarrier("Test Scout Aura");
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        Assert.True(Lookup.UnitHas(s, Props, a.Id, "Scout"));
    }

    [Fact]
    public void A_conditioned_grant_an_all_entry_or_a_text_only_aura_gives_no_rule_by_name()
    {
        foreach (var rule in new[] { "Test Shooting Scout Aura", "Test All Scout", "Test Text Aura" })
        {
            var a = OneCarrier(rule);
            var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
            Assert.False(Lookup.UnitHas(s, Props, a.Id, "Scout"), rule);
        }
    }
}
