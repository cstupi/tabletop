using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>Under strict: Flying and impassable terrain (p.11, p.13, p.7), a model standing in impassable terrain, and a stand-still answer.</summary>
public class GfMovesTerrainTests
{
    /// <summary>A 1" x 10" impassable wall centred on (x, y), its long side along Y.</summary>
    static TerrainPlacement Wall(double x, double y) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 10), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    static DispatchOutcome Move(Sim sim, Guid team, params ModelRelocation[] moves) =>
        sim.Dispatch(new AnswerAsk(sim.AskFor(team).Id, new Destinations(moves)), team == Samples.Team1 ? Samples.P1 : P2, team);

    static void RefusedFor(Sim sim, Guid team, string rule, params ModelRelocation[] moves)
    {
        Assert.Equal(rule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(team), new Destinations(moves))?.Rule);
        Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, team, moves)).Reason);
    }

    static Sim Advancing(PropertyInstance[] rules, TerrainPlacement wall, out Unit unit)
    {
        unit = Unit("A", "4+", "4+", rules, 1, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict, terrain: [wall]), 4);
        Place(sim, unit.Models[0].Id, 20, 24);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(unit.Id, [], [], GfNames.Advance));
        return sim;
    }

    [Fact]
    public void Under_strict_a_flying_advance_passes_over_impassable_terrain_but_may_not_end_in_it()
    {
        // p.13 Flying: "may move through units and terrain"; p.11: "unless they have any rules that allow them to ignore it", but "may not end
        // their move partially overhanging gaps or terrain", and p.7: never "placed in physically impossible locations".
        var wall = Wall(24, 24);
        var sim = Advancing([P("Flying")], wall, out var unit);
        var a = unit.Models[0].Id;
        Assert.Empty(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).ImpassableVolumeIds);
        RefusedFor(sim, Samples.Team1, GfNames.MoveImpassableRule, At(sim, a, 24, 24));                     // lands in the wall
        Assert.IsType<Applied>(Move(sim, Samples.Team1, At(sim, a, 25.5, 24)));                              // over it
        sim.Run();
        Assert.Equal(25.5, ((OnTable)sim.Model(a).Location).Placement.Position.X, 6);
    }

    [Fact]
    public void Under_strict_a_unit_without_flying_still_may_not_move_through_impassable_terrain()
    {
        var wall = Wall(24, 24);
        var sim = Advancing([], wall, out var unit);
        Assert.Equal([wall.Volumes[0].Id], Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).ImpassableVolumeIds);
        RefusedFor(sim, Samples.Team1, GfNames.MoveImpassableRule, At(sim, unit.Models[0].Id, 25.5, 24));
    }

    [Fact]
    public void Under_strict_a_model_standing_in_impassable_terrain_may_stand_still_or_move_out_but_not_end_in_it()
    {
        var wall = Wall(24, 24);
        var sim = Advancing([], wall, out var unit);
        var a = unit.Models[0].Id;
        Place(sim, a, 24, 24);                                                                               // a hand placement put it in the wall
        RefusedFor(sim, Samples.Team1, GfNames.MoveImpassableRule, At(sim, a, 24, 27));                      // still in it
        Assert.Null(GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations([At(sim, a, 24, 24)])));   // standing still
        Assert.IsType<Applied>(Move(sim, Samples.Team1, At(sim, a, 20, 24)));                                // out
    }

    [Fact]
    public void Under_strict_a_model_standing_in_impassable_terrain_may_move_out_of_it_but_not_along_or_through_it()
    {
        // p.11: "Units may not move through impassable terrain". Only a hand move puts a model there (p.7 "never ... be placed in physically
        // impossible locations"); it may head out of the volume, never along it or deeper in (Movement.Blocking).
        var wall = Wall(24, 24);                                                                             // x 23.5..24.5, y 19..29
        var sim = Advancing([], wall, out var unit);
        var a = unit.Models[0].Id;
        Place(sim, a, 24, 24);
        RefusedFor(sim, Samples.Team1, GfNames.MoveImpassableRule, At(sim, a, 24, 30.5));                   // along the wall and out of its end
        Assert.Null(GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations([At(sim, a, 26, 25)])));   // out, east
        Place(sim, a, 23.2, 24);                                                                             // its base over the west face
        RefusedFor(sim, Samples.Team1, GfNames.MoveImpassableRule, At(sim, a, 26, 24));                     // on through it
        Assert.Null(GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations([At(sim, a, 20, 24)])));   // back off it
    }

    [Fact]
    public void Under_strict_a_flying_charger_reaches_and_charges_over_a_wall_and_a_flying_defender_closes_in_over_one()
    {
        var charger = Unit("C", "4+", "4+", [P("Flying")], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [P("Flying")], 2, [], Melee("Fist", 1));
        var walls = new[] { Wall(24, 24), Wall(28, 31) };
        var sim = Sim.Gf(State([charger], [target], enforcement: Enforcement.Strict, terrain: walls), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24); Place(sim, target.Models[1].Id, 29.5, 31);
        Assert.Contains(target.Id, GfMelee.ChargeTargets(sim.State, TestProperties.Core, charger.Id));
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.Empty(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).ImpassableVolumeIds);
        Assert.IsType<Applied>(Move(sim, Samples.Team1, At(sim, charger.Models[0].Id, 26.74, 24)));        // over the first wall into contact
        sim.Run();
        // p.9: the free defender closes in 3"; it flies, so the second wall is no obstacle, but it may not end in it.
        var free = target.Models[1].Id;
        Assert.Empty(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal).ImpassableVolumeIds);
        RefusedFor(sim, Samples.Team2, GfNames.MoveImpassableRule, At(sim, free, 28, 31));
        Assert.IsType<Applied>(Move(sim, Samples.Team2, At(sim, free, 26.5, 31)));
    }

    [Fact]
    public void A_charger_without_flying_cannot_reach_a_target_behind_a_wall()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], enforcement: Enforcement.Strict, terrain: [Wall(24, 24)]), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24);
        Assert.DoesNotContain(target.Id, GfMelee.ChargeTargets(sim.State, TestProperties.Core, charger.Id));
    }

    [Fact]
    public void Under_strict_a_disembarking_model_answered_where_it_is_stays_inside_and_the_rest_get_out()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([bus, riders], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict), 4);
        var container = bus.Models[0].Id;
        Place(sim, container, 36, 24);
        sim.Append([new ModelsMoved(riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        var (a, b) = (riders.Models[0].Id, riders.Models[1].Id);
        Assert.IsType<Applied>(Move(sim, Samples.Team1,
            new ModelRelocation(a, new Inside(container), new OnTable(new Placement(new Point3(40, 24, 0), new Yaw(0)))),
            new ModelRelocation(b, new Inside(container), new Inside(container))));                        // nowhere to stand: it stays put
        sim.Run();
        Assert.IsType<OnTable>(sim.Model(a).Location);
        Assert.Equal(new Inside(container), sim.Model(b).Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Under_strict_a_disembark_may_not_land_in_impassable_terrain(bool flying)
    {
        // p.11 "may not end their move partially overhanging gaps or terrain", p.7 "physically impossible locations": an exit is a move's end too.
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", flying ? [P("Flying")] : [], 1, []);
        var sim = Sim.Gf(State([bus, riders], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict, terrain: [Wall(40, 24)]), 4);
        var container = bus.Models[0].Id;
        Place(sim, container, 36, 24);
        sim.Append([new ModelsMoved([new ModelRelocation(riders.Models[0].Id, sim.Model(riders.Models[0].Id).Location, new Inside(container))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        ModelRelocation Out(double x) => new(riders.Models[0].Id, new Inside(container), new OnTable(new Placement(new Point3(x, 24, 0), new Yaw(0))));
        RefusedFor(sim, Samples.Team1, GfNames.MoveExitRule, Out(40));                                    // inside the wall, 4" out
        Assert.IsType<Applied>(Move(sim, Samples.Team1, Out(32)));
        sim.Run();
        Assert.IsType<OnTable>(sim.Model(riders.Models[0].Id).Location);
    }

    [Fact]
    public void Under_warn_a_disembark_into_impassable_terrain_is_not_applied_and_the_model_stays_inside()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([bus, riders], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Warn, terrain: [Wall(40, 24)]), 4);
        var container = bus.Models[0].Id;
        Place(sim, container, 36, 24);
        sim.Append([new ModelsMoved([new ModelRelocation(riders.Models[0].Id, sim.Model(riders.Models[0].Id).Location, new Inside(container))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(riders.Models[0].Id, new Inside(container), new OnTable(new Placement(new Point3(40, 24, 0), new Yaw(0))))]));
        Assert.Equal(new Inside(container), sim.Model(riders.Models[0].Id).Location);
    }
}
