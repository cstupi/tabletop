namespace Tabletop.Geometry.Tests;

/// <summary>
/// The area's walk (ReachMesh: coarse cells up to 8", a fine cell wholly inside as two triangles, coarse cells on several threads) against the plain
/// walk it replaced (PlainWalk: cells up to 2", four triangles a fine cell, one thread): the same area in every band and the same points inside, over
/// oval bases among many bodies, difficult, dangerous and unstable ground (CavalryRushScene). And several models' areas as one (MoveReach.Union)
/// against each model's own: a point clear of every band's edge by more than a fine cell's chord is in each band some model reaches it in.
/// </summary>
[Collection(TimingCollection.Name)]
public class ReachMeshReferenceTests
{
    static MoveReach Rider(int i, bool dangers = true)
    {
        var difficult = CavalryRushScene.DifficultGround.Skip(1).Concat(Movement.SteepEntering(CavalryRushScene.Riders[i], CavalryRushScene.Hills)).ToList();
        return MoveReach.Build(CavalryRushScene.Riders[i], 16, [CavalryRushScene.Building], difficult, 6, CavalryRushScene.Table, CavalryRushScene.Others,
            CavalryRushScene.Volumes, dangers ? [CavalryRushScene.DifficultGround[0]] : null, dangers ? [CavalryRushScene.Lairs[1]] : null, 6);
    }

    static double Area(IReadOnlyList<Point2> t)
    {
        var sum = 0.0;
        for (var i = 0; i + 2 < t.Count; i += 3) sum += Math.Abs((t[i + 1] - t[i]).Cross(t[i + 2] - t[i])) / 2;
        return sum;
    }

    static bool Inside(IReadOnlyList<Point2> triangles, Point2 p)
    {
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            var (a, b, c) = (triangles[i], triangles[i + 1], triangles[i + 2]);
            double d1 = (b - a).Cross(p - a), d2 = (c - b).Cross(p - b), d3 = (a - c).Cross(p - c);
            if ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0)) return true;
        }
        return false;
    }

    static MoveBand? BandOf(MoveBands b, Point2 p) =>
        Inside(b.Clear, p) ? MoveBand.Clear : Inside(b.Difficult, p) ? MoveBand.Difficult : Inside(b.Dangerous, p) ? MoveBand.Dangerous : null;

    static int Count(MoveBands b) => b.Clear.Count + b.Difficult.Count + b.Dangerous.Count;

    /// <summary>Sample points off the fine grid's lines (irrational offsets), across a 34" square about a point.</summary>
    static IEnumerable<Point2> Samples(Point2 about, double step)
    {
        for (var x = -17.0; x <= 17; x += step)
            for (var y = -17.0; y <= 17; y += step)
                yield return about + new Point2(x + 0.0123456789 * Math.Sqrt(2), y + 0.0098765432 * Math.Sqrt(3));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void The_walk_draws_the_area_the_plain_walk_drew(int rider, bool dangers)
    {
        var reach = Rider(rider, dangers);
        var walked = reach.Bands();
        var plain = reach.Bands(null, PlainWalk.Build);
        Assert.NotEmpty(walked.Clear);
        Assert.Equal(Area(plain.Clear), Area(walked.Clear), 6);
        Assert.Equal(Area(plain.Difficult), Area(walked.Difficult), 6);
        Assert.Equal(Area(plain.Dangerous), Area(walked.Dangerous), 6);
        Assert.True(Count(walked) < Count(plain) * 0.6, $"{Count(walked)} against {Count(plain)}");   // the same area in fewer triangles
        foreach (var p in Samples(reach.Origin, 0.37)) Assert.Equal(BandOf(plain, p), BandOf(walked, p));
    }

    [Fact]
    public void The_walk_is_the_same_on_every_run()
    {
        var first = Rider(1).Bands();
        for (var n = 0; n < 3; n++)
        {
            var again = Rider(1).Bands();
            Assert.Equal(first.Clear, again.Clear); Assert.Equal(first.Difficult, again.Difficult); Assert.Equal(first.Dangerous, again.Dangerous);
        }
    }

    /// <summary>How far from a band's edge a point must be to be checked: the walk cuts a fine cell (0.25") along a chord.</summary>
    const double Clearance = 0.15;

    [Fact]
    public void Several_models_areas_as_one_hold_each_band_wherever_any_model_reaches_a_point_in_it()
    {
        var reaches = Enumerable.Range(0, 5).Select(i => Rider(i)).ToList();
        var union = MoveReach.Union(reaches);
        HashSet<MoveBand> Exact(Point2 p) => [.. reaches.Select(r => r.BandAt(p)).OfType<MoveBand>()];
        Point2[] around = [new(Clearance, 0), new(-Clearance, 0), new(0, Clearance), new(0, -Clearance)];
        var checkedPoints = 0;
        foreach (var p in Samples(new Point2(33.6, 4.8), 0.53))
        {
            var exact = Exact(p);
            if (around.Any(d => !Exact(p + d).SetEquals(exact))) continue;   // within a fine cell of an edge: the chord may fall either side
            checkedPoints++;
            Assert.Equal(exact.Contains(MoveBand.Clear), Inside(union.Clear, p));
            Assert.Equal(exact.Contains(MoveBand.Difficult), Inside(union.Difficult, p));
            Assert.Equal(exact.Contains(MoveBand.Dangerous), Inside(union.Dangerous, p));
        }
        Assert.True(checkedPoints > 1000);
        Assert.True(Count(union) < reaches.Sum(r => Count(r.Bands())) / 2); // fewer triangles than the areas drawn one over another
    }

    [Fact]
    public void A_point_the_straight_line_reaches_has_the_levels_every_field_gives()
    {
        foreach (var rider in new[] { 0, 2 })
        {
            var reach = Rider(rider);
            foreach (var p in Samples(reach.Origin, 0.61)) Assert.Equal(reach.Nested(p, null, direct: false), reach.Nested(p, null));
        }
    }

    [Fact]
    public void One_models_area_as_one_is_its_own()
    {
        var reach = Rider(2);
        Assert.Equal(reach.Bands().Clear, MoveReach.Union([reach]).Clear);
    }

    [Fact]
    public void A_minimum_cuts_each_models_area_in_the_union()
    {
        var reaches = Enumerable.Range(0, 2).Select(i => Rider(i, false)).ToList();
        var union = MoveReach.Union(reaches, [.. reaches.Select(_ => (Func<Point2, double>)(_ => 10.0))]);
        var start = CavalryRushScene.Riders[0].Placement.Position.Horizontal;
        Assert.Null(BandOf(union, start + new Point2(0.3, 4)));
        Assert.NotNull(BandOf(union, start + new Point2(12, 0.3)));
    }
}

/// <summary>ReachMesh's walk before coarse cells grew to 8" and the walk went to several threads, kept as the reference.</summary>
internal static class PlainWalk
{
    const int Levels = 3;


    public static List<Point2>[] Build(Point2 origin, double reach, int bands, ReachMesh.Fields fields, Func<Point2, double, MoveSpace.Cover> cover)
    {
        var top = 1 << Levels;
        var fine = Math.Max(0.25, 2 * reach / 256);
        var cells = (int)Math.Ceiling(2 * reach / fine / top) * top;
        var corner = origin - new Point2(cells * fine / 2, cells * fine / 2);
        Point2 At(int i, int j) => corner + new Point2(i * fine, j * fine);
        var values = new double[cells + 1, cells + 1, bands];
        var known = new bool[cells + 1, cells + 1];
        var buffer = new double[bands];
        double Value(Point2 p, int k)
        {
            fields(p, buffer);
            return buffer[k];
        }
        double V(int i, int j, int k)
        {
            if (!known[i, j])
            {
                fields(At(i, j), buffer);
                for (var b = 0; b < bands; b++) values[i, j, b] = buffer[b];
                known[i, j] = true;
            }
            return values[i, j, k];
        }
        var crossings = new Dictionary<(int, int, int, int, int), Point2>();
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
        void Add(int k, Point2 a, Point2 b, Point2 c)
        {
            if (Math.Abs((b - a).Cross(c - a)) < 1e-12) return;
            triangles[k].Add(a); triangles[k].Add(b); triangles[k].Add(c);
        }
        (int, int)[] ring = [(0, 0), (1, 0), (1, 1), (0, 1)];
        void March(int i, int j, int k)
        {
            var ins = new bool[4];
            var count = 0;
            for (var c = 0; c < 4; c++) if (ins[c] = V(i + ring[c].Item1, j + ring[c].Item2, k) <= 0) count++;
            if (count == 0) return;
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
            var centre = new Point2(polygon.Average(p => p.X), polygon.Average(p => p.Y));
            for (var n = 0; n < polygon.Count; n++) Add(k, centre, polygon[n], polygon[(n + 1) % polygon.Count]);
        }
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
        for (var i = 0; i < cells; i += top)
            for (var j = 0; j < cells; j += top)
                Cell(i, j, top);
        return triangles;
    }
}
