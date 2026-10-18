using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>One value per connected participant, latest wins, never logged. Existence is connectedness.</summary>
public sealed record Presence(Guid ParticipantId, Cursor? Cursor, Selection? Selection, IReadOnlyList<PresencePlacement> Placements, Measurement? Measurement);

/// <summary>Where the ray landed, and the piece under it.</summary>
public sealed record Cursor(Point3 Point, Guid? Target);

/// <summary>A unit and optionally models within it; during setup a terrain placement or marker. Empty ModelIds means the whole unit.</summary>
public sealed record Selection(Guid PieceId, IReadOnlyList<Guid> ModelIds);

public enum PlacementKind { Held, Proposed }

/// <summary>A piece somewhere it is not committed to. Held: others render it there. Proposed: a translucent copy.</summary>
public sealed record PresencePlacement(Guid PieceId, Point3 Position, Yaw Yaw, PlacementKind Kind);

/// <summary>Two points already resolved edge to edge; the receiver draws the line and computes the length the Mode names. Null
/// Mode (a client that sends none) is Horizontal.</summary>
public sealed record Measurement(Point3 From, Point3 To, MeasureMode? Mode = null);

/// <summary>How a measurement reads: Horizontal is the distance across the table plane (2D), Straight the line through the
/// height difference (3D).</summary>
public enum MeasureMode { Horizontal, Straight }

/// <summary>A one-shot pointer with no words.</summary>
public sealed record Ping(Guid Sender, Point3 Point, Guid? Target);
