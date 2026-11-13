using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>3.6 Banner: "This model and its unit get +1 to Morale Test rolls triggered by shooting/melee."</summary>
public class ArmyRuleAuditMoraleTests
{
    static bool BannerOn(Sim sim) => sim.Steps("test").Last().Rolls.Single().Modifiers.Any(m => m.Source == "Banner");

    static (Sim Sim, Unit Holder) Table(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var b = Unit("B", "4+", "6+", [], 1, [P("Banner"), P(GfNames.Tough, 9)]);
        return (Sim.Gf(rules, State([a], [b], x1: -4, x2: 4), Enumerable.Repeat(4, 20).ToArray()), b);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, true)]
    [InlineData(RuleSets.Gf360, false)]
    public void A_spells_morale_test_gets_the_banner_only_under_3_5_1(string rules, bool on)
    {
        var (sim, b) = Table(rules);
        sim.Start(GfNames.MoraleTest, new SequenceContext(b.Id, [], [], "spell"));
        Assert.Equal(on, BannerOn(sim));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_morale_test_after_shooting_gets_the_banner(string rules)
    {
        var (sim, b) = Table(rules);
        var a = sim.State.Board.Armies[0].Snapshot.Units[0];
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [b.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        sim.Start(GfNames.MoraleTest, new SequenceContext(b.Id, [], [], "wounds"));
        Assert.True(BannerOn(sim));
    }
}
