using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class HandMovesTests
{
    static readonly Guid M0 = TableUx.ModelOf(TableUx.Squad), M1 = TableUx.ModelOf(TableUx.Squad, 1);
    static Projection P => TableUx.After(TableUx.Deploy);
    static readonly Point3 Landing = new(20, 20, 0);

    static HeldDrop Drop(Guid piece, params Guid[] models)
    {
        var grabs = models.ToDictionary(id => id, id => new Placement(new Point3(1, 2, 0), new Yaw(10)));
        return new HeldDrop(piece, models, grabs, models.ToDictionary(id => id, _ => Landing), models.ToDictionary(id => id, _ => new Yaw(90)));
    }
    static HeldDrop PieceDrop(Guid piece) => new(piece, [piece], new Dictionary<Guid, Placement> { [piece] = new(new Point3(1, 2, 0), new Yaw(10)) },
        new Dictionary<Guid, Point3> { [piece] = Landing }, new Dictionary<Guid, Yaw> { [piece] = new(90) });

    [Fact]
    public void A_hand_move_goes_from_each_grab_to_its_landing()
    {
        var mm = HandMoves.Models(Drop(M1, M1, M0));
        Assert.Equal([M1, M0], mm.Moves.Select(m => m.ModelId));
        Assert.All(mm.Moves, m =>
        {
            Assert.Equal(new OnTable(new Placement(new Point3(1, 2, 0), new Yaw(10))), m.From);
            Assert.Equal(new OnTable(new Placement(Landing, new Yaw(90))), m.To);
        });
    }

    [Fact]
    public void Drop_of_models_the_projection_holds_is_a_move_models()
        => Assert.IsType<MoveModels>(HandMoves.Drop(GameStatus.Playing, P, null, Drop(M0, M0, M1))!.Command);

    [Fact]
    public void Drop_of_a_draft_terrain_piece_in_setup_is_move_terrain()
    {
        var draft = TableUx.Board.Table; var t = draft.Terrain[0].Id;
        var call = HandMoves.Drop(GameStatus.Setup, null, draft, PieceDrop(t))!;
        Assert.Null(call.Command);
        Assert.Equal(new MoveTerrain(t, Landing, new Yaw(90)), call.Edit);
    }

    [Fact]
    public void Drop_of_a_draft_marker_in_setup_is_move_setup_marker()
    {
        var draft = TableUx.Board.Table; var id = draft.Markers[0].Id;
        Assert.Equal(new MoveSetupMarker(id, Landing, new Yaw(90)), HandMoves.Drop(GameStatus.Setup, null, draft, PieceDrop(id))!.Edit);
    }

    [Fact]
    public void Drop_of_a_table_marker_in_play_is_move_marker()
    {
        var id = TableUx.Board.Table.Markers[0].Id;
        var call = HandMoves.Drop(GameStatus.Playing, P, null, PieceDrop(id))!;
        Assert.Null(call.Edit);
        Assert.Equal(new MoveMarker(id, Landing, new Yaw(90)), call.Command);
    }

    [Fact]
    public void Drop_of_nothing_known_is_null()
        => Assert.Null(HandMoves.Drop(GameStatus.Playing, P, null, PieceDrop(Guid.CreateVersion7())));
}
