using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Quick Shot Mark (generated): "which friendly units gets Quick Shot against once", Quick Shot being "This model may shoot after using Rush actions".</summary>
public class QuickShotMarkTests
{
    static Sim Rushing(Tabletop.Content.Unit a, Tabletop.Content.Unit b)
    {
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(Enumerable.Repeat(4, 60).ToArray()));
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Passed());                                          // stays put: the enemy is 12" away
        return sim;
    }

    [Fact]
    public void A_marked_enemy_may_be_shot_after_rush_and_the_shooting_spends_the_mark()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Quick Shot Mark")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Rushing(a, b);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Quick Shot Mark", a.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Single(sim.Unit(b.Id).Grants);
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);                         // the pick reopens the move: the Rush's 12" are still unspent
        sim.Answer(Samples.Team1, new Passed());
        Assert.Equal("shoot_or_not", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Empty(sim.Unit(b.Id).Grants);                                              // once: spent by the shooting it allowed
    }

    [Fact]
    public void Without_the_mark_a_rushing_unit_is_not_asked_to_shoot()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Quick Shot Mark")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Rushing(a, b);
        sim.Answer(Samples.Team1, new Passed());                                          // the react Ask: the Mark is not used
        Assert.NotEqual("shoot_or_not", sim.AskForOrNull(Samples.Team1)?.Question);
    }
}
