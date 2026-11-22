using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>Which piece the pointer picks: a model under it, else a model within the slop of it, else the nearest other piece hit.</summary>
public class TablePickTests
{
    [Fact]
    public void A_model_hit_beats_terrain_hit_first()
    {
        Assert.Equal(1, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Model, 14, 0)], slop: 12));
    }

    [Fact]
    public void A_model_the_pointer_just_misses_beats_the_terrain_it_is_over()
    {
        Assert.Equal(2, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Model, 14, 20), new(HitKind.Model, 15, 6)], slop: 12));
        Assert.Equal(0, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Model, 14, 20)], slop: 12));   // too far: the terrain
    }

    [Fact]
    public void Among_models_one_under_the_pointer_beats_one_beside_it_and_the_nearer_hit_wins()
    {
        Assert.Equal(1, TablePick.Best([new(HitKind.Model, 10, 3), new(HitKind.Model, 20, 0)], slop: 12));
        Assert.Equal(1, TablePick.Best([new(HitKind.Model, 20, 0), new(HitKind.Model, 12, 0)], slop: 12));
    }

    [Fact]
    public void Without_a_model_the_nearest_other_hit_wins_and_a_miss_is_none()
    {
        Assert.Equal(1, TablePick.Best([new(HitKind.Other, 20, 0), new(HitKind.Other, 12, 0), new(HitKind.Other, 5, 4)], slop: 12));
        Assert.Null(TablePick.Best([new(HitKind.Other, 5, 4)], slop: 12));
        Assert.Null(TablePick.Best([], slop: 12));
    }

    [Fact]
    public void An_unlocked_marker_under_terrain_beats_the_terrain_and_a_locked_one_does_not()
    {
        // The terrain box is entered first (along 10); the marker's disc lies under it (along 14).
        Assert.Equal(1, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Marker, 14, 0)], slop: 12));
        Assert.Equal(0, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Other, 14, 0)], slop: 12));   // locked: an ordinary piece
    }

    [Fact]
    public void A_marker_beside_terrain_leaves_the_terrain_under_the_pointer_picked()
    {
        Assert.Equal(0, TablePick.Best([new(HitKind.Other, 10, 0)], slop: 12));
        Assert.Equal(0, TablePick.Best([new(HitKind.Marker, 14, 0)], slop: 12));
    }

    [Fact]
    public void A_model_over_a_marker_and_terrain_beats_both()
    {
        Assert.Equal(2, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Marker, 14, 0), new(HitKind.Model, 12, 0)], slop: 12));
        Assert.Equal(2, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Marker, 14, 0), new(HitKind.Model, 12, 5)], slop: 12));
    }

    [Fact]
    public void The_disc_holds_the_point_where_the_ray_meets_the_marker_plane()
    {
        var down = new Point3(0, 0, -1);
        Assert.Equal(9.9, TablePick.OnDisc(new Point3(10, 10, 10), down, new Point3(11, 10, 0.1), radius: 1.5)!.Value, 6);
        Assert.Null(TablePick.OnDisc(new Point3(10, 10, 10), down, new Point3(12, 10, 0.1), radius: 1.5));   // beside the disc
        Assert.Null(TablePick.OnDisc(new Point3(10, 10, 10), new Point3(0, 0, 1), new Point3(10, 10, 0.1), radius: 1.5));   // away from it
        var slant = new Point3(1, 0, -1);   // reaches z 0 at x 20, along √200
        Assert.Equal(Math.Sqrt(200), TablePick.OnDisc(new Point3(10, 10, 10), slant, new Point3(20, 10, 0), radius: 0.5)!.Value, 6);
    }

    static WorldVolume Prism(Footprint f, double x, double y, double yaw, double top, double scale = 1) =>
        new(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, f, new Point2(0, 0), new Yaw(0), 0, top, TerrainDeclarations.None, null),
            new TerrainTransform(new Point3(x, y, 0), new Yaw(yaw), scale));

    [Fact]
    public void An_oblique_ray_over_a_low_piece_picks_it_and_not_the_tall_turned_piece_whose_box_it_enters_first()
    {
        // From (0, 0, 20) toward (10, 10, 0): the ray passes through the tall diamond's box (x 6.17..8.83, under 10" high) but never
        // meets the diamond, then comes down onto the low square under the pointer, through its top at (9.5, 9.5, 1).
        var from = new Point3(0, 0, 20); var dir = new Point3(10, 10, -20);
        var tall = Prism(Footprint.Rect(4, 4), 6, 9, 45, top: 10);
        var low = Prism(Footprint.Rect(4, 4), 10, 10, 0, top: 1);
        Assert.Null(TablePick.EnterPrism(from, dir, tall));
        Assert.Equal(Math.Sqrt(9.5 * 9.5 * 2 + 19 * 19), TablePick.EnterPrism(from, dir, low)!.Value, 6);
    }

    [Fact]
    public void The_prism_entry_is_the_nearest_face_the_ray_meets()
    {
        var down = new Point3(0, 0, -1);
        Assert.Equal(7, TablePick.EnterPrism(new Point3(10, 10, 10), down, Prism(Footprint.Rect(4, 2), 11.5, 10, 0, top: 3))!.Value, 6);
        Assert.Null(TablePick.EnterPrism(new Point3(10, 10, 10), down, Prism(Footprint.Rect(4, 2), 10, 11.5, 0, top: 3)));   // 1" deep in y
        Assert.NotNull(TablePick.EnterPrism(new Point3(10, 10, 10), down, Prism(Footprint.Rect(4, 2), 10, 11.5, 90, top: 3)));   // turned: 2" deep
        Assert.Equal(4, TablePick.EnterPrism(new Point3(10, 10, 10), down, Prism(Footprint.Rect(2, 2), 10, 10, 0, top: 3, scale: 2))!.Value, 6);
        // A circle from the side: its curved wall 1" short of the centre; a ray 2" off misses; a start inside enters at once.
        Assert.Equal(9, TablePick.EnterPrism(new Point3(0, 10, 1), new Point3(1, 0, 0), Prism(Footprint.Circle(1), 10, 10, 0, top: 3))!.Value, 6);
        Assert.Null(TablePick.EnterPrism(new Point3(0, 12, 1), new Point3(1, 0, 0), Prism(Footprint.Circle(1), 10, 10, 0, top: 3)));
        Assert.Equal(0, TablePick.EnterPrism(new Point3(10, 10, 1), new Point3(1, 0, 0), Prism(Footprint.Circle(1), 10, 10, 0, top: 3))!.Value, 6);
    }
}
