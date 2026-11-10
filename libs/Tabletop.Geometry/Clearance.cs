namespace Tabletop.Geometry;

/// <summary>
/// A placement held "over X away" from models, measured base to base (p.5), with an area where the distance is lifted: the area a placed base's
/// edge may not enter (each model's base grown by its own distance, unioned, less the area within `liftedWithin` of a lifted model's base), and the
/// test of one placed base against it.
/// </summary>
public static class Clearance
{
    /// <summary>The index of the first of `from` whose base is within over[i] of the placed base, or -1: none, or the placed base is within
    /// `liftedWithin` of a lifted model's base (an Ambush Beacon's "may ignore distance restrictions").</summary>
    public static int Breaks(PlacedModel placed, IReadOnlyList<PlacedModel> from, IReadOnlyList<double> over, IReadOnlyList<PlacedModel> lifted, double liftedWithin)
    {
        if (over.Count != from.Count) throw new ArgumentException("one distance per model", nameof(over));
        if (lifted.Any(l => Distance.InRange(placed, l, liftedWithin))) return -1;
        for (var i = 0; i < from.Count; i++)
            if (Distance.InRange(placed, from[i], over[i])) return i;
        return -1;
    }

    /// <summary>
    /// The outline of the area a placed base's edge may not enter, as RangeOutline segments: an outer loop counter-clockwise, a hole clockwise. With
    /// lifted models, the rings' arcs inside the lifted area are cut away and the lifted area's arcs inside the rings are added, reversed, so the
    /// loops close round what is left.
    /// </summary>
    public static IReadOnlyList<(Point2 A, Point2 B)> Outline(IReadOnlyList<PlacedModel> from, IReadOnlyList<double> over, IReadOnlyList<PlacedModel> lifted, double liftedWithin)
    {
        if (from.Count == 0) return [];
        var rings = RangeOutline.Of(from, over);
        if (lifted.Count == 0 || liftedWithin <= 0) return rings;
        var fromEdges = from.Select((m, i) => (Edge: new BaseEdge(m), Over: over[i])).ToList();
        var liftEdges = lifted.Select(m => new BaseEdge(m)).ToList();
        bool InLifted(Point2 q) => liftEdges.Any(e => e.To(q) < liftedWithin);
        bool InRings(Point2 q) => fromEdges.Any(e => e.Edge.To(q) < e.Over);

        var cuts = new List<Point2>();
        var kept = Keep(rings, q => !InLifted(q), cuts, snap: null);
        var hole = RangeOutline.Of(lifted, liftedWithin);
        var added = Keep(hole, InRings, null, snap: cuts);
        return [.. kept, .. added.Select(s => (s.B, s.A))];
    }

    /// <summary>The parts of the segments where `keep` holds, each segment cut where `keep` changes along it (bisected). A cut is recorded in
    /// `cuts`, or snapped to the nearest of `snap`, so the two outlines' pieces meet at the same point.</summary>
    static List<(Point2 A, Point2 B)> Keep(IReadOnlyList<(Point2 A, Point2 B)> segments, Func<Point2, bool> keep, List<Point2>? cuts, List<Point2>? snap)
    {
        var result = new List<(Point2 A, Point2 B)>();
        foreach (var (a, b) in segments)
        {
            bool ka = keep(a), kb = keep(b);
            if (ka && kb) { result.Add((a, b)); continue; }
            if (!ka && !kb) continue;
            double lo = 0, hi = 1;
            for (var n = 0; n < 40; n++) { var mid = (lo + hi) / 2; if (keep(a + (b - a) * mid) == ka) lo = mid; else hi = mid; }
            var cut = a + (b - a) * ((lo + hi) / 2);
            if (cuts is not null) cuts.Add(cut);
            if (snap is { Count: > 0 }) cut = snap.MinBy(s => s.DistanceTo(cut));
            result.Add(ka ? (a, cut) : (cut, b));
        }
        return result;
    }
}
