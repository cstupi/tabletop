using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Built scenery's parts, authored in inches: chamfered blocks (cut stone, bricks, panels, chunks), I-section girders,
/// bent rebar, leaf cards, rubble heaps of individual chunks, and the copy of an inches-authored model into the unit box for a
/// mesh shared by entries of different sizes.</summary>
public static class Masonry
{
    /// <summary>A flat quad (four shared vertices) or triangle, wound to face <paramref name="outward"/> in the authored space, its
    /// normal taken where it is drawn (after the clamp). A face the clamp collapses is dropped.</summary>
    public static void Face(Part part, Vector3[] q, Vector3 outward, Func<Vector3, Vector4> colour)
    {
        var cross = q.Length == 4 ? Vector3.Cross(q[2] - q[0], q[3] - q[1]) : Vector3.Cross(q[1] - q[0], q[2] - q[0]);
        if (Vector3.Dot(cross, outward) < 0) q = q.Reverse().ToArray();
        if (q.Length == 3) { part.Tri(q[0], q[1], q[2], colour(q[0]), colour(q[1]), colour(q[2])); return; }
        var c = q.Select(part.Clamp).ToArray();
        var n = Vector3.Cross(c[2] - c[0], c[3] - c[1]);
        if (n.LengthSquared() < 1e-14f) return;
        n = Vector3.Normalize(n);
        var t0 = Vector3.Cross(c[1] - c[0], c[2] - c[0]); var t1 = Vector3.Cross(c[2] - c[0], c[3] - c[0]);
        if (t0.LengthSquared() > 1e-14f && t1.LengthSquared() > 1e-14f && Vector3.Dot(Vector3.Normalize(t0), Vector3.Normalize(t1)) < 0.9999f)
        {
            // Not flat: two triangles, each with its own normal.
            part.Tri(q[0], q[1], q[2], colour(q[0]), colour(q[1]), colour(q[2]));
            part.Tri(q[0], q[2], q[3], colour(q[0]), colour(q[2]), colour(q[3]));
            return;
        }
        var first = part.Positions.Count;
        for (var i = 0; i < 4; i++) part.Vertex(c[i], n, colour(q[i]));
        if (Vector3.Cross(c[1] - c[0], c[2] - c[0]).LengthSquared() > 1e-14f) part.Indices.AddRange([first, first + 1, first + 2]);
        if (Vector3.Cross(c[2] - c[0], c[3] - c[0]).LengthSquared() > 1e-14f) part.Indices.AddRange([first, first + 2, first + 3]);
    }

    /// <summary>A rotation turning +x, +y, +z onto the given axes (orthonormal).</summary>
    public static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);

    /// <summary>A rotation whose +y runs along <paramref name="along"/>, turned <paramref name="spin"/> about it.</summary>
    public static Matrix4x4 Along(Vector3 along, float spin = 0)
    {
        var y = Vector3.Normalize(along);
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(y.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX, y));
        var z = Vector3.Cross(x, y);
        return Matrix4x4.CreateRotationY(spin) * Basis(x, y, z);
    }

    public static Matrix4x4 Turn(float yaw, float pitch = 0, float roll = 0) => Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

    /// <summary>A box of <paramref name="size"/> about <paramref name="centre"/>, turned by <paramref name="rotation"/>, every edge cut
    /// by a chamfer <paramref name="chamfer"/> wide (so a drybrush catches it). With <paramref name="chip"/>, each corner's chamfer
    /// varies and the corners shift by up to <paramref name="rough"/> of the size: worn, chipped or broken stone. A face lying on the
    /// ground is left out; without <paramref name="corners"/> the small corner facets are too (trim seen only from afar).</summary>
    public static void Block(Part part, Vector3 centre, Vector3 size, Matrix4x4 rotation, float chamfer, Func<Vector3, Vector4> colour, Random? chip = null, float rough = 0, bool corners = true)
    {
        var h = size / 2;
        var limit = MathF.Min(h.X, MathF.Min(h.Y, h.Z)) * 0.7f;
        var v = new Vector3[8, 3];
        for (var k = 0; k < 8; k++)
        {
            var s = new Vector3((k & 4) != 0 ? 1 : -1, (k & 2) != 0 ? 1 : -1, (k & 1) != 0 ? 1 : -1);
            var c = MathF.Min(chamfer * (chip is null ? 1 : 0.4f + 1.4f * (float)chip.NextDouble()), limit);
            var inner = s * (h - new Vector3(c));
            if (chip is not null && rough > 0)
                inner += new Vector3((float)chip.NextDouble() - 0.5f, (float)chip.NextDouble() - 0.5f, (float)chip.NextDouble() - 0.5f) * size * rough;
            for (var a = 0; a < 3; a++)
            {
                var e = a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;
                v[k, a] = centre + Vector3.TransformNormal(inner + e * (Component(s, a) * c), rotation);
            }
        }
        var faces = new List<(Vector3[] Q, Vector3 Out)>();
        int K(int x, int y, int z) => (x > 0 ? 4 : 0) | (y > 0 ? 2 : 0) | (z > 0 ? 1 : 0);
        for (var a = 0; a < 3; a++)
            foreach (var sa in new[] { -1, 1 })
            {
                // The face: its four corners in turn round the other two axes.
                var ring = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
                faces.Add((ring.Select(r => v[Corner(a, sa, r.Item1, r.Item2), a]).ToArray(), Vector3.Zero));
            }
        // The edges: along axis a, between faces (b, sb) and (c, sc).
        for (var a = 0; a < 3; a++)
        {
            int b = (a + 1) % 3, c = (a + 2) % 3;
            foreach (var sb in new[] { -1, 1 })
                foreach (var sc in new[] { -1, 1 })
                {
                    int lo = CornerAxes(a, -1, b, sb, c, sc), hi = CornerAxes(a, 1, b, sb, c, sc);
                    faces.Add(([v[lo, b], v[hi, b], v[hi, c], v[lo, c]], Vector3.Zero));
                }
        }
        if (corners) for (var k = 0; k < 8; k++) faces.Add(([v[k, 0], v[k, 1], v[k, 2]], Vector3.Zero));
        foreach (var (q, _) in faces)
        {
            if (q.All(p => p.Y < 1e-4f)) continue;
            var mid = q.Aggregate(Vector3.Zero, (s, p) => s + p) / q.Length;
            Face(part, q, mid - centre, colour);
        }

        static float Component(Vector3 s, int a) => a == 0 ? s.X : a == 1 ? s.Y : s.Z;
        int Corner(int a, int sa, int r1, int r2)
        {
            var s = new int[3]; s[a] = sa; s[(a + 1) % 3] = r1; s[(a + 2) % 3] = r2;
            return K(s[0], s[1], s[2]);
        }
        int CornerAxes(int a, int sa, int b, int sb, int c, int sc)
        {
            var s = new int[3]; s[a] = sa; s[b] = sb; s[c] = sc;
            return K(s[0], s[1], s[2]);
        }
    }

    public static void Block(Part part, Vector3 centre, Vector3 size, Matrix4x4 rotation, float chamfer, Vector4 colour, Random? chip = null, float rough = 0, bool corners = true) =>
        Block(part, centre, size, rotation, chamfer, _ => colour, chip, rough, corners);

    /// <summary>An axis-aligned chamfered block between two corners.</summary>
    public static void Span(Part part, Vector3 min, Vector3 max, float chamfer, Func<Vector3, Vector4> colour) =>
        Block(part, (min + max) / 2, max - min, Matrix4x4.Identity, chamfer, colour);

    public static void Span(Part part, Vector3 min, Vector3 max, float chamfer, Vector4 colour) => Span(part, min, max, chamfer, _ => colour);

    /// <summary>An I-section girder from <paramref name="a"/> to <paramref name="b"/>: two flanges <paramref name="width"/> wide and a
    /// web <paramref name="depth"/> deep, the web upright when the girder lies level (turned by <paramref name="spin"/>).</summary>
    public static void Girder(Part part, Vector3 a, Vector3 b, float width, float depth, Func<Vector3, Vector4> colour, float spin = 0)
    {
        var len = Vector3.Distance(a, b);
        var y = Vector3.Normalize(b - a);
        var side = Vector3.Normalize(Vector3.Cross(MathF.Abs(y.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX, y));
        var up = Vector3.Cross(y, side);
        var (cs, sn) = (MathF.Cos(spin), MathF.Sin(spin));
        (side, up) = (side * cs + up * sn, up * cs - side * sn);
        var r = Basis(side, y, up);
        var mid = (a + b) / 2;
        var t = MathF.Max(depth * 0.14f, 0.012f);
        Block(part, mid + up * (depth / 2 - t / 2), new Vector3(width, len, t), r, t * 0.3f, colour);
        Block(part, mid - up * (depth / 2 - t / 2), new Vector3(width, len, t), r, t * 0.3f, colour);
        Block(part, mid, new Vector3(t, len, depth - 2 * t + 0.002f), r, t * 0.25f, colour);
    }

    /// <summary>A bar of rebar from <paramref name="root"/> out along <paramref name="dir"/>, bent over partway, rusty.</summary>
    public static void Rebar(Part part, Random rng, Vector3 root, Vector3 dir, float length, float radius = 0.022f)
    {
        dir = Vector3.Normalize(dir);
        var bend = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, -0.3f - 0.4f * (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f));
        var knee = root + dir * length * (0.4f + 0.3f * (float)rng.NextDouble());
        var tip = knee + Vector3.Normalize(dir + bend * (0.6f + 0.8f * (float)rng.NextDouble())) * length * 0.5f;
        var mid = knee + (tip - knee) * 0.5f + bend * length * 0.05f;
        Shapes.Chain(part, [root - dir * radius * 3, knee, mid, tip], radius, radius * 0.9f, 4,
            p => Mix(Rgb(0.42f, 0.24f, 0.13f), Rgb(0.3f, 0.29f, 0.28f), Fbm(p * 9, 77) * 0.6f));
    }

    /// <summary>A leaf: a kite-shaped card at <paramref name="at"/> on the plane of <paramref name="normal"/>, its tip toward
    /// <paramref name="up"/>, folded slightly along its midrib. For a double-sided material.</summary>
    public static void Leaf(Part part, Vector3 at, Vector3 normal, Vector3 up, float size, Vector4 colour)
    {
        normal = Vector3.Normalize(normal);
        var tipDir = Vector3.Normalize(up - normal * Vector3.Dot(up, normal));
        var side = Vector3.Cross(normal, tipDir);
        var tip = at + tipDir * size; var mid = at + tipDir * size * 0.45f + normal * size * 0.12f;
        var l = mid + side * size * 0.38f - normal * size * 0.05f; var r = mid - side * size * 0.38f - normal * size * 0.05f;
        var dark = Shade(colour, 0.8f);
        part.Tri(at, r, tip, dark, colour, colour);
        part.Tri(at, tip, l, dark, colour, colour);
    }

    /// <summary>A heap of rubble about <paramref name="centre"/> (on the ground): a low mound of grit, then <paramref name="count"/>
    /// individual pieces laid over it, broken stone chunks, bricks and slabs of <paramref name="size"/>, highest in the middle.</summary>
    public static void Rubble(Part chunks, Part grit, Random rng, Vector3 centre, Vector2 radius, float height, int count, float size, Func<Random, Vector4> paint, Vector4 dust, float yaw = 0)
    {
        var turn = Matrix4x4.CreateRotationY(yaw);
        Shapes.Blob(grit, centre, new Vector3(radius.X, height * 0.75f, radius.Y), 2, 0.3f, rng.Next(1000), (p, n) => Shade(dust, 0.8f + 0.3f * Fbm(p * 6, 5)),
            yaw: yaw, shape: q => q with { Y = MathF.Max(q.Y, centre.Y) }, frequency: 2.2f);
        for (var k = 0; k < count; k++)
        {
            var a = (float)(rng.NextDouble() * Math.Tau); var r = MathF.Sqrt((float)rng.NextDouble()) * 0.95f;
            var off = Vector3.Transform(new Vector3(MathF.Cos(a) * r * radius.X, 0, MathF.Sin(a) * r * radius.Y), turn);
            var y = centre.Y + height * 0.75f * MathF.Sqrt(MathF.Max(0, 1 - r * r)) * 0.85f;
            var s = size * (0.45f + 0.9f * (float)rng.NextDouble());
            var kind = rng.NextDouble();
            var dims = kind < 0.4 ? new Vector3(s * 1.6f, s * 0.75f, s * 0.8f)     // a brick or block
                : kind < 0.65 ? new Vector3(s * 1.7f, s * 0.28f, s * 1.3f)            // a slab
                : new Vector3(s, s * 0.8f, s * 0.9f);                                  // a broken chunk
            var rot = Turn((float)(rng.NextDouble() * Math.Tau), ((float)rng.NextDouble() - 0.5f) * 1.2f, ((float)rng.NextDouble() - 0.5f) * 1.2f);
            var col = paint(rng);
            Block(chunks, centre + off with { Y = 0 } + Vector3.UnitY * (y - centre.Y + dims.Y * 0.25f), dims, rot, s * 0.12f,
                p => Shade(col, 0.92f + 0.12f * Noise(p.X * 13, p.Y * 13, p.Z * 13, 3)), rng, kind < 0.65 ? 0.08f : 0.28f);
        }
    }

    /// <summary>An inches-authored model copied into the unit box (its vertices are already divided by its inches): for a mesh shared
    /// by entries of different sizes, authored at its typical size and fitted to each.</summary>
    public static Model Unit(Model authored)
    {
        var unit = new Model(authored.Name, authored.Round);
        foreach (var p in authored.Parts)
        {
            var q = unit[p.Material];
            var offset = q.Positions.Count;
            q.Positions.AddRange(p.Positions); q.Normals.AddRange(p.Normals); q.Colours.AddRange(p.Colours);
            q.Indices.AddRange(p.Indices.Select(i => i + offset));
        }
        return unit;
    }
}
