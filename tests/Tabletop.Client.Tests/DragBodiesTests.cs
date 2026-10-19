using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
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
    public void A_drag_and_its_region_pass_above_a_lower_model()
    {
        var roof = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(4, 20), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.None, null),
            new TerrainTransform(new Point3(10, 10, 0), new Yaw(0), 1));                                    // x 8..12, y 0..20, top 4
        var big = new PlacedModel(new ModelBody(Base.Circle(3), 4.5), new Placement(new Point3(11, 3, 4), new Yaw(0)));   // on the roof, edge out to x 12.5
        PlacedModel[] below = [M(12.8, 10)];                                                                   // on the ground at the roof's foot
        Assert.Equal(new Point3(11, 13, 4), Drag(big, new(11, 13), below, [roof]));
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = big }, new MoveRegion([A], 10, [], [], null), [roof], new TableSize(72, 48), below);
        Assert.True(Areas.Inside(regions.Outline(A), new Point2(11, 12.9)));
    }

    [Fact]
    public void A_terrain_piece_rests_on_a_hill_and_stops_at_a_building()
    {
        var hill = Piece(20, VolumeKind.Solid, Footprint.Rect(6, 6), 2, TerrainDeclarations.None);
        var building = Piece(40, VolumeKind.Solid, Footprint.Rect(6, 6), 8, TerrainDeclarations.Impassable);
        var wood = Piece(2, VolumeKind.Area, Footprint.Circle(3), 6, TerrainDeclarations.None);
        Assert.Equal(new Point3(20, 30, 2), DragBodies.Piece([hill, building, wood], wood.Id, At(wood), new(20, 30), free: false));
        Assert.True(DragBodies.Piece([hill, building, wood], wood.Id, At(wood), new(40, 30), free: false).X <= 34 + 1e-3);
    }

    static Placement At(TerrainPlacement t, double yaw = 0) => new(t.Position, new Yaw(yaw));

    [Fact]
    public void A_held_piece_slides_from_where_it_is_held_and_jumps_to_a_free_cursor()
    {
        var building = Piece(40, VolumeKind.Solid, Footprint.Rect(6, 6), 8, TerrainDeclarations.Impassable);    // x 37..43, y 27..33
        var wall = Piece(30, VolumeKind.Solid, Footprint.Rect(1, 4), 2, TerrainDeclarations.None);             // x 29.5..30.5, y 28..32
        // Held against the building's left side, the cursor moves up along it: the wall follows, never stuck at the contact.
        var held = new Placement(new Point3(36.4, 30, 0), new Yaw(0));
        var up = DragBodies.Piece([building, wall], wall.Id, held, new(38, 32), free: false);
        Assert.Equal(32, up.Y, 3);
        Assert.True(up.X <= 36.5 + 1e-3, $"at {up}");
        // The cursor past the building: the spot is free, the piece goes there.
        Assert.Equal(new Point3(50, 30, 0), DragBodies.Piece([building, wall], wall.Id, held, new(50, 30), free: false));
    }

    [Fact]
    public void A_turn_into_another_piece_moves_the_turned_piece_clear()
    {
        var building = Piece(40, VolumeKind.Solid, Footprint.Rect(6, 6), 8, TerrainDeclarations.Impassable);    // x 37..43
        var wall = Piece(35.5, VolumeKind.Solid, Footprint.Rect(1, 6), 2, TerrainDeclarations.None);           // x 35..36: turned 90° it spans x 32.5..38.5
        var at = DragBodies.Turn([building, wall], wall.Id, new Yaw(90));
        Assert.True(at.X <= 34 + 1e-3, $"at {at}");
        Assert.Equal(new Point3(35.5, 30, 0), DragBodies.Turn([building, wall], wall.Id, new Yaw(0)));
        var held = DragBodies.Piece([building, wall], wall.Id, At(wall, 90), new(35.5, 30), free: false);
        Assert.True(held.X <= 34 + 1e-3, $"held at {held}");                                                       // a turn in hand clears the same way
    }

    [Fact]
    public void The_drawn_region_goes_around_models_unless_the_unit_passes_them()
    {
        var region = new MoveRegion([A], 6, [], [], null);
        MoveRegions Of(MoveRegion legal) => new(new Dictionary<Guid, PlacedModel> { [A] = M(10, 10) }, legal, [], new TableSize(72, 48), [M(13, 10)]);
        Assert.False(Areas.Inside(Of(region).Outline(A), new Point2(13, 10)));
        Assert.True(Areas.Inside(Of(region).Outline(A), new Point2(15, 10)));                                       // behind it, round it
        Assert.True(Areas.Inside(Of(region with { PassesModels = true }).Outline(A), new Point2(13, 10)));
    }

    static readonly Unit Squad = new(Guid.CreateVersion7(), 1, "Squad", new Dictionary<string, string>(), [],
        Enumerable.Range(1, 3).Select(i => new ModelSlot(Guid.CreateVersion7(), i, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, [])).ToList());
    static Guid S(int i) => Squad.Models[i].Id;
    /// <summary>The squad's first two models on the table at (10, 10) and (20, 10); the third on the shelf.</summary>
    static Projection Squads()
    {
        var pa = Guid.CreateVersion7(); var team = Guid.CreateVersion7();
        var game = new GameInfo(Guid.CreateVersion7(), "Friday", GameStatus.Playing, pa, "grimdark-future", "sha256:abc", SchedulerShape.AlternatingActivation,
            Enforcement.Strict, false, 1, [new Team(team, 1)], [new Participant(pa, Guid.CreateVersion7(), "Clay", team, null)]);
        var a = new EventApplier(new Board(TableStateTests.Sample(), [new ArmyInPlay(pa, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Squad", "grimdark-future", "test", [Squad]), Guid.CreateVersion7()))]), game);
        ModelRelocation Put(int i, double x) => new(S(i), new OnShelf(), new OnTable(new Placement(new Point3(x, 10, 0), new Yaw(0))));
        a.ApplyAll([new GameEvent(1, pa, new ModelsMoved([Put(0, 10), Put(1, 20)]), null)]);
        return a.Projection;
    }

    [Fact]
    public void The_stationary_bodies_leave_out_the_held_and_the_off_table_models()
    {
        var p = Squads();
        Assert.Equal([new Point3(20, 10, 0)], DragBodies.Stationary(p, [S(0)]).Select(b => b.Placement.Position));
    }

    [Fact]
    public void A_proposed_model_collides_where_its_ghost_stands()
    {
        var p = Squads();
        var ghosts = new Dictionary<Guid, Placement> { [S(1)] = new(new Point3(30, 12, 0), new Yaw(0)), [S(2)] = new(new Point3(40, 12, 0), new Yaw(0)) };
        Assert.Equal([new Point3(30, 12, 0), new Point3(40, 12, 0)],
            DragBodies.Stationary(p, [S(0)], ghosts).Select(b => b.Placement.Position).OrderBy(q => q.X));      // a disembark ghost, off the table, is still a body
    }

    [Fact]
    public void Each_held_model_gets_its_own_end()
    {
        Guid x = Guid.CreateVersion7(), y = Guid.CreateVersion7();
        var ends = DragBodies.Models([new HeldBody(x, M(0, 0), new(5, 0)), new HeldBody(y, M(0, 10), new(5, 10))], [M(4, 10)], [], null, free: false);
        Assert.Equal(new Point3(5, 0, 0), ends[x]);
        Assert.True(ends[y].X < 4 - 1.25);                                               // y alone meets the model at (4, 10)
    }

    [Fact]
    public void A_re_grabbed_proposed_model_starts_where_its_ghost_is_drawn_not_where_the_projection_has_it()
    {
        Guid proposed = Guid.CreateVersion7(), undrawn = Guid.CreateVersion7(), other = Guid.CreateVersion7();
        var projected = new Placement(new Point3(0, 0, 0), new Yaw(0));
        var ghost = new Placement(new Point3(5, 3, 0), new Yaw(45));
        var grabs = DragBodies.Grabs([proposed, undrawn, other], new HashSet<Guid> { proposed, undrawn },
            id => id == undrawn ? null : ghost, _ => projected);
        Assert.Equal(ghost, grabs[proposed]);
        Assert.Equal(projected, grabs[undrawn]);
        Assert.Equal(projected, grabs[other]);                                          // a hand move starts from the projection
    }
}
