using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>3.6.0 p.14 Cumbersome: "May only use Hold or Advance actions, but can’t shoot when moving." 3.5.1 has no such rule.</summary>
public class GfCumbersome36Tests
{
    static readonly string[] Rows = ["Hold", "Advance", "Rush", "Charge"];

    static Unit Tank() => Unit("Tank", "4+", "2+", [], 1, [P("Cumbersome")], Gun("Cannon", "24\"", 1));

    static Destinations MoveAll(Sim sim, Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location,
        new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Cumbersome_offers_hold_and_advance_only_with_its_note(string rulesId)
    {
        var tank = Tank();
        var state = State([tank], [Unit("Enemy", "4+", "4+", [], 1, [])], x1: -4, x2: 4).Under(rulesId);   // within charge distance
        PropertySet core = rulesId == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        var (actions, note) = GrimdarkFutureRules.ActivationChoices(state, core, state.Projection.Units[tank.Id], Rows);
        if (rulesId == RuleSets.Gf360)
        {
            Assert.Equal(["Hold", "Advance"], actions);
            Assert.Equal("Cumbersome: may only use Hold or Advance actions, and can't shoot when moving (3.6.0 p.14)", note);
        }
        else
        {
            Assert.Equal(Rows, actions);
            Assert.Null(note);
        }
    }

    [Theory]
    [InlineData(RuleSets.Gf351, true)]
    [InlineData(RuleSets.Gf360, false)]
    public void A_cumbersome_unit_that_advanced_does_not_shoot_and_one_that_held_does(string rulesId, bool shootsAfterMoving)
    {
        var tank = Tank();
        var sim = Sim.Gf(rulesId, State([tank], [Unit("T", "4+", "4+", [], 1, [])], x1: -10, x2: 10), 1);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(tank.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, MoveAll(sim, tank, 2));
        Assert.Equal(shootsAfterMoving, sim.AskForOrNull(Samples.Team1)?.Question == "shoot_or_not");
        var step = sim.Steps("shoot-or-not");
        if (!shootsAfterMoving)
        {
            Assert.Equal(1, Assert.Single(step).Values!["cumbersome_no_shot"]);
            Assert.Empty(sim.State.Projection.Stack);
        }

        var held = Tank();
        var sim2 = Sim.Gf(rulesId, State([held], [Unit("T", "4+", "4+", [], 1, [])], x1: -10, x2: 10), 1);
        sim2.Start(GfNames.HoldSeq, new SequenceContext(held.Id, [], [], GfNames.Hold));
        Assert.Equal("shoot_or_not", sim2.AskFor(Samples.Team1).Question);

        // An Advance that does not move is not "moving": the shot is offered.
        var stayed = Tank();
        var sim3 = Sim.Gf(rulesId, State([stayed], [Unit("T", "4+", "4+", [], 1, [])], x1: -10, x2: 10), 1);
        sim3.Start(GfNames.AdvanceSeq, new SequenceContext(stayed.Id, [], [], GfNames.Advance));
        sim3.Answer(Samples.Team1, new Passed());
        Assert.Equal("shoot_or_not", sim3.AskFor(Samples.Team1).Question);
    }
}
