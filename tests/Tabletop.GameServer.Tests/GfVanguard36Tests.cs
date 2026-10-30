using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 Fanatic and Vanguard: "After this model is deployed, it may be placed anywhere fully within 9" of its position, over 9" away from non-Vanguard
/// enemy units." 3.5.1: "it may be placed anywhere fully within 9” of its position".
/// </summary>
public class GfVanguard36Tests
{
    /// <summary>The rule's placement Ask for a unit at x -12, an enemy unit at x 6 (same y), and the breach of a placement dx along x.</summary>
    static string? Breach(string rules, string rule, double dx, IReadOnlyList<PropertyInstance>? enemyRules = null)
    {
        var f = Unit("F", "4+", "4+", [], 1, [P(rule)]);
        var e = Unit("E", "4+", "4+", [], 1, [.. enemyRules ?? []]);
        var sim = Sim.Gf(rules, State([f], [e], x1: -12, x2: 6), 1);
        sim.Start(GfNames.RepositionSeq, new SequenceContext(f.Id, [], [], rule));
        var ask = sim.AskFor(Samples.Team1);
        Assert.IsType<MoveRegion>(ask.Legal);
        var m = f.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        var answer = new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }))]);
        return GrimdarkFutureRules.Instance.AnswerBreach(sim.State, ask, answer)?.Rule;
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Fanatic", null)]
    [InlineData(RuleSets.Gf360, "Fanatic", GfNames.VanguardDistanceRule)]
    [InlineData(RuleSets.Gf351, "Vanguard", null)]
    [InlineData(RuleSets.Gf360, "Vanguard", GfNames.VanguardDistanceRule)]
    public void A_placement_within_nine_inches_of_an_enemy_breaks_the_3_6_rule(string rules, string rule, string? breach)
    {
        Assert.Equal(breach, Breach(rules, rule, 8));      // 10" between centres: 8.74" edge to edge
        Assert.Null(Breach(rules, rule, 4));               // 12.74"
    }

    [Fact]
    public void An_enemy_vanguard_unit_is_no_bar()
    {
        Assert.Null(Breach(RuleSets.Gf360, "Fanatic", 8, [P("Vanguard")]));
        Assert.Equal(GfNames.VanguardDistanceRule, Breach(RuleSets.Gf360, "Vanguard", 8, [P("Fanatic")]));
    }
}
