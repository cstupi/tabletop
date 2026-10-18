using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>
/// What one presence value may carry before it is relayed to everyone (security review M3). A drag holds one unit's models or one setup
/// piece; a selection is one piece and its models. The frame itself is held to PresenceFrameBytes. A value over a limit, or with a
/// coordinate no renderer can draw, is dropped: presence is latest-wins, so the next value supersedes it.
/// </summary>
public static class PresenceLimits
{
    public const int Placements = 64, SelectedModels = 64;

    public static bool Holds(Presence p) =>
        p.Placements is not null && p.Placements.Count <= Placements && (p.Selection is null || p.Selection.ModelIds is not null && p.Selection.ModelIds.Count <= SelectedModels)
        && (p.Cursor is null || Finite(p.Cursor.Point))
        && (p.Measurement is null || Finite(p.Measurement.From) && Finite(p.Measurement.To) && (p.Measurement.Mode is not { } mode || Enum.IsDefined(mode)))
        && p.Placements.All(x => Finite(x.Position) && double.IsFinite(x.Yaw.Degrees) && Enum.IsDefined(x.Kind));

    public static bool Holds(Ping p) => Finite(p.Point);

    static bool Finite(Point3 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
