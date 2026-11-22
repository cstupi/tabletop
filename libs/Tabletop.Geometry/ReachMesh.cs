using System.Runtime.CompilerServices;

namespace Tabletop.Geometry;

/// <summary>
/// Triangles covering where a field is at most zero, over the square of half-side `reach` about `origin`. The field is a path length less a limit:
/// it changes no faster than the distance moved, except where an obstacle or the table edge cuts it off. So the square is split as a quadtree: a cell
/// the field is clear of zero on by more than its diagonal, with no edge crossing it, is drawn whole (or not at all); one wholly inside an obstacle is
/// dropped; the rest are split down to cells of about 0.25", which marching squares cut where the field crosses zero: interpolated along a cell's
/// edge where the field is smooth there, bisected where an obstacle cuts it off; a fine cell wholly inside is two triangles. Several fields (bands)
/// share one walk and one evaluation per point: a cell is drawn whole or dropped only when every band is clear of zero on it. The coarsest cells are
/// 32 fine cells (8") across, so open ground deep inside the area is a few large quads. Several models' areas drawn together are one walk over a box
/// holding them all, each band's field the least of the models' (MoveReach.Union).
/// </summary>
internal static class ReachMesh
{
    const int Levels = 5;

    /// <summary>Each band's field at a point, written into `into` (one value per band).</summary>
    public delegate void Fields(Point2 p, Span<double> into);

    public static IReadOnlyList<Point2> Build(Point2 origin, double reach, Func<Point2, double> value, Func<Point2, double, MoveSpace.Cover> cover) =>
        Build(origin, reach, 1, (p, into) => into[0] = value(p), cover)[0];

    /// <summary>
    /// How far from zero a value tells nothing more: past every coarse cell's diagonal (the most a cell is decided by) and a fine cell's side (the
    /// most a crossing is interpolated over), the walk reads only its sign. A field held within this of zero gives the same triangles.
    /// </summary>
    public static double Deep(double fine) => (1 << Levels) * fine * Math.Sqrt(2) + 2 * fine;

    /// <summary>The fine cell's side for an area reaching this far: 0.25", coarser past a 32" reach.</summary>
    public static double Fine(double reach) => Math.Max(0.25, 2 * reach / 256);

    public static List<Point2>[] Build(Point2 origin, double reach, int bands, Fields fields, Func<Point2, double, MoveSpace.Cover> cover) =>
        Build(origin, origin - new Point2(reach, reach), origin + new Point2(reach, reach), Fine(reach), bands, fields, cover);

    /// <summary>Over the box from `min` to `max`, rounded out to whole coarse cells about its centre, in fine cells of side `fine` whose corners are
    /// `anchor` plus whole cells (one area's own grid is anchored on its origin).</summary>
    public static List<Point2>[] Build(Point2 anchor, Point2 min, Point2 max, double fine, int bands, Fields fields, Func<Point2, double, MoveSpace.Cover> cover)
    {
        var top = 1 << Levels;
        (int First, int Count) Span(double lo, double hi, double at)
        {
            var first = (int)Math.Floor((lo - at) / fine + 1e-9);
            var n = Math.Max(1, (int)Math.Ceiling((hi - at) / fine - 1e-9) - first);
            var count = (n + top - 1) / top * top;
            return (first - (count - n) / 2, count);
        }
        var (i0, across) = Span(min.X, max.X, anchor.X);
        var (j0, along) = Span(min.Y, max.Y, anchor.Y);
        var corner = anchor + new Point2(i0 * fine, j0 * fine);
        Point2 At(int i, int j) => corner + new Point2(i * fine, j * fine);
        // One coarse cell from grid point (first, low): its own grid values, crossings and triangles.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        List<Point2>[] Coarse(int first, int low)
        {
            var values = new double[top + 1, top + 1, bands];
            var known = new bool[top + 1, top + 1];
            var buffer = new double[bands];
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            double Value(Point2 p, int k)
            {
                fields(p, buffer);
                return buffer[k];
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            double V(int i, int j, int k)
            {
                int x = i - first, y = j - low;
                if (!known[x, y])
                {
                    fields(At(i, j), buffer);
                    for (var b = 0; b < bands; b++) values[x, y, b] = buffer[b];
                    known[x, y] = true;
                }
                return values[x, y, k];
            }
            var crossings = new Dictionary<(int, int, int, int, int), Point2>();
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            Point2 Cross(int k, int i0, int j0, int i1, int j1)
            {
                var key = i0 < i1 || (i0 == i1 && j0 < j1) ? (k, i0, j0, i1, j1) : (k, i1, j1, i0, j0);
                if (crossings.TryGetValue(key, out var hit)) return hit;
                Point2 inside = At(i0, j0), outside = At(i1, j1);
                double vi = V(i0, j0, k), vo = V(i1, j1, k);
                if (vi > 0) { (inside, outside) = (outside, inside); (vi, vo) = (vo, vi); }
                // Changing no faster than the distance moved, the field is smooth along this edge and the crossing is where the line meets zero.
                if (vo - vi <= fine * 1.01) return crossings[key] = inside + (outside - inside) * (-vi / (vo - vi));
                for (var n = 0; n < 7; n++)
                {
                    var mid = (inside + outside) * 0.5;
                    if (Value(mid, k) <= 0) inside = mid; else outside = mid;
                }
                return crossings[key] = inside;
            }
            var triangles = new List<Point2>[bands];
            for (var b = 0; b < bands; b++) triangles[b] = [];
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void Add(int k, Point2 a, Point2 b, Point2 c)
            {
                if (Math.Abs((b - a).Cross(c - a)) < 1e-12) return;
                triangles[k].Add(a); triangles[k].Add(b); triangles[k].Add(c);
            }
            (int, int)[] ring = [(0, 0), (1, 0), (1, 1), (0, 1)];
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void March(int i, int j, int k)
            {
                var ins = new bool[4];
                var count = 0;
                for (var c = 0; c < 4; c++) if (ins[c] = V(i + ring[c].Item1, j + ring[c].Item2, k) <= 0) count++;
                if (count == 0) return;
                if (count == 4)
                {
                    Add(k, At(i, j), At(i + 1, j), At(i + 1, j + 1));
                    Add(k, At(i, j), At(i + 1, j + 1), At(i, j + 1));
                    return;
                }
                if (count == 2 && ins[0] == ins[2] && Value(At(i, j) + new Point2(fine / 2, fine / 2), k) > 0)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        if (!ins[c]) continue;
                        var (a, b) = (ring[c], ring[(c + 1) % 4]); var prev = ring[(c + 3) % 4];
                        Add(k, At(i + a.Item1, j + a.Item2), Cross(k, i + a.Item1, j + a.Item2, i + b.Item1, j + b.Item2), Cross(k, i + prev.Item1, j + prev.Item2, i + a.Item1, j + a.Item2));
                    }
                    return;
                }
                var polygon = new List<Point2>(8);
                for (var c = 0; c < 4; c++)
                {
                    var (a, b) = (ring[c], ring[(c + 1) % 4]);
                    if (ins[c]) polygon.Add(At(i + a.Item1, j + a.Item2));
                    if (ins[c] != ins[(c + 1) % 4]) polygon.Add(Cross(k, i + a.Item1, j + a.Item2, i + b.Item1, j + b.Item2));
                }
                // Its corners lie round the cell's edge in order, so the polygon is convex: a fan from its first corner.
                for (var n = 1; n + 1 < polygon.Count; n++) Add(k, polygon[0], polygon[n], polygon[n + 1]);
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void Cell(int i, int j, int size)
            {
                if (size == 1) { for (var k = 0; k < bands; k++) March(i, j, k); return; }
                var half = size * fine / 2;
                var kind = cover(At(i, j) + new Point2(half, half), half);
                if (kind == MoveSpace.Cover.Covered) return;
                if (kind == MoveSpace.Cover.Free)
                {
                    var margin = size * fine * Math.Sqrt(2);
                    var decided = true;
                    for (var k = 0; k < bands && decided; k++)
                    {
                        double a = V(i, j, k), b = V(i + size, j, k), c = V(i + size, j + size, k), d = V(i, j + size, k);
                        decided = (a <= -margin && b <= -margin && c <= -margin && d <= -margin) || (a >= margin && b >= margin && c >= margin && d >= margin);
                    }
                    if (decided)
                    {
                        for (var k = 0; k < bands; k++)
                        {
                            if (V(i, j, k) > 0) continue;
                            Add(k, At(i, j), At(i + size, j), At(i + size, j + size));
                            Add(k, At(i, j), At(i + size, j + size), At(i, j + size));
                        }
                        return;
                    }
                }
                var s = size / 2;
                Cell(i, j, s); Cell(i + s, j, s); Cell(i, j + s, s); Cell(i + s, j + s, s);
            }
            Cell(first, low, top);
            return triangles;
        }
        // Coarse cells share nothing but the fields, which any thread may read, and each point's values are the same whichever cell asks: the
        // triangles are the same, in the same order, as one walk cell by cell. The browser's WebAssembly has one thread.
        var rows = along / top;
        var columns = new List<Point2>[across / top * rows][];
        void Walk(int c) => columns[c] = Coarse(c / rows * top, c % rows * top);
        if (OperatingSystem.IsBrowser() || columns.Length == 1) for (var c = 0; c < columns.Length; c++) Walk(c);
        else Parallel.For(0, columns.Length, Walk);
        var triangles = new List<Point2>[bands];
        for (var k = 0; k < bands; k++)
        {
            triangles[k] = new List<Point2>(columns.Sum(c => c[k].Count));
            foreach (var c in columns) triangles[k].AddRange(c[k]);
        }
        return triangles;
    }
}
