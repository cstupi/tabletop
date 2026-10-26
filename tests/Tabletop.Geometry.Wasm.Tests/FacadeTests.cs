using System.Text.Json;
using Tabletop.Geometry;

namespace Tabletop.Geometry.Wasm.Tests;

public class FacadeTests
{
    static readonly JsonSerializerOptions J = FacadeJson.Options;
    static string W<T>(T v) => JsonSerializer.Serialize(v, J);
    static T R<T>(string json) => JsonSerializer.Deserialize<T>(json, J)!;

    static PlacedModel Model(double x, double y, double z = 0, double diameter = 1, double height = 1.5) =>
        new(new ModelBody(Base.Circle(diameter), height), new Placement(new Point3(x, y, z), new Yaw(0)));
    static PieceVolume Box(double w, double l, double bottom, double top, TerrainDeclarations d = TerrainDeclarations.None, VolumeKind kind = VolumeKind.Solid) =>
        new(Guid.CreateVersion7(), new Volume(kind, Footprint.Rect(w, l), new Point2(0, 0), new Yaw(0), bottom, top, d, null));
    static Piece PieceAt(double x, double y, params PieceVolume[] volumes) => new(Guid.CreateVersion7(), new Point3(x, y, 0), new Yaw(0), 1, volumes);
    static Placement Pl(double x, double y, double z = 0) => new(new Point3(x, y, z), new Yaw(0));
    static Guid[] Impassable(Piece p) => p.Volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToArray();
    static IReadOnlyList<WorldVolume> Volumes(params Piece[] ps) => ps.SelectMany(p => p.WorldVolumes()).ToList();

    [Fact]
    public void LandModels_equals_Land_and_Hover()
    {
        var block = PieceAt(10, 10, Box(4, 4, 0, 3, TerrainDeclarations.Impassable));
        var a = Model(2, 2); var b = Model(2, 4);
        var held = new[] { new HeldModel(Guid.CreateVersion7(), a, new Point2(10, 10)), new HeldModel(Guid.CreateVersion7(), b, new Point2(10, 10)) };
        var req = new LandRequest(held, [], Impassable(block), [block]);
        var ends = R<HeldEnd[]>(Facade.LandModels(W(req)));
        var vols = Volumes(block);
        var lands = Collision.Land(held.Select(h => (h.At, h.To, (Func<Point2, bool>?)null)).ToList(), [], vols, vols);
        Assert.Equal(lands[0], ends[0].Lands);
        Assert.Equal(lands[1], ends[1].Lands);
        Assert.Equal(12.5, ends[0].Lands.X, 9); Assert.Equal(10, ends[0].Lands.Y, 9); Assert.Equal(0, ends[0].Lands.Z, 9);
        Assert.NotEqual(ends[0].Lands, ends[1].Lands);
        Assert.Equal(3.5, ends[0].Drawn.Z, 9);
        Assert.Equal(held[0].Id, ends[0].Id);
    }

    [Fact]
    public void LandModels_draws_a_charger_within_the_abutment_flush_against_its_target()
    {
        var target = Model(30, 20);
        var held = new[] { new HeldModel(Guid.CreateVersion7(), Model(20, 20), new Point2(28.7, 20)) };   // 1" bases: 0.3" short
        var ends = R<HeldEnd[]>(Facade.LandModels(W(new LandRequest(held, [target], [], [], new Abutment([target], 0.5)))));
        Assert.Equal(29, ends[0].Lands.X, 9);
        Assert.Equal(28.7, R<HeldEnd[]>(Facade.LandModels(W(new LandRequest(held, [target], [], [])))) [0].Lands.X, 9);
    }

    [Fact]
    public void LandModels_ignores_volumes_not_named_walls()
    {
        var block = PieceAt(10, 10, Box(4, 4, 0, 3, TerrainDeclarations.Impassable));
        var held = new[] { new HeldModel(Guid.CreateVersion7(), Model(2, 2), new Point2(10, 10)) };
        var ends = R<HeldEnd[]>(Facade.LandModels(W(new LandRequest(held, [], [], [block]))));
        Assert.Equal(3, ends[0].Lands.Z, 9);   // not a wall: it rests on the top
        Assert.Equal(10, ends[0].Lands.X, 9);
    }

    [Fact]
    public void LandPiece_equals_DragBodies_Piece_lifted()
    {
        var block = PieceAt(10, 10, Box(4, 4, 0, 3, TerrainDeclarations.Impassable));
        var crate = PieceAt(30, 30, Box(2, 2, 0, 1));
        var terrain = new[] { block, crate };
        var end = R<PieceEnd>(Facade.LandPiece(W(new PieceRequest(terrain, crate.Id, Pl(30, 30), new Point2(10, 10)))));
        var direct = Collision.Land(crate.WorldVolumes().Select(v => v with { Transform = new TerrainTransform(new Point3(30, 30, 0), new Yaw(0), 1) }).ToList(),
            new Point2(10, 10), Volumes(block).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList(), Volumes(block)).Position;
        Assert.Equal(direct, end.Lands);
        Assert.NotEqual(10, end.Lands.X);   // beside the block
        Assert.Equal(Collision.Lift + 3, end.Drawn.Z, 9);

        var hill = PieceAt(10, 10, Box(4, 4, 0, 3));
        var onHill = R<PieceEnd>(Facade.LandPiece(W(new PieceRequest([hill, crate], crate.Id, Pl(30, 30), new Point2(10, 10)))));
        Assert.Equal(new Point3(10, 10, 3), onHill.Lands);
    }

    [Fact]
    public void A_piece_with_no_volumes_lands_flat()
    {
        var empty = PieceAt(30, 30);
        var end = R<PieceEnd>(Facade.LandPiece(W(new PieceRequest([empty], empty.Id, Pl(30, 30), new Point2(5, 6)))));
        Assert.Equal(new Point3(5, 6, 0), end.Lands);
        Assert.Equal(new Point3(5, 6, 0), end.Drawn);
    }

    [Fact]
    public void TurnPiece_moves_clear_of_an_impassable_piece()
    {
        var block = PieceAt(10, 10, Box(4, 4, 0, 3, TerrainDeclarations.Impassable));
        var long_ = PieceAt(13, 10, Box(2, 6, 0, 1));
        var turned = R<Point3>(Facade.TurnPiece(W(new TurnRequest([block, long_], long_.Id, new Yaw(90)))));
        var at = long_.WorldVolumes().Select(v => v with { Transform = new TerrainTransform(long_.Position, new Yaw(90), 1) }).ToList();
        var direct = Collision.Clear(at, Volumes(block), Volumes(block)).Position;
        Assert.Equal(direct, turned);
        Assert.NotEqual(long_.Position, turned);
    }

    [Fact]
    public void RestModel_without_level_is_the_highest_open_surface()
    {
        var hill = PieceAt(10, 10, Box(4, 4, 0, 3));
        Assert.Equal(3, R<double>(Facade.RestModel(W(new ModelRestRequest(Model(10, 10), [hill], null)))), 9);
        Assert.Equal(Collision.Surfaces(Model(10, 10), Volumes(hill)), R<double[]>(Facade.ModelSurfaces(W(new ModelRestRequest(Model(10, 10), [hill], null)))));
    }

    [Fact]
    public void RestModel_at_level_zero_under_a_bridge_is_zero()
    {
        var bridge = PieceAt(10, 10, Box(6, 6, 4, 5));
        Assert.Equal(0, R<double>(Facade.RestModel(W(new ModelRestRequest(Model(10, 10, height: 1.5), [bridge], 0)))), 9);
        Assert.Equal(5, R<double>(Facade.RestModel(W(new ModelRestRequest(Model(10, 10, height: 1.5), [bridge], null)))), 9);
    }

    [Fact]
    public void PieceSurfaces_equals_Collision_Surfaces()
    {
        var hill = PieceAt(10, 10, Box(4, 4, 0, 3));
        var crate = PieceAt(30, 30, Box(2, 2, 0, 1));
        var got = R<double[]>(Facade.PieceSurfaces(W(new PieceRestRequest([hill, crate], crate.Id, Pl(10, 10), null))));
        var moved = crate.WorldVolumes().Select(v => v with { Transform = new TerrainTransform(new Point3(10, 10, 0), new Yaw(0), 1) }).ToList();
        Assert.Equal(Collision.Surfaces(moved, Volumes(hill)), got);
        Assert.Equal(Collision.RestNear(moved, Volumes(hill), double.PositiveInfinity), R<double>(Facade.RestPiece(W(new PieceRestRequest([hill, crate], crate.Id, Pl(10, 10), null)))), 9);
    }

    [Fact]
    public void Contacts_equals_Contacts_Of()
    {
        Guid ua = Guid.CreateVersion7(), ub = Guid.CreateVersion7();
        ContactBody Cb(Guid u, double x, double y) => new(Guid.CreateVersion7(), u, new ModelBody(Base.Circle(1), 1.5), Pl(x, y));
        var mover = Cb(ua, 10, 10); var other = Cb(ub, 30, 30);
        var c1 = Cb(ua, 50, 10); var wallM = Cb(ua, 52, 10);
        var wall = PieceAt(20, 20, Box(4, 4, 0, 3, TerrainDeclarations.Impassable));
        var proposed = new Dictionary<Guid, Placement> { [mover.Id] = Pl(30.5, 30) };
        var terms = new ContactTerms(3, true, true);
        var req = new ContactsRequest([mover, other, c1, wallM], proposed, [wall], terms);
        var found = R<ContactFound[]>(Facade.Contacts(W(req)));
        var direct = Contacts.Of(req.Models, req.Proposed, Volumes(wall), terms);
        Assert.Equal(direct.Select(kv => new ContactFound(kv.Key, kv.Value.Kind, kv.Value.UnitId)).OrderBy(f => f.ModelId), found.OrderBy(f => f.ModelId));
        Assert.Equal(ContactKind.OnBase, found.Single().Kind);

        var kinds = new[]
        {
            (Terms: new ContactTerms(null, true, true), To: Pl(20, 20), Kind: ContactKind.InImpassable),
            (Terms: new ContactTerms(4, true, false), To: Pl(30, 33), Kind: ContactKind.TooClose),
        };
        foreach (var k in kinds)
            Assert.Equal(k.Kind, R<ContactFound[]>(Facade.Contacts(W(new ContactsRequest([mover, other], new Dictionary<Guid, Placement> { [mover.Id] = k.To }, [wall], k.Terms)))).Single().Kind);
        var boxed = new[] { mover, Cb(ub, 12, 10), Cb(ub, 10, 12), Cb(ub, 10, 8) };
        var cross = new ContactTerms(null, false, false, new ContactMove(3, [], [], null));
        Assert.Equal(ContactKind.Crosses, R<ContactFound[]>(Facade.Contacts(W(new ContactsRequest(boxed, new Dictionary<Guid, Placement> { [mover.Id] = Pl(13, 10) }, [], cross)))).Single().Kind);
    }

    [Fact]
    public void EdgeToEdge_BodyToBody_EdgeToPoint_EdgeToMarker_equal_Distance()
    {
        PlacedModel a = Model(10, 10), b = Model(13, 10, 0, 1, 3);
        Assert.Equal(Distance.EdgeToEdge(a, b), R<double>(Facade.EdgeToEdge(W(new PairRequest(a, b)))), 9);
        Assert.Equal(Distance.BodyToBody(a, b), R<double>(Facade.BodyToBody(W(new PairRequest(a, b)))), 9);
        Assert.Equal(Overlap.Bases(a, b), R<bool>(Facade.OverlapBases(W(new PairRequest(a, b)))));
        Assert.Equal(Distance.EdgeToEdge(a, new Point2(15, 12)), R<double>(Facade.EdgeToPoint(W(new PointRequest(a, new Point2(15, 12))))), 9);
        Assert.Equal(Distance.EdgeToMarker(a, new Point2(15, 12), 1.5), R<double>(Facade.EdgeToMarker(W(new MarkerRequest(a, new Point2(15, 12), 1.5)))), 9);
        Assert.Equal(2, R<double>(Facade.EdgeToEdge(W(new PairRequest(Model(10, 10), Model(13, 10))))), 9);
    }

    [Fact]
    public void NearestPair_picks_the_closest_pair_and_its_indices()
    {
        var from = new[] { Model(0, 0), Model(20, 0) };
        var to = new[] { Model(50, 0), Model(23, 0), Model(5, 40) };
        var n = R<Nearest>(Facade.NearestPair(W(new NearestRequest(from, to))));
        Assert.Equal(1, n.From); Assert.Equal(1, n.To);
        Assert.Equal(Distance.EdgeToEdge(from[1], to[1]), n.Distance, 9);
    }

    [Fact]
    public void RangeOutline_equals_RangeOutline_Of()
    {
        var models = new[] { Model(10, 10), Model(12, 10), Model(30, 30) };
        var got = R<Segment[]>(Facade.RangeOutline(W(new RingRequest(models, 6))));
        var direct = Geometry.RangeOutline.Of(models, 6);
        Assert.Equal(direct.Select(s => new Segment(s.A, s.B)), got);
    }

    [Fact]
    public void Zone_answers_equal_ZoneStyles()
    {
        var size = new TableSize(72, 48);
        var polys = R<Point2[][]>(Facade.ZonePolygons(W(new ZoneRequest("frontline/1", size, null, null))));
        Assert.Equal(ZoneStyles.Resolve("frontline/1", size).Select(p => p.Vertices.ToArray()), polys);
        bool In(Point2 p) => R<bool>(Facade.ZoneContains(W(new ZoneRequest("frontline/1", size, p, null))));
        Assert.True(In(new Point2(10, 40))); Assert.False(In(new Point2(10, 10)));
        var straddle = Model(10, 36, diameter: 1);
        var inside = Model(10, 44, diameter: 1);
        Assert.False(R<bool>(Facade.ZoneHoldsBase(W(new ZoneRequest("frontline/1", size, null, straddle)))));
        Assert.True(R<bool>(Facade.ZoneHoldsBase(W(new ZoneRequest("frontline/1", size, null, inside)))));
        Assert.Equal(ZoneStyles.BaseFullyWithin("frontline/1", size, straddle), R<bool>(Facade.ZoneHoldsBase(W(new ZoneRequest("frontline/1", size, null, straddle)))));
    }

    [Fact]
    public void TableHoldsBase_equals_Table_ContainsBase()
    {
        var size = new TableSize(72, 48);
        foreach (var m in new[] { Model(10, 10), Model(0.2, 10), Model(72, 48) })
            Assert.Equal(Table.ContainsBase(size, m), R<bool>(Facade.TableHoldsBase(W(new TableRequest(size, m)))));
    }

    [Fact]
    public void ExitYaw_equals_Containment_ExitYaw_over_the_named_impassable_volumes()
    {
        var truck = new PlacedModel(new ModelBody(Base.Circle(4), 2), Pl(10, 10));
        var body = new ModelBody(Base.Circle(1), 1);
        var wall = PieceAt(14, 10, Box(2, 2, 0, 3, TerrainDeclarations.Impassable));
        var forest = PieceAt(20, 10, Box(4, 4, 0, 3, TerrainDeclarations.Cover, VolumeKind.Area));
        var size = new TableSize(72, 48);
        string Ask(Point2 at, params Guid[] impassable) => Facade.ExitYaw(W(new ExitRequest(body, truck, 6, at, impassable, [wall, forest], size)));
        Assert.Equal(new Yaw(0), R<Yaw?>(Ask(new Point2(12, 10))));
        Assert.Null(R<Yaw?>(Ask(new Point2(14, 10), Impassable(wall))));                // the wall named: the drop is in it
        Assert.Equal(new Yaw(0), R<Yaw?>(Ask(new Point2(14, 10))));                      // not named: it is only terrain
        Assert.Equal(new Yaw(0), R<Yaw?>(Ask(new Point2(14.5, 10), Impassable(forest)))); // a volume named is judged, whatever it declares
        Assert.Null(R<Yaw?>(Ask(new Point2(40, 10))));
    }

    [Fact]
    public void Bad_json_throws_JsonException()
    {
        Assert.ThrowsAny<JsonException>(() => Facade.EdgeToEdge("{"));
        Assert.ThrowsAny<JsonException>(() => Facade.RestModel("{\"model\":{\"body\":{\"base\":{\"shape\":\"hexagon\",\"a\":1,\"b\":1},\"height\":1},\"placement\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"yaw\":{\"degrees\":0}}},\"terrain\":[]}"));
    }
}
