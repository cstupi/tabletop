using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Casting support read against Caster Group's designated caster.</summary>
public class ArmyRuleAuditCastingTests
{
    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Casting_buff_may_pick_a_caster_groups_designated_caster_and_not_the_others(string rules)
    {
        // Casting Buff: "pick one friendly model within 12" with Caster"; Caster Group: "Pick one model with this rule in this unit to have Caster(X)".
        var buffer = Unit("B", "4+", "4+", [], 1, [P("Casting Buff")]);
        var group = Unit("G", "4+", "4+", [], 3, [P("Caster Group")]);
        var state = State([buffer, group], [Unit("E", "4+", "4+", [], 1, [])], x1: -12);
        if (rules == RuleSets.Gf360) state = state.Under(RuleSets.Gf360);
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        var targets = GfCasting.Targets(state, buffer.Models[0].Id, props.Entry("Casting Buff")!.Targeting!, entry: "Casting Buff");
        Assert.Equal([GfSpellSupport.DesignatedCaster(state, group.Id)!.Value], targets);
    }
}
