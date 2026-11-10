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
    static WorldVolume Wood(double x, double y, double radius) => new(Guid.CreateVersion7(),
        new Volume(VolumeKind.Area, Footprint.Circle(radius), new Point2(0, 0), new Yaw(0), 0, 8, TerrainDeclarations.Difficult, null), new TerrainTransform(new Point3(x, y, 0), new Yaw(0), 1));
    static readonly TableSize Opr = new(72, 48);
    static MoveRegions One(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, IReadOnlyList<PlacedModel>? others = null) =>
        new(new Dictionary<Guid, PlacedModel> { [A] = start }, legal, volumes, Opr, others ?? []);

    [Fact]
    public void Clamp_pulls_a_target_past_the_allowance_back_to_the_nearest_point_in_reach()
    {
        var regions = One(At(36, 24), new MoveRegion([A], 6, [], [], null), []);
        Assert.Equal(42, regions.Clamp(A, new Point2(50, 24)).X, 6);
        var c = regions.Clamp(A, new Point2(42, 32));
        Assert.Equal(6, new Point2(36, 24).DistanceTo(c), 6);
        Assert.Equal(new Point2(39, 28), regions.Clamp(A, new Point2(39, 28)));                     // within it: untouched
    }

    [Fact]
    public void A_drag_round_a_small_wood_is_not_capped_and_one_into_a_big_wood_is()
    {
        // p.11 caps a unit that "moves in or through difficult terrain"; a path round the wood never enters it.
        var small = Forest(39, 24);
        var round = One(At(36, 24), new MoveRegion([A], 12, [], [small.Id], 6), [small]);
        Assert.Equal(new Point2(47, 24), round.Clamp(A, new Point2(47, 24)));                       // 11" straight through it, a little more round it
        Assert.False(round.Route(A, new Point2(47, 24))!.EntersDifficult);
        var big = Wood(44, 24, 4);                                                                 // x 40..48
        var regions = One(At(36, 24), new MoveRegion([A], 12, [], [big.Id], 6), [big]);
        Assert.Equal(new Point2(41, 24), regions.Clamp(A, new Point2(41, 24)));                     // in it, under 6"
        Assert.True(regions.Route(A, new Point2(41, 24))!.EntersDifficult);
        Assert.True(regions.Route(A, regions.Clamp(A, new Point2(44, 24)))!.Distance <= 6 + 1e-3);   // deep in it: held to 6"
        Assert.True(regions.Route(A, regions.Clamp(A, new Point2(44, 40)))!.Distance <= 12 + 1e-3);
    }

    [Fact]
    public void Clamp_goes_round_walls()
    {
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Circle(1), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(39, 24, 0), new Yaw(0), 1));
        var regions = One(At(36, 24), new MoveRegion([A], 6, [wall.Id], [], null), [wall]);
        var held = regions.Clamp(A, new Point2(50, 24));
        Assert.True(held.X < 42);
        Assert.True(regions.Route(A, held)!.Distance <= 6 + 1e-3);
        Assert.True(regions.Route(A, held)!.Points.Count > 2);                                     // the ruler bends round it
    }

    [Fact]
    public void Reaches_is_whether_clamp_leaves_the_target_alone()
    {
        var regions = One(At(36, 24), new MoveRegion([A], 6, [], [], null), []);
        Assert.True(regions.Reaches(A, new Point2(40, 24)));
        Assert.False(regions.Reaches(A, new Point2(50, 24)));
        Assert.False(regions.Reaches(A, new Point2(40, 24), cap: 3));
        Assert.True(regions.Reaches(Guid.CreateVersion7(), new Point2(70, 40)));                       // a model the Ask does not move is not held
    }

    [Fact]
    public async Task Outlines_computed_on_other_threads_agree_with_the_main_threads_answers()
    {
        // The decision box draws a pickup's areas off the main thread while the drag clamps and routes on it (performance spec → The Godot client).
        var big = Wood(44, 24, 4);
        MoveRegions Make() => new(new Dictionary<Guid, PlacedModel> { [A] = At(36, 24), [B] = At(36, 10) }, new MoveRegion([A, B], 12, [], [big.Id], 6), [big], Opr, []);
        var alone = Make();
        var (a, b, clamp) = (alone.Bands(A).Clear, alone.Bands(B).Clear, alone.Clamp(B, new Point2(50, 10)));
        var shared = Make();
        var outlines = await shared.BandsAsync([A, B]);
        var raced = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => i % 2 == 0 ? shared.Bands(A).Clear.Count : shared.Clamp(B, new Point2(50, 10)).X)));
        Assert.Equal(a, outlines[0].Clear); Assert.Equal(b, outlines[1].Clear);
        Assert.Equal(clamp, shared.Clamp(B, new Point2(50, 10)));
        Assert.All(raced.Where((_, i) => i % 2 == 0), n => Assert.Equal(a.Count, n));
        Assert.Same(shared.Bands(A), outlines[0]);                                                   // still computed once per model
    }

    [Fact]
    public async Task Prepare_builds_every_models_reach_on_the_pool_so_a_later_drag_finds_it_built()
    {
        // A pickup's first drag frame clamps every held model on the main thread: built at the Ask's open, it does not wait on the build there.
        var big = Wood(44, 24, 4);
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = At(36, 24), [B] = At(36, 10) }, new MoveRegion([A, B], 12, [], [big.Id], 6), [big], Opr, []);
        Assert.False(regions.Built(A));
        await regions.Prepare();
        Assert.True(regions.Built(A)); Assert.True(regions.Built(B));
        Assert.False(regions.Built(Guid.CreateVersion7()));
    }

    [Fact]
    public void Each_model_has_its_own_area_from_where_it_stands()
    {
        var big = Wood(44, 24, 4);
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = At(36, 24), [B] = At(36, 10) }, new MoveRegion([A, B], 12, [], [big.Id], 6),
            [big], Opr, []);
        Assert.True(Areas.Inside(regions.Bands(A), new Point2(41, 24)));                         // into the wood, under 6"
        Assert.False(Areas.Inside(regions.Bands(A), new Point2(43, 24)));                        // deeper in it, over 6"
        Assert.True(Areas.Inside(regions.Bands(B), new Point2(47.5, 10)));                       // B's way east passes south of it
        Assert.Equal(new Point2(50, 10), regions.Clamp(Guid.CreateVersion7(), new Point2(50, 10)));   // a model the Ask does not move is not held
        Assert.Same(regions.Bands(A), regions.Bands(A));                                       // computed once per model
    }

    [Fact]
    public void The_area_is_drawn_in_bands_by_the_volumes_the_ask_names()
    {
        // A wood east of A and a pit west of it, a ruin north that is dangerous only to a path over 6": a 12" Rush capped at 6".
        var wood = Wood(44, 24, 4);
        var pit = Wood(28, 24, 2);
        var ruin = Wood(36, 34, 3);
        var legal = new MoveRegion([A], 12, [], [wood.Id], 6, DangerousVolumeIds: [pit.Id], UnstableVolumeIds: [ruin.Id], UnstableOver: 6);
        var bands = One(At(36, 24), legal, [wood, pit, ruin]).Bands(A);
        Assert.Equal(MoveBand.Difficult, Areas.BandOf(bands, new Point2(41, 24)));     // into the wood, under 6"
        Assert.Equal(MoveBand.Dangerous, Areas.BandOf(bands, new Point2(28, 24)));     // into the pit
        Assert.Equal(MoveBand.Dangerous, Areas.BandOf(bands, new Point2(36, 32.5)));   // into the ruin, over 6"
        Assert.Equal(MoveBand.Clear, Areas.BandOf(bands, new Point2(36, 28)));         // short of it
        Assert.Equal(MoveBand.Clear, Areas.BandOf(bands, new Point2(36, 14)));
        // The same Ask naming none of them: no dangerous band.
        Assert.Empty(One(At(36, 24), legal with { DangerousVolumeIds = null, UnstableVolumeIds = null, UnstableOver = null }, [wood, pit, ruin]).Bands(A).Dangerous);
    }

    [Fact]
    public void A_minimum_cuts_the_middle_out_of_each_area()
    {
        var regions = One(At(36, 24), new MoveRegion([A], 36, [], [], null, MinDistance: 30), []);
        Assert.Equal(30, regions.Minimum(A));
        Assert.False(Areas.Inside(regions.Bands(A), new Point2(40, 24)));
        Assert.True(Areas.Inside(regions.Bands(A), new Point2(68, 24)));
    }

    [Fact]
    public void Once_any_models_path_enters_difficult_terrain_every_model_is_held_to_the_cap()
    {
        // p.11: "If any model in a unit moves in or through difficult terrain at any point of its move, then all models in the unit may not move
        // more than 6" for that movement." A's path east enters the wood; B's, well south of it, is held to 6" with it.
        var big = Wood(44, 24, 4);
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = At(36, 24), [B] = At(36, 10) }, new MoveRegion([A, B], 12, [], [big.Id], 6),
            [big], Opr, []);
        Assert.Equal(6, regions.Allowance(new Dictionary<Guid, Point2> { [A] = new(41, 24), [B] = new(45, 10) }));
        Assert.Equal(12, regions.Allowance(new Dictionary<Guid, Point2> { [A] = new(30, 24), [B] = new(45, 10) }));
        Assert.Equal(12, regions.Allowance(new Dictionary<Guid, Point2> { [A] = new(36, 24), [B] = new(45, 10) }));   // A not moved yet
        Assert.Equal(42, regions.Clamp(B, new Point2(50, 10), 6).X, 3);
        Assert.Equal(48, regions.Clamp(B, new Point2(50, 10)).X, 3);
        var capless = One(At(36, 24), new MoveRegion([A], 12, [], [big.Id], null), [big]);
        Assert.Equal(12, capless.Allowance(new Dictionary<Guid, Point2> { [A] = new(41, 24) }));                      // Strider, Flying: no cap
    }

    [Fact]
    public void A_minimum_the_table_has_no_room_for_is_as_far_as_the_table_allows_in_the_direction_taken()
    {
        // Owner ruling: on a table without 30" of room an Aircraft moves as far as it can in the direction taken (the server's GfMoves.Minimum).
        var start = At(15, 15);
        var size = new TableSize(30, 30);
        var legal = new MoveRegion([A], 36, [], [], null, MinDistance: 30);
        var east = Movement.RoomAlong(start, new Point2(20, 15), size);
        Assert.Equal(east, MoveRuler.Minimum(start, legal, size, new Point2(20, 15))!.Value, 6);
        Assert.Equal(Movement.FarthestOnTable(start, size).Distance, MoveRuler.Minimum(start, legal, size)!.Value, 6);   // no direction taken yet
        Assert.Equal(30, MoveRuler.Minimum(At(36, 24), legal, new TableSize(72, 48), new Point2(37, 24)));               // room for 30" somewhere: 30" every way
        Assert.False(MoveRuler.Read(new Point2(15, 15), new Point2(15 + east, 15), 36, MoveRuler.Minimum(start, legal, size, new Point2(15 + east, 15))).UnderMinimum);
        Assert.True(MoveRuler.Read(new Point2(15, 15), new Point2(20, 15), 36, MoveRuler.Minimum(start, legal, size, new Point2(20, 15))).UnderMinimum);
        var regions = new MoveRegions(new Dictionary<Guid, PlacedModel> { [A] = start }, legal, [], size, []);
        Assert.Equal(east, regions.Minimum(A, new Point2(20, 15))!.Value, 6);
        Assert.False(Areas.Inside(regions.Bands(A), new Point2(15 + east - 1, 15)));                                  // short of the edge: under the least
    }

    [Fact]
    public void A_path_reads_its_length_and_lifts_to_the_heights_it_climbs()
    {
        Assert.Equal(new RulerReadout(7, 5, false), MoveRuler.Read(7, 12));
        Assert.True(MoveRuler.Read(13, 12).OverAllowance);
        var lifted = RulerPath.Lift([new(0, 0), new(3, 4), new(6, 0)], new Point3(0, 0, 0), new Point3(6, 0, 2));
        Assert.Equal([new Point3(0, 0, 0), new Point3(3, 4, 1), new Point3(6, 0, 2)], lifted);
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
