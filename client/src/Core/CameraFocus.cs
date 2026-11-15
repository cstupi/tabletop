using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>The camera's focus point in the table frame (spec → Camera): WASD pan clamped to the table plus 12", the 0.3 s glide, the follow rule.</summary>
public static class CameraFocus
{
    /// <summary>Degrees a second Q/E turn what is in hand while held; Shift turns a terrain piece finer (and held models each where it stands, TableInput).</summary>
    public const double TurnDegreesPerSecond = 120, FineTurnDegreesPerSecond = 30;
    public const double Margin = 12, GlideSeconds = 0.3, QuietSeconds = 2, PanPerInchOfDistance = 0.8, OrbitRadiansPerSecond = 1.5, TableLevelPitch = 0.1;
    public static readonly HotKey[] HeldKeys = [HotKey.W, HotKey.A, HotKey.S, HotKey.D, HotKey.Q, HotKey.E];
    public static Point2 Clamp(Point2 f, TableSize s) => new(Math.Clamp(f.X, -Margin, s.Width + Margin), Math.Clamp(f.Y, -Margin, s.Length + Margin));

    /// <summary>Held intents to (forward, right, orbit), each −1, 0 or 1: nothing while typing; with a unit in hand Q/E are its turn.</summary>
    public static (int Forward, int Right, int Orbit) Steer(IReadOnlySet<Intent> held, bool holding, bool typing)
    {
        if (typing) return (0, 0, 0);
        int On(Intent i) => held.Contains(i) ? 1 : 0;
        return (On(Intent.CameraForward) - On(Intent.CameraBack), On(Intent.CameraRight) - On(Intent.CameraLeft), holding ? 0 : On(Intent.TurnLeft) - On(Intent.TurnRight));
    }

    /// <summary>The turn this frame for what is in hand, in degrees, positive counter-clockwise (Q): none while typing or with nothing in hand.</summary>
    public static double HeldTurn(IReadOnlySet<Intent> held, bool holding, bool typing, bool fine, double seconds)
    {
        if (typing || !holding) return 0;
        var sign = (held.Contains(Intent.TurnLeft) ? 1 : 0) - (held.Contains(Intent.TurnRight) ? 1 : 0);
        return sign * (fine ? FineTurnDegreesPerSecond : TurnDegreesPerSecond) * seconds;
    }

    public static Point2 Pan(Point2 focus, Point2 heading, int forward, int right, double distance, double seconds, TableSize size)
    {
        var dir = heading * forward + new Point2(heading.Y, -heading.X) * right;
        return dir.Length < 1e-9 ? Clamp(focus, size) : Clamp(focus + dir * (distance * PanPerInchOfDistance * seconds / dir.Length), size);
    }

    public static Point3 Glide(Point3 a, Point3 b, double elapsed)
    {
        var t = Math.Clamp(elapsed / GlideSeconds, 0, 1); var k = t * t * (3 - 2 * t);
        return new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Z + (b.Z - a.Z) * k);
    }

    public static bool Follows(bool enabled, double secondsSinceMoved, bool holding) => enabled && !holding && secondsSinceMoved >= QuietSeconds;
    /// <summary>End's pitch: the markers hide so the sight check stays clean.</summary>
    public static bool TableLevel(double pitch) => pitch < TableLevelPitch;

    public static Point3? Centroid(Projection p, Guid unitId)
    {
        var at = p.Models.Values.Where(m => m.UnitId == unitId).Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
        return at.Count == 0 ? null : new Point3(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z));
    }

    /// <summary>Where the camera goes when a client holding several teams starts acting for another (Seats.Acting): that team's units on the
    /// table, else the middle of its deployment zone, else nowhere.</summary>
    public static Point3? TeamSide(Projection p, GameInfo game, Guid team, ZoneLegs? legs = null)
    {
        var seats = game.Participants.Where(x => x.TeamId == team).Select(x => x.Id).ToHashSet();
        var at = p.Models.Values.Where(m => p.Units.TryGetValue(m.UnitId, out var u) && seats.Contains(u.ParticipantId))
            .Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
        if (at.Count > 0) return new Point3(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z));
        if (game.Teams.FirstOrDefault(t => t.Id == team) is not { } t || p.Board.Table.Zones.FirstOrDefault(z => z.TeamOrdinal == t.Ordinal) is not { } zone) return null;
        IReadOnlyList<Polygon> shapes;
        try { shapes = ZoneStyles.Resolve(zone.Shape, p.Board.Table.Size, legs); } catch (ArgumentException) { return null; }
        var corners = shapes.SelectMany(s => s.Vertices).ToList();
        return corners.Count == 0 ? null : new Point3(corners.Average(c => c.X), corners.Average(c => c.Y), 0);
    }

    /// <summary>Where follow glides: the actor of the last action frame (SequenceStarted with an action) the batch starts.</summary>
    public static Point3? FollowTarget(IReadOnlyList<GameEvent> batch, Projection p) =>
        batch.Select(e => e.Payload).OfType<SequenceStarted>().LastOrDefault(s => s.Context is { Action: not null, ActorUnitId: not null }) is { } s ? Centroid(p, s.Context.ActorUnitId!.Value) : null;
}
