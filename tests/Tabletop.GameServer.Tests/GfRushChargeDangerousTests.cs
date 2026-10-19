using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.12: "Ruins - Cover + Dangerous when using rush/charge". p.11: "If a model moves in or through dangerous terrain, or is activated in it, then it
/// must take a dangerous terrain test." A DangerousOnRushCharge volume is that dangerous terrain for a Rush or a Charge, and nothing for Hold or Advance.
/// </summary>
public class GfRushChargeDangerousTests
{
    /// <summary>A 4" x 4" ruin centred on (x, -20), across the row the first team's single model stands on.</summary>
    static TerrainPlacement Ruin(double x, double length = 4) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(x, -20, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(4, length), new Point2(0, 0), new Yaw(0), 0, 4,
            TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge, null))]);

    static Destinations By(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location,
        new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    static readonly int[] Fours = Enumerable.Repeat(4, 30).ToArray();

    static Sim Moving(string sequence, string action, double ruinX, out Tabletop.Content.Unit unit)
    {
        unit = Unit("A", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Ruin(ruinX)]), Fours);
        sim.Start(sequence, new SequenceContext(unit.Id, [], [], action));
        return sim;
    }

    [Fact]
    public void A_rush_through_ruins_takes_a_dangerous_terrain_test()
    {
        var sim = Moving(GfNames.RushSeq, GfNames.Rush, -6, out var a);
        Assert.Empty(sim.Steps("test"));                                                           // not activated in it
        sim.Answer(Samples.Team1, By(sim, a, 12));                                                 // -12 → 0, through the ruin at x -8..-4
        Assert.Single(sim.Steps("test").Single().Rolls.Single().Dice);
    }

    [Fact]
    public void An_advance_through_ruins_takes_no_test()
    {
        var sim = Moving(GfNames.AdvanceSeq, GfNames.Advance, -8, out var a);
        sim.Answer(Samples.Team1, By(sim, a, 6));                                                  // -12 → -6, through the ruin at x -10..-6
        Assert.Empty(sim.Steps("test"));
    }

    [Fact]
    public void A_rush_activated_in_ruins_tests_at_activation_and_an_advance_does_not()
    {
        var rush = Moving(GfNames.RushSeq, GfNames.Rush, -12, out _);
        Assert.Single(rush.Steps("test").Single().Rolls.Single().Dice);                            // p.11 "or is activated in it", using Rush
        var advance = Moving(GfNames.AdvanceSeq, GfNames.Advance, -12, out _);
        Assert.Empty(advance.Steps("test"));
        var hold = Moving(GfNames.HoldSeq, GfNames.Hold, -12, out _);
        Assert.Empty(hold.Steps("test"));
    }

    [Fact]
    public void A_charge_through_ruins_takes_a_dangerous_terrain_test()
    {
        var charger = Unit("A", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var target = Unit("B", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var sim = Sim.Gf(State([charger], [target], x1: -10, x2: 0, terrain: [Ruin(-5, 30)]), Fours);   // long: the charge cannot go round it
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.Empty(sim.Steps("test"));
        sim.Answer(Samples.Team1, By(sim, charger, 8.7));                                          // -10 → -1.3, base to base, through the ruin at x -7..-3
        Assert.Single(sim.Steps("test").Single().Rolls.Single().Dice);
    }

    [Fact]
    public void A_flying_rush_through_ruins_takes_no_test()
    {
        var unit = Unit("A", "4+", "4+", [P("Flying")], 1, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Ruin(-6)]), Fours);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, By(sim, unit, 12));
        Assert.Empty(sim.Steps("test"));                                                           // p.13 Flying: "ignore terrain effects whilst moving"
    }
}
