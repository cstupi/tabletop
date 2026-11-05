using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using static Tabletop.GameServer.Tests.AppliedKindsTests;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.14: "The Hero’s unit, and units within 6” of friendly Heroes with Tough(9) or higher, may use the Hero’s Quality for Morale Tests as long as
/// the Hero isn’t Shaken." 3.5.1 p.14 lends a Hero's Quality to its own unit only.
/// </summary>
public class GfMoraleAura36Tests
{
    /// <summary>Armies.State stands team 1's first unit at y -18.5 and -17 (x -12); a 1.26" base edge to edge from (x, y) to (-12, -17) is the gap less 1.26.</summary>
    static EventPayload Stand(Sim sim, Tabletop.Content.Unit u, double x, double y) =>
        new ModelsMoved([new ModelRelocation(u.Models[0].Id, sim.Model(u.Models[0].Id).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))]);

    static Tabletop.Content.Unit Big(string name, string quality, int tough = 9) => Unit(name, quality, "4+", [], 1, [P("Hero"), P("Tough", tough)]);

    /// <summary>The squad's morale test after its setup, on a 4.</summary>
    static Sim Test(string rules, Tabletop.Content.Unit squad, IReadOnlyList<Tabletop.Content.Unit> friends, IReadOnlyList<Tabletop.Content.Unit> enemies, Func<Sim, IEnumerable<EventPayload>> setup)
    {
        var sim = Sim.Gf(rules, State([squad, .. friends], enemies.Count == 0 ? [Unit("E", "4+", "4+", [], 1, [])] : enemies), 4);
        sim.Append(setup(sim));
        sim.Start(GfNames.MoraleTest, new SequenceContext(squad.Id, [], [], "wounds"));
        return sim;
    }

    static int? Threshold(Sim sim) => sim.Steps("test").Single().Rolls.Single().Threshold;

    [Theory]
    [InlineData(RuleSets.Gf351, 5, false)]
    [InlineData(RuleSets.Gf360, 3, true)]
    public void A_tough_nine_hero_within_six_inches_lends_its_quality_under_3_6_not_3_5_1(string rules, int threshold, bool lent)
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, []);
        var hero = Big("Captain", "3+");
        var near = Test(rules, squad, [hero], [], s => [Stand(s, hero, -12, -12)]);              // 5" apart, 3.74" edge to edge
        Assert.Equal(threshold, Threshold(near));
        Assert.Equal(lent ? 1 : 0, near.Steps("consequence").Single().Values!["passed"]);
        Assert.Equal(lent ? ["Hero"] : [], Applied(near, "test"));
        Assert.Equal(threshold, near.Steps("test").Single().Rolls.Single().Basis!.Printed);       // the lending Hero's printed Quality, or the unit's

        var far = Test(rules, squad, [hero], [], s => [Stand(s, hero, -12, -9)]);                // 8" apart, 6.74" edge to edge
        Assert.Equal(5, Threshold(far));
        Assert.Empty(Applied(far, "test"));
    }

    [Fact]
    public void A_shaken_or_tough_six_hero_lends_nothing()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, []);
        var shaken = Big("Captain", "3+");
        var sim = Test(RuleSets.Gf360, squad, [shaken], [], s => [Stand(s, shaken, -12, -12), new ConditionSet(new Subject(SubjectKind.Unit, shaken.Id), GfNames.Shaken, true)]);
        Assert.Equal(5, Threshold(sim));

        var small = Big("Sergeant", "3+", tough: 6);
        Assert.Equal(5, Threshold(Test(RuleSets.Gf360, squad, [small], [], s => [Stand(s, small, -12, -12)])));

        var enemy = Big("Warlord", "2+");                                                         // not friendly
        Assert.Equal(5, Threshold(Test(RuleSets.Gf360, squad, [], [enemy], s => [Stand(s, enemy, -10, -17)])));
    }

    [Fact]
    public void The_better_of_several_applies()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, []);
        var joined = squad with { Models = [squad.Models[0] with { Stats = new Dictionary<string, string> { ["Quality"] = "4+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var (a, b) = (Big("A", "3+"), Big("B", "2+"));
        var sim = Test(RuleSets.Gf360, joined, [a, b], [], s => [Stand(s, a, -12, -12), Stand(s, b, -10, -17)]);
        Assert.Equal(2, Threshold(sim));
        Assert.Equal(["Hero"], Applied(sim, "test"));

        var worse = Test(RuleSets.Gf360, joined, [Big("C", "5+")], [], s => []);
        Assert.Equal(4, Threshold(worse));                                                        // the joined Hero's own, an out-of-range or worse lender aside
    }
}
