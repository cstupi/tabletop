using Tabletop.Geometry;
namespace Tabletop.Client.Core;

/// <summary>Readable rings plan: how a ring of each kind draws. FillAlpha is the team colour's opacity over the area, LineWidth the outline in
/// screen pixels, Icon the theme icon its label carries.</summary>
public sealed record RingLook(float FillAlpha, float LineWidth, string Icon)
{
    public static readonly RingLook Charge = new(0.22f, 3f, "charge"), Shoot = new(0.12f, 3f, "shoot"), Range = new(0.08f, 2.5f, "shoot");
    /// <summary>The area a placement keeps out of: a heavy shade (<see cref="KeepAwayArea.Shade"/>, never a team colour), a thick rim, the warning icon.</summary>
    public static readonly RingLook KeepAway = new(0.45f, 3.5f, "warning");
    public static RingLook Of(RingKind kind) => kind switch { RingKind.Charge => Charge, RingKind.Shoot => Shoot, RingKind.KeepAway => KeepAway, _ => Range };
}

/// <summary>A ring's outline segments as closed loops, the area they enclose as triangles, and the points along them where a label may stand.
/// An outer loop runs counter-clockwise (positive area) and a hole clockwise; a hole is bridged into the outer loop holding it, so the fill
/// leaves it open. Built once per ring set, never per frame.</summary>
public sealed record RingShape(IReadOnlyList<IReadOnlyList<Point2>> Loops, IReadOnlyList<Point2> Triangles, IReadOnlyList<Point2> Candidates)
{
    public const double Join = 1e-3;
    public const int Labels = 48;

    public static RingShape Of(IReadOnlyList<(Point2 A, Point2 B)> segments, int candidates = Labels)
    {
        var loops = LoopsOf(segments);
        return new(loops, Fill(loops), Spread(loops, candidates));
    }

    /// <summary>Twice-signed area halved: positive counter-clockwise.</summary>
    public static double Area(IReadOnlyList<Point2> loop)
    {
        var a = 0.0;
        for (var i = 0; i < loop.Count; i++) a += loop[i].Cross(loop[(i + 1) % loop.Count]);
        return a / 2;
    }

    /// <summary>Each segment's end joined to the start of another within <see cref="Join"/>; a chain that does not close is dropped.</summary>
    public static IReadOnlyList<IReadOnlyList<Point2>> LoopsOf(IReadOnlyList<(Point2 A, Point2 B)> segments)
    {
        static (long, long) Cell(Point2 p) => ((long)Math.Floor(p.X / Join), (long)Math.Floor(p.Y / Join));
        var starts = new Dictionary<(long, long), List<int>>();
        for (var i = 0; i < segments.Count; i++)
        {
            var c = Cell(segments[i].A);
            if (!starts.TryGetValue(c, out var list)) starts[c] = list = [];
            list.Add(i);
        }
        var used = new bool[segments.Count];
        int Next(Point2 at)
        {
            var (cx, cy) = Cell(at); var best = -1; var bestD = Join;
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (starts.TryGetValue((cx + dx, cy + dy), out var list))
                        foreach (var i in list)
                            if (!used[i] && segments[i].A.DistanceTo(at) is var d && d <= bestD) { best = i; bestD = d; }
            return best;
        }
        var loops = new List<IReadOnlyList<Point2>>();
        for (var s = 0; s < segments.Count; s++)
        {
            if (used[s]) continue;
            used[s] = true;
            var loop = new List<Point2> { segments[s].A }; var at = segments[s].B; var closed = false;
            while (true)
            {
                if (at.DistanceTo(loop[0]) <= Join && loop.Count > 2) { closed = true; break; }
                var n = Next(at);
                if (n < 0) break;
                used[n] = true; loop.Add(segments[n].A); at = segments[n].B;
            }
            if (closed) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>The loops' area as triangles (three points each): every outer loop with the holes inside it bridged in, ear-clipped.</summary>
    public static IReadOnlyList<Point2> Fill(IReadOnlyList<IReadOnlyList<Point2>> loops)
    {
        var outers = loops.Where(l => Area(l) > 0).OrderBy(Area).ToList();
        var holes = loops.Where(l => Area(l) < 0).ToList();
        var within = outers.ToDictionary(o => o, _ => new List<IReadOnlyList<Point2>>());
        foreach (var h in holes)
            if (outers.FirstOrDefault(o => Inside(o, h[0])) is { } o) within[o].Add(h);   // the smallest outer holding it
        var triangles = new List<Point2>();
        foreach (var o in outers) EarClip(Bridge(o, within[o]), triangles);
        return triangles;
    }

    /// <summary>Points spread evenly by length along every loop, at most count.</summary>
    public static IReadOnlyList<Point2> Spread(IReadOnlyList<IReadOnlyList<Point2>> loops, int count)
    {
        var total = loops.Sum(Perimeter);
        if (total <= 0 || count <= 0) return [];
        var step = total / count; var points = new List<Point2>(); var carry = 0.0;
        foreach (var loop in loops)
            for (var i = 0; i < loop.Count; i++)
            {
                Point2 a = loop[i], b = loop[(i + 1) % loop.Count]; var len = a.DistanceTo(b);
                while (carry <= len && points.Count < count) { points.Add(len == 0 ? a : a + (b - a) * (carry / len)); carry += step; }
                carry -= len;
            }
        return points;
    }

    static double Perimeter(IReadOnlyList<Point2> loop) => Enumerable.Range(0, loop.Count).Sum(i => loop[i].DistanceTo(loop[(i + 1) % loop.Count]));

    public static bool Inside(IReadOnlyList<Point2> loop, Point2 q)
    {
        var inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            Point2 a = loop[i], b = loop[j];
            if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>Each hole joined to the outer loop by a cut from its rightmost point to the nearest outer point the cut reaches without
    /// crossing an edge; the polygon then runs out along the cut, round the hole, and back.</summary>
    static List<Point2> Bridge(IReadOnlyList<Point2> outer, List<IReadOnlyList<Point2>> holes)
    {
        var poly = outer.ToList();
        var pending = holes.OrderByDescending(h => h.Max(p => p.X)).ToList();
        while (pending.Count > 0)
        {
            var hole = pending[0]; pending.RemoveAt(0);
            var mi = Enumerable.Range(0, hole.Count).MaxBy(i => hole[i].X); var m = hole[mi];
            var edges = Edges(poly).Concat(pending.SelectMany(Edges)).Concat(Edges(hole)).ToList();
            var best = -1; var bestD = double.MaxValue;
            for (var i = 0; i < poly.Count; i++)
            {
                var d = poly[i].DistanceTo(m);
                if (d >= bestD || edges.Any(e => Crosses(m, poly[i], e.A, e.B))) continue;
                best = i; bestD = d;
            }
            if (best < 0) best = Enumerable.Range(0, poly.Count).MinBy(i => poly[i].DistanceTo(m));
            var merged = poly.Take(best + 1).ToList();
            for (var k = 0; k <= hole.Count; k++) merged.Add(hole[(mi + k) % hole.Count]);
            merged.Add(poly[best]); merged.AddRange(poly.Skip(best + 1));
            poly = merged;
        }
        return poly;
    }

    static IEnumerable<(Point2 A, Point2 B)> Edges(IReadOnlyList<Point2> loop) => Enumerable.Range(0, loop.Count).Select(i => (loop[i], loop[(i + 1) % loop.Count]));

    /// <summary>Do the open segments pq and ab cross at a point inside both (a shared end is no crossing).</summary>
    static bool Crosses(Point2 p, Point2 q, Point2 a, Point2 b)
    {
        const double E = 1e-12;
        if (p.DistanceTo(a) < E || p.DistanceTo(b) < E || q.DistanceTo(a) < E || q.DistanceTo(b) < E) return false;
        double d1 = (q - p).Cross(a - p), d2 = (q - p).Cross(b - p), d3 = (b - a).Cross(p - a), d4 = (b - a).Cross(q - a);
        return ((d1 > E && d2 < -E) || (d1 < -E && d2 > E)) && ((d3 > E && d4 < -E) || (d3 < -E && d4 > E));
    }

    /// <summary>Ear clipping of a counter-clockwise polygon; only a reflex vertex can stand inside an ear, so only those are tested.</summary>
    static void EarClip(List<Point2> poly, List<Point2> into)
    {
        var idx = Enumerable.Range(0, poly.Count).ToList();
        bool Convex(int a, int b, int c) => (poly[b] - poly[a]).Cross(poly[c] - poly[b]) > 1e-12;
        var guard = 0;
        while (idx.Count > 3 && guard < 4 * poly.Count + 16)
        {
            var reflex = new List<int>();
            for (var k = 0; k < idx.Count; k++) if (!Convex(idx[(k + idx.Count - 1) % idx.Count], idx[k], idx[(k + 1) % idx.Count])) reflex.Add(idx[k]);
            var clipped = 0;
            for (var k = 0; k < idx.Count && idx.Count > 3;)
            {
                int a = idx[(k + idx.Count - 1) % idx.Count], b = idx[k], c = idx[(k + 1) % idx.Count];
                if (Convex(a, b, c) && !reflex.Any(r => r != a && r != b && r != c && !Same(poly[r], poly[a]) && !Same(poly[r], poly[b]) && !Same(poly[r], poly[c])
                        && InTriangle(poly[r], poly[a], poly[b], poly[c])))
                {
                    into.Add(poly[a]); into.Add(poly[b]); into.Add(poly[c]);
                    idx.RemoveAt(k); reflex.Remove(b); clipped++;
                    if (k >= idx.Count) k = 0;
                    continue;
                }
                k++;
            }
            if (clipped == 0) { guard = int.MaxValue; break; }   // degenerate: what is left stays open
            guard++;
        }
        if (idx.Count == 3 && Convex(idx[0], idx[1], idx[2])) { into.Add(poly[idx[0]]); into.Add(poly[idx[1]]); into.Add(poly[idx[2]]); }
    }

    static bool Same(Point2 a, Point2 b) => a.DistanceTo(b) < 1e-12;

    static bool InTriangle(Point2 q, Point2 a, Point2 b, Point2 c) =>
        (b - a).Cross(q - a) >= 0 && (c - b).Cross(q - b) >= 0 && (a - c).Cross(q - c) >= 0;
}
