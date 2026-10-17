using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class MoveRulerTests
{
    static PlacedModel At(double x, double y) => new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(x, y, 0), new Yaw(0)));
    static WorldVolume Forest(double x, double y) => new(Guid.CreateVersion7(),
        new Volume(VolumeKind.Area, Footprint.Circle(1), new Point2(0, 0), new Yaw(0), 0, 8, TerrainDeclarations.Difficult, null), new TerrainTransform(new Point3(x, y, 0), new Yaw(0), 1));
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7();
    static IReadOnlyList<Point2> Open(Point2 origin, double allowance) => MoveRuler.Reach(At(origin.X, origin.Y), new MoveRegion([A], allowance, [], [], null), []);

    [Fact]
    public void Clamp_pulls_a_target_past_the_allowance_back_along_the_line_from_the_origin()
    {
        Assert.Equal(6, MoveRuler.Clamp(new Point2(0, 0), Open(new Point2(0, 0), 6), new Point2(10, 0)).X, 6);
        var c = MoveRuler.Clamp(new Point2(1, 1), Open(new Point2(1, 1), 5), new Point2(7, 9));
        Assert.Equal(5, new Point2(1, 1).DistanceTo(c), 6);
        Assert.Equal(4, c.X, 6); Assert.Equal(5, c.Y, 6);
    }

    [Fact]
    public void Clamp_leaves_a_target_within_the_allowance()
    {
        Assert.Equal(new Point2(3, 4), MoveRuler.Clamp(new Point2(0, 0), Open(new Point2(0, 0), 6), new Point2(3, 4)));
        Assert.Equal(new Point2(0, 0), MoveRuler.Clamp(new Point2(0, 0), Open(new Point2(0, 0), 0), new Point2(0, 0)));
    }

    [Fact]
    public void Clamp_holds_a_move_into_difficult_terrain_to_the_cap_and_leaves_the_rest_at_the_allowance()
    {
        // A forest 3" east: a straight move that touches it may go only the 6" cap (p.11); westward is open to the full 12".
        var forest = Forest(39, 24);
        var reach = MoveRuler.Reach(At(36, 24), new MoveRegion([A], 12, [], [forest.Id], 6), [forest]);
        Assert.Equal(42, MoveRuler.Clamp(new Point2(36, 24), reach, new Point2(50, 24)).X, 6);
        Assert.Equal(24, MoveRuler.Clamp(new Point2(36, 24), reach, new Point2(10, 24)).X, 6);
        Assert.Equal(new Point2(40, 24), MoveRuler.Clamp(new Point2(36, 24), reach, new Point2(40, 24)));   // inside the cap: untouched
    }

    [Fact]
    public void Clamp_ignores_walls_and_other_models_which_the_drag_slides_along()
    {
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Circle(1), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(39, 24, 0), new Yaw(0), 1));
        var reach = MoveRuler.Reach(At(36, 24), new MoveRegion([A], 6, [wall.Id], [], null), [wall]);
        Assert.Equal(42, MoveRuler.Clamp(new Point2(36, 24), reach, new Point2(50, 24)).X, 6);
    }

    [Fact]
    public void Each_model_has_its_own_region_from_where_it_stands()
    {
        var forest = Forest(39, 24);
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = At(36, 24), [B] = At(36, 10) }, new MoveRegion([A, B], 12, [], [forest.Id], 6),
            [forest], new TableSize(72, 48), []);
        Assert.Equal(42, regions.Outline(A)[0].X, 6);                                    // A's eastward ray enters the forest
        Assert.Equal(48, regions.Outline(B)[0].X, 6);                                    // B's passes south of it
        Assert.Equal(42, regions.Clamp(A, new Point2(50, 24)).X, 6);
        Assert.Equal(48, regions.Clamp(B, new Point2(50, 10)).X, 6);
        Assert.Equal(new Point2(50, 10), regions.Clamp(Guid.CreateVersion7(), new Point2(50, 10)));   // a model the Ask does not move is not held
        Assert.Same(regions.Outline(A), regions.Outline(A));                             // computed once per model
        Assert.Null(regions.Inner(A));
    }

    [Fact]
    public void A_minimum_cuts_each_models_inner_edge()
    {
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = At(36, 24) }, new MoveRegion([A], 36, [], [], null, MinDistance: 30), [], new TableSize(72, 48), []);
        Assert.Equal(30, regions.Minimum(A));
        Assert.All(regions.Inner(A)!, p => Assert.True(p.DistanceTo(new Point2(36, 24)) <= 30 + 1e-6));
    }

    [Fact]
    public void Gate_allows_a_commit_with_every_model_in_range()
    {
        var gate = MoveRuler.Gate([MoveRuler.Read(new Point2(0, 0), new Point2(3, 4), 6)], Enforcement.Strict);
        Assert.Equal(new MoveGate(true, null), gate);
    }

    [Fact]
    public void Gate_names_how_many_models_are_past_their_allowance_and_strict_disables_the_commit()
    {
        var over = MoveRuler.Read(new Point2(0, 0), new Point2(8, 0), 6);
        var fine = MoveRuler.Read(new Point2(0, 0), new Point2(2, 0), 6);
        Assert.Equal(new MoveGate(false, "2 models past their allowance."), MoveRuler.Gate([over, fine, over], Enforcement.Strict));
        Assert.Equal(new MoveGate(false, "1 model past its allowance."), MoveRuler.Gate([over], Enforcement.Strict));
    }

    [Fact]
    public void Gate_under_warn_or_silent_shows_the_reason_and_allows_the_commit()
    {
        var over = MoveRuler.Read(new Point2(0, 0), new Point2(8, 0), 6);
        Assert.Equal(new MoveGate(true, "1 model past its allowance."), MoveRuler.Gate([over], Enforcement.Warn));
        Assert.Equal(new MoveGate(true, "1 model past its allowance."), MoveRuler.Gate([over], Enforcement.Silent));
    }

    [Fact]
    public void Gate_counts_a_model_short_of_its_minimum()
    {
        var shortOf = MoveRuler.Read(new Point2(0, 0), new Point2(20, 0), 36, 30);
        var over = MoveRuler.Read(new Point2(0, 0), new Point2(40, 0), 36, 30);
        Assert.Equal(new MoveGate(false, "1 model past its allowance · 2 models short of their minimum."), MoveRuler.Gate([over, shortOf, shortOf], Enforcement.Strict));
    }
}
