using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Writes flat triangles into a <see cref="Part"/>, each point authored in inches and divided by <see cref="Scale"/> first:
/// <see cref="Vector3.One"/> for a model with its own inches, the typical size for a unit-box model (a hill).</summary>
public readonly record struct Pen(Part Part, Vector3 Scale)
{
    public Pen(Part part) : this(part, Vector3.One) { }
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 colour) => Part.Tri(a / Scale, b / Scale, c / Scale, colour);
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc) => Part.Tri(a / Scale, b / Scale, c / Scale, ca, cb, cc);
}

/// <summary>Hard-faceted stone: stratified outcrops (<see cref="Stack"/>), broken chunks (<see cref="Chunk"/>, convex hulls of
/// a few points), and flocked bases (<see cref="Base"/>). Authored in inches; flat-shaded, a tone per facet.</summary>
public static class Crag
{
    /// <summary>One stratified outcrop: rows by height, columns by angle round its axis, its radius at each the outline's ellipse
    /// narrowed by <see cref="Taper"/>, bent by noise, cut flat by vertical joint planes (<see cref="Cuts"/>), notched by
    /// <see cref="Cracks"/> and <see cref="Chips"/>; each layer is set back a little and grooved at its bedding plane, so the
    /// strata step back in ledges with a lip over each groove.</summary>
    public sealed class Strata
    {
        public Vector2 Centre;
        public float Base = -0.1f, Height = 1;
        public Vector2 Radii = Vector2.One;
        public float Yaw, Pitch, Roll;
        public int Layers = 4, Cuts = 6, Cracks = 3, Chips = 6, Columns = 96, Seed = 1;
        public float Taper = 0.2f, Lumps = 0.12f, Inset = 0.07f, Groove = 0.07f, Lip = 0.03f, RowStep = 0.12f, Rough = 0;
        /// <summary>How far the bedding planes dip across the piece (inches per inch of radius), and toward which angle.</summary>
        public float Dip = 0.12f, DipToward;
        /// <summary>Joint planes of each layer alone, on top of the ones through every layer.</summary>
        public int LayerCuts = 3;
        /// <summary>How far each layer above the first is shifted off the axis, as a fraction of the radius.</summary>
        public float Shift = 0.05f;
        /// <summary>Scales how deep joints, chips and cracks bite (1: a free-standing rock; small for a band of cliff round a hill), how big chips are, and how much each layer's outline wanders.</summary>
        public float CutDepth = 1, ChipScale = 1, Wobble = 0.09f;
        /// <summary>A flat top at <see cref="Height"/>; otherwise a broken dome <see cref="Dome"/> high.</summary>
        public bool FlatTop = true;
        /// <summary>The radius by angle, in place of the <see cref="Radii"/> ellipse (a ring of cliff round a hill).</summary>
        public Func<float, float>? Outline;
        /// <summary>The radius by height as a fraction of the outline, in place of <see cref="Taper"/>.</summary>
        public Func<float, float>? Profile;
        /// <summary>Takes the radius at (angle, height) and gives the one to build: a cave cut into the side.</summary>
        public Func<float, float, float, float>? Carve;
        /// <summary>No crown and no underside: a band of cliff whose top edge turns in under the turf above it.</summary>
        public bool Band;
        public float Dome = 0.3f;
    }

    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            var h = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65536f;
        }
    }

    /// <summary>Writes <paramref name="s"/>; <paramref name="colour"/> paints each triangle at its centre and normal (inches) and
    /// layer, then a tone per joint face and layer varies it.</summary>
    public static void Stack(Pen pen, Strata s, Func<Vector3, Vector3, int, Vector4> colour)
    {
        var rng = new Random(s.Seed);
        float F() => (float)rng.NextDouble();
        var r0 = MathF.Max(s.Radii.X, s.Radii.Y);
        // Layers: random thicknesses, each set back and shifted a little.
        var weights = Enumerable.Range(0, s.Layers).Select(_ => 0.4f + 1.2f * F()).ToArray();
        var bounds = new float[s.Layers + 1]; bounds[0] = s.Base;
        for (var l = 0; l < s.Layers; l++) bounds[l + 1] = bounds[l] + (s.Height - s.Base) * weights[l] / weights.Sum();
        bounds[s.Layers] = s.Height;
        var inset = Enumerable.Range(0, s.Layers).Select(l => l == 0 ? 0 : s.Inset * (1.3f * F() - 0.3f)).ToArray();
        var shift = Enumerable.Range(0, s.Layers).Select(l => l == 0 ? Vector2.Zero : new Vector2(F() - 0.5f, F() - 0.5f) * r0 * s.Shift).ToArray();
        // Joints: near-vertical planes, normal (cos φ, lean, sin φ), at d of the outline's radius that way.
        var cuts = Enumerable.Range(0, s.Cuts).Select(i => (Phi: (i + 0.6f * F()) * MathF.Tau / s.Cuts, Lean: (F() - 0.5f) * 0.5f, D: 1 - (0.3f - 0.18f * F()) * s.CutDepth, Layer: -1))
            .Concat(Enumerable.Range(0, s.Layers * s.LayerCuts).Select(i => (Phi: F() * MathF.Tau, Lean: (F() - 0.5f) * 0.3f, D: 1 - (0.3f - 0.22f * F()) * s.CutDepth, Layer: i / s.LayerCuts))).ToArray();
        var cracks = Enumerable.Range(0, s.Cracks).Select(_ => (Theta: F() * MathF.Tau, Width: 0.04f + 0.04f * F(), Depth: 0.1f + 0.12f * F(),
            Lo: s.Base + (s.Height - s.Base) * F() * 0.5f, Hi: s.Base + (s.Height - s.Base) * (0.5f + 0.5f * F()))).ToArray();
        // Chips: a joint face through a band of height only, deeper than the joints, slanted: a broken-off corner.
        var chips = Enumerable.Range(0, s.Chips).Select(_ => (Phi: F() * MathF.Tau, Lean: (F() - 0.5f) * 1.2f, D: 1 - (0.4f - 0.2f * F()) * s.CutDepth,
            Y: s.Base + (s.Height - s.Base) * F(), Half: r0 * s.ChipScale * (0.12f + 0.2f * F()))).ToArray();
        var mid = (s.Base + s.Height) / 2;

        float Narrow(float y, float h, float k) => s.Profile is { } p ? p(y) : 1 - s.Taper * h * k;
        float Ellipse(float t) { if (s.Outline is { } o) return o(t); var (c, n) = (MathF.Cos(t), MathF.Sin(t)); return 1 / MathF.Sqrt(c * c / (s.Radii.X * s.Radii.X) + n * n / (s.Radii.Y * s.Radii.Y)); }
        (float R, int Facet) Radius(float t, float y, int l, float f)
        {
            var (c, n) = (MathF.Cos(t), MathF.Sin(t));
            var h = (y - s.Base) / (s.Height - s.Base);
            var e = Ellipse(t);
            var r = e * Narrow(y, h, 1) * (1 + s.Lumps * (Fbm(c * 1.3f + s.Seed, n * 1.3f, y / r0 * 0.7f, s.Seed) * 2 - 1));
            r *= 1 + s.Wobble * (Noise(c * 2.5f + l * 3.1f, n * 2.5f, l * 1.7f, s.Seed + 5) * 2 - 1);   // each layer's outline its own
            var facet = -1;
            for (var i = 0; i < cuts.Length; i++)
            {
                var (phi, lean, d, only) = cuts[i];
                if (only >= 0 && only != l) continue;
                var along = MathF.Cos(t - phi);
                if (along < 0.08f) continue;
                var at = (d * Ellipse(phi) * Narrow(y, h, 0.8f) - lean * (y - mid)) / along;
                if (at < r) { r = at; facet = i; }
            }
            foreach (var k in cracks)
            {
                if (y < k.Lo || y > k.Hi) continue;
                var dt = MathF.Abs(MathF.IEEERemainder(t - k.Theta - 0.04f * MathF.Sin(y * 3 / r0), MathF.Tau));
                var fade = Smooth(k.Lo, k.Lo + 0.15f * r0, y) * Smooth(k.Hi, k.Hi - 0.15f * r0, y);
                r *= 1 - k.Depth * s.CutDepth * fade * MathF.Max(0, 1 - dt / k.Width);
            }
            for (var i = 0; i < chips.Length; i++)
            {
                var k = chips[i];
                if (MathF.Abs(y - k.Y) > k.Half) continue;
                var along = MathF.Cos(t - k.Phi);
                if (along < 0.15f) continue;
                var at = (k.D * Ellipse(k.Phi) * Narrow(y, h, 0.8f) - k.Lean * (y - k.Y)) / along;
                if (at < r) { r = at; facet = 100 + i; }
            }
            r *= (1 - inset[l]) * (1 - s.Groove * (1 - Smooth(0, 0.16f, f))) * (1 + s.Lip * Smooth(0.7f, 1, f));
            r *= 1 + s.Rough * (Noise(t * r0 * 7, y * 7 / MathF.Max(r0, 0.5f) * r0, l, s.Seed + 9) * 2 - 1);
            if (s.Carve is { } carve) r = carve(t, y, r);
            return (MathF.Max(r, 0.02f), facet);
        }

        // Rows: per layer the bedding groove, then rows up to its top; two rows at one height where layers meet make the shelf.
        var rows = new List<(float Y, int L, float F)>();
        for (var l = 0; l < s.Layers; l++)
        {
            var (yb, yt) = (bounds[l], bounds[l + 1]);
            var n = Math.Max(2, (int)MathF.Ceiling((yt - yb) / s.RowStep));
            rows.Add((yb, l, 0));
            if (yt - yb > 0.12f) rows.Add((yb + (yt - yb) * 0.16f, l, 0.16f));
            for (var k = 1; k <= n; k++) { var f = (float)k / n; if (f > 0.17f) rows.Add((yb + (yt - yb) * f, l, f)); }
        }
        var cols = s.Columns;
        var rotation = Matrix4x4.CreateFromYawPitchRoll(s.Yaw, s.Pitch, s.Roll);
        var origin = new Vector3(s.Centre.X, 0, s.Centre.Y);
        Vector3 World(Vector3 local) => Vector3.Transform(local, rotation) + origin;
        var grid = new Vector3[rows.Count + 1, cols];
        var facets = new int[rows.Count + 1, cols];
        for (var i = 0; i < rows.Count; i++)
        {
            var (y, l, f) = rows[i];
            for (var j = 0; j < cols; j++)
            {
                var t = MathF.Tau * j / cols;
                var (r, facet) = Radius(t, y, l, f);
                var dip = i == 0 || i == rows.Count - 1 ? 0 : s.Dip * r * MathF.Cos(t - s.DipToward) * MathF.Min(1, MathF.Min(y - s.Base, s.Height - y) / (s.Dip * r0 + 1e-3f));
                grid[i, j] = new Vector3(shift[l].X + r * MathF.Cos(t), y + dip, shift[l].Y + r * MathF.Sin(t));
                facets[i, j] = facet;
            }
        }
        var rowCount = rows.Count;
        if (s.Band)
            for (var j = 0; j < cols; j++) grid[rowCount - 1, j] = grid[rowCount - 1, j] * new Vector3(0.94f, 1, 0.94f);
        if (!s.FlatTop && !s.Band)
        {
            // A broken dome: rings drawing in toward a crown, each ring's radius wobbling.
            var top = rows[^1]; var steps = 4;
            for (var k = 1; k <= steps; k++)
            {
                var a = MathF.PI / 2 * k / (steps + 0.6f);
                var arr = new Vector3[cols];
                for (var j = 0; j < cols; j++)
                {
                    var p = grid[rowCount - 1, j]; var c = new Vector3(shift[top.L].X, 0, shift[top.L].Y);
                    var q = c + (p with { Y = 0 } - c) * MathF.Cos(a) * (0.92f + 0.12f * Noise(j * 0.3f, k, 0, s.Seed + 11));
                    arr[j] = q with { Y = s.Height + s.Dome * MathF.Sin(a) * (0.85f + 0.3f * Noise(j * 0.2f, k * 0.7f, 1, s.Seed + 12)) };
                }
                var g2 = new Vector3[rowCount + 1, cols]; var f2 = new int[rowCount + 1, cols];
                for (var i = 0; i < rowCount; i++) for (var j = 0; j < cols; j++) { g2[i, j] = grid[i, j]; f2[i, j] = facets[i, j]; }
                for (var j = 0; j < cols; j++) { g2[rowCount, j] = arr[j]; f2[rowCount, j] = 50 + k; }
                grid = g2; facets = f2; rows.Add((arr[0].Y, top.L, 1)); rowCount++;
            }
        }
        void Face(Vector3 a, Vector3 b, Vector3 c, int layer, int facet)
        {
            var (wa, wb, wc) = (World(a), World(b), World(c));
            var cross = Vector3.Cross(wb - wa, wc - wa);
            if (cross.LengthSquared() < 1e-12f) return;
            var n = Vector3.Normalize(cross);
            var tone = 0.9f + 0.18f * Hash(facet + 7, layer, s.Seed);
            pen.Tri(wa, wb, wc, Shade(colour(wa, n, layer), tone), Shade(colour(wb, n, layer), tone), Shade(colour(wc, n, layer), tone));
        }
        for (var i = 0; i < rowCount - 1; i++)
            for (var j = 0; j < cols; j++)
            {
                var j1 = (j + 1) % cols;
                var (a, b, c, d) = (grid[i, j], grid[i, j1], grid[i + 1, j1], grid[i + 1, j]);
                var layer = rows[i + 1].L;
                var facet = facets[i + 1, j] == facets[i, j] ? facets[i, j] : facets[i + 1, j] * 7 + 3;
                Face(a, d, c, layer, facet); Face(a, c, b, layer, facet);
            }
        if (s.Band) return;
        // Crown: a fan over the last ring, through a ring halfway in so the top is not one long fan.
        var last = rowCount - 1;
        var centre = Enumerable.Range(0, cols).Aggregate(Vector3.Zero, (acc, j) => acc + grid[last, j]) / cols;
        if (!s.FlatTop) centre.Y += s.Dome * 0.08f;
        // Rings in toward the centre, so the top's triangles are short and its paint does not streak.
        var outer = Enumerable.Range(0, cols).Select(j => grid[last, j]).ToArray();
        foreach (var f in new[] { 0.7f, 0.4f, 0.15f })
        {
            var inner = Enumerable.Range(0, cols).Select(j => Vector3.Lerp(centre, grid[last, j], f + 0.06f * Hash(j, (int)(f * 100), s.Seed)) with { Y = s.FlatTop ? s.Height : centre.Y - s.Dome * 0.03f }).ToArray();
            for (var j = 0; j < cols; j++)
            {
                var j1 = (j + 1) % cols;
                Face(outer[j], inner[j], inner[j1], s.Layers - 1, 200);
                Face(outer[j], inner[j1], outer[j1], s.Layers - 1, 200);
            }
            outer = inner;
        }
        for (var j = 0; j < cols; j++) Face(centre, outer[(j + 1) % cols], outer[j], s.Layers - 1, 200);
        // Underside, for a piece tilted or lifted off the table.
        var under = Enumerable.Range(0, cols).Aggregate(Vector3.Zero, (acc, j) => acc + grid[0, j]) / cols;
        if (World(under).Y > -0.02f || MathF.Abs(s.Pitch) + MathF.Abs(s.Roll) > 0.01f)
            for (var j = 0; j < cols; j++) Face(under, grid[0, j], grid[0, (j + 1) % cols], 0, 400);
    }

    /// <summary>The convex hull of a few points: triangles (indices) wound outward.</summary>
    public static List<(int A, int B, int C)> Hull(IReadOnlyList<Vector3> p)
    {
        var n = p.Count;
        var i0 = 0;
        var i1 = Enumerable.Range(0, n).MaxBy(i => Vector3.DistanceSquared(p[i], p[i0]));
        var i2 = Enumerable.Range(0, n).MaxBy(i => Vector3.Cross(p[i1] - p[i0], p[i] - p[i0]).LengthSquared());
        var normal = Vector3.Cross(p[i1] - p[i0], p[i2] - p[i0]);
        var i3 = Enumerable.Range(0, n).MaxBy(i => MathF.Abs(Vector3.Dot(normal, p[i] - p[i0])));
        var inside = (p[i0] + p[i1] + p[i2] + p[i3]) / 4;
        var faces = new List<(int A, int B, int C)>();
        void Add(int a, int b, int c) => faces.Add(Vector3.Dot(Vector3.Cross(p[b] - p[a], p[c] - p[a]), inside - p[a]) > 0 ? (a, c, b) : (a, b, c));
        Add(i0, i1, i2); Add(i0, i1, i3); Add(i0, i2, i3); Add(i1, i2, i3);
        var scale = Enumerable.Range(0, n).Max(i => Vector3.Distance(p[i], inside));
        for (var k = 0; k < n; k++)
        {
            if (k == i0 || k == i1 || k == i2 || k == i3) continue;
            var visible = faces.Where(f => Vector3.Dot(Vector3.Normalize(Vector3.Cross(p[f.B] - p[f.A], p[f.C] - p[f.A])), p[k] - p[f.A]) > 1e-5f * scale).ToList();
            if (visible.Count == 0) continue;
            var edges = visible.SelectMany(f => new[] { (f.A, f.B), (f.B, f.C), (f.C, f.A) }).ToHashSet();
            var horizon = edges.Where(e => !edges.Contains((e.Item2, e.Item1))).ToList();
            faces.RemoveAll(visible.Contains);
            foreach (var (a, b) in horizon) faces.Add((a, b, k));
        }
        return faces;
    }

    /// <summary>A broken chunk of stone: the hull of <paramref name="points"/> points on an ellipsoid of <paramref name="size"/>
    /// (full extents, inches), turned and placed at <paramref name="centre"/>; a tone per face.</summary>
    public static void Chunk(Pen pen, Random rng, Vector3 centre, Vector3 size, float yaw, float pitch, float roll, Func<Vector3, Vector3, Vector4> colour, int points = 11)
    {
        var rotation = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        var p = new Vector3[points];
        for (var i = 0; i < points; i++)
        {
            Vector3 d;
            do d = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1); while (d.LengthSquared() is > 1 or < 0.05f);
            d = Vector3.Normalize(d) * (0.7f + 0.3f * (float)rng.NextDouble());
            p[i] = centre + Vector3.Transform(d * size / 2, rotation);
        }
        var seed = rng.Next();
        foreach (var (a, b, c) in Hull(p))
        {
            var cross = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            if (cross.LengthSquared() < 1e-14f) continue;
            var n = Vector3.Normalize(cross);
            pen.Tri(p[a], p[b], p[c], Shade(colour((p[a] + p[b] + p[c]) / 3, n), 0.88f + 0.24f * Hash(a * 31 + b, c, seed)));
        }
    }

    /// <summary>A scatter of chunks: <paramref name="count"/> pieces from <paramref name="place"/> (the ground point under one, its
    /// size), each sunk a little into the ground there.</summary>
    public static void Scree(Pen pen, Random rng, int count, Func<Random, (Vector3 At, float Size)?> place, Func<Vector3, Vector3, Vector4> colour)
    {
        for (var k = 0; k < count; k++)
        {
            if (place(rng) is not { } q) continue;
            var s = q.Size;
            var size = new Vector3(s * (1 + 0.6f * (float)rng.NextDouble()), s * (0.45f + 0.4f * (float)rng.NextDouble()), s);
            Chunk(pen, rng, q.At + Vector3.UnitY * size.Y * 0.25f, size, (float)rng.NextDouble() * 6.3f, ((float)rng.NextDouble() - 0.5f) * 0.6f, ((float)rng.NextDouble() - 0.5f) * 0.6f, colour, 8 + rng.Next(5));
        }
    }

    /// <summary>A flocked base: a polar grid out to <paramref name="outline"/>(θ) round <paramref name="centre"/>, rising to
    /// <paramref name="height"/>(x, z) inside a bevelled edge <paramref name="bevel"/> wide, the rim on the table. Smooth-shaded
    /// (flock is a fine grain, not facets).</summary>
    public static void Base(Model model, Material m, Vector3 scale, Vector2 centre, Func<float, float> outline, int rings, int segments, float bevel,
        Func<float, float, float> height, Func<Vector3, Vector3, Vector4> colour)
    {
        model[m].Grid(rings + 1, segments, wrap: true, (i, j) =>
        {
            var t = MathF.Tau * j / segments; var edge = outline(t);
            var u = (float)i / rings;
            var r = edge * u;
            var (x, z) = (centre.X + r * MathF.Cos(t), centre.Y + r * MathF.Sin(t));
            var y = i == rings ? 0 : height(x, z) * Smooth(edge, edge - bevel, r);
            return new Vector3(x, y, z) / scale;
        }, (p, n) => colour(model.Inch(p) * scale, n));
    }

    /// <summary>Tufts of static grass rooted where <paramref name="place"/> says (inches).</summary>
    public static void Tufts(Part part, Vector3 scale, Random rng, int count, Func<Random, Vector3?> place, float height, Vector4 root, Vector4 tip)
    {
        for (var k = 0; k < count; k++)
        {
            if (place(rng) is not { } at) continue;
            var h = height * (0.7f + 0.6f * (float)rng.NextDouble());
            var tint = 0.85f + 0.3f * (float)rng.NextDouble();
            var p = at - Vector3.UnitY * 0.01f;
            for (var i = 0; i < 7; i++)
            {
                var a = (float)(rng.NextDouble() * MathF.Tau);
                var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                var side = new Vector3(-dir.Z, 0, dir.X) * h * 0.09f;
                var o = p + dir * h * 0.08f;
                var bh = h * (0.6f + 0.4f * (float)rng.NextDouble());
                var top = o + dir * 0.45f * bh + Vector3.UnitY * bh;
                part.Tri((o - side) / scale, (o + side) / scale, top / scale, Shade(root, tint), Shade(root, tint), Shade(tip, tint));
            }
        }
    }
}
