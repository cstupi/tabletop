namespace Tabletop.Geometry;

/// <summary>
/// Shortest paths over the reduced visibility graph: the start, the goal and the ring corners not held by an obstacle of the search's groups,
/// joined where the way between is clear (Clear) and, between two corners, where the line is tangent to both corners' rings — a shortest path
/// among convex obstacles bends only round them. A* for one destination, Dijkstra for every corner within a length (a field).
/// </summary>
internal sealed partial class MoveSpace
{
    /// <summary>The line from a corner towards `to` leaves the corner's ring on one side: it can be part of a shortest path.</summary>
    static bool Tangent(in Node n, Point2 to)
    {
        var d = to - n.At;
        double c1 = d.Cross(n.Prev - n.At), c2 = d.Cross(n.Next - n.At);
        return c1 * c2 >= -1e-12;
    }

    /// <summary>The shortest path from the start to `goal` of length at most `limit` meeting no obstacle of the groups in `mask`; null when none is.</summary>
    public (List<Point2> Path, double Length)? Route(Point2 goal, int mask, double limit)
    {
        var start = Origin;
        var straight = start.DistanceTo(goal);
        if (straight > limit + Tolerance.Distance || Blocked(goal, mask)) return null;
        if (Clear(start, goal, mask, fromStart: true)) return ([start, goal], straight);
        var nodes = new List<int>();
        for (var i = 0; i < Nodes.Count; i++)
            if ((Nodes[i].Inside & mask) == 0 && start.DistanceTo(Nodes[i].At) + Nodes[i].At.DistanceTo(goal) <= limit + Tolerance.Distance) nodes.Add(i);
        if (nodes.Count == 0) return null;
        // Index -1 is the start; nodes[k] is corner k.
        var count = nodes.Count;
        var g = new double[count]; var parent = new int[count]; var closed = new bool[count];
        Array.Fill(g, double.PositiveInfinity); Array.Fill(parent, -2);
        var open = new PriorityQueue<int, double>();
        double bestGoal = double.PositiveInfinity; var goalParent = -2;
        void Relax(int from, Point2 at, double gAt, bool fromStart)
        {
            if (gAt + at.DistanceTo(goal) < bestGoal && gAt + at.DistanceTo(goal) <= limit + Tolerance.Distance && Clear(at, goal, mask, fromStart))
            { bestGoal = gAt + at.DistanceTo(goal); goalParent = from; }
            for (var k = 0; k < count; k++)
            {
                if (closed[k]) continue;
                var n = Nodes[nodes[k]];
                var cost = gAt + at.DistanceTo(n.At);
                if (cost >= g[k] || cost + n.At.DistanceTo(goal) > Math.Min(limit + Tolerance.Distance, bestGoal)) continue;
                // A line from the start may leave from inside a ring (a base touching or standing in an obstacle), so only corner-to-corner lines are held to tangents.
                if (from >= 0 && (!Tangent(n, at) || !Tangent(Nodes[nodes[from]], n.At))) continue;
                if (!Clear(at, n.At, mask, fromStart)) continue;
                g[k] = cost; parent[k] = from;
                open.Enqueue(k, cost + n.At.DistanceTo(goal));
            }
        }
        Relax(-1, start, 0, fromStart: true);
        while (open.TryDequeue(out var u, out var f))
        {
            if (closed[u]) continue;
            if (f >= bestGoal) break;
            closed[u] = true;
            Relax(u, Nodes[nodes[u]].At, g[u], fromStart: false);
        }
        if (goalParent == -2) return null;
        var path = new List<Point2> { goal };
        for (var k = goalParent; k >= 0; k = parent[k]) path.Add(Nodes[nodes[k]].At);
        path.Add(start);
        path.Reverse();
        return (path, bestGoal);
    }

    /// <summary>Every corner reachable from the start within `limit` avoiding the groups in `mask`, with its distance and the corner before it.</summary>
    public Field Settle(int mask, double limit)
    {
        var start = Origin;
        var nodes = new List<int>();
        for (var i = 0; i < Nodes.Count; i++)
            if ((Nodes[i].Inside & mask) == 0 && start.DistanceTo(Nodes[i].At) <= limit + Tolerance.Distance) nodes.Add(i);
        var count = nodes.Count;
        var g = new double[count]; var parent = new int[count]; var closed = new bool[count];
        Array.Fill(g, double.PositiveInfinity); Array.Fill(parent, -2);
        var open = new PriorityQueue<int, double>();
        void Relax(int from, Point2 at, double gAt, bool fromStart)
        {
            for (var k = 0; k < count; k++)
            {
                if (closed[k]) continue;
                var n = Nodes[nodes[k]];
                var cost = gAt + at.DistanceTo(n.At);
                if (cost >= g[k] || cost > limit + Tolerance.Distance) continue;
                // A line from the start may leave from inside a ring (a base touching or standing in an obstacle), so only corner-to-corner lines are held to tangents.
                if (from >= 0 && (!Tangent(n, at) || !Tangent(Nodes[nodes[from]], n.At))) continue;
                if (!Clear(at, n.At, mask, fromStart)) continue;
                g[k] = cost; parent[k] = from;
                open.Enqueue(k, cost);
            }
        }
        Relax(-1, start, 0, fromStart: true);
        var settled = new List<(Point2 At, double G, int Parent)>();
        var index = new int[count]; Array.Fill(index, -1);
        while (open.TryDequeue(out var u, out var cost))
        {
            if (closed[u] || cost > g[u]) continue;
            closed[u] = true;
            index[u] = settled.Count;
            settled.Add((Nodes[nodes[u]].At, g[u], parent[u] < 0 ? -1 : index[parent[u]]));
            Relax(u, Nodes[nodes[u]].At, g[u], fromStart: false);
        }
        return new Field(this, mask, limit, settled);
    }

    /// <summary>
    /// The corners a search settled, nearest first. A point's distance is the least, over the start and each corner it can see, of the corner's
    /// distance plus the straight line from it.
    /// </summary>
    internal sealed class Field(MoveSpace space, int mask, double limit, List<(Point2 At, double G, int Parent)> settled)
    {
        public double Limit => limit;
        public int Mask => mask;
        public IReadOnlyList<(Point2 At, double G, int Parent)> Corners => settled;
        int _hint = -1;

        /// <summary>The source a point is reached from: -1 the start, else a settled corner; null when it is reached from none within `bound`.</summary>
        public (int Source, double Distance)? Best(Point2 p, double bound = double.PositiveInfinity)
        {
            var origin = space.Origin;
            if (origin.DistanceTo(p) > bound || space.Blocked(p, mask)) return null;     // no path is shorter than the straight line
            if (space.Clear(origin, p, mask, fromStart: true)) return (-1, origin.DistanceTo(p));
            (int, double)? best = null; var bestD = bound;
            // The corner the last point asked was reached from comes first: asked in order across a grid, it is usually this one's too, and a close
            // first answer lets the scan skip most corners unchecked. Only the speed depends on it.
            var hint = _hint;
            if (hint >= 0 && hint < settled.Count && settled[hint].G + settled[hint].At.DistanceTo(p) is var h && h < bestD && space.Clear(settled[hint].At, p, mask))
            { best = (hint, h); bestD = h; }
            for (var i = 0; i < settled.Count; i++)
            {
                var (at, g, _) = settled[i];
                if (g >= bestD) break;
                var d = g + at.DistanceTo(p);
                if (d >= bestD) continue;
                if (!space.Clear(at, p, mask)) continue;
                best = (i, d); bestD = d;
            }
            if (best is { } found) _hint = found.Item1;
            return best;
        }

        public double Distance(Point2 p, double bound = double.PositiveInfinity) => Best(p, bound)?.Distance ?? double.PositiveInfinity;

        /// <summary>The path to p through the corners, or null when p is reached from none.</summary>
        public (List<Point2> Path, double Length)? Path(Point2 p)
        {
            if (Best(p) is not { } best) return null;
            var path = new List<Point2> { p };
            for (var k = best.Source; k >= 0; k = settled[k].Parent) path.Add(settled[k].At);
            path.Add(space.Origin);
            path.Reverse();
            return (path, best.Distance);
        }

        /// <summary>
        /// The point nearest `target` that a path no longer than `within` reaches: from the start and each settled corner with length left, as far
        /// toward the target as the length left and the way allow; the nearest of those.
        /// </summary>
        public (Point2 At, double Gap)? Nearest(Point2 target, double within)
        {
            (Point2, double)? best = null;
            void Try(Point2 from, double left, bool fromStart)
            {
                if (left < 0) return;
                var d = target - from;
                var len = d.Length;
                var to = len <= left ? target : from + d * (left / len);
                var at = space.Reach(from, to, mask, fromStart);
                var gap = at.DistanceTo(target);
                if (best is not { } b || gap < b.Item2 - 1e-9) best = (at, gap);
            }
            Try(space.Origin, within, fromStart: true);
            foreach (var (at, g, _) in settled)
                if (g < within) Try(at, within - g, fromStart: false);
            return best;
        }
    }
}
