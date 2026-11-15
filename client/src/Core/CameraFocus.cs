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
        return (On(Intent.CameraForward) - On(Intent.CameraBack), On(Intent.CameraRight) - On(Intent.CameraLeft), holding ? 0 : On(Intent.TurnRight) - On(Intent.TurnLeft));   // Q swings the camera right about the focus, so the table turns left
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

    public static Point3? Centroid(Projection p, Guid unitId) => Centroid(p, [unitId]);

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

    /// <summary>Where follow glides after a batch: to the last frame the batch starts that is an activation's own action frame (an
    /// action and an actor, no frame open under it), at its actor; or a melee frame, at the charging and charged units together. The frames
    /// nested in an activation (attacks, strike back, morale, wounds) leave the camera where it is, so a melee stays framed to its end.</summary>
    public static Point3? FollowTarget(IReadOnlyList<GameEvent> batch, Projection p)
    {
        var open = OpenBefore(batch, p); Point3? at = null;
        foreach (var e in batch)
            switch (e.Payload)
            {
                case SequenceStarted { Context: { Action: not null, ActorUnitId: { } actor } c } s when open.Count == 0 || s.Sequence == MeleeSequence:
                    at = s.Sequence == MeleeSequence ? Centroid(p, [actor, .. c.TargetUnitIds]) : Centroid(p, actor);
                    open.Add(s.FrameId); break;
                case SequenceStarted s: open.Add(s.FrameId); break;
                case SequenceFinished f: Close(open, f.FrameId); break;
                case SequenceAbandoned f: Close(open, f.FrameId); break;
            }
        return at;
    }

    const string MeleeSequence = "melee";

    /// <summary>Playing several teams: where the camera goes when the acting team changes (TeamSide), but only between activations. Inside
    /// a frame the Asks pass between the teams (wounds to allocate, strike back, a defender's move, consolidation) and the camera stays.</summary>
    public static Point3? SideOnSwitch(Projection p, GameInfo game, Guid? was, Guid? acting, ZoneLegs? legs = null) =>
        acting is { } team && acting != was && p.Stack.Count == 0 ? TeamSide(p, game, team, legs) : null;

    /// <summary>The frames open before the batch: the projection's stack after it, with the batch's frames taken back off.</summary>
    static List<Guid> OpenBefore(IReadOnlyList<GameEvent> batch, Projection p)
    {
        var open = p.Stack.Select(f => f.Id).ToList();
        for (var i = batch.Count - 1; i >= 0; i--)
            switch (batch[i].Payload)
            {
                case SequenceStarted s: open.Remove(s.FrameId); break;
                case SequenceFinished f when !open.Contains(f.FrameId): open.Add(f.FrameId); break;
                case SequenceAbandoned f when !open.Contains(f.FrameId): open.Add(f.FrameId); break;
            }
        return open;
    }

    static void Close(List<Guid> open, Guid frame) { var i = open.IndexOf(frame); if (i >= 0) open.RemoveRange(i, open.Count - i); }

    static Point3? Centroid(Projection p, IReadOnlyList<Guid> units)
    {
        var at = p.Models.Values.Where(m => units.Contains(m.UnitId)).Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
        return at.Count == 0 ? null : new Point3(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z));
    }
}
