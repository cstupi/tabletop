using System.Buffers;

namespace Tabletop.Geometry;

/// <summary>
/// One sight-line measurement: Clear as <see cref="LineOfSight.Check"/>, and Blockers, in input order, the volumes that stop at least
/// one of its lines: <see cref="LineOfSightResult.ObstructedBy"/> when not clear, <see cref="Cover.SightBlockersBetween"/> when clear.
/// </summary>
public sealed record SightReading(bool Clear, IReadOnlyList<Guid> Blockers);

/// <summary>
/// The blocking volumes of one table prepared for many sight lines: each volume's world outline, heights and bounding box built once,
/// and per pair a box test that drops every volume none of the pair's lines can reach before the exact test. Answers equal
/// <see cref="LineOfSight.Check"/> and <see cref="Cover.SightBlockersBetween"/> over the same volumes less those with the ignored id.
/// </summary>
public sealed class SightBlockers
{
    /// <summary>The largest distance <see cref="RayMargin"/> returns: a clipped segment Tolerance.Distance long.</summary>
    const double PolygonRayBound = 4 + 2 * Tolerance.Distance;

    readonly IReadOnlyList<WorldVolume> volumes;
    readonly Entry[] entries;

    /// <summary>
    /// A blocking volume ready to test. Margin widens the box by how far outside the outline the exact test's tolerances still accept a
    /// segment; a polygon adds <see cref="RayMargin"/>, which depends on the clipped segment's length.
    /// </summary>
    readonly record struct Entry(Guid Id, Outline Outline, double Bottom, double Top, bool SeeInAndOut, bool Polygon,
        double MinX, double MinY, double MaxX, double MaxY);

    public SightBlockers(IReadOnlyList<WorldVolume> volumes)
    {
        this.volumes = volumes;
        entries = LineOfSight.Blockers(volumes).Select(Prepare).ToArray();
    }

    static Entry Prepare(WorldVolume v)
    {
        var outline = v.WorldOutline();
        var (minX, minY, maxX, maxY, margin) = outline switch
        {
            CircleOutline c => (c.Centre.X - c.Radius, c.Centre.Y - c.Radius, c.Centre.X + c.Radius, c.Centre.Y + c.Radius, 2 * Tolerance.Distance),
            PolygonOutline p => (p.Vertices.Min(x => x.X), p.Vertices.Min(x => x.Y), p.Vertices.Max(x => x.X), p.Vertices.Max(x => x.Y), PolygonMargin(p)),
            _ => throw new ArgumentOutOfRangeException(nameof(v)),
        };
        return new Entry(v.Id, outline, v.WorldBottom(), v.WorldTop(), v.Volume.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut),
            outline is PolygonOutline, minX - margin, minY - margin, maxX + margin, maxY + margin);
    }

    /// <summary>
    /// How far outside a convex polygon its tolerant tests accept a point: `Contains` lets a point Tolerance.Distance / L past an edge of
    /// length L, which past a corner of interior angle θ reaches that over sin(θ/2); `OnSegment` along an edge reaches 2·Tolerance.Distance / min(L, 1).
    /// Doubled for rounding. Infinite for a degenerate corner, so such a polygon is never dropped.
    /// </summary>
    static double PolygonMargin(PolygonOutline p)
    {
        var vs = p.Vertices;
        double minEdge = double.PositiveInfinity, minSinHalf = 1;
        for (var i = 0; i < vs.Count; i++)
        {
            var (prev, at, next) = (vs[(i + vs.Count - 1) % vs.Count], vs[i], vs[(i + 1) % vs.Count]);
            var (a, b) = (prev - at, next - at);
            minEdge = Math.Min(minEdge, b.Length);
            var cos = a.Dot(b) / (a.Length * b.Length);
            minSinHalf = Math.Min(minSinHalf, Math.Sqrt(Math.Max(0, (1 - cos) / 2)));
        }
        if (!(minEdge > 0) || !(minSinHalf > 1e-9)) return double.PositiveInfinity;
        var contains = Tolerance.Distance / minEdge / minSinHalf;
        var onEdge = 2 * Tolerance.Distance / Math.Min(minEdge, 1);
        return 2 * Math.Max(contains, onEdge) + Tolerance.Distance;
    }

    /// <summary>
    /// How far from a clipped segment of length s a polygon's vertex can lie and still count as on it (`OnSegment` with the segment as
    /// its first edge): Tolerance.Distance when s is shorter than that, else Tolerance.Distance · max(1, s) / s across and Tolerance.Distance / s along. Doubled for rounding.
    /// </summary>
    static double RayMargin(double s) =>
        s < Tolerance.Distance ? 2 * Tolerance.Distance : 2 * (Tolerance.Distance * Math.Max(1, s) / s + Tolerance.Distance / s) + Tolerance.Distance;

    /// <summary>Equals <see cref="LineOfSight.Check"/> over the volumes less those whose id is <paramref name="ignoring"/>.</summary>
    public LineOfSightResult Check(PlacedModel from, PlacedModel to, Guid? ignoring = null)
    {
        var clear = Measure(from, to, ignoring, stopAtClear: true, out var blockers);
        return new LineOfSightResult(clear, clear ? [] : blockers);
    }

    /// <summary>Every line drawn: Clear as <see cref="Check"/>, Blockers the volumes stopping any of them, in input order.</summary>
    public SightReading Measure(PlacedModel from, PlacedModel to, Guid? ignoring = null)
    {
        var clear = Measure(from, to, ignoring, stopAtClear: false, out var blockers);
        return new SightReading(clear, blockers);
    }

    bool Measure(PlacedModel from, PlacedModel to, Guid? ignoring, bool stopAtClear, out IReadOnlyList<Guid> blockers)
    {
        var eye = from.Eye();
        Span<Point3> targets = stackalloc Point3[3 * (1 + LineOfSight.RimSamples)];
        var count = LineOfSight.TargetPoints(to, targets);
        targets = targets[..count];

        double minX = eye.X, minY = eye.Y, minZ = eye.Z, maxX = eye.X, maxY = eye.Y, maxZ = eye.Z;
        foreach (var t in targets)
        {
            (minX, maxX) = (Math.Min(minX, t.X), Math.Max(maxX, t.X));
            (minY, maxY) = (Math.Min(minY, t.Y), Math.Max(maxY, t.Y));
            (minZ, maxZ) = (Math.Min(minZ, t.Z), Math.Max(maxZ, t.Z));
        }

        const int OnStack = 256;
        var rented = entries.Length > OnStack ? ArrayPool<int>.Shared.Rent(entries.Length) : null;
        var hitRented = entries.Length > OnStack ? ArrayPool<bool>.Shared.Rent(entries.Length) : null;
        Span<int> candidates = stackalloc int[OnStack];
        Span<bool> hit = stackalloc bool[OnStack];
        if (rented is not null) candidates = rented;
        if (hitRented is not null) hit = hitRented;
        try
        {
            var n = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                ref readonly var e = ref entries[i];
                if (e.Id == ignoring) continue;
                if (e.Bottom - Tolerance.Distance * 2 > maxZ || e.Top + Tolerance.Distance * 2 < minZ) continue;
                var slack = e.Polygon ? PolygonRayBound : 0;
                if (e.MinX - slack > maxX || e.MaxX + slack < minX || e.MinY - slack > maxY || e.MaxY + slack < minY) continue;
                candidates[n] = i;
                hit[n++] = false;
            }

            var clear = false;
            var anyHit = false;
            foreach (var target in targets)
            {
                var rayHit = false;
                for (var c = 0; c < n; c++)
                {
                    if (!Obstructs(entries[candidates[c]], eye, target)) continue;
                    hit[c] = rayHit = anyHit = true;
                }
                if (rayHit) continue;
                clear = true;
                if (stopAtClear) break;
            }

            if (!anyHit || (clear && stopAtClear)) { blockers = []; return clear; }
            var ids = new HashSet<Guid>();
            for (var c = 0; c < n; c++) if (hit[c]) ids.Add(entries[candidates[c]].Id);
            blockers = volumes.Where(v => v.Id != ignoring && ids.Contains(v.Id)).Select(v => v.Id).ToList();
            return clear;
        }
        finally
        {
            if (rented is not null) ArrayPool<int>.Shared.Return(rented);
            if (hitRented is not null) ArrayPool<bool>.Shared.Return(hitRented);
        }
    }

    /// <summary>`LineOfSight.Obstructions` for one volume, with the outline built once and a box test before the exact one.</summary>
    static bool Obstructs(in Entry e, Point3 a, Point3 b)
    {
        if (!WorldVolumes.ClipToRange(e.Bottom, e.Top, a, b, out var p, out var q)) return false;
        var margin = e.Polygon ? RayMargin(p.DistanceTo(q)) : 0;
        if (e.MinX - margin > Math.Max(p.X, q.X) || e.MaxX + margin < Math.Min(p.X, q.X)
            || e.MinY - margin > Math.Max(p.Y, q.Y) || e.MaxY + margin < Math.Min(p.Y, q.Y)) return false;
        if (!e.Outline.IntersectsSegment(p, q)) return false;
        return !e.SeeInAndOut || !(Inside(e, a) || Inside(e, b));
    }

    static bool Inside(in Entry e, Point3 p) =>
        p.Z >= e.Bottom - Tolerance.Distance && p.Z <= e.Top + Tolerance.Distance && e.Outline.Contains(p.Horizontal);
}
