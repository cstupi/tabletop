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

/// <summary>
/// Moves judged along the path the base takes, under strict. p.5: "always measure so that no part of its base moves further than the total
/// distance"; p.11: difficult terrain caps a unit that "moves in or through" it, and dangerous terrain tests a model that does; p.9: "charge moves
/// don't have to be in a straight line".
/// </summary>
public class GfMovesPathTests
{
    static TerrainPlacement Piece(double x, double y, double width, double length, VolumeKind kind, TerrainDeclarations d) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("piece"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(kind, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, 3, d, null))]);

    static TerrainPlacement Pit(double x, double y, double radius) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("pit"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Circle(radius), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves) =>
        sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);

    static string? Breach(Sim sim, params ModelRelocation[] moves) =>
        GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations(moves))?.Rule;

    static Sim Moving(string sequence, string action, TerrainPlacement[] terrain, out Unit unit, int models = 1)
    {
        unit = Unit("A", "4+", "4+", [], models, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict, terrain: terrain), 4);
        for (var i = 0; i < models; i++) Place(sim, unit.Models[i].Id, 20, 24 + 2 * i);
        sim.Start(sequence, new SequenceContext(unit.Id, [], [], action));
        return sim;
    }

    [Fact]
    public void A_rush_around_a_wood_is_not_held_to_six_inches()
    {
        var wood = Piece(26, 24, 2, 2, VolumeKind.Area, TerrainDeclarations.Difficult);                     // x 25..27, y 23..25
        var sim = Moving(GfNames.RushSeq, GfNames.Rush, [wood], out var unit);
        var a = unit.Models[0].Id;
        Assert.Equal(GfNames.DifficultCapRule, Breach(sim, At(sim, a, 31.9, 24)));                           // round it 12.43": only through it, capped at 6"
        Assert.IsType<Applied>(Move(sim, At(sim, a, 31, 24)));                                                // straight through 11", round it 11.59"
        sim.Run();
        Assert.Equal(31, ((OnTable)sim.Model(a).Location).Placement.Position.X, 6);
    }

    [Fact]
    public void A_move_goes_round_an_impassable_block_to_reach_behind_it()
    {
        var block = Piece(24, 24, 2, 4, VolumeKind.Solid, TerrainDeclarations.Impassable);                    // x 23..25, y 22..26
        var advance = Moving(GfNames.AdvanceSeq, GfNames.Advance, [block], out var walker);
        Assert.Equal(GfNames.MoveImpassableRule, Breach(advance, At(advance, walker.Models[0].Id, 27, 24)));  // round it 9.42", over 6"
        var sim = Moving(GfNames.RushSeq, GfNames.Rush, [block], out var unit);
        var a = unit.Models[0].Id;
        Assert.IsType<Applied>(Move(sim, At(sim, a, 27, 24)));
        sim.Run();
        Assert.Equal(27, ((OnTable)sim.Model(a).Location).Placement.Position.X, 6);
    }

    [Fact]
    public void A_path_round_dangerous_terrain_owes_no_test_and_one_through_it_does()
    {
        var pit = Pit(23, 24, 1);
        var rush = Moving(GfNames.RushSeq, GfNames.Rush, [pit], out var runner);
        Assert.IsType<Applied>(Move(rush, At(rush, runner.Models[0].Id, 26, 24)));                          // round it 6.91", within 12"
        rush.Run();
        Assert.Equal(0, rush.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.DangerousTerrain));
        var advance = Moving(GfNames.AdvanceSeq, GfNames.Advance, [pit], out var walker);
        Assert.IsType<Applied>(Move(advance, At(advance, walker.Models[0].Id, 26, 24)));                     // round it is over 6": through it
        advance.Run();
        Assert.Equal(1, advance.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.DangerousTerrain));
    }

    [Fact]
    public void A_charger_reaches_a_target_round_a_block_and_charges_round_it()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var block = Piece(24, 24, 2, 4, VolumeKind.Solid, TerrainDeclarations.Impassable);
        var sim = Sim.Gf(State([charger], [target], enforcement: Enforcement.Strict, terrain: [block]), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24);
        Assert.Contains(target.Id, GfMelee.ChargeTargets(sim.State, TestProperties.Core, charger.Id));
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.IsType<Applied>(Move(sim, At(sim, charger.Models[0].Id, 26.74, 24)));                         // into contact behind the block
    }

    [Fact]
    public void A_path_goes_round_another_model_and_one_that_only_fits_through_it_is_refused()
    {
        Sim With(string sequence, string action, out Guid mover)
        {
            var unit = Unit("A", "4+", "4+", [], 1, []);
            var other = Unit("L", "4+", "4+", [], 1, []);
            var sim = Sim.Gf(State([unit, other], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict), 4);
            Place(sim, unit.Models[0].Id, 20, 24); Place(sim, other.Models[0].Id, 23, 24);
            sim.Start(sequence, new SequenceContext(unit.Id, [], [], action));
            mover = unit.Models[0].Id;
            return sim;
        }
        var rush = With(GfNames.RushSeq, GfNames.Rush, out var runner);
        Assert.Null(Breach(rush, At(rush, runner, 26, 24)));                                                  // round it, within 12"
        var advance = With(GfNames.AdvanceSeq, GfNames.Advance, out var walker);
        Assert.Equal(GfNames.MoveThroughModelsRule, Breach(advance, At(advance, walker, 26, 24)));            // 6" straight through it, more round it
    }
}
