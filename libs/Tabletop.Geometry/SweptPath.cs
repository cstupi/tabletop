namespace Tabletop.Geometry;

/// <summary>One footprint of a moving base along its path: where its centre stands (the base at the start's facing) and whether any part of the base
/// there touches a dangerous volume.</summary>
public sealed record PathFootprint(Point2 At, bool Dangerous);

/// <summary>A stretch of a path's walked length, From to To inches from its start, along which any part of the base touches a dangerous volume.</summary>
public sealed record PathStretch(double From, double To);

/// <summary>A move's base swept along its path: the footprints along it, the dangerous volumes any part of the base enters or crosses and, one per
/// such volume, the stretch of the walk along which the base touches it (the marks both clients draw there take the dangerous colour).</summary>
public sealed record SweptBase(IReadOnlyList<PathFootprint> Footprints, IReadOnlyList<Guid> Dangerous, IReadOnlyList<PathStretch> Stretches);

/// <summary>
/// The base swept along the path a move is measured along (Movement.Measured), as both clients draw it and warn of it before a commit: the dangerous
/// volumes are Movement.VolumesOnPath's, the call the game server's ApplyMove tests with, so the warning and the server's test agree ("If any model
/// in a unit moves into or through Dangerous terrain", any part of the base counting). Footprints stand one base length (Base.A) apart along the
/// walked length from the start to the end, plus one where the base is deepest in each dangerous volume it touches, so a sliver shows.
/// </summary>
public static class SweptPath
{
    /// <summary>The volumes dangerous to a path of this length: the dangerous ones at any length, the unstable ones over `unstableOver` (what the move
    /// has left of the unstable distance, as a move Ask's UnstableOver and the server's ApplyMove read it).</summary>
    public static IReadOnlyList<WorldVolume> DangerousTo(double distance, IReadOnlyList<WorldVolume> dangerous, IReadOnlyList<WorldVolume> unstable, double unstableOver) =>
        unstable.Count > 0 && distance > unstableOver + Tolerance.Distance ? [.. dangerous, .. unstable] : dangerous;

    public static SweptBase Of(PlacedModel start, IReadOnlyList<Point2> path, IReadOnlyList<WorldVolume> dangerous)
    {
        if (path.Count == 0) path = [start.Placement.Position.Horizontal];
        var hits = Movement.VolumesOnPath(start, path, dangerous);
        var total = Length(path);
        var spacing = Math.Max(start.Body.Base.A, 0.25);
        var stations = new List<double>();
        for (var s = 0.0; s < total - Tolerance.Distance; s += spacing) stations.Add(s);
        stations.Add(total);
        var hitVolumes = dangerous.Where(v => hits.Contains(v.Id)).DistinctBy(v => v.Id).ToList();
        var stretches = hitVolumes.Select(v => Touching(start, path, total, v)).ToList();
        foreach (var t in stretches) stations.Add((t.From + t.To) / 2);
        stations.Sort();
        var footprints = stations.Select(s => At(path, s)).Select(p => new PathFootprint(p, hitVolumes.Count > 0 && Touches(start, p, hitVolumes))).ToList();
        return new SweptBase(footprints, hits, stretches);
    }

    /// <summary>The stretch along which the base touches the volume, from the first touch found on prefixes of the path to the last on suffixes; its
    /// middle is where the base is deepest in it.</summary>
    static PathStretch Touching(PlacedModel start, IReadOnlyList<Point2> path, double total, WorldVolume v)
    {
        bool Before(double s) => Movement.VolumesOnPath(start, Prefix(path, s), [v]).Count > 0;
        bool After(double s) { var from = At(path, s); return Movement.VolumesOnPath(Moved(start, from), Suffix(path, s), [v]).Count > 0; }
        double lo = 0, hi = total;
        for (var i = 0; i < 30 && hi - lo > 1e-4; i++) { var mid = (lo + hi) / 2; if (Before(mid)) hi = mid; else lo = mid; }
        var first = hi;
        lo = first; hi = total;
        for (var i = 0; i < 30 && hi - lo > 1e-4; i++) { var mid = (lo + hi) / 2; if (After(mid)) lo = mid; else hi = mid; }
        return new PathStretch(first, lo);
    }

    static bool Touches(PlacedModel start, Point2 at, IReadOnlyList<WorldVolume> volumes) => Movement.VolumesOnPath(Moved(start, at), [at], volumes).Count > 0;

    static PlacedModel Moved(PlacedModel start, Point2 at) =>
        start with { Placement = start.Placement with { Position = new Point3(at.X, at.Y, start.Placement.Position.Z) } };

    static double Length(IReadOnlyList<Point2> path)
    {
        var total = 0.0;
        for (var i = 1; i < path.Count; i++) total += path[i - 1].DistanceTo(path[i]);
        return total;
    }

    /// <summary>The point `s` along the path's walked length.</summary>
    static Point2 At(IReadOnlyList<Point2> path, double s)
    {
        for (var i = 1; i < path.Count; i++)
        {
            var leg = path[i - 1].DistanceTo(path[i]);
            if (s <= leg) return leg <= 0 ? path[i] : path[i - 1] + (path[i] - path[i - 1]) * (s / leg);
            s -= leg;
        }
        return path[^1];
    }

    static List<Point2> Prefix(IReadOnlyList<Point2> path, double s)
    {
        var points = new List<Point2> { path[0] };
        for (var i = 1; i < path.Count; i++)
        {
            var leg = path[i - 1].DistanceTo(path[i]);
            if (s <= leg) { points.Add(At([path[i - 1], path[i]], s)); return points; }
            points.Add(path[i]); s -= leg;
        }
        return points;
    }

    static List<Point2> Suffix(IReadOnlyList<Point2> path, double s)
    {
        var points = new List<Point2>();
        for (var i = 1; i < path.Count; i++)
        {
            var leg = path[i - 1].DistanceTo(path[i]);
            if (points.Count == 0 && s <= leg) points.Add(At([path[i - 1], path[i]], s));
            else if (points.Count == 0) { s -= leg; continue; }
            points.Add(path[i]);
        }
        if (points.Count == 0) points.Add(path[^1]);
        return points;
    }
}
