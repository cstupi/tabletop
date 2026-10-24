namespace Tabletop.Geometry.Wasm;

/// <summary>A terrain volume row of a wire TerrainPlacement.</summary>
public sealed record PieceVolume(Guid Id, Volume Volume);

/// <summary>Reads a wire TerrainPlacement (its `mesh` is ignored).</summary>
public sealed record Piece(Guid Id, Point3 Position, Yaw Yaw, double Scale, IReadOnlyList<PieceVolume> Volumes)
{
    public IReadOnlyList<WorldVolume> WorldVolumes() => Volumes.Select(v => new WorldVolume(v.Id, v.Volume, new(Position, Yaw, Scale))).ToList();
}

public sealed record HeldModel(Guid Id, PlacedModel At, Point2 To);
public sealed record LandRequest(IReadOnlyList<HeldModel> Held, IReadOnlyList<PlacedModel> Stationary, IReadOnlyList<Guid> Walls, IReadOnlyList<Piece> Terrain);
public sealed record HeldEnd(Guid Id, Point3 Drawn, Point3 Lands);
public sealed record PieceRequest(IReadOnlyList<Piece> Terrain, Guid PieceId, Placement At, Point2 To);
public sealed record PieceEnd(Point3 Drawn, Point3 Lands);
public sealed record TurnRequest(IReadOnlyList<Piece> Terrain, Guid PieceId, Yaw Yaw);
public sealed record ModelRestRequest(PlacedModel Model, IReadOnlyList<Piece> Terrain, double? Level);
public sealed record PieceRestRequest(IReadOnlyList<Piece> Terrain, Guid PieceId, Placement At, double? Level);
public sealed record ContactsRequest(IReadOnlyList<ContactBody> Models, IReadOnlyDictionary<Guid, Placement> Proposed, IReadOnlyList<Piece> Terrain, ContactTerms Terms);
public sealed record ContactFound(Guid ModelId, ContactKind Kind, Guid? UnitId);
public sealed record PairRequest(PlacedModel A, PlacedModel B);
public sealed record PointRequest(PlacedModel Model, Point2 Point);
public sealed record MarkerRequest(PlacedModel Model, Point2 Centre, double Diameter);
public sealed record NearestRequest(IReadOnlyList<PlacedModel> From, IReadOnlyList<PlacedModel> To);
public sealed record Nearest(double Distance, int From, int To);
public sealed record RingRequest(IReadOnlyList<PlacedModel> Models, double Range);
public sealed record Segment(Point2 A, Point2 B);
public sealed record ZoneRequest(string Shape, TableSize Size, Point2? Point, PlacedModel? Model);
public sealed record TableRequest(TableSize Size, PlacedModel Model);
