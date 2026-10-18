using System.Numerics;

namespace Tabletop.TerrainMeshes;

/// <summary>Colours (sRGB, alpha 1) and the deterministic noise every piece shades and roughens with.</summary>
public static class Paint
{
    public static Vector4 Rgb(float r, float g, float b) => new(r, g, b, 1);
    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, Math.Clamp(t, 0, 1));
    public static Vector4 Shade(Vector4 c, float f) => new(Math.Clamp(c.X * f, 0, 1), Math.Clamp(c.Y * f, 0, 1), Math.Clamp(c.Z * f, 0, 1), c.W);
    public static Vector4 Jitter(Vector4 c, Random rng, float amount) => Shade(c, 1 + (float)(rng.NextDouble() * 2 - 1) * amount);
    public static float Smooth(float e0, float e1, float x) { var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); }

    static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 1440662683 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Value noise in [0, 1], smooth between integer lattice points.</summary>
    public static float Noise(float x, float y, float z, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float L(float a, float b, float t) => a + (b - a) * t;
        float H(int dx, int dy, int dz) => Hash(ix + dx, iy + dy, iz + dz, seed);
        return L(L(L(H(0, 0, 0), H(1, 0, 0), fx), L(H(0, 1, 0), H(1, 1, 0), fx), fy),
                 L(L(H(0, 0, 1), H(1, 0, 1), fx), L(H(0, 1, 1), H(1, 1, 1), fx), fy), fz);
    }

    /// <summary>Three octaves of <see cref="Noise"/>, in [0, 1].</summary>
    public static float Fbm(float x, float y, float z, int seed) =>
        (Noise(x, y, z, seed) * 4 + Noise(x * 2.03f, y * 2.03f, z * 2.03f, seed + 1) * 2 + Noise(x * 4.1f, y * 4.1f, z * 4.1f, seed + 2)) / 7;

    public static float Fbm(Vector3 p, int seed) => Fbm(p.X, p.Y, p.Z, seed);
}

/// <summary>Primitive solids, each written into a <see cref="Part"/> through a transform of its unit shape.</summary>
public static class Shapes
{
    public static Matrix4x4 At(Vector3 centre, Vector3 size, float yaw = 0, float pitch = 0, float roll = 0) =>
        Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll) * Matrix4x4.CreateTranslation(centre);

    /// <summary>An axis-aligned box from its min and max corners.</summary>
    public static void Box(Part part, Vector3 min, Vector3 max, Vector4 colour) => Box(part, At((min + max) / 2, max - min), _ => colour);

    public static void Box(Part part, Vector3 min, Vector3 max, Func<Vector3, Vector4> colour) => Box(part, At((min + max) / 2, max - min), colour);

    /// <summary>The unit cube [−0.5, 0.5]³ under <paramref name="m"/>; faces wound outward. A face lying on the ground facing down is left out.</summary>
    public static void Box(Part part, Matrix4x4 m, Func<Vector3, Vector4> colour)
    {
        Vector3 P(int x, int y, int z) => Vector3.Transform(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f), m);
        var centre = Vector3.Transform(Vector3.Zero, m);
        int[][] faces = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];   // corners by bit x·4 + y·2 + z
        foreach (var f in faces)
        {
            var q = f.Select(i => P(i >> 2 & 1, i >> 1 & 1, i & 1)).ToArray();
            if (q.All(v => v.Y < 1e-4f)) continue;
            Oriented(part, q, (q[0] + q[1] + q[2] + q[3]) / 4 - centre, colour);
        }
    }

    public static void Box(Part part, Matrix4x4 m, Vector4 colour) => Box(part, m, _ => colour);

    /// <summary>A prism (or a cone when <paramref name="top"/> is 0): a ring of radius 1 at y = 0 and of radius <paramref name="top"/>
    /// at y = 1, with caps, under <paramref name="m"/>.</summary>
    public static void Prism(Part part, Matrix4x4 m, int sides, float top, Func<Vector3, Vector4> colour, float phase = 0)
    {
        Vector3 P(float r, int i, float y) { var t = phase + 2 * MathF.PI * i / sides; return Vector3.Transform(new Vector3(r * MathF.Cos(t), y, r * MathF.Sin(t)), m); }
        var (bottom, apex) = (Vector3.Transform(Vector3.Zero, m), Vector3.Transform(Vector3.UnitY, m));
        for (var i = 0; i < sides; i++)
        {
            var mid = Vector3.Transform(new Vector3(0, 0.5f, 0), m);
            if (top > 0) Oriented(part, [P(1, i, 0), P(1, i + 1, 0), P(top, i + 1, 1), P(top, i, 1)], (P(1, i, 0) + P(1, i + 1, 0) + P(top, i, 1) + P(top, i + 1, 1)) / 4 - mid, colour);
            else Oriented(part, [P(1, i, 0), P(1, i + 1, 0), apex], (P(1, i, 0) + P(1, i + 1, 0) + apex) / 3 - mid, colour);
            if (bottom.Y > 1e-4f || apex.Y < bottom.Y) Oriented(part, [bottom, P(1, i + 1, 0), P(1, i, 0)], bottom - apex, colour);
            if (top > 0) Oriented(part, [apex, P(top, i, 1), P(top, i + 1, 1)], apex - bottom, colour);
        }
    }

    public static void Prism(Part part, Matrix4x4 m, int sides, float top, Vector4 colour, float phase = 0) => Prism(part, m, sides, top, _ => colour, phase);

    /// <summary>A quad or triangle wound so its front faces <paramref name="outward"/>.</summary>
    public static void Oriented(Part part, Vector3[] q, Vector3 outward, Func<Vector3, Vector4> colour)
    {
        if (Vector3.Dot(Vector3.Cross(q[1] - q[0], q[2] - q[0]), outward) < 0) Array.Reverse(q);
        part.Tri(q[0], q[1], q[2], colour(q[0]), colour(q[1]), colour(q[2]));
        if (q.Length == 4) part.Tri(q[0], q[2], q[3], colour(q[0]), colour(q[2]), colour(q[3]));
    }

    static readonly (Vector3[] Vertices, int[] Faces)[] Spheres = new (Vector3[], int[])[4];

    /// <summary>An icosphere subdivided <paramref name="level"/> times, unit radius.</summary>
    public static (Vector3[] Vertices, int[] Faces) Icosphere(int level)
    {
        if (Spheres[level].Vertices is { } done) return Spheres[level];
        var t = (1 + MathF.Sqrt(5)) / 2;
        var v = new List<Vector3> { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
        for (var i = 0; i < v.Count; i++) v[i] = Vector3.Normalize(v[i]);
        var f = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        for (var l = 0; l < level; l++)
        {
            var mids = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mids.TryGetValue(key, out var m)) return m;
                v.Add(Vector3.Normalize(v[a] + v[b])); return mids[key] = v.Count - 1;
            }
            var next = new List<int>();
            for (var i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]);
            }
            f = next;
        }
        return Spheres[level] = (v.ToArray(), f.ToArray());
    }

    /// <summary>A lumpy sphere: the icosphere's points pushed out by noise (<paramref name="lumps"/> of the radius), scaled by
    /// <paramref name="radii"/>, turned by <paramref name="yaw"/>, and moved to <paramref name="centre"/>; <paramref name="shape"/>
    /// then bends each point (a flattened top, a sunk base). Flat-shaded unless <paramref name="smooth"/>.</summary>
    public static void Blob(Part part, Vector3 centre, Vector3 radii, int level, float lumps, int seed, Func<Vector3, Vector3, Vector4> colour,
        bool smooth = false, float yaw = 0, Func<Vector3, Vector3>? shape = null, float frequency = 1.6f)
    {
        var (unit, faces) = Icosphere(level);
        var rotation = Matrix4x4.CreateRotationY(yaw);
        var p = unit.Select(u =>
        {
            var d = 1 + lumps * (Paint.Fbm(u * frequency + new Vector3(seed * 1.7f), seed) * 2 - 1);
            var q = centre + Vector3.Transform(u * d * radii, rotation);
            return shape is null ? q : shape(q);
        }).ToArray();
        if (!smooth)
        {
            for (var i = 0; i < faces.Length; i += 3)
            {
                var (a, b, c) = (p[faces[i]], p[faces[i + 1]], p[faces[i + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                if (n.LengthSquared() < 1e-14f) continue;
                var col = colour((a + b + c) / 3, Vector3.Normalize(n));
                part.Tri(a, b, c, col);
            }
            return;
        }
        p = p.Select(part.Clamp).ToArray();
        var normals = new Vector3[p.Length];
        for (var i = 0; i < faces.Length; i += 3)
        {
            var (a, b, c) = (faces[i], faces[i + 1], faces[i + 2]);
            var n = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        var index = p.Select((q, i) => { var n = normals[i].LengthSquared() > 0 ? Vector3.Normalize(normals[i]) : Vector3.UnitY; return part.Vertex(q, n, colour(q, n)); }).ToArray();
        for (var i = 0; i < faces.Length; i += 3) part.Indices.AddRange([index[faces[i]], index[faces[i + 1]], index[faces[i + 2]]]);
    }

    /// <summary>A tuft of thin blades rising from <paramref name="root"/>: each a triangle leaning outward, coloured from
    /// <paramref name="base"/> at the root to <paramref name="tip"/>. For a double-sided material.</summary>
    public static void Tuft(Part part, Random rng, Vector3 root, int blades, float height, float width, float lean, Vector4 @base, Vector4 tip)
    {
        for (var i = 0; i < blades; i++)
        {
            var a = (float)(rng.NextDouble() * MathF.PI * 2);
            var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var side = new Vector3(-dir.Z, 0, dir.X) * width / 2;
            var h = height * (0.65f + 0.35f * (float)rng.NextDouble());
            var o = root + dir * width * 0.6f;
            var top = o + dir * lean * h + Vector3.UnitY * h;
            part.Tri(o - side, o + side, top, @base, @base, tip);
        }
    }

    /// <summary>A prism from <paramref name="a"/> to <paramref name="b"/>: radius <paramref name="r"/> at a, r × <paramref name="taper"/> at b.</summary>
    public static void Limb(Part part, Vector3 a, Vector3 b, float r, float taper, int sides, Func<Vector3, Vector4> colour)
    {
        var d = b - a; var y = Vector3.Normalize(d);
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(y.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, y)); var z = Vector3.Cross(x, y);
        Prism(part, new Matrix4x4(x.X * r, x.Y * r, x.Z * r, 0, d.X, d.Y, d.Z, 0, z.X * r, z.Y * r, z.Z * r, 0, a.X, a.Y, a.Z, 1), sides, taper, colour);
    }

    public static void Limb(Part part, Vector3 a, Vector3 b, float r, float taper, int sides, Vector4 colour) => Limb(part, a, b, r, taper, sides, _ => colour);

    /// <summary>A chain of limbs through <paramref name="points"/>, the radius falling linearly from <paramref name="r0"/> to <paramref name="r1"/>.</summary>
    public static void Chain(Part part, IReadOnlyList<Vector3> points, float r0, float r1, int sides, Func<Vector3, Vector4> colour)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            float ra = r0 + (r1 - r0) * i / (points.Count - 1), rb = r0 + (r1 - r0) * (i + 1) / (points.Count - 1);
            Limb(part, points[i], points[i + 1], ra, rb / ra, sides, colour);
        }
    }

    public static void Chain(Part part, IReadOnlyList<Vector3> points, float r0, float r1, int sides, Vector4 colour) => Chain(part, points, r0, r1, sides, _ => colour);

    /// <summary>A flat-topped solid between <paramref name="y0"/> and <paramref name="y1"/> over a star-shaped outline (counter-clockwise
    /// seen from above or not), the top a fan from the outline's centroid, the sides one quad per edge.</summary>
    public static void Slab(Part part, IReadOnlyList<Vector2> outline, float y0, float y1, Func<Vector3, Vector3, Vector4> colour, float topScale = 1)
    {
        var c = outline.Aggregate(Vector2.Zero, (s, v) => s + v) / outline.Count;
        Vector2 Top(Vector2 v) => c + (v - c) * topScale;
        for (var i = 0; i < outline.Count; i++)
        {
            var (a, b) = (outline[i], outline[(i + 1) % outline.Count]);
            var (ta, tb) = (Top(a), Top(b));
            var mid = (a + b) / 2 - c;
            var q = new[] { new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y), new Vector3(tb.X, y1, tb.Y), new Vector3(ta.X, y1, ta.Y) };
            var side = new Vector3(mid.X, 0, mid.Y);
            Oriented(part, q, side, p => colour(p, Vector3.Normalize(side)));
            Oriented(part, [new Vector3(c.X, y1, c.Y), new Vector3(ta.X, y1, ta.Y), new Vector3(tb.X, y1, tb.Y)], Vector3.UnitY, p => colour(p, Vector3.UnitY));
            if (y0 > 1e-4f) Oriented(part, [new Vector3(c.X, y0, c.Y), new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y)], -Vector3.UnitY, p => colour(p, -Vector3.UnitY));
        }
    }

    /// <summary>A height field over a rect footprint authored in inches (x in ±a/2, z in ±b/2), n × m cells, the border at y = 0.</summary>
    public static void Ground(Model model, Material m, int n, int k, Func<float, float, float> height, Func<Vector3, Vector3, Vector4> colour)
    {
        var (a, b) = (model.Inches.X, model.Inches.Z);
        model[m].Grid(k + 1, n + 1, wrap: false, (i, j) =>
        {
            var x = a * (-0.5f + (float)j / n); var z = b * (-0.5f + (float)i / k);
            return new Vector3(x, i == 0 || j == 0 || i == k || j == n ? 0 : height(x, z), z);
        }, (p, nrm) => colour(model.Inch(p), nrm), flipped: true);
    }

    /// <summary>A height field over a round footprint authored in inches (radius a/2), the rim at y = 0.</summary>
    public static void Disc(Model model, Material m, int rings, int segments, Func<float, float, float> height, Func<Vector3, Vector3, Vector4> colour)
    {
        var radius = model.Inches.X / 2;
        model[m].Grid(rings + 1, segments, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments; var r = radius * i / rings;
            var (x, z) = (r * MathF.Cos(t), r * MathF.Sin(t));
            return new Vector3(x, i == rings ? 0 : height(x, z), z);
        }, (p, nrm) => colour(model.Inch(p), nrm));
    }
}
