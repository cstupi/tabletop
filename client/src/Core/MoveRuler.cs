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
    public static RulerReadout Read(Point2 start, Point2 current, double? allowance, double? minimum = null)
    {
        var d = start.DistanceTo(current);
        return new RulerReadout(d, allowance is { } a ? Math.Max(0, a - d) : null, allowance is { } b && d > b + 1e-6, minimum is { } m && d < m - 1e-6);
    }

    /// <summary>How far a straight move may go along each of Movement.Rays rays: the Ask's allowance (p.7), and its difficult cap on a ray that
    /// touches one of its difficult volumes (p.11). Walls, other models and the table edge are left out: the drag slides along those itself.</summary>
    public static IReadOnlyList<Point2> Reach(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes) =>
        Movement.Region(start, Math.Max(0, legal.Allowance), [], volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList(), legal.DifficultCap, null).Boundary;

    /// <summary>Where a drag may put a model: the target pulled back along the line from the model's origin to the reach (Reach). Between two
    /// rays the nearer of their two ends holds, so a cap never leaks past the ray beside it.</summary>
    public static Point2 Clamp(Point2 origin, IReadOnlyList<Point2> reach, Point2 target)
    {
        var d = origin.DistanceTo(target);
        if (reach.Count == 0 || d < 1e-9) return target;
        var angle = Math.Atan2(target.Y - origin.Y, target.X - origin.X);
        var at = (angle < 0 ? angle + 2 * Math.PI : angle) / (2 * Math.PI) * reach.Count;
        var i = (int)Math.Floor(at) % reach.Count;
        var r = Math.Min(origin.DistanceTo(reach[i]), origin.DistanceTo(reach[(i + 1) % reach.Count]));
        return d <= r + 1e-9 ? target : origin + (target - origin) * (r / d);
    }

    /// <summary>The commit against every held model's readout: any out of range is named, and under strict disables it; under warn and
    /// silent it goes and the server judges.</summary>
    public static MoveGate Gate(IEnumerable<RulerReadout> readouts, Enforcement enforcement)
    {
        var all = readouts.ToList();
        int over = all.Count(r => r.OverAllowance), under = all.Count(r => r.UnderMinimum);
        if (over + under == 0) return new MoveGate(true, null);
        static string Models(int n, string what) => n == 1 ? $"1 model {what} its" : $"{n} models {what} their";
        var parts = new List<string>();
        if (over > 0) parts.Add($"{Models(over, "past")} allowance");
        if (under > 0) parts.Add($"{Models(under, "short of")} minimum");
        return new MoveGate(enforcement != Enforcement.Strict, string.Join(" · ", parts) + ".");
    }

    /// <summary>The least this model must move: the Ask's MinDistance, or as far as the table lets its whole base go when that is less
    /// (the server's own reading, Movement.FarthestOnTable). Null when the Ask sets no minimum.</summary>
    public static double? Minimum(PlacedModel start, MoveRegion legal, TableSize size) =>
        legal.MinDistance is { } m ? Math.Min(m, Movement.FarthestOnTable(start, size).Distance) : null;

    /// <summary>The inner edge of the annulus a minimum cuts from the region: each boundary vertex pulled in to the minimum along its own ray, or
    /// left where it is when the ray stops short of it. Paired with the boundary vertex for vertex.</summary>
    public static IReadOnlyList<Point2> Inner(Point2 origin, IReadOnlyList<Point2> boundary, double minimum) =>
        boundary.Select(p => origin.DistanceTo(p) is var d && d > minimum ? origin + (p - origin) * (minimum / d) : p).ToList();

    /// <summary>Distance a model has already moved inside a frame: the sum of its logged table-to-table relocations in the frame's completed steps.</summary>
    public static double UsedInFrame(Frame frame, Guid modelId) =>
        frame.Steps.SelectMany(s => s.Changes).OfType<ModelsMoved>().SelectMany(m => m.Moves)
            .Where(r => r.ModelId == modelId && r.From is OnTable && r.To is OnTable)
            .Sum(r => ((OnTable)r.From).Placement.Position.Horizontal.DistanceTo(((OnTable)r.To).Placement.Position.Horizontal));

    /// <summary>The reachable region: Movement.Region from the Ask's parameters — the allowance, the impassable and difficult volumes resolved by id from the board, and the cap GameRules computed. One frame, no conversion.</summary>
    public static IReadOnlyList<Point2> Region(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size, IReadOnlyList<PlacedModel>? others = null)
    {
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var difficult = volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList();
        return Movement.Region(start, legal.Allowance, impassable, difficult, legal.DifficultCap, size, legal.PassesModels ? null : others).Boundary;
    }

    /// <summary>The unit is inside a container, so this move is a disembark and <see cref="Region"/> cannot serve it: there is no start placement to sweep from and Allowance binds nothing (contracts plan Task 8).</summary>
    public static bool IsDisembark(MoveRegion legal) => legal.Containment?.ExitingId is not null;

    /// <summary>
    /// The disembark region: rulebook p.15, "must stay fully within 6\" of it when exiting", measured edge to edge from the
    /// container's base — so it is the container's footprint grown by the exit distance, not a circle about its centre. One
    /// vertex per ray out of the container's base centre, marched outward in Movement.Step increments and bisected at the
    /// first refusal, which is the loop Movement.Region uses and is what stops a ring of impassable terrain re-admitting
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

    /// <summary>How many facings a non-circular base is tried at. p.7 lets a model take any facing, so a placement is legal
    /// if some yaw fits; the drawn region is the union over fitting yaws. Eight are 45° apart, which is
    /// finer than the region's own 2° rays are deep. A circular base is yaw-invariant and skips every one of them.</summary>
    public const int ExitYaws = 8;

    /// <summary>
    /// Is this position offered at any facing? Fully within the exit distance of the container's base, on the slab, and clear
    /// of the impassable volumes the Ask named. The last two are asked through Movement.Check with the candidate as both start
    /// and destination: the distance is zero, so Check samples exactly that placement and answers CrossesImpassable and
    /// LeavesTable for it — no new geometry primitive, and the same predicates the straight-line region uses.
    /// </summary>
    static bool Admits(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size) =>
        ExitYawAt(body, container, within, at, impassable, size) is not null;

    /// <summary>A facing this position is legal at, or null if none is. The panel asks for it when it builds the answer, so the
    /// placement it sends is one the server's own ExitPlacement will accept; a circular base answers at the first try.</summary>
    public static Yaw? ExitYawAt(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size)
    {
        var yaws = body.Base.Shape == BaseShape.Circle ? 1 : ExitYaws;
        for (var i = 0; i < yaws; i++)
        {
            var yaw = new Yaw(360.0 * i / yaws);
            if (AdmitsAt(body, container, within, at, yaw, impassable, size)) return yaw;
        }
        return null;
    }

    static bool AdmitsAt(ModelBody body, PlacedModel container, double within, Point2 at, Yaw yaw, IReadOnlyList<WorldVolume> impassable, TableSize size)
    {
        var candidate = new PlacedModel(body, new Placement(new Point3(at.X, at.Y, container.Placement.Position.Z), yaw));
        if (!Containment.BaseFullyWithin(candidate, container, within)) return false;
        var check = Movement.Check(candidate, at, 0, impassable, [], null, size);
        return !check.CrossesImpassable && !check.LeavesTable;
    }
}
