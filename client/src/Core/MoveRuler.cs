using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Distance is the base's displacement from where it started (GF p.7); Remaining is what the allowance leaves; UnderMinimum is short of a
/// minimum move the Ask set (MoveRegion.MinDistance).</summary>
public sealed record RulerReadout(double Distance, double? Remaining, bool OverAllowance, bool UnderMinimum = false)
{
    /// <summary>Outside the distances the move allows: the model shows red.</summary>
    public bool OutOfRange => OverAllowance || UnderMinimum;
}

/// <summary>Whether a move's commit may go, and the line that says how many models are out of range.</summary>
public sealed record MoveGate(bool Allowed, string? Reason);

public static class MoveRuler
{
    public static RulerReadout Read(Point2 start, Point2 current, double? allowance, double? minimum = null) => Read(start.DistanceTo(current), allowance, minimum);

    /// <summary>A readout for a move measured along its path (MoveRegions.Route): `distance` is the path's length.</summary>
    public static RulerReadout Read(double distance, double? allowance, double? minimum = null) =>
        new(distance, allowance is { } a ? Math.Max(0, a - distance) : null, allowance is { } b && distance > b + 1e-6, minimum is { } m && distance < m - 1e-6);

    /// <summary>The commit against every held model's readout and its contact marks (Violations.Marked): the models out of range are counted,
    /// each contact reason named once with its count, and under strict any of them disables it; under warn and silent it goes and the server judges.</summary>
    public static MoveGate Gate(IEnumerable<RulerReadout> readouts, Enforcement enforcement, IReadOnlyDictionary<Guid, string>? marked = null)
    {
        var all = readouts.ToList();
        int over = all.Count(r => r.OverAllowance), under = all.Count(r => r.UnderMinimum);
        var contacts = (marked?.Values ?? []).GroupBy(r => r).ToList();
        if (over + under + contacts.Count == 0) return new MoveGate(true, null);
        static string Models(int n, string what) => n == 1 ? $"1 model {what} its" : $"{n} models {what} their";
        var parts = new List<string>();
        if (over > 0) parts.Add($"{Models(over, "past")} allowance");
        if (under > 0) parts.Add($"{Models(under, "short of")} minimum");
        parts.AddRange(contacts.Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ({g.Count()} models)"));
        return new MoveGate(enforcement != Enforcement.Strict, string.Join(" · ", parts) + ".");
    }

    /// <summary>The least this model must move, as the server's GfMoves.Minimum reads it: the Ask's MinDistance where the table has room for it in
    /// some direction (p.13 "Aircraft can't move into the table edges to move less than 30""); where it has none, as far as the table lets its whole
    /// base go in the direction taken, towards `toward` (Movement.RoomAlong; owner ruling), or, with no direction taken yet, the farthest any way
    /// (Movement.FarthestOnTable). Null when the Ask sets no minimum.</summary>
    public static double? Minimum(PlacedModel start, MoveRegion legal, TableSize size, Point2? toward = null)
    {
        if (legal.MinDistance is not { } m) return null;
        var farthest = Movement.FarthestOnTable(start, size).Distance;
        if (farthest >= m) return m;
        return toward is { } t && t.DistanceTo(start.Placement.Position.Horizontal) > 1e-6 ? Movement.RoomAlong(start, t, size) : farthest;
    }

    /// <summary>Distance a model has already moved inside a frame: the sum of its logged table-to-table relocations in the frame's completed steps.</summary>
    public static double UsedInFrame(Frame frame, Guid modelId) =>
        frame.Steps.SelectMany(s => s.Changes).OfType<ModelsMoved>().SelectMany(m => m.Moves)
            .Where(r => r.ModelId == modelId && r.From is OnTable && r.To is OnTable)
            .Sum(r => ((OnTable)r.From).Placement.Position.Horizontal.DistanceTo(((OnTable)r.To).Placement.Position.Horizontal));

    /// <summary>The unit is inside a container, so this move is a disembark and <see cref="Region"/> cannot serve it: there is no start placement to sweep from and Allowance binds nothing (contracts plan Task 8).</summary>
    public static bool IsDisembark(MoveRegion legal) => legal.Containment?.ExitingId is not null;

    /// <summary>The same anchored region over models on the shelf (a Spawn or Split reserve placed within 6" of its anchor model): the ghosts start
    /// around the anchor, not inside it, and the Ask's prompt says what is placed.</summary>
    public static bool IsShelfPlacement(MoveRegion legal, Projection p) =>
        IsDisembark(legal) && legal.ModelIds.Count > 0 && p.Models.GetValueOrDefault(legal.ModelIds[0])?.Location is OnShelf;

    /// <summary>
    /// The disembark region: rulebook p.15, "must stay fully within 6\" of it when exiting", measured edge to edge from the
    /// container's base — so it is the container's footprint grown by the exit distance, not a circle about its centre. One
    /// vertex per ray out of the container's base centre, marched outward in Movement.Step increments and bisected at the
    /// first refusal, which is what stops a ring of impassable terrain re-admitting
    /// placements beyond it. The candidate takes the container's Z, because the drop settles the height locally while the
    /// server settles it for the log; its yaw is whatever fits, since p.7 lets a model take any facing.
    /// </summary>
    public static IReadOnlyList<Point2> ExitRegion(ModelBody body, PlacedModel container, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        var within = legal.Containment?.ExitWithin ?? 0;
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var origin = container.Placement.Position.Horizontal;
        var reach = within + Math.Max(body.Base.A, body.Base.B) + Math.Max(container.Body.Base.A, container.Body.Base.B);
        var boundary = new List<Point2>(Movement.Rays);
        for (var r = 0; r < Movement.Rays; r++)
        {
            var angle = 2 * Math.PI * r / Movement.Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var reached = 0.0;
            for (var k = 1; ; k++)
            {
                var d = Math.Min(k * Movement.Step, reach);
                if (!Admits(body, container, within, origin + dir * d, impassable, size))
                {
                    var (lo, hi) = (reached, d);                                  // bisect the last step: eight halvings of 0.25" is under 0.001"
                    for (var i = 0; i < 8; i++)
                    {
                        var mid = (lo + hi) / 2;
                        if (Admits(body, container, within, origin + dir * mid, impassable, size)) lo = mid; else hi = mid;
                    }
                    reached = lo;
                    break;
                }
                reached = d;
                if (d >= reach) break;
            }
            boundary.Add(origin + dir * reached);
        }
        return boundary;
    }

    /// <summary>How many facings a non-circular base is tried at (<see cref="Containment.ExitYaws"/>).</summary>
    public const int ExitYaws = Containment.ExitYaws;

    /// <summary>Is this position offered at any facing? <see cref="Containment.ExitYaw"/> finds one.</summary>
    static bool Admits(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size) =>
        ExitYawAt(body, container, within, at, impassable, size) is not null;

    /// <summary>A facing this position is legal at, or null if none is. The panel asks for it when it builds the answer, so the
    /// placement it sends is one the server's own ExitPlacement will accept; a circular base answers at the first try.</summary>
    public static Yaw? ExitYawAt(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size) =>
        Containment.ExitYaw(body, container, within, at, impassable, size);
}
