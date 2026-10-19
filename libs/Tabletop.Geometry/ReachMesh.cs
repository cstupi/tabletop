namespace Tabletop.Geometry;

/// <summary>
/// Triangles covering where a field is at most zero, over the square of half-side `reach` about `origin`. The field is a path length less a limit:
/// it changes no faster than the distance moved, except where an obstacle or the table edge cuts it off. So the square is split as a quadtree: a cell
/// the field is clear of zero on by more than its diagonal, with no edge crossing it, is drawn whole (or not at all); one wholly inside an obstacle is
/// dropped; the rest are split down to cells of about 0.25", which marching squares cut where the field crosses zero: interpolated along a cell's
/// edge where the field is smooth there, bisected where an obstacle cuts it off.
/// </summary>
internal static class ReachMesh
{
    const int Levels = 3;

    public static IReadOnlyList<Point2> Build(Point2 origin, double reach, Func<Point2, double> value, Func<Point2, double, MoveSpace.Cover> cover)
    {
        var top = 1 << Levels;
        var fine = Math.Max(0.25, 2 * reach / 256);
        var cells = (int)Math.Ceiling(2 * reach / fine / top) * top;
        var corner = origin - new Point2(cells * fine / 2, cells * fine / 2);
        Point2 At(int i, int j) => corner + new Point2(i * fine, j * fine);
        var values = new double[cells + 1, cells + 1];
        for (var i = 0; i <= cells; i++) for (var j = 0; j <= cells; j++) values[i, j] = double.NaN;
        double V(int i, int j) => double.IsNaN(values[i, j]) ? values[i, j] = value(At(i, j)) : values[i, j];
        var crossings = new Dictionary<(int, int, int, int), Point2>();
        Point2 Cross(int i0, int j0, int i1, int j1)
        {
            var key = i0 < i1 || (i0 == i1 && j0 < j1) ? (i0, j0, i1, j1) : (i1, j1, i0, j0);
            if (crossings.TryGetValue(key, out var hit)) return hit;
            Point2 inside = At(i0, j0), outside = At(i1, j1);
            double vi = V(i0, j0), vo = V(i1, j1);
            if (vi > 0) { (inside, outside) = (outside, inside); (vi, vo) = (vo, vi); }
            // Changing no faster than the distance moved, the field is smooth along this edge and the crossing is where the line meets zero.
            if (vo - vi <= fine * 1.01) return crossings[key] = inside + (outside - inside) * (-vi / (vo - vi));
            for (var k = 0; k < 7; k++)
            {
                var mid = (inside + outside) * 0.5;
                if (value(mid) <= 0) inside = mid; else outside = mid;
            }
            return crossings[key] = inside;
        }
        var triangles = new List<Point2>();
        void Add(Point2 a, Point2 b, Point2 c)
        {
            if (Math.Abs((b - a).Cross(c - a)) < 1e-12) return;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }
        (int, int)[] ring = [(0, 0), (1, 0), (1, 1), (0, 1)];
        void March(int i, int j)
        {
            var ins = new bool[4];
            var count = 0;
            for (var c = 0; c < 4; c++) if (ins[c] = V(i + ring[c].Item1, j + ring[c].Item2) <= 0) count++;
            if (count == 0) return;
            if (count == 2 && ins[0] == ins[2] && value(At(i, j) + new Point2(fine / 2, fine / 2)) > 0)
            {
                for (var c = 0; c < 4; c++)
                {
                    if (!ins[c]) continue;
                    var (a, b) = (ring[c], ring[(c + 1) % 4]); var prev = ring[(c + 3) % 4];
                    Add(At(i + a.Item1, j + a.Item2), Cross(i + a.Item1, j + a.Item2, i + b.Item1, j + b.Item2), Cross(i + prev.Item1, j + prev.Item2, i + a.Item1, j + a.Item2));
                }
                return;
            }
            var polygon = new List<Point2>(8);
            for (var c = 0; c < 4; c++)
            {
                var (a, b) = (ring[c], ring[(c + 1) % 4]);
                if (ins[c]) polygon.Add(At(i + a.Item1, j + a.Item2));
                if (ins[c] != ins[(c + 1) % 4]) polygon.Add(Cross(i + a.Item1, j + a.Item2, i + b.Item1, j + b.Item2));
            }
            var centre = new Point2(polygon.Average(p => p.X), polygon.Average(p => p.Y));
            for (var k = 0; k < polygon.Count; k++) Add(centre, polygon[k], polygon[(k + 1) % polygon.Count]);
        }
        void Cell(int i, int j, int size)
        {
            if (size == 1) { March(i, j); return; }
            var half = size * fine / 2;
            var kind = cover(At(i, j) + new Point2(half, half), half);
            if (kind == MoveSpace.Cover.Covered) return;
            if (kind == MoveSpace.Cover.Free)
            {
                var margin = size * fine * Math.Sqrt(2);
                double a = V(i, j), b = V(i + size, j), c = V(i + size, j + size), d = V(i, j + size);
                if (a <= -margin && b <= -margin && c <= -margin && d <= -margin)
                {
                    Add(At(i, j), At(i + size, j), At(i + size, j + size));
                    Add(At(i, j), At(i + size, j + size), At(i, j + size));
                    return;
                }
                if (a >= margin && b >= margin && c >= margin && d >= margin) return;
            }
            var s = size / 2;
            Cell(i, j, s); Cell(i + s, j, s); Cell(i, j + s, s); Cell(i + s, j + s, s);
        }
        for (var i = 0; i < cells; i += top)
            for (var j = 0; j < cells; j += top)
                Cell(i, j, top);
        return triangles;
    }
}
