using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Piercing Spotter / Precision Spotter (3.6): "When attacking, friendly units may remove tokens from their target before rolling to block to get +AP(X) on
/// their weapons" / "before rolling to hit to get +X to hit rolls when attacking, where X is the number of removed tokens." The attacking team picks how
/// many before the hit roll, once per target per shooting or melee of the unit; exactly those go, and X is that count for every weapon the unit attacks
/// that target with.
/// </summary>
public class ArmyRuleAuditFinishSpotterTests
{
    static PropertySet Props(string rules) => rules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;

    /// <summary>A joined Hero firing a Rifle with other stats than the squad's: the Rifle group resolves as two attacks (GfGroups.Split).</summary>
    static (Sim Sim, Unit A, Unit B) Shooting(string rules, string entry, int tokens, params int[] dice)
    {
        var a = ArmyRuleAuditHoldersTests.Joined(Unit("A", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1)), [], Gun("Rifle", "24\"", 3));
        var b = Unit("B", "4+", "2+", [], 5, []);   // a 4 still blocks at AP(2), so no wound Ask stops the run
        var sim = Sim.Gf(rules, State([a], [b], x1: -4, x2: 4), dice.Length == 0 ? Enumerable.Repeat(4, 60).ToArray() : dice);
        sim.Append([.. GfMarkers.Place(Props(rules).Entry(entry)!, sim.Unit(b.Id), a.Id, tokens)]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        return (sim, a, b);
    }

    static int Asked(Sim sim) => sim.Emitted.OfType<AskOpened>().Count(o => o.Ask.Question == GfChoice.RemoveTokens);
    static int Left(Sim sim, Unit b, string entry) => sim.Unit(b.Id).Grants.Count(g => g.EntryName == entry);

    [Theory]
    [InlineData(RuleSets.Gf360, "2", 2, 1)]
    [InlineData(RuleSets.Gf360, "0", 0, 3)]
    [InlineData(RuleSets.Gf351, "3", 3, 0)]
    public void Precision_spotter_asks_once_for_a_split_group_and_every_part_gets_the_removed_count(string rules, string answer, int bonus, int left)
    {
        var (sim, _, b) = Shooting(rules, "Precision Spotter", 3);
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(GfChoice.RemoveTokens, ask.Question);
        sim.Answer(Samples.Team1, new Chosen(answer));
        Assert.Equal(1, Asked(sim));                                                   // once for the target, not once per part
        var hits = sim.Steps("roll-to-hit");
        Assert.Equal(2, hits.Count);                                                   // the squad's Rifles and the Hero's
        foreach (var h in hits)
            Assert.Equal(bonus, h.Rolls.SelectMany(r => r.Modifiers).Where(m => m.Source == "Precision Spotter").Sum(m => m.Amount));
        Assert.Equal(left, Left(sim, b, "Precision Spotter"));                       // exactly the removed tokens go
    }

    [Theory]
    [InlineData(RuleSets.Gf360, "1", -1, 1)]
    [InlineData(RuleSets.Gf360, "0", 0, 2)]
    [InlineData(RuleSets.Gf351, "2", -2, 0)]
    public void Piercing_spotter_gives_ap_equal_to_the_removed_tokens_on_every_block(string rules, string answer, int ap, int left)
    {
        var (sim, _, b) = Shooting(rules, "Piercing Spotter", 2);
        sim.Answer(Samples.Team1, new Chosen(answer));
        Assert.Equal(1, Asked(sim));
        var blocks = sim.Emitted.OfType<StepCompleted>().SelectMany(c => c.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
        Assert.Equal(2, blocks.Count);
        foreach (var block in blocks) Assert.Equal(ap, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.Equal(left, Left(sim, b, "Piercing Spotter"));
    }

    [Fact]
    public void A_target_without_the_attacking_teams_tokens_asks_nothing()
    {
        var (sim, _, _) = Shooting(RuleSets.Gf360, "Precision Spotter", 0);
        Assert.Equal(0, Asked(sim));
        Assert.Equal(2, sim.Steps("roll-to-hit").Count);
    }

    [Fact]
    public void A_melee_strike_asks_the_charging_team_and_its_hits_get_the_removed_count()
    {
        var c = Unit("C", "4+", "4+", [], 2, [], Melee("Claws", 1));
        var d = Unit("D", "4+", "4+", [], 3, [], Melee("Spear", 1));
        var sim = Sim.Gf(RuleSets.Gf351, State([c], [d], x1: 0, x2: 1.3), Enumerable.Repeat(4, 60).ToArray());
        sim.Append([.. GfMarkers.Place(TestProperties.Core.Entry("Precision Spotter")!, sim.Unit(d.Id), c.Id, 2)]);
        sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
        Assert.Equal(GfChoice.RemoveTokens, sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Chosen("1"));
        var charger = sim.Emitted.OfType<StepCompleted>().Where(s => s.Step == "roll-to-hit").First();
        Assert.Equal(1, charger.Rolls.SelectMany(r => r.Modifiers).Where(m => m.Source == "Precision Spotter").Sum(m => m.Amount));
        Assert.Equal(1, Left(sim, d, "Precision Spotter"));
        Assert.Equal(1, Asked(sim));                                                   // the defender striking back holds no tokens on the charger
    }
}
