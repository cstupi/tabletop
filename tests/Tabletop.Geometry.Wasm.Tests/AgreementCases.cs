using System.Text.Json;

namespace Tabletop.Geometry.Wasm.Tests;

/// <summary>One call: the facade method, the request as the browser would send it, and the answer of the direct Geometry call.</summary>
public sealed record AgreementCase(string Name, string Method, JsonElement Request, JsonElement Expected);

/// <summary>The cases the browser's WebAssembly replays (viewer/tests/fixtures/geometry.cases.json). `Expected` comes from calling Tabletop.Geometry
/// directly, never through <see cref="Facade"/>. Ids are counted, so the file is the same on every run.</summary>
public static class AgreementCases
{
    public static IReadOnlyList<AgreementCase> All => Cached.Value;
    static readonly Lazy<IReadOnlyList<AgreementCase>> Cached = new(Build);

    static readonly JsonSerializerOptions J = FacadeJson.Options;
    static int _next;
    static Guid Id() => new(++_next, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
    static JsonElement E<T>(T v) => JsonSerializer.SerializeToElement(v, J);

    const TerrainDeclarations Impassable = TerrainDeclarations.Impassable;
    static PlacedModel Model(double x, double y, double z = 0, double diameter = 1, double height = 1.5, double yaw = 0) =>
        new(new ModelBody(Base.Circle(diameter), height), new Placement(new Point3(x, y, z), new Yaw(yaw)));
    static PieceVolume Vol(VolumeKind kind, Footprint f, double top, TerrainDeclarations d, double bottom = 0, Point2? offset = null) =>
        new(Id(), new Volume(kind, f, offset ?? new Point2(0, 0), new Yaw(0), bottom, top, d, null));
    static Piece PieceAt(double x, double y, double yaw = 0, double scale = 1, params PieceVolume[] volumes) => new(Id(), new Point3(x, y, 0), new Yaw(yaw), scale, volumes);
    static Placement Pl(double x, double y, double z = 0, double yaw = 0) => new(new Point3(x, y, z), new Yaw(yaw));
    static List<WorldVolume> Volumes(IEnumerable<Piece> ps) => ps.SelectMany(p => p.WorldVolumes()).ToList();
    static List<WorldVolume> ImpassableOf(IReadOnlyList<WorldVolume> v) => v.Where(x => x.Volume.Declarations.HasFlag(Impassable)).ToList();
    static Guid[] ImpassableIds(IEnumerable<Piece> ps) => ps.SelectMany(p => p.Volumes).Where(v => v.Volume.Declarations.HasFlag(Impassable)).Select(v => v.Id).ToArray();

    // The built-in catalog's pieces, copied by value (client/content/builtin/catalog.json).
    static Piece LargeSightBlocker(double x, double y, double yaw = 0) =>
        PieceAt(x, y, yaw, 1, Vol(VolumeKind.Solid, Footprint.Rect(6, 6), 6, Impassable | TerrainDeclarations.BlocksLineOfSight));
    static Piece Ruin(double x, double y, double yaw = 0) =>
        PieceAt(x, y, yaw, 1, Vol(VolumeKind.Area, Footprint.Rect(4, 4), 4,
            TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut | TerrainDeclarations.Cover | TerrainDeclarations.Difficult));
    static Piece Hill(double x, double y) => PieceAt(x, y, 0, 1, Vol(VolumeKind.Solid, Footprint.Rect(8, 8), 2, TerrainDeclarations.None));
    static Piece Crate(double x, double y) => PieceAt(x, y, 0, 1, Vol(VolumeKind.Solid, Footprint.Rect(2, 2), 1, TerrainDeclarations.None));
    static Piece Block(double x, double y) => PieceAt(x, y, 0, 1, Vol(VolumeKind.Solid, Footprint.Rect(4, 4), 3, Impassable));
    static Piece Bridge(double x, double y) => PieceAt(x, y, 0, 1, Vol(VolumeKind.Solid, Footprint.Rect(6, 6), 5, TerrainDeclarations.None, bottom: 4));
    static Piece SteepHill(double x, double y) => PieceAt(x, y, 0, 1, Vol(VolumeKind.Area, Footprint.Circle(1.5), 0.1, TerrainDeclarations.Steep));

    /// <summary>A busy table: two catalog pieces, a hill, ten 25 mm and 32 mm models around them.</summary>
    static (Piece[] Terrain, PlacedModel[] Models) Busy()
    {
        Piece[] terrain = [LargeSightBlocker(20, 20), Ruin(32, 24, 30), Hill(44, 12)];
        double[] d = [0.98, 1.26];
        PlacedModel[] models = Enumerable.Range(0, 10).Select(i => Model(14 + (i % 5) * 1.6, 12 + (i / 5) * 1.6, 0, d[i % 2], 1.5)).ToArray();
        return (terrain, models);
    }

    static IReadOnlyList<AgreementCase> Build()
    {
        _next = 0;
        var cases = new List<AgreementCase>();
        void Add<TReq, TRes>(string name, string method, TReq request, TRes expected) => cases.Add(new AgreementCase(name, method, E(request), E(expected)));

        // LandModels: DragBodies.Models lifted.
        HeldEnd[] Land(IReadOnlyList<HeldModel> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<Piece> terrain, Abutment? abut = null)
        {
            var volumes = Volumes(terrain);
            var walls = volumes.Where(v => ImpassableIds(terrain).Contains(v.Id)).ToList();
            var lands = Collision.Land(held.Select(h => (h.At, h.To, (Func<Point2, bool>?)null)).ToList(), stationary, walls, volumes, abut);
            return held.Select((h, i) =>
            {
                var at = h.At with { Placement = h.At.Placement with { Position = new Point3(h.To.X, h.To.Y, 0) } };
                return new HeldEnd(h.Id, new Point3(h.To.X, h.To.Y, Collision.Hover(at, volumes, stationary)), lands[i]);
            }).ToArray();
        }
        var block = Block(10, 10);
        HeldModel[] two = [new(Id(), Model(2, 2), new Point2(10, 10)), new(Id(), Model(2, 4), new Point2(10, 10))];
        Add("land_models_beside_an_impassable_block", "LandModels", new LandRequest(two, [], ImpassableIds([block]), [block]), Land(two, [], [block]));
        Add("land_models_ignores_volumes_not_named_walls", "LandModels", new LandRequest(two, [], [], [block]), LandNoWalls(two, [block]));

        var (busy, models) = Busy();
        HeldModel[] group = [new(Id(), Model(2, 2, 0, 1.26), new Point2(20, 20)), new(Id(), Model(2, 4, 0, 0.98), new Point2(20.5, 20)), new(Id(), Model(2, 6, 0, 1.26), new Point2(15, 13))];
        Add("land_models_busy_table", "LandModels", new LandRequest(group, models, ImpassableIds(busy), busy), Land(group, models, busy));
        // A charge: the first lands 0.3" short of the target and is drawn flush; the second, 1" off, is not.
        var chargeTarget = Model(30, 20, 0, 1.26);
        var abut = new Abutment([chargeTarget], 0.5);
        HeldModel[] charging = [new(Id(), Model(20, 20, 0, 1.26), new Point2(28.44, 20.2)), new(Id(), Model(20, 22, 0, 1.26), new Point2(27.5, 22))];
        Add("land_models_abut_a_charge_target", "LandModels", new LandRequest(charging, [chargeTarget], [], [], abut), Land(charging, [chargeTarget], [], abut));
        // The same target 4" up: beyond a 3" vertical reach the landing is not drawn against it.
        var raised = chargeTarget with { Placement = chargeTarget.Placement with { Position = chargeTarget.Placement.Position with { Z = 4 } } };
        var high = new Abutment([raised], 0.5, 3);
        Add("land_models_abut_beyond_the_vertical_reach", "LandModels", new LandRequest(charging, [raised], [], [], high), Land(charging, [raised], [], high));

        // LandPiece, TurnPiece, surfaces, rests: DragBodies.Piece, Turn, Surfaces and the free branch's RestNear.
        PieceEnd LandP(IReadOnlyList<Piece> terrain, Piece piece, Placement at, Point2 to)
        {
            var held = piece with { Position = at.Position, Yaw = at.Yaw };
            var others = Volumes(terrain.Where(t => t.Id != piece.Id));
            if (held.Volumes.Count == 0) { var flat = new Point3(to.X, to.Y, 0); return new PieceEnd(flat, flat); }
            var there = held with { Position = new Point3(to.X, to.Y, 0) };
            return new PieceEnd(new Point3(to.X, to.Y, Collision.Hover(there.WorldVolumes(), others)), Collision.Land(held.WorldVolumes(), to, ImpassableOf(others), others).Position);
        }
        var crate = Crate(30, 30); var hill = Hill(10, 10);
        Add("land_piece_beside_an_impassable_block", "LandPiece", new PieceRequest([block, crate], crate.Id, Pl(30, 30), new Point2(10, 10)), LandP([block, crate], crate, Pl(30, 30), new Point2(10, 10)));
        Add("land_piece_onto_a_hill", "LandPiece", new PieceRequest([hill, crate], crate.Id, Pl(30, 30), new Point2(10, 10)), LandP([hill, crate], crate, Pl(30, 30), new Point2(10, 10)));
        var empty = PieceAt(30, 30);
        Add("a_piece_with_no_volumes_lands_flat", "LandPiece", new PieceRequest([empty], empty.Id, Pl(30, 30), new Point2(5, 6)), LandP([empty], empty, Pl(30, 30), new Point2(5, 6)));
        var crateBusy = Crate(60, 40);
        Piece[] busyAndCrate = [.. busy, crateBusy];
        Add("land_piece_busy_table", "LandPiece", new PieceRequest(busyAndCrate, crateBusy.Id, Pl(60, 40, 0, 45), new Point2(20.5, 19)), LandP(busyAndCrate, crateBusy, Pl(60, 40, 0, 45), new Point2(20.5, 19)));

        var longPiece = PieceAt(13, 10, 0, 1, Vol(VolumeKind.Solid, Footprint.Rect(2, 6), 1, TerrainDeclarations.None));
        Point3 Turn(IReadOnlyList<Piece> terrain, Piece piece, Yaw yaw)
        {
            var others = Volumes(terrain.Where(t => t.Id != piece.Id));
            var turned = piece with { Yaw = yaw };
            return turned.Volumes.Count == 0 ? turned.Position : Collision.Clear(turned.WorldVolumes(), ImpassableOf(others), others).Position;
        }
        Add("turn_piece_clear_of_an_impassable_piece", "TurnPiece", new TurnRequest([block, longPiece], longPiece.Id, new Yaw(90)), Turn([block, longPiece], longPiece, new Yaw(90)));

        var m = Model(10, 10);
        var bridge = Bridge(10, 10);
        Add("model_surfaces_on_a_hill", "ModelSurfaces", new ModelRestRequest(m, [hill], null), Collision.Surfaces(m, Volumes([hill])).ToArray());
        Add("model_surfaces_under_a_bridge", "ModelSurfaces", new ModelRestRequest(m, [bridge], null), Collision.Surfaces(m, Volumes([bridge])).ToArray());
        Add("rest_model_highest", "RestModel", new ModelRestRequest(m, [hill], null), Collision.RestNear(m, Volumes([hill]), double.PositiveInfinity));
        Add("rest_model_level_zero_under_a_bridge", "RestModel", new ModelRestRequest(m, [bridge], 0), Collision.RestNear(m, Volumes([bridge]), 0));
        var onRuin = Model(32, 24);
        Add("rest_model_busy_table", "RestModel", new ModelRestRequest(onRuin, busy, null), Collision.RestNear(onRuin, Volumes(busy), double.PositiveInfinity));
        Add("model_surfaces_busy_table", "ModelSurfaces", new ModelRestRequest(onRuin, busy, null), Collision.Surfaces(onRuin, Volumes(busy)).ToArray());

        double[] PieceSurf(IReadOnlyList<Piece> terrain, Piece piece, Placement at)
        {
            var held = piece with { Position = at.Position, Yaw = at.Yaw };
            return held.Volumes.Count == 0 ? [0] : Collision.Surfaces(held.WorldVolumes(), Volumes(terrain.Where(t => t.Id != piece.Id))).ToArray();
        }
        double PieceRest(IReadOnlyList<Piece> terrain, Piece piece, Placement at, double level)
        {
            var held = piece with { Position = at.Position, Yaw = at.Yaw };
            return held.Volumes.Count == 0 ? 0 : Collision.RestNear(held.WorldVolumes(), Volumes(terrain.Where(t => t.Id != piece.Id)), level);
        }
        Add("piece_surfaces_over_a_hill", "PieceSurfaces", new PieceRestRequest([hill, crate], crate.Id, Pl(10, 10), null), PieceSurf([hill, crate], crate, Pl(10, 10)));
        Add("rest_piece_highest", "RestPiece", new PieceRestRequest([hill, crate], crate.Id, Pl(10, 10), null), PieceRest([hill, crate], crate, Pl(10, 10), double.PositiveInfinity));
        Add("rest_piece_at_level_zero", "RestPiece", new PieceRestRequest([hill, crate], crate.Id, Pl(10, 10), 0), PieceRest([hill, crate], crate, Pl(10, 10), 0));

        // Contacts: the four kinds in one request.
        Guid ua = Id(), ub = Id();
        ContactBody Cb(Guid unit, double x, double y) => new(Id(), unit, new ModelBody(Base.Circle(1), 1.5), Pl(x, y));
        var onBaseMover = Cb(ua, 10, 10); var target = Cb(ub, 30, 30);
        var impMover = Cb(ua, 12, 10); var wallP = Block(50, 50);
        var crossMover = Cb(ua, 70, 10); var east = Cb(ub, 72, 10); var north = Cb(ub, 70, 12); var south = Cb(ub, 70, 8);
        var closeMover = Cb(ua, 10, 20); var far = Cb(ub, 20, 40);
        ContactBody[] bodies = [onBaseMover, target, impMover, crossMover, east, north, south, closeMover, far];
        var proposed = new Dictionary<Guid, Placement>
        {
            [onBaseMover.Id] = Pl(30.5, 30), [impMover.Id] = Pl(50, 50), [crossMover.Id] = Pl(73, 10), [closeMover.Id] = Pl(20, 42),
        };
        var terms = new ContactTerms(3, false, true, new ContactMove(3, [], [], null));
        var found = Contacts.Of(bodies, proposed, Volumes([wallP]), terms).Select(kv => new ContactFound(kv.Key, kv.Value.Kind, kv.Value.UnitId)).ToArray();
        Add("contacts_every_kind", "Contacts", new ContactsRequest(bodies, proposed, [wallP], terms), found);
        // Steep: going onto the hill caps the way round the line of five (Movement.SteepEntering).
        var steepMover = Cb(ua, 10, 10); var hillP = SteepHill(14, 10);
        ContactBody[] steepBodies = [steepMover, Cb(ub, 12, 8), Cb(ub, 12, 9), Cb(ub, 12, 10), Cb(ub, 12, 11), Cb(ub, 12, 12)];
        var steepProposed = new Dictionary<Guid, Placement> { [steepMover.Id] = Pl(14, 10) };
        var steepTerms = new ContactTerms(null, false, true, new ContactMove(8, [], [], 6, [hillP.Volumes[0].Id]));
        Add("contacts_steep_caps_the_way_round", "Contacts", new ContactsRequest(steepBodies, steepProposed, [hillP], steepTerms),
            Contacts.Of(steepBodies, steepProposed, Volumes([hillP]), steepTerms).Select(kv => new ContactFound(kv.Key, kv.Value.Kind, kv.Value.UnitId)).ToArray());
        var placement = new ContactTerms(null, true, false);
        Add("contacts_placement_on_a_base", "Contacts", new ContactsRequest([onBaseMover, target], new Dictionary<Guid, Placement> { [onBaseMover.Id] = Pl(30.5, 30) }, [], placement),
            Contacts.Of([onBaseMover, target], new Dictionary<Guid, Placement> { [onBaseMover.Id] = Pl(30.5, 30) }, [], placement).Select(kv => new ContactFound(kv.Key, kv.Value.Kind, kv.Value.UnitId)).ToArray());

        // Distances.
        PlacedModel a = Model(10, 10), b = Model(13, 10, 0, 1, 3), tall = Model(10, 10, 3, 1.26, 2);
        Add("overlap_bases_apart", "OverlapBases", new PairRequest(a, b), Overlap.Bases(a, b));
        Add("overlap_bases_stacked", "OverlapBases", new PairRequest(a, Model(10.5, 10)), Overlap.Bases(a, Model(10.5, 10)));
        Add("edge_to_edge", "EdgeToEdge", new PairRequest(a, b), Distance.EdgeToEdge(a, b));
        Add("edge_to_edge_oval_and_rect", "EdgeToEdge", new PairRequest(new PlacedModel(new ModelBody(Base.Oval(2.4, 1.4), 1.5), Pl(10, 10, 0, 35)), new PlacedModel(new ModelBody(Base.Rect(2, 1), 1.5), Pl(14, 11, 0, 80))),
            Distance.EdgeToEdge(new PlacedModel(new ModelBody(Base.Oval(2.4, 1.4), 1.5), Pl(10, 10, 0, 35)), new PlacedModel(new ModelBody(Base.Rect(2, 1), 1.5), Pl(14, 11, 0, 80))));
        Add("body_to_body", "BodyToBody", new PairRequest(a, tall), Distance.BodyToBody(a, tall));
        Add("edge_to_point", "EdgeToPoint", new PointRequest(a, new Point2(15, 12)), Distance.EdgeToEdge(a, new Point2(15, 12)));
        Add("edge_to_marker", "EdgeToMarker", new MarkerRequest(a, new Point2(15, 12), 1.5), Distance.EdgeToMarker(a, new Point2(15, 12), 1.5));
        PlacedModel[] from = [Model(0, 0), Model(20, 0)], to = [Model(50, 0), Model(23, 0), Model(5, 40)];
        var best = (D: double.PositiveInfinity, I: 0, J: 0);
        for (var i = 0; i < from.Length; i++) for (var j = 0; j < to.Length; j++) { var d = Distance.EdgeToEdge(from[i], to[j]); if (d < best.D) best = (d, i, j); }
        Add("nearest_pair", "NearestPair", new NearestRequest(from, to), new Nearest(best.D, best.I, best.J));
        Add("nearest_pair_busy_table", "NearestPair", new NearestRequest(models[..5], models[5..]), NearestOf(models[..5], models[5..]));

        // Range outline, zones, table.
        Add("range_outline_busy_table", "RangeOutline", new RingRequest(models, 6), RangeOutline.Of(models, 6).Select(s => new Segment(s.A, s.B)).ToArray());
        Add("range_outline_per_model", "RangeOutline", new RingRequest(models[..6], 0, [6, 12, 18, 6, 24, 9]), RangeOutline.Of(models[..6], [6.0, 12, 18, 6, 24, 9]).Select(s => new Segment(s.A, s.B)).ToArray());
        Add("range_outline_one_base", "RangeOutline", new RingRequest([Model(10, 10)], 12), RangeOutline.Of([Model(10, 10)], 12).Select(s => new Segment(s.A, s.B)).ToArray());
        Add("clearance_outline_lifted", "ClearanceOutline", new ClearanceRequest([Model(10, 10), Model(30, 10)], [9, 12], [Model(20, 10)], 6),
            Clearance.Outline([Model(10, 10), Model(30, 10)], [9.0, 12], [Model(20, 10)], 6).Select(s => new Segment(s.A, s.B)).ToArray());
        Add("clearance_breaks_repel", "ClearanceBreaks", new ClearanceRequest([Model(10, 10), Model(30, 10)], [9, 12], Placed: Model(30, 22)),
            Clearance.Breaks(Model(30, 22), [Model(10, 10), Model(30, 10)], [9.0, 12], [], 0));
        var size = new TableSize(72, 48);
        Add("zone_polygons_frontline", "ZonePolygons", new ZoneRequest("frontline/1", size, null, null), ZoneStyles.Resolve("frontline/1", size).Select(p => p.Vertices.ToArray()).ToArray());
        Add("zone_polygons_disordered", "ZonePolygons", new ZoneRequest("disordered/2", size, null, null), ZoneStyles.Resolve("disordered/2", size).Select(p => p.Vertices.ToArray()).ToArray());
        var legs36 = new ZoneLegs(2.0 / 3, 0.75);
        Add("zone_polygons_side_battle_36", "ZonePolygons", new ZoneRequest("side-battle/1", size, null, null, legs36), ZoneStyles.Resolve("side-battle/1", size, legs36).Select(p => p.Vertices.ToArray()).ToArray());
        Add("zone_contains_side_battle_36", "ZoneContains", new ZoneRequest("side-battle/1", size, new Point2(2, 33), null, legs36), ZoneStyles.Contains("side-battle/1", size, new Point2(2, 33), legs36));
        Add("zone_contains_inside", "ZoneContains", new ZoneRequest("frontline/1", size, new Point2(10, 40), null), ZoneStyles.Contains("frontline/1", size, new Point2(10, 40)));
        Add("zone_contains_outside", "ZoneContains", new ZoneRequest("frontline/1", size, new Point2(10, 10), null), ZoneStyles.Contains("frontline/1", size, new Point2(10, 10)));
        var straddle = Model(10, 36); var inside = Model(10, 44);
        Add("zone_holds_base_straddling", "ZoneHoldsBase", new ZoneRequest("frontline/1", size, null, straddle), ZoneStyles.BaseFullyWithin("frontline/1", size, straddle));
        Add("zone_holds_base_inside", "ZoneHoldsBase", new ZoneRequest("frontline/1", size, null, inside), ZoneStyles.BaseFullyWithin("frontline/1", size, inside));
        Add("table_holds_base_inside", "TableHoldsBase", new TableRequest(size, a), Table.ContainsBase(size, a));
        var edge = Model(0.2, 10);
        Add("table_holds_base_over_the_edge", "TableHoldsBase", new TableRequest(size, edge), Table.ContainsBase(size, edge));
        // ExitYaw: Containment.ExitYaw over the volumes the Ask names Impassable (a disembark's facing, p.7 and p.15).
        var truck = new PlacedModel(new ModelBody(Base.Circle(4), 2), Pl(10, 10));
        var rider = new ModelBody(Base.Circle(1), 1);
        var oval = new ModelBody(Base.Oval(3, 1), 1);
        var wall = Block(14, 10); var wallIds = ImpassableIds([wall]);
        void Exit(string name, ModelBody body, Point2 at, bool named) => Add(name, "ExitYaw",
            new ExitRequest(body, truck, 6, at, named ? wallIds : [], [wall], size),
            Containment.ExitYaw(body, truck, 6, at, named ? ImpassableOf(Volumes([wall])) : [], size));
        Exit("exit_yaw_round_base_within_reach", rider, new Point2(12, 10), false);
        Exit("exit_yaw_in_a_named_impassable_volume", rider, new Point2(14, 10), true);
        Exit("exit_yaw_in_a_volume_not_named", rider, new Point2(14, 10), false);
        Exit("exit_yaw_oval_within_reach", oval, new Point2(12, 11), false);
        Exit("exit_yaw_beyond_reach", rider, new Point2(30, 10), false);
        // MoveBands: a 2.5" move capped at 1.5" past a wood, a pit and an unstable ruin (dangerous over 1.5"), round a standing model; small, as the
        // answer is every triangle.
        var wood = PieceAt(11.6, 10, 0, 1, Vol(VolumeKind.Area, Footprint.Circle(0.6), 4, TerrainDeclarations.Difficult));
        var pit = PieceAt(10, 8.4, 0, 1, Vol(VolumeKind.Area, Footprint.Circle(0.5), 1, TerrainDeclarations.Dangerous));
        var ruin = PieceAt(8.6, 11.2, 0, 1, Vol(VolumeKind.Area, Footprint.Rect(1, 1), 3, TerrainDeclarations.DangerousOnRushCharge));
        Piece[] field = [wood, pit, ruin];
        var mover = Model(10, 10);
        PlacedModel[] standing = [Model(11, 11.6)];
        var bands = MoveReach.Build(mover, 2.5, [], Volumes([wood]), 1.5, size, standing, Volumes(field), Volumes([pit]), Volumes([ruin]), 1.5).Bands();
        Add("move_bands_wood_pit_and_ruin", "MoveBands",
            new BandsRequest(mover, 2.5, [], [wood.Volumes[0].Id], 1.5, field, size, standing, Dangerous: [pit.Volumes[0].Id], Unstable: [ruin.Volumes[0].Id], UnstableOver: 1.5),
            new BandsAnswer([.. bands.Clear], [.. bands.Difficult], [.. bands.Dangerous]));
        // MoveRoutes: the same table, a 9" move past the wood and the pit, and the same moves capped.
        var routeWood = PieceAt(16, 10, 0, 1, Vol(VolumeKind.Area, Footprint.Circle(2), 4, TerrainDeclarations.Difficult));
        var routePit = PieceAt(10, 4, 0, 1, Vol(VolumeKind.Area, Footprint.Circle(1.5), 1, TerrainDeclarations.Dangerous));
        var routeRuin = PieceAt(6, 16, 0, 1, Vol(VolumeKind.Area, Footprint.Rect(3, 3), 3, TerrainDeclarations.DangerousOnRushCharge));
        Piece[] routeField = [routeWood, routePit, routeRuin];
        var routeVolumes = Volumes(routeField);
        RouteModel[] routeModels = [new(Id(), Model(10, 10), new Point2(10, 1.5)), new(Id(), Model(12, 10), new Point2(19, 10)), new(Id(), Model(8, 10), new Point2(5, 18.5))];
        PlacedModel[] routeStanding = [Model(30, 30)];
        RouteFound[] Routes(bool capped) => [.. routeModels.Select(m =>
        {
            var path = capped ? Movement.Check(m.Start, m.To, 6, [], [], null, size, routeStanding, routeVolumes)
                : Movement.Measured(m.Start, m.To, 9, [], Volumes([routeWood]), 6, size, routeStanding, routeVolumes, Volumes([routePit]), Volumes([routeRuin]), 6, []);
            var swept = SweptPath.Of(m.Start, path.Path, SweptPath.DangerousTo(path.Distance, Volumes([routePit]), Volumes([routeRuin]), 6));
            return new RouteFound(m.Id, [.. path.Path], path.Distance, path.EntersDifficult, [.. swept.Footprints], [.. swept.Dangerous], [.. swept.Stretches]);
        })];
        foreach (var capped in new[] { false, true })
            Add(capped ? "move_routes_capped" : "move_routes_wood_pit_and_ruin", "MoveRoutes",
                new RoutesRequest(routeModels, 9, [], [routeWood.Volumes[0].Id], 6, routeField, size, routeStanding, Dangerous: [routePit.Volumes[0].Id],
                    Unstable: [routeRuin.Volumes[0].Id], UnstableOver: 6, Capped: capped), Routes(capped));
        // MoveRoutes with a base's edge clipping dangerous ground its centre line misses: a game's 16" Rush of 60 x 35 mm ovals past an 8" toxic smog,
        // the first rider's base about 0.35" in (owner ruling: any part of the base counts).
        var smog = PieceAt(15.955780558267506, 16.17559109636377, 255, 1, Vol(VolumeKind.Area, Footprint.Circle(4), 6, TerrainDeclarations.Dangerous));
        var cavalry = new ModelBody(Base.Oval(2.362204724409449, 1.3779527559055118), 4.43);
        RouteModel[] riders = [new(Id(), new PlacedModel(cavalry, Pl(14.497011184692383, 9.693385124206543)), new Point2(28.048744327367796, 18.199296529206755)),
            new(Id(), new PlacedModel(cavalry, Pl(16.074962615966797, 9.693385124206543)), new Point2(29.9576309014231, 17.64772490045357))];
        Add("move_routes_base_edge_clips_dangerous", "MoveRoutes", new RoutesRequest(riders, 16, [], [], 6, [smog], size, [], Dangerous: [smog.Volumes[0].Id]),
            riders.Select(m =>
            {
                var path = Movement.Measured(m.Start, m.To, 16, [], [], 6, size, [], Volumes([smog]), Volumes([smog]), [], 0, []);
                var swept = SweptPath.Of(m.Start, path.Path, Volumes([smog]));
                return new RouteFound(m.Id, [.. path.Path], path.Distance, path.EntersDifficult, [.. swept.Footprints], [.. swept.Dangerous], [.. swept.Stretches]);
            }).ToArray());
        return cases;
    }

    static Nearest NearestOf(PlacedModel[] from, PlacedModel[] to)
    {
        var best = (D: double.PositiveInfinity, I: 0, J: 0);
        for (var i = 0; i < from.Length; i++) for (var j = 0; j < to.Length; j++) { var d = Distance.EdgeToEdge(from[i], to[j]); if (d < best.D) best = (d, i, j); }
        return new Nearest(best.D, best.I, best.J);
    }

    /// <summary>LandModels with no wall named: every model lands at its target, nothing stops it.</summary>
    static HeldEnd[] LandNoWalls(IReadOnlyList<HeldModel> held, IReadOnlyList<Piece> terrain)
    {
        var volumes = Volumes(terrain);
        var lands = Collision.Land(held.Select(h => (h.At, h.To, (Func<Point2, bool>?)null)).ToList(), [], [], volumes);
        return held.Select((h, i) =>
        {
            var at = h.At with { Placement = h.At.Placement with { Position = new Point3(h.To.X, h.To.Y, 0) } };
            return new HeldEnd(h.Id, new Point3(h.To.X, h.To.Y, Collision.Hover(at, volumes, [])), lands[i]);
        }).ToArray();
    }
}
