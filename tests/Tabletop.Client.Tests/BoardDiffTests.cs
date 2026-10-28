using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class BoardDiffTests
{
    static readonly Volume Box = new(VolumeKind.Solid, Footprint.Rect(4, 2), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Impassable, null);
    static TerrainPlacement Terrain(Guid id, double x = 10, params Volume[] volumes) =>
        new(id, VisualRef.OfBuiltin("ruin"), new Point3(x, 10, 0), new Yaw(0), 1, (volumes.Length == 0 ? [Box] : volumes).Select((v, i) => new TerrainVolume(new Guid(i + 1, 0, 0, new byte[8]), v)).ToList());
    static Marker Objective(Guid id, double x = 5, string? label = null) => new(id, MarkerKind.Objective, new Point3(x, 5, 0), new Yaw(0), 3, label, VisualRef.OfBuiltin("objective-ring"));

    static BoardDiff<TerrainPlacement> Diff(IEnumerable<TerrainPlacement> drawn, IEnumerable<TerrainPlacement> wanted) =>
        BoardDiff.Of(drawn.ToDictionary(t => t.Id), wanted, t => t.Id, BoardDiff.SameShape);

    [Fact]
    public void The_same_board_is_all_kept()
    {
        var a = Terrain(Guid.NewGuid()); var b = Terrain(Guid.NewGuid(), 30);
        var d = Diff([a, b], [a with { Volumes = a.Volumes.ToList() }, b]);   // a fresh list of equal volumes is the same shape
        Assert.Empty(d.Added); Assert.Empty(d.Removed); Assert.Empty(d.Reshaped);
        Assert.Equal([a.Id, b.Id], d.Kept.Select(t => t.Id));
    }

    [Fact]
    public void New_ids_are_added_and_missing_ids_removed()
    {
        var a = Terrain(Guid.NewGuid()); var b = Terrain(Guid.NewGuid()); var c = Terrain(Guid.NewGuid());
        var d = Diff([a, b], [b, c]);
        Assert.Equal([c.Id], d.Added.Select(t => t.Id));
        Assert.Equal([a.Id], d.Removed);
        Assert.Equal([b.Id], d.Kept.Select(t => t.Id));
    }

    [Fact]
    public void A_move_keeps_the_node_and_a_volume_change_rebuilds_it()
    {
        var a = Terrain(Guid.NewGuid()); var b = Terrain(Guid.NewGuid());
        var moved = a with { Position = new Point3(40, 20, 0), Yaw = new Yaw(90), Scale = 2 };
        var reshaped = Terrain(b.Id, 10, Box with { Top = 6 });
        var d = Diff([a, b], [moved, reshaped]);
        Assert.Equal([moved], d.Kept);
        Assert.Equal([reshaped], d.Reshaped);
        Assert.Empty(d.Added); Assert.Empty(d.Removed);
    }

    [Fact]
    public void A_mesh_swap_is_a_reshape()
    {
        var a = Terrain(Guid.NewGuid());
        Assert.Single(Diff([a], [a with { Mesh = VisualRef.OfBuiltin("hill") }]).Reshaped);
    }

    [Fact]
    public void A_marker_moves_without_rebuilding_and_a_relabel_rebuilds()
    {
        var id = Guid.NewGuid(); var m = Objective(id);
        Assert.True(BoardDiff.SameShape(m, Objective(id, 20) with { Yaw = new Yaw(45) }));
        Assert.False(BoardDiff.SameShape(m, Objective(id, label: "A")));
        Assert.False(BoardDiff.SameShape(m, m with { Diameter = 5 }));
    }

    [Fact]
    public void The_shape_test_is_the_callers()
    {
        var id = Guid.NewGuid();
        var drawn = new Dictionary<Guid, (Guid Id, int Colour)> { [id] = (id, 1) };
        var d = BoardDiff.Of(drawn, [(Id: id, Colour: 2)], x => x.Id, (x, y) => x.Colour == y.Colour);
        Assert.Equal([(id, 2)], d.Reshaped);
    }

    [Fact]
    public void An_empty_board_removes_everything()
    {
        var a = Terrain(Guid.NewGuid());
        var d = Diff([a], []);
        Assert.Equal([a.Id], d.Removed); Assert.Empty(d.Kept);
    }

    [Fact]
    public void A_duplicate_wanted_id_throws()
    {
        var a = Terrain(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => Diff([], [a, a]));
    }
}
