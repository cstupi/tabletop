using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>What both clients draw for who can shoot and how far (Shooters, ShotRanges) reads the parts an attack resolves (GfGroups.Split): 3.6 Increased
/// Shooting Range, "This model gets +6" range on its weapons when shooting", held by a joined Hero alone reaches its own Rifle, not the squad's.</summary>
public class ArmyRuleAuditFinishFactsTests
{
    [Theory]
    [InlineData(RuleSets.Gf360)]
    [InlineData(RuleSets.Gf351)]
    public void A_range_rule_only_one_carrier_of_a_weapon_name_holds_shows_that_carriers_range_and_only_it_shoots(string rules)
    {
        var a = ArmyRuleAuditHoldersTests.Joined(Unit("A", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1)), [P("Increased Shooting Range")], Gun("Rifle", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 3, []);
        var sim = Sim.Gf(rules, State([a], [b], x1: -13, x2: 13));
        var engine = rules == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.V351;
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        var range = Assert.Single(engine.ShotRanges(sim.State, props, a.Id, b.Id));
        Assert.Equal(("Rifle", 24.0, 30.0), (range.Weapon, range.Printed, range.Inches));
        Assert.Contains("Increased Shooting Range", range.By);
        Assert.Equal([a.Models[2].Id], engine.Shooters(sim.State, props, a.Id, b.Id));   // the Hero, beyond the squad's 24"
        var threat = engine.Threats(sim.State, props)[a.Id].Ranges;                         // the hover outline reads the same parts
        Assert.Equal([24.0, 24.0, 30.0], a.Models.Select(m => threat[m.Id]));
    }
}
