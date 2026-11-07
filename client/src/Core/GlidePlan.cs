using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>How a relocated model travels: Walk (a move, a forced move), Charge (the charging unit's own move in its charge frame),
/// Place (from off the table: deployment, reserves, a disembark).</summary>
public enum GlideKind { Walk, Charge, Place }

/// <summary>One model's glide: its move, its kind, its length on the table in inches and how long it takes.</summary>
public sealed record Glide(ModelRelocation Move, GlideKind Kind, double Inches, double Seconds);

/// <summary>
/// The glides an event's relocations play (client AGENTS → Animation). It follows the frames as the Animator plays events (Observe), so
/// a move inside a `charge` frame by the frame's actor is told from the target's close-in; seeded from the projection's open frames on a
/// new board.
/// </summary>
public sealed class GlidePlan
{
    const string ChargeSeq = "charge";
    readonly Dictionary<Guid, (string Sequence, Guid? Actor)> _frames = new();

    public void Seed(Projection p) { _frames.Clear(); foreach (var f in p.Stack) _frames[f.Id] = (f.Sequence, f.Context.ActorUnitId); }

    /// <summary>Each event as it plays, in order: frames open and close.</summary>
    public void Observe(GameEvent e)
    {
        switch (e.Payload)
        {
            case SequenceStarted s: _frames[s.FrameId] = (s.Sequence, s.Context.ActorUnitId); break;
            case SequenceFinished f: _frames.Remove(f.FrameId); break;
            case SequenceAbandoned f: _frames.Remove(f.FrameId); break;
        }
    }

    /// <param name="unitOf">A model's unit.</param>
    public IReadOnlyList<Glide> Of(GameEvent e, Func<Guid, Guid?> unitOf)
    {
        var charger = e.Payload is StepCompleted step && _frames.TryGetValue(step.FrameId, out var f) && f.Sequence == ChargeSeq ? f.Actor : null;
        return AnimationQueue.Relocations(e).Where(r => r.To is OnTable).Select(r =>
        {
            var to = ((OnTable)r.To).Placement.Position;
            if (r.From is not OnTable { Placement.Position: var from }) return new Glide(r, GlideKind.Place, 0, PlaceSeconds);
            var inches = Distance(from, to);
            return charger is { } unit && unitOf(r.ModelId) == unit
                ? new Glide(r, GlideKind.Charge, inches, Math.Clamp(0.3 + 0.03 * inches, 0.35, 0.6))
                : new Glide(r, GlideKind.Walk, inches, Math.Clamp(0.3 + 0.07 * inches, 0.35, 0.9));
        }).ToList();
    }

    public const double PlaceSeconds = 0.3;

    static double Distance(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}

/// <summary>Where a glide stands at time fraction u (0 to 1): Along, the fraction of the way from start to end (below 0 or above 1 for
/// a charge's pull back and overshoot), Lift, inches above the path, and Turn, the fraction of the yaw turned.</summary>
public readonly record struct GlidePoint(double Along, double Lift, double Turn);

public static class GlideCurve
{
    /// <summary>A walk's step bob, a charge's pull back and overshoot (inches, at most), a placement's drop.</summary>
    public const double Bob = 0.05, PullBack = 0.3, Overshoot = 0.12, Drop = 1.2;
    /// <summary>A charge's phases: the pull back ends at Wind, the run at Run, then it settles.</summary>
    public const double Wind = 0.18, Run = 0.88;

    public static GlidePoint At(Glide g, double u)
    {
        u = Math.Clamp(u, 0, 1);
        switch (g.Kind)
        {
            case GlideKind.Place:
                return new(1, Drop * (1 - u * u), 1);
            case GlideKind.Charge when g.Inches > 1e-6:
            {
                var back = Math.Min(PullBack, 0.05 * g.Inches) / g.Inches; var over = Math.Min(Overshoot, 0.03 * g.Inches) / g.Inches;
                double along;
                if (u < Wind) along = -back * Math.Sin(Math.PI / 2 * u / Wind);
                else if (u < Run) { var v = (u - Wind) / (Run - Wind); along = -back + (1 + over + back) * v * v * v; }
                else along = 1 + over * (1 - Smooth((u - Run) / (1 - Run)));
                return new(along, 0, Smooth(Math.Min(1, u / Wind)));
            }
            default:
            {
                var steps = Math.Max(1, Math.Round(g.Inches / 1.5));
                return new(Smooth(u), Bob * Math.Abs(Math.Sin(Math.PI * steps * u)), Smooth(Math.Min(1, 2 * u)));
            }
        }
    }

    static double Smooth(double u) => u * u * (3 - 2 * u);
}
