namespace Tabletop.Geometry.Wasm;

/// <summary>A terrain volume row of a wire TerrainPlacement.</summary>
public sealed record PieceVolume(Guid Id, Volume Volume);

/// <summary>Reads a wire TerrainPlacement (its `mesh` is ignored).</summary>
public sealed record Piece(Guid Id, Point3 Position, Yaw Yaw, double Scale, IReadOnlyList<PieceVolume> Volumes)
{
    public IReadOnlyList<WorldVolume> WorldVolumes() => Volumes.Select(v => new WorldVolume(v.Id, v.Volume, new(Position, Yaw, Scale))).ToList();
}

public sealed record HeldModel(Guid Id, PlacedModel At, Point2 To);
/// <summary>Abut: a charge Ask's ContactWith models and ContactGrace (DragBodies.Abutment); omitted for any other drag.</summary>
public sealed record LandRequest(IReadOnlyList<HeldModel> Held, IReadOnlyList<PlacedModel> Stationary, IReadOnlyList<Guid> Walls, IReadOnlyList<Piece> Terrain, Abutment? Abut = null);
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
/// <summary>A range outline: every model at Range, or each at its own Ranges[i] when given.</summary>
public sealed record RingRequest(IReadOnlyList<PlacedModel> Models, double Range, IReadOnlyList<double>? Ranges = null);
public sealed record Segment(Point2 A, Point2 B);
/// <summary>A unit's models and its coherency distances (Coherency.Gaps): Chain, Elevated at different elevation, Spread.</summary>
public sealed record CoherencyRequest(IReadOnlyList<PlacedModel> Models, double Chain, double Elevated, double Spread);
/// <summary>A keep-away (Clearance): each of From at Over[i], lifted within LiftedWithin of each of Lifted; Placed, the base ClearanceBreaks tests.</summary>
public sealed record ClearanceRequest(IReadOnlyList<PlacedModel> From, IReadOnlyList<double> Over, IReadOnlyList<PlacedModel>? Lifted = null, double LiftedWithin = 0,
    PlacedModel? Placed = null);
/// <summary>Legs: Side Battle's, from the game's Welcome; null is ZoneLegs.Default. Reach: the inches from the zone a base stands fully within
/// (PieceSelection.ZoneReach: a Scout's); null or 0 is the zone itself.</summary>
public sealed record ZoneRequest(string Shape, TableSize Size, Point2? Point, PlacedModel? Model, ZoneLegs? Legs = null, double? Reach = null);
public sealed record TableRequest(TableSize Size, PlacedModel Model);
public sealed record ExitRequest(ModelBody Body, PlacedModel Container, double Within, Point2 At, IReadOnlyList<Guid> Impassable, IReadOnlyList<Piece> Terrain, TableSize Size);

/// <summary>One model's move area in bands: the move Ask's parameters (MoveRegion's volume ids, cap, MinDistance), the board's terrain, the table and
/// the models standing that stop it (none when the Ask PassesModels), with `Units` their units (MoveUnits: each other unit's perimeter, grown by the
/// Ask's Separation).</summary>
public sealed record BandsRequest(PlacedModel Start, double Allowance, IReadOnlyList<Guid> Impassable, IReadOnlyList<Guid> Difficult, double? DifficultCap,
    IReadOnlyList<Piece> Terrain, TableSize Size, IReadOnlyList<PlacedModel> Stationary, IReadOnlyList<Guid>? Steep = null, IReadOnlyList<Guid>? Dangerous = null,
    IReadOnlyList<Guid>? Unstable = null, double? UnstableOver = null, double? MinDistance = null, MoveUnits? Units = null);
public sealed record BandsAnswer(Point2[] Clear, Point2[] Difficult, Point2[] Dangerous);

/// <summary>One held model's move: where it stands and where it is going.</summary>
public sealed record RouteModel(Guid Id, PlacedModel Start, Point2 To);
/// <summary>The paths the held models' moves are measured along, from the move Ask's parameters (as BandsRequest); `Capped`: the unit is held to the
/// cap, each move judged by its shortest path within it.</summary>
public sealed record RoutesRequest(IReadOnlyList<RouteModel> Models, double Allowance, IReadOnlyList<Guid> Impassable, IReadOnlyList<Guid> Difficult,
    double? DifficultCap, IReadOnlyList<Piece> Terrain, TableSize Size, IReadOnlyList<PlacedModel> Stationary, IReadOnlyList<Guid>? Steep = null,
    IReadOnlyList<Guid>? Dangerous = null, IReadOnlyList<Guid>? Unstable = null, double? UnstableOver = null, bool Capped = false, MoveUnits? Units = null);
/// <summary>Footprints, Dangerous and Stretches: the base swept along the path (SweptPath.Of).</summary>
public sealed record RouteFound(Guid Id, Point2[] Points, double Distance, bool EntersDifficult, PathFootprint[] Footprints, Guid[] Dangerous, PathStretch[] Stretches);

/// <summary>One model a move Ask moves, where it stands.</summary>
public sealed record AskModel(Guid Id, PlacedModel Start);
/// <summary>A move Ask kept under `Key` (MoveAsks): its models, its parameters (MoveRegion's volume ids, cap, MinDistance, Separation, PassesModels,
/// whether a landing in impassable terrain counts), the board's terrain, the table, the standing models that stop a path (none when the Ask
/// PassesModels) with their units (`Units`, MoveUnits; its Bases, a charge's target units, the contacts' too) and every model as a contact body.
/// `Prepare`: every model's reach is built at once, with the fields its route reads (the worker that answers drag frames), rather than on first use.</summary>
public sealed record MoveAskRequest(string Key, IReadOnlyList<AskModel> Models, double Allowance, IReadOnlyList<Guid> Impassable, IReadOnlyList<Guid> Difficult,
    double? DifficultCap, IReadOnlyList<Piece> Terrain, TableSize Size, IReadOnlyList<PlacedModel> Stationary, IReadOnlyList<ContactBody> Bodies,
    double? Separation = null, bool PassesModels = false, bool Landing = true, IReadOnlyList<Guid>? Steep = null, IReadOnlyList<Guid>? Dangerous = null,
    IReadOnlyList<Guid>? Unstable = null, double? UnstableOver = null, double? MinDistance = null, bool Prepare = false, MoveUnits? Units = null);
/// <summary>A kept Ask's area for one model, or the share Part of Parts of it.</summary>
public sealed record MoveAreaRequest(string Key, Guid Id, int Part = 0, int Parts = 1);
/// <summary>The area's bands as flat coordinates, x then y, three points a triangle.</summary>
public sealed record FlatBands(double[] Clear, double[] Difficult, double[] Dangerous);
/// <summary>A kept Ask's drag frame: where each of its models is proposed; `Contacts` false asks routes only.</summary>
public sealed record MoveFrameRequest(string Key, IReadOnlyDictionary<Guid, Placement> Proposed, bool Contacts = true);
/// <summary>The unit's allowance, each moved model's route and the landings' contacts.</summary>
public sealed record MoveFrame(double Allowance, RouteFound[] Routes, ContactFound[] Contacts);
public sealed record MoveKeyRequest(string Key);
