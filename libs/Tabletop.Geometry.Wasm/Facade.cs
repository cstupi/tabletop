using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Tabletop.Geometry.Wasm;

/// <summary>Tabletop.Geometry as plain string-to-string methods: a JSON request in, a JSON answer out. A JSON that does not read throws JsonException.
/// Held things are always lifted (the browser drops them), so the compositions are DragBodies.Models and DragBodies.Piece with `free` false and no level.</summary>
public static class Facade
{
    static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)FacadeJson.Options.GetTypeInfo(typeof(T));
    static T Read<T>(string json) => JsonSerializer.Deserialize(json, Info<T>()) ?? throw new JsonException("The request is null.");
    static string Write<T>(T value) => JsonSerializer.Serialize(value, Info<T>());

    static List<WorldVolume> Volumes(IEnumerable<Piece> terrain) => terrain.SelectMany(p => p.WorldVolumes()).ToList();
    static List<WorldVolume> ImpassableOf(IReadOnlyList<WorldVolume> volumes) => volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
    static Piece PieceOf(IReadOnlyList<Piece> terrain, Guid id) => terrain.Single(t => t.Id == id);
    static List<WorldVolume> OthersOf(IReadOnlyList<Piece> terrain, Guid id) => Volumes(terrain.Where(t => t.Id != id));

    /// <summary>The held piece at a placement (its yaw included), and the volumes of every other piece.</summary>
    static (Piece Held, List<WorldVolume> Others) Held(IReadOnlyList<Piece> terrain, Guid id, Placement at) =>
        (PieceOf(terrain, id) with { Position = at.Position, Yaw = at.Yaw }, OthersOf(terrain, id));

    public static string LandModels(string json)
    {
        var r = Read<LandRequest>(json);
        var volumes = Volumes(r.Terrain);
        var walls = volumes.Where(v => r.Walls.Contains(v.Id)).ToList();
        var lands = Collision.Land(r.Held.Select(h => (h.At, h.To, (Func<Point2, bool>?)null)).ToList(), r.Stationary, walls, volumes, r.Abut);
        return Write(r.Held.Select((h, i) =>
        {
            var at = h.At with { Placement = h.At.Placement with { Position = new Point3(h.To.X, h.To.Y, 0) } };
            return new HeldEnd(h.Id, new Point3(h.To.X, h.To.Y, Collision.Hover(at, volumes, r.Stationary)), lands[i]);
        }).ToArray());
    }

    public static string LandPiece(string json)
    {
        var r = Read<PieceRequest>(json);
        var (held, others) = Held(r.Terrain, r.PieceId, r.At);
        if (held.Volumes.Count == 0) { var flat = new Point3(r.To.X, r.To.Y, 0); return Write(new PieceEnd(flat, flat)); }
        var there = held with { Position = new Point3(r.To.X, r.To.Y, 0) };
        return Write(new PieceEnd(new Point3(r.To.X, r.To.Y, Collision.Hover(there.WorldVolumes(), others)),
            Collision.Land(held.WorldVolumes(), r.To, ImpassableOf(others), others).Position));
    }

    public static string TurnPiece(string json)
    {
        var r = Read<TurnRequest>(json);
        var others = OthersOf(r.Terrain, r.PieceId);
        var turned = PieceOf(r.Terrain, r.PieceId) with { Yaw = r.Yaw };
        return Write(turned.Volumes.Count == 0 ? turned.Position : Collision.Clear(turned.WorldVolumes(), ImpassableOf(others), others).Position);
    }

    public static string ModelSurfaces(string json)
    {
        var r = Read<ModelRestRequest>(json);
        return Write(Collision.Surfaces(r.Model, Volumes(r.Terrain)).ToArray());
    }

    public static string RestModel(string json)
    {
        var r = Read<ModelRestRequest>(json);
        return Write(Collision.RestNear(r.Model, Volumes(r.Terrain), r.Level ?? double.PositiveInfinity));
    }

    public static string PieceSurfaces(string json)
    {
        var r = Read<PieceRestRequest>(json);
        var (held, others) = Held(r.Terrain, r.PieceId, r.At);
        return Write(held.Volumes.Count == 0 ? [0.0] : Collision.Surfaces(held.WorldVolumes(), others).ToArray());
    }

    public static string RestPiece(string json)
    {
        var r = Read<PieceRestRequest>(json);
        var (held, others) = Held(r.Terrain, r.PieceId, r.At);
        return Write(held.Volumes.Count == 0 ? 0.0 : Collision.RestNear(held.WorldVolumes(), others, r.Level ?? double.PositiveInfinity));
    }

    public static string Contacts(string json)
    {
        var r = Read<ContactsRequest>(json);
        return Write(Geometry.Contacts.Of(r.Models, r.Proposed, Volumes(r.Terrain), r.Terms).Select(kv => new ContactFound(kv.Key, kv.Value.Kind, kv.Value.UnitId)).ToArray());
    }

    public static string OverlapBases(string json) { var r = Read<PairRequest>(json); return Write(Overlap.Bases(r.A, r.B)); }
    public static string EdgeToEdge(string json) { var r = Read<PairRequest>(json); return Write(Distance.EdgeToEdge(r.A, r.B)); }
    public static string BodyToBody(string json) { var r = Read<PairRequest>(json); return Write(Distance.BodyToBody(r.A, r.B)); }
    public static string EdgeToPoint(string json) { var r = Read<PointRequest>(json); return Write(Distance.EdgeToEdge(r.Model, r.Point)); }
    public static string EdgeToMarker(string json) { var r = Read<MarkerRequest>(json); return Write(Distance.EdgeToMarker(r.Model, r.Centre, r.Diameter)); }

    public static string NearestPair(string json)
    {
        var r = Read<NearestRequest>(json);
        var best = (Distance: double.PositiveInfinity, From: -1, To: -1);
        for (var i = 0; i < r.From.Count; i++)
            for (var j = 0; j < r.To.Count; j++)
            {
                var d = Distance.EdgeToEdge(r.From[i], r.To[j]);
                if (d < best.Distance) best = (d, i, j);
            }
        if (best.From < 0) throw new JsonException("NearestPair needs a model on each side.");
        return Write(new Nearest(best.Distance, best.From, best.To));
    }

    public static string RangeOutline(string json)
    {
        var r = Read<RingRequest>(json);
        return Write((r.Ranges is { } each ? Geometry.RangeOutline.Of(r.Models, each) : Geometry.RangeOutline.Of(r.Models, r.Range)).Select(s => new Segment(s.A, s.B)).ToArray());
    }

    /// <summary>Clearance.Outline: the area a placed base's edge may not enter.</summary>
    public static string ClearanceOutline(string json)
    {
        var r = Read<ClearanceRequest>(json);
        return Write(Clearance.Outline(r.From, r.Over, r.Lifted ?? [], r.LiftedWithin).Select(s => new Segment(s.A, s.B)).ToArray());
    }

    /// <summary>Clearance.Breaks: the index into From the placed base comes within the distance of, or -1.</summary>
    public static string ClearanceBreaks(string json)
    {
        var r = Read<ClearanceRequest>(json);
        return Write(Clearance.Breaks(r.Placed ?? throw new JsonException("ClearanceBreaks needs a placed model."), r.From, r.Over, r.Lifted ?? [], r.LiftedWithin));
    }

    public static string ZonePolygons(string json)
    {
        var r = Read<ZoneRequest>(json);
        return Write(ZoneStyles.Resolve(r.Shape, r.Size, r.Legs).Select(p => p.Vertices.ToArray()).ToArray());
    }

    public static string ZoneContains(string json)
    {
        var r = Read<ZoneRequest>(json);
        return Write(ZoneStyles.Contains(r.Shape, r.Size, r.Point ?? throw new JsonException("ZoneContains needs a point."), r.Legs));
    }

    public static string ZoneHoldsBase(string json)
    {
        var r = Read<ZoneRequest>(json);
        return Write(ZoneStyles.BaseFullyWithin(r.Shape, r.Size, r.Model ?? throw new JsonException("ZoneHoldsBase needs a model."), r.Legs));
    }

    /// <summary>Containment.ExitYaw over the terrain's volumes the Ask names Impassable: a facing the exit position is legal at, or null.</summary>
    public static string ExitYaw(string json)
    {
        var r = Read<ExitRequest>(json);
        var impassable = Volumes(r.Terrain).Where(v => r.Impassable.Contains(v.Id)).ToList();
        return Containment.ExitYaw(r.Body, r.Container, r.Within, r.At, impassable, r.Size) is { } yaw ? Write(yaw) : "null";
    }

    /// <summary>MoveReach.Bands as the Godot client's MoveRegions builds them: the Ask's volumes by id (the steep ones the model starts outside added to
    /// its difficult ones, Movement.SteepEntering), its cap, the volumes dangerous to the move, the standing models and the table; a MinDistance cuts
    /// the middle out as MoveRuler.Minimum reads it (the farthest the table allows when that is less, in the direction the model goes).</summary>
    public static string MoveBands(string json)
    {
        var r = Read<BandsRequest>(json);
        var volumes = Volumes(r.Terrain);
        List<WorldVolume> Named(IReadOnlyList<Guid>? ids) => ids is null ? [] : volumes.Where(v => ids.Contains(v.Id)).ToList();
        var difficult = Named(r.Difficult);
        if (r.Steep is { Count: > 0 }) difficult.AddRange(Movement.SteepEntering(r.Start, Named(r.Steep)));
        var reach = MoveReach.Build(r.Start, Math.Max(0, r.Allowance), Named(r.Impassable), difficult, r.DifficultCap, r.Size, r.Stationary, volumes,
            Named(r.Dangerous), Named(r.Unstable), r.UnstableOver ?? 0);
        Func<Point2, double>? least = null;
        if (r.MinDistance is { } m)
        {
            var origin = r.Start.Placement.Position.Horizontal;
            var farthest = Movement.FarthestOnTable(r.Start, r.Size).Distance;
            least = p => farthest >= m ? m : p.DistanceTo(origin) > 1e-6 ? Movement.RoomAlong(r.Start, p, r.Size) : farthest;
        }
        var bands = reach.Bands(least);
        return Write(new BandsAnswer([.. bands.Clear], [.. bands.Difficult], [.. bands.Dangerous]));
    }

    /// <summary>Each held model's path as the server measures it (Movement.Measured over the Ask's volumes, the standing models and the table; the
    /// steep volumes it starts outside difficult), the path MoveReach.Route draws in the Godot client; `Capped`, the shortest within the cap. Each with
    /// its base swept along it (SweptPath: footprints and the volumes dangerous to the path its base enters or crosses, as the server tests it).</summary>
    public static string MoveRoutes(string json)
    {
        var r = Read<RoutesRequest>(json);
        var volumes = Volumes(r.Terrain);
        List<WorldVolume> Named(IReadOnlyList<Guid>? ids) => ids is null ? [] : volumes.Where(v => ids.Contains(v.Id)).ToList();
        var (impassable, difficult, steep, dangerous, unstable) = (Named(r.Impassable), Named(r.Difficult), Named(r.Steep), Named(r.Dangerous), Named(r.Unstable));
        return Write(r.Models.Select(m =>
        {
            var path = r.Capped
                ? Movement.Check(m.Start, m.To, Math.Min(r.Allowance, r.DifficultCap ?? r.Allowance), impassable, [], null, r.Size, r.Stationary, volumes)
                : Movement.Measured(m.Start, m.To, r.Allowance, impassable, difficult, r.DifficultCap, r.Size, r.Stationary, volumes, dangerous,
                    unstable, r.UnstableOver ?? 0, steep);
            var swept = SweptPath.Of(m.Start, path.Path, SweptPath.DangerousTo(path.Distance, dangerous, unstable, r.UnstableOver ?? 0));
            return new RouteFound(m.Id, [.. path.Path], path.Distance, path.EntersDifficult, [.. swept.Footprints], [.. swept.Dangerous], [.. swept.Stretches]);
        }).ToArray());
    }

    public static string TableHoldsBase(string json) { var r = Read<TableRequest>(json); return Write(Table.ContainsBase(r.Size, r.Model)); }
}
