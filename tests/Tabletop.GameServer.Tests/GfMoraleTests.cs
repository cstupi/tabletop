using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfMoraleTests
{
    static Sim WithLosses(Tabletop.Content.Unit u, int dead, params int[] dice)
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [u]), dice);
        sim.Append(u.Models.Take(dead).Select(m => (EventPayload)new ModelsMoved([new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf())])));
        return sim;
    }

    [Fact]
    public void A_failed_wounds_test_shakes_and_a_passed_one_does_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var sim = WithLosses(u, 2, 3);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        var step = sim.Steps("test").Single();
        Assert.Equal((GfNames.Morale, 4, false), (step.Rolls[0].Roll, step.Rolls[0].Threshold, step.Rolls[0].Dice[0].Success));
        Assert.Contains(GfNames.Shaken, sim.Unit(u.Id).Conditions);
        Assert.Equal(2, sim.Alive(u.Id));
        var ok = WithLosses(u, 2, 5);
        ok.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        Assert.DoesNotContain(GfNames.Shaken, ok.Unit(u.Id).Conditions);
        Assert.Equal(1, ok.Steps("test").Single().Values!["passed"]);
    }

    [Fact]
    public void A_failed_melee_test_at_half_or_less_routs_and_above_half_shakes()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var routed = WithLosses(u, 2, 1);
        routed.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Equal(0, routed.Alive(u.Id)); Assert.Equal(1, routed.Steps("test").Single().Values!["routed"]);
        var shaken = WithLosses(u, 1, 1);
        shaken.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Equal(3, shaken.Alive(u.Id)); Assert.Contains(GfNames.Shaken, shaken.Unit(u.Id).Conditions);
    }

    [Fact]
    public void Fearless_rerolls_a_failure_at_four_plus_and_a_shaken_unit_rolls_no_quality_die()
    {
        var u = Unit("U", "4+", "4+", [], 3, [P("Fearless")]);
        var sim = WithLosses(u, 1, 1, 4);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        var step = sim.Steps("test").Single();
        Assert.Equal([GfNames.Morale, "Fearless"], step.Rolls.Select(r => r.Roll));
        Assert.Equal(4, step.Rolls[1].Threshold);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);

        var already = WithLosses(Unit("S", "4+", "4+", [], 2, []), 0, 6);
        var s = already.State.Board.Armies[1].Snapshot.Units[0].Id;
        already.Append([new ConditionSet(new Subject(SubjectKind.Unit, s), GfNames.Shaken, true)]);
        already.Start(GfNames.MoraleTest, new SequenceContext(s, [], [], "melee"));
        Assert.Empty(already.Steps("test").Single().Rolls);                                     // no die: Shaken always fails
        Assert.Equal(2, already.Alive(s));                                                     // above half: a failed melee test shakes, and it already is

        var fearless = Unit("F", "4+", "4+", [], 2, [P("Fearless")]);
        var shakenFearless = WithLosses(fearless, 0, 5);
        shakenFearless.Append([new ConditionSet(new Subject(SubjectKind.Unit, fearless.Id), GfNames.Shaken, true)]);
        shakenFearless.Start(GfNames.MoraleTest, new SequenceContext(fearless.Id, [], [], "wounds"));
        var f = shakenFearless.Steps("test").Single();
        Assert.Equal(["Fearless"], f.Rolls.Select(r => r.Roll));                               // the automatic failure still triggers Fearless
        Assert.Equal(1, f.Values!["passed"]);
    }

    [Fact]
    public void A_unit_with_no_living_models_finishes_without_testing()
    {
        var u = Unit("U", "4+", "4+", [], 2, []);
        var sim = WithLosses(u, 2, 1);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Empty(sim.Steps("test").Single().Rolls);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);
        Assert.Empty(sim.State.Projection.Stack);
    }
}
