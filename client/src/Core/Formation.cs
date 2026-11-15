using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>
/// A held unit arranged in rows around a point (spec: in-game UX → Formations). Rows run across the facing, the front row toward it; the
/// models split as evenly as they go, the extra ones in the front rows (10 in 3 rows is 4-3-3), in input order, front row first.
/// Neighbours in a row sit <see cref="Gap"/> apart edge to edge, and each row sits back from the one in front until its closest pair
/// across the two is <see cref="Gap"/> apart, so every model is within 1" of another and the rows form one chain (p.7, unit coherency).
/// </summary>
public static class Formation
{
    /// <summary>Edge-to-edge spacing, inside p.7's 1" with room for a dragged unit's rounding.</summary>
    public const double Gap = 0.5;

    public static IReadOnlyList<Point3> Arrange(IReadOnlyList<Base> bases, int rows, Point3 centre, Yaw facing)
    {
        var n = bases.Count;
        if (n == 0) return [];
        rows = Math.Clamp(rows, 1, n);
        // The local frame faces +Y (yaw 0): a row runs along X, the front row is at Y = 0 and later rows step back toward -Y.
        var local = new Point2[n];
        IReadOnlyList<int> ahead = [];
        var line = 0.0; var first = 0;
        for (var r = 0; r < rows; r++)
        {
            var row = Enumerable.Range(first, n / rows + (r < n % rows ? 1 : 0)).ToList();
            first += row.Count;
            var x = -(row.Sum(i => bases[i].B) + (row.Count - 1) * Gap) / 2;     // B is the width across the facing
            foreach (var i in row) { local[i] = new Point2(x + bases[i].B / 2, 0); x += bases[i].B + Gap; }
            if (r > 0) line = Behind(bases, local, ahead, row, line);
            foreach (var i in row) local[i] = local[i] with { Y = line };
            ahead = row;
        }
        var front = Enumerable.Range(0, n).Max(i => local[i].Y + bases[i].A / 2);  // A is the length along the facing
        var back = Enumerable.Range(0, n).Min(i => local[i].Y - bases[i].A / 2);
        var middle = new Point2(0, (front + back) / 2);
        return local.Select(p => (p - middle).Rotate(facing)).Select(p => new Point3(centre.X + p.X, centre.Y + p.Y, centre.Z)).ToList();
    }

    /// <summary>The line behind <paramref name="line"/> at which the closest pair across the two rows is <see cref="Gap"/> apart. The closest
    /// distance only grows as the row steps back, so a bisection between touching and a full row depth behind finds it.</summary>
    static double Behind(IReadOnlyList<Base> bases, Point2[] local, IReadOnlyList<int> ahead, IReadOnlyList<int> row, double line)
    {
        double Closest(double y) => ahead.Min(i => row.Min(j => Distance.EdgeToEdge(At(bases[i], local[i] with { Y = line }), At(bases[j], local[j] with { Y = y }))));
        double near = 0, far = ahead.Max(i => bases[i].A) / 2 + Gap + row.Max(j => bases[j].A) / 2;
        for (var k = 0; k < 40; k++) { var step = (near + far) / 2; if (Closest(line - step) < Gap) near = step; else far = step; }
        return line - far;
    }

    static PlacedModel At(Base b, Point2 p) => new(new ModelBody(b, 1), new Placement(new Point3(p.X, p.Y, 0), new Yaw(0)));

    /// <summary>
    /// The held pieces as one body: each grab moved by the cursor's travel (dx, dy) and turned by <paramref name="degrees"/> (counter-clockwise)
    /// about the centre of the grabs, its facing turned with it and its height kept. One piece turns where it stands.
    /// </summary>
    public static Dictionary<Guid, Placement> Spun(IReadOnlyDictionary<Guid, Placement> grabs, double degrees, double dx, double dy)
    {
        var cx = grabs.Values.Average(g => g.Position.X); var cy = grabs.Values.Average(g => g.Position.Y);
        var r = degrees * Math.PI / 180; var (sin, cos) = Math.SinCos(r);
        return grabs.ToDictionary(kv => kv.Key, kv =>
        {
            var x = kv.Value.Position.X - cx; var y = kv.Value.Position.Y - cy;
            return new Placement(new Point3(cx + x * cos - y * sin + dx, cy + x * sin + y * cos + dy, kv.Value.Position.Z), new Yaw(kv.Value.Yaw.Degrees + degrees));
        });
    }
}
