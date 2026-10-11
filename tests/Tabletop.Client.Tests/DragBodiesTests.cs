using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class DragBodiesTests
{
    static readonly Guid A = Guid.CreateVersion7();
    static PlacedModel M(double x, double y) => new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(x, y, 0), new Yaw(0)));
    static WorldVolume Wall(TerrainDeclarations d, double top = 6) => new(Guid.CreateVersion7(),
        new Volume(VolumeKind.Solid, Footprint.Rect(0.5, 20), new Point2(0, 0), new Yaw(0), 0, top, d, null), new TerrainTransform(new Point3(10, 10, 0), new Yaw(0), 1));
    static TerrainPlacement Piece(double x, VolumeKind kind, Footprint f, double top, TerrainDeclarations d) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"),
        new Point3(x, 30, 0), new Yaw(0), 1, [new TerrainVolume(Guid.CreateVersion7(), new Volume(kind, f, new Point2(0, 0), new Yaw(0), 0, top, d, null))]);
    static Point3 Drag(PlacedModel from, Point2 to, IReadOnlyList<PlacedModel> others, IReadOnlyList<WorldVolume> volumes, MoveRegion? region = null, bool free = false) =>
        DragBodies.Models([new HeldBody(A, from, to)], others, volumes, region, free)[A];

    [Fact]
    public void A_drag_stops_at_an_impassable_wall_and_climbs_anything_else()
    {
        Assert.Equal(9.12, Drag(M(8, 10), new(12, 10), [], [Wall(TerrainDeclarations.Impassable)]).X, 2);
        Assert.Equal(new Point3(10, 10, 2), Drag(M(8, 10), new(10, 10), [], [Wall(TerrainDeclarations.None, 2)]));
    }

    [Fact] public void Alt_places_freely_and_still_rests() =>
        Assert.Equal(new Point3(10, 10, 2), Drag(M(8, 10), new(10, 10), [M(9, 10)], [Wall(TerrainDeclarations.Impassable, 2)], free: true));

    [Fact]
    public void A_move_ask_names_the_walls_and_a_flyer_passes_models()
    {
        var wall = Wall(TerrainDeclarations.None);
        var region = new MoveRegion([A], 6, [wall.Id], [], null);
        Assert.Equal(9.12, Drag(M(8, 10), new(12, 10), [], [wall], region).X, 2);
        Assert.True(Drag(M(8, 4), new(12, 4), [M(10, 4)], [], region).X < 10 - 1.25);
        Assert.Equal(12, Drag(M(8, 4), new(12, 4), [M(10, 4)], [], region with { PassesModels = true }).X, 6);
    }

    [Fact]
    public void A_terrain_piece_rests_on_a_hill_and_stops_at_a_building()
    {
        var hill = Piece(20, VolumeKind.Solid, Footprint.Rect(6, 6), 2, TerrainDeclarations.None);
        var building = Piece(40, VolumeKind.Solid, Footprint.Rect(6, 6), 8, TerrainDeclarations.Impassable);
        var wood = Piece(2, VolumeKind.Area, Footprint.Circle(3), 6, TerrainDeclarations.None);
        Assert.Equal(new Point3(20, 30, 2), DragBodies.Piece([hill, building, wood], wood.Id, new(20, 30), free: false));
        Assert.True(DragBodies.Piece([hill, building, wood], wood.Id, new(40, 30), free: false).X <= 34 + 1e-3);
    }

    [Fact]
    public void The_drawn_region_goes_around_models_unless_the_unit_passes_them()
    {
        var region = new MoveRegion([A], 6, [], [], null);
        Assert.True(MoveRuler.Region(M(10, 10), region, [], new TableSize(72, 48), [M(13, 10)])[0].X <= 11.74 + 1e-6);
        Assert.Equal(16, MoveRuler.Region(M(10, 10), region with { PassesModels = true }, [], new TableSize(72, 48), [M(13, 10)])[0].X, 6);
    }
}
