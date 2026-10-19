using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Writes into a part in inches: every point is multiplied by <see cref="S"/> before the part's clamp. One for a model
/// authored in inches; one over the size it is drawn at for a unit-box model.</summary>
public readonly record struct Quill(Part Part, Vector3 S)
{
    /// <summary>A pen on <paramref name="m"/> of <paramref name="model"/>, authoring in inches at <paramref name="drawn"/> (the model's
    /// own inches when it has them).</summary>
    public static Quill Of(Model model, Material m, Vector3 drawn) => new(model[m], model.Inches == Vector3.One ? Vector3.One / drawn : Vector3.One);

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc) => Part.Tri(a * S, b * S, c * S, ca, cb, cc);
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 colour) => Tri(a, b, c, colour, colour, colour);

    /// <summary>A triangle wound so its front faces <paramref name="outward"/>.</summary>
    public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector4 ca, Vector4 cb, Vector4 cc)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) Tri(a, c, b, ca, cc, cb); else Tri(a, b, c, ca, cb, cc);
    }

    public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Func<Vector3, Vector4> colour) => Face(a, b, c, outward, colour(a), colour(b), colour(c));

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Func<Vector3, Vector4> colour)
    {
        Face(a, b, c, outward, colour); Face(a, c, d, outward, colour);
    }
}

/// <summary>Hobby-scenery detail in inches through a <see cref="Quill"/>: faceted bark tubes, branching wire-armature trees with
/// clump foliage of many small leaf cards, conifers of drooping needle sprays, fronds, blades, grass tufts, stones, leaf litter
/// and faceted ground. Everything is flat-shaded; leaves, blades and fronds are single cards for a double-sided material.</summary>
public static class Foliage
{
    public static readonly Vector3 Up = Vector3.UnitY;

    static float U(Random rng) => (float)rng.NextDouble();
    static float S(Random rng) => (float)rng.NextDouble() * 2 - 1;
    public static Vector3 Sphere(Random rng) { Vector3 v; do v = new Vector3(S(rng), S(rng), S(rng)); while (v.LengthSquared() is > 1 or < 1e-4f); return Vector3.Normalize(v); }
    public static Vector3 Flat(float a) => new(MathF.Cos(a), 0, MathF.Sin(a));
    static Vector3 Perp(Vector3 d) => Vector3.Normalize(Vector3.Cross(MathF.Abs(d.Y) < 0.9f ? Up : Vector3.UnitX, d));

    /// <summary>A faceted tube through <paramref name="points"/> with a radius per point, its rings carried along by parallel transport
    /// and roughened by <paramref name="rough"/> (bark ridges run along it). A radius of 0 ends in a point; <paramref name="cap"/> closes the end.</summary>
    public static void Tube(Quill pen, IReadOnlyList<Vector3> points, IReadOnlyList<float> radii, int sides, Func<Vector3, Vector4> colour, int seed, float rough = 0.18f, bool cap = false)
    {
        var n = points.Count;
        var x = Perp(Vector3.Normalize(points[1] - points[0]));
        var rings = new Vector3[n][];
        var phase = seed * 0.37f;
        for (var i = 0; i < n; i++)
        {
            var t = Vector3.Normalize(i == 0 ? points[1] - points[0] : i == n - 1 ? points[n - 1] - points[n - 2] : points[i + 1] - points[i - 1]);
            var px = x - t * Vector3.Dot(x, t);
            x = px.LengthSquared() > 1e-8f ? Vector3.Normalize(px) : Perp(t);
            var z = Vector3.Cross(t, x);
            rings[i] = new Vector3[sides];
            for (var j = 0; j < sides; j++)
            {
                var a = phase + 2 * MathF.PI * j / sides;
                var r = radii[i] * (1 + rough * (Noise(j * 1.9f, i * 0.45f, 0, seed) * 2 - 1));
                rings[i][j] = points[i] + (x * MathF.Cos(a) + z * MathF.Sin(a)) * r;
            }
        }
        for (var i = 0; i < n - 1; i++)
        {
            var axis = (points[i] + points[i + 1]) / 2;
            for (var j = 0; j < sides; j++)
            {
                var j1 = (j + 1) % sides;
                var (a, b, c, d) = (rings[i][j], rings[i][j1], rings[i + 1][j1], rings[i + 1][j]);
                var outward = (a + b + c + d) / 4 - axis;
                if (radii[i + 1] <= 1e-5f) pen.Face(a, b, points[i + 1], outward, colour);
                else pen.Quad(a, b, c, d, outward, colour);
            }
        }
        if (cap && radii[n - 1] > 1e-5f)
        {
            var end = points[n - 1]; var t = Vector3.Normalize(points[n - 1] - points[n - 2]);
            for (var j = 0; j < sides; j++) pen.Face(end, rings[n - 1][j], rings[n - 1][(j + 1) % sides], t, colour);
        }
    }

    /// <summary>A diamond leaf card centred at <paramref name="at"/>, facing <paramref name="normal"/>, pointing along <paramref name="dir"/>.</summary>
    public static void Leaf(Quill pen, Vector3 at, Vector3 normal, Vector3 dir, float length, float width, Vector4 colour, bool single = false)
    {
        if (single)
        {
            var half = Vector3.Normalize(Vector3.Cross(normal, dir)) * width / 2;
            pen.Face(at - dir * length * 0.4f - half, at - dir * length * 0.4f + half, at + dir * length * 0.6f, normal, colour, colour, colour);
            return;
        }
        var side = Vector3.Normalize(Vector3.Cross(normal, dir)) * width / 2;
        var b = at - dir * length * 0.42f; var t = at + dir * length * 0.58f; var m = at + dir * length * 0.06f;
        pen.Face(b, m + side, t, normal, colour, colour, colour);
        pen.Face(b, t, m - side, normal, colour, colour, colour);
    }

    /// <summary>A clump of <paramref name="count"/> leaf cards in an ellipsoid, crowded toward its skin, each turned outward and up with
    /// some scatter; <paramref name="pick"/> colours each leaf; <paramref name="confine"/> keeps them in the piece.</summary>
    public static void Clump(Quill pen, Random rng, Vector3 centre, Vector3 radii, int count, float size, Func<Vector3, Random, Vector4> pick, Func<Vector3, Vector3>? confine = null, float lift = 0.7f, bool single = false)
    {
        for (var k = 0; k < count; k++)
        {
            var o = Sphere(rng);
            if (o.Y < -0.3f && U(rng) < 0.6f) o = o with { Y = -o.Y * 0.5f };   // fewer leaves on the underside
            var at = centre + o * radii * (0.55f + 0.45f * MathF.Cbrt(U(rng)));
            if (confine is not null) at = confine(at);
            var normal = Vector3.Normalize(Vector3.Normalize(o) + Up * lift + Sphere(rng) * 0.65f);
            var dir = Vector3.Normalize(Vector3.Cross(normal, Sphere(rng)));
            var l = size * (0.7f + 0.6f * U(rng));
            Leaf(pen, at, normal, dir, l, l * (single ? 0.7f + 0.3f * U(rng) : 0.45f + 0.2f * U(rng)), pick(at, rng), single);
        }
    }

    /// <summary>A leaf colour: one of <paramref name="greens"/> with per-leaf jitter, now and then yellowed or dark.</summary>
    public static Vector4 LeafColour(Random rng, Vector4[] greens, Vector4 yellow, Vector4 dark, float yellowed = 0.05f, float darker = 0.1f)
    {
        var u = U(rng);
        var c = u < yellowed ? Mix(greens[rng.Next(greens.Length)], yellow, 0.5f + 0.5f * U(rng)) : u < yellowed + darker ? dark : greens[rng.Next(greens.Length)];
        return new Vector4(Math.Clamp(c.X * (0.88f + 0.24f * U(rng)), 0, 1), Math.Clamp(c.Y * (0.88f + 0.24f * U(rng)), 0, 1), Math.Clamp(c.Z * (0.9f + 0.2f * U(rng)), 0, 1), 1);
    }

    /// <summary>Branches grown from <paramref name="from"/> along <paramref name="dir"/>: each limb a bent bark tube that splits into two or
    /// three children <paramref name="depth"/> times, each child shorter and thinner and turned out and up. Returns the twig tips and
    /// the points along the finest limbs, where foliage goes.</summary>
    public static List<(Vector3 At, Vector3 Dir)> Branches(Quill bark, Random rng, Vector3 from, Vector3 dir, float length, float radius, int depth,
        Func<Vector3, Vector4> colour, Func<Vector3, Vector3> confine, float spread = 0.7f, float rise = 0.3f, float shrink = 0.7f, int seed = 0)
    {
        var tips = new List<(Vector3, Vector3)>();
        void Grow(Vector3 a, Vector3 d, float len, float r, int level, int id)
        {
            var bend = Sphere(rng) * 0.22f;
            var mid = confine(a + d * len * 0.5f + bend * len * 0.3f);
            var b = confine(a + Vector3.Normalize(d + bend * 0.6f) * len);
            var sides = r > 0.1f ? 7 : r > 0.05f ? 5 : r > 0.025f ? 4 : 3;
            if (level == 0) Tube(bark, [a, b], [r, r * 0.6f], sides, colour, seed * 31 + id, 0.2f);
            else Tube(bark, [a, mid, b], [r, r * 0.86f, r * 0.7f], sides, colour, seed * 31 + id, 0.2f);
            var heading = Vector3.Normalize(b - a);
            if (level == 0) { tips.Add((b, heading)); tips.Add((mid, heading)); return; }
            var kids = level >= 2 ? 3 : 2 + rng.Next(2);
            var turn = U(rng) * 6.28f;
            for (var k = 0; k < kids; k++)
            {
                var az = turn + 2 * MathF.PI * k / kids + S(rng) * 0.4f;
                var x = Perp(heading); var z = Vector3.Cross(heading, x);
                var side = x * MathF.Cos(az) + z * MathF.Sin(az);
                var ang = spread * (0.65f + 0.5f * U(rng));
                var nd = Vector3.Normalize(heading * MathF.Cos(ang) + side * MathF.Sin(ang) + Up * rise);
                Grow(b, nd, len * shrink * (0.85f + 0.3f * U(rng)), r * 0.64f, level - 1, id * 4 + k + 1);
            }
            if (level >= 2 && U(rng) < 0.7f)   // a side shoot from the middle of the limb
            {
                var side = Perp(heading); side = Vector3.Normalize(side * MathF.Cos(U(rng) * 6.28f) + Vector3.Cross(heading, side) * MathF.Sin(U(rng) * 6.28f));
                Grow(mid, Vector3.Normalize(heading * 0.4f + side + Up * rise), len * shrink * 0.8f, r * 0.5f, level - 2, id * 4 + 9);
            }
        }
        Grow(from, dir, length, radius, depth, 1);
        return tips;
    }

    /// <summary>Roots spreading from a trunk's foot into the ground: tapering tubes, half sunk.</summary>
    public static void Roots(Quill bark, Random rng, Vector3 foot, float radius, int count, Func<Vector3, Vector4> colour, int seed, float reach = 4.5f)
    {
        var turn = U(rng) * 6.28f;
        for (var k = 0; k < count; k++)
        {
            var o = Flat(turn + 2 * MathF.PI * k / count + S(rng) * 0.35f);
            var l = radius * reach * (0.7f + 0.5f * U(rng));
            var bend = Flat(U(rng) * 6.28f) * l * 0.15f;
            Tube(bark, [foot + Up * radius * 1.4f + o * radius * 0.4f, foot + o * l * 0.45f + bend + Up * radius * 0.35f, foot + o * l + bend * 1.5f - Up * radius * 0.2f],
                [radius * 0.55f, radius * 0.32f, radius * 0.06f], 4, colour, seed + k, 0.25f);
        }
    }

    /// <summary>A sawtooth spray along <paramref name="spine"/>: a leaflet each side per segment, raked forward, its tip <paramref name="width"/>
    /// out and <paramref name="droop"/> down; the side direction tilted by <paramref name="tilt"/> about the spine.</summary>
    public static void Frond(Quill pen, IReadOnlyList<Vector3> spine, Func<float, float> width, Vector4 baseColour, Vector4 tipColour, float rake = 0.6f, float droop = 0.3f, float tilt = 0, float extra = 0, float gap = 0)
    {
        for (var i = 0; i < spine.Count - 1; i++)
        {
            var (a, b) = (spine[i], spine[i + 1]); var d = b - a;
            if (d.LengthSquared() < 1e-10f) continue;
            var t = Vector3.Normalize(d);
            var side = Vector3.Cross(t, Up); side = side.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(side);
            var up = Vector3.Cross(side, t);
            var w = width((i + 0.5f) / (spine.Count - 1));
            foreach (var s in new[] { -1f, 1f })
            {
                var sd = side * s * MathF.Cos(tilt) + up * MathF.Sin(tilt);
                var tip = a + d * (0.5f + rake) + sd * w - up * droop * w;
                var normal = Vector3.Cross(d, tip - a) * s;
                pen.Face(a, a + d * (1 - gap), tip, normal.LengthSquared() > 1e-12f ? normal : up, baseColour, baseColour, tipColour);
                if (extra > 0) pen.Face(a + d * 0.5f, b + d * 0.5f, tip + d * extra, normal.LengthSquared() > 1e-12f ? normal : up, baseColour, baseColour, tipColour);
            }
        }
    }

    /// <summary>A long blade from <paramref name="root"/> out along <paramref name="dir"/>, arching up by <paramref name="rise"/> and falling
    /// <paramref name="fall"/> at the tip, widest a third of the way along, in <paramref name="segments"/> pieces.</summary>
    public static void Blade(Quill pen, Vector3 root, Vector3 dir, float length, float width, float rise, float fall, Vector4 rootColour, Vector4 tipColour, int segments = 4, float twist = 0)
    {
        var flat = Vector3.Normalize(dir with { Y = 0 } + new Vector3(1e-4f, 0, 0));
        var side0 = new Vector3(-flat.Z, 0, flat.X);
        Vector3 P(float u) => root + dir * length * u + Up * (rise * MathF.Sin(MathF.PI * MathF.Min(u * 1.2f, 1)) * (1 - u * 0.3f) - fall * u * u);
        Vector3 Side(float u) { var a = twist * u; return (side0 * MathF.Cos(a) + Up * MathF.Sin(a)) * width * 0.5f * MathF.Sin(MathF.PI * (0.12f + 0.88f * u)) * (u < 0.35f ? 0.6f + u / 0.35f * 0.4f : 1); }
        for (var i = 0; i < segments; i++)
        {
            float u0 = (float)i / segments, u1 = (float)(i + 1) / segments;
            var (a, b) = (P(u0), P(u1)); var (sa, sb) = (Side(u0), Side(u1));
            var (ca, cb) = (Mix(rootColour, tipColour, u0), Mix(rootColour, tipColour, u1));
            var normal = Vector3.Cross(b - a, sa + sb);
            if (i == segments - 1) { pen.Face(a - sa, a + sa, b, normal, ca, ca, cb); continue; }
            pen.Face(a - sa, a + sa, b + sb, normal, ca, ca, cb); pen.Face(a - sa, b + sb, b - sb, normal, ca, cb, cb);
        }
    }

    /// <summary>A tuft of grass blades from <paramref name="root"/>: thin triangles splaying out, one segment each, or two (bent) when <paramref name="bent"/>.</summary>
    public static void Tuft(Quill pen, Random rng, Vector3 root, int blades, float height, float width, float splay, Vector4 baseColour, Vector4 tipColour, bool bent = false)
    {
        for (var i = 0; i < blades; i++)
        {
            var o = Flat(U(rng) * 6.28f); var side = new Vector3(-o.Z, 0, o.X) * width / 2;
            var h = height * (0.6f + 0.4f * U(rng)); var lean = splay * (0.4f + 0.8f * U(rng));
            var foot = root + o * width * 0.8f * U(rng);
            var tip = foot + o * lean * h + Up * h;
            var tc = Mix(tipColour, baseColour, U(rng) * 0.3f);
            if (!bent) { pen.Face(foot - side, foot + side, tip, Vector3.Cross(side, tip - foot), baseColour, baseColour, tc); continue; }
            var mid = foot + o * lean * h * 0.3f + Up * h * 0.6f; var midColour = Mix(baseColour, tc, 0.5f);
            var normal = Vector3.Cross(side, tip - foot);
            pen.Face(foot - side, foot + side, mid + side * 0.6f, normal, baseColour, baseColour, midColour);
            pen.Face(foot - side, mid + side * 0.6f, mid - side * 0.6f, normal, baseColour, midColour, midColour);
            pen.Face(mid - side * 0.6f, mid + side * 0.6f, tip, normal, midColour, midColour, tc);
        }
    }

    /// <summary>A faceted stone: an icosphere roughened by noise, scaled by <paramref name="size"/>, turned by <paramref name="yaw"/>; sunk where it meets the table.</summary>
    public static void Stone(Quill pen, Vector3 at, Vector3 size, int seed, float yaw, Func<Vector3, Vector3, Vector4> colour, int level = 0, float lumps = 0.35f)
    {
        var (unit, faces) = Shapes.Icosphere(level);
        var rot = Matrix4x4.CreateRotationY(yaw);
        var p = unit.Select(u => at + Vector3.Transform(u * (1 + lumps * (Noise(u.X * 1.7f + seed, u.Y * 1.7f, u.Z * 1.7f, seed) * 2 - 1)) * size, rot)).ToArray();
        for (var i = 0; i < faces.Length; i += 3)
        {
            var (a, b, c) = (p[faces[i]], p[faces[i + 1]], p[faces[i + 2]]);
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-14f) continue;
            var col = colour((a + b + c) / 3, Vector3.Normalize(n));
            pen.Face(a, b, c, (a + b + c) / 3 - at, col, col, col);
        }
    }

    /// <summary>A small leaf lying on the ground at <paramref name="at"/>.</summary>
    public static void Litter(Quill pen, Random rng, Vector3 at, float size, Vector4 colour)
    {
        var n = Vector3.Normalize(Up + Sphere(rng) * 0.35f);
        Leaf(pen, at, n, Vector3.Normalize(Vector3.Cross(n, Sphere(rng))), size, size * 0.55f, colour);
    }

    /// <summary>A faceted height field: <paramref name="rows"/> × <paramref name="cols"/> points from <paramref name="at"/>, two flat triangles a
    /// cell, each facing up; colours asked per corner with the face's normal.</summary>
    public static void Field(Quill pen, int rows, int cols, Func<int, int, Vector3> at, Func<Vector3, Vector3, Vector4> colour, bool wrap = false)
    {
        var p = new Vector3[rows, cols];
        for (var i = 0; i < rows; i++) for (var j = 0; j < cols; j++) p[i, j] = at(i, j);
        var last = wrap ? cols : cols - 1;
        for (var i = 0; i < rows - 1; i++)
            for (var j = 0; j < last; j++)
            {
                var j1 = (j + 1) % cols;
                var (a, b, c, d) = (p[i, j], p[i, j1], p[i + 1, j1], p[i + 1, j]);
                var flip = ((i + j) & 1) == 0;
                foreach (var (x, y, z) in flip ? new[] { (a, b, c), (a, c, d) } : new[] { (a, b, d), (b, c, d) })
                {
                    var n = Vector3.Cross(y - x, z - x);
                    if (n.LengthSquared() < 1e-14f) continue;
                    if (n.Y < 0) n = -n;
                    var nn = Vector3.Normalize(n);
                    pen.Face(x, y, z, n, colour(x, nn), colour(y, nn), colour(z, nn));
                }
            }
    }

    /// <summary>Deterministic jitter in [−1, 1] for lattice point (i, j).</summary>
    public static float Jitter(int i, int j, int seed) => Noise(i * 0.5f + 0.25f, j * 0.5f + 0.25f, seed * 0.13f, seed) * 2 - 1;

    /// <summary>A faceted round base: <paramref name="rings"/> rings out to an irregular <paramref name="outline"/> (radius by angle), the rim at y = 0
    /// and the ground at <paramref name="height"/>(x, z, fraction of the way out).</summary>
    public static void Disc(Quill pen, int rings, int segments, Func<float, float> outline, Func<float, float, float, float> height, Func<Vector3, Vector3, Vector4> colour, int seed)
    {
        Field(pen, rings + 1, segments, (i, j) =>
        {
            var u = (float)i / rings;
            var t = 2 * MathF.PI * (j + (i is > 0 and < 1000 && i < rings ? 0.35f * Jitter(i, j, seed) : 0)) / segments;
            var r = outline(t) * MathF.Min(1, u + (i > 0 && i < rings ? 0.3f / rings * Jitter(j, i, seed + 1) : 0));
            var (x, z) = (r * MathF.Cos(t), r * MathF.Sin(t));
            return new Vector3(x, i == rings ? 0 : height(x, z, u), z);
        }, colour, wrap: true);
    }

    /// <summary>A faceted rect ground a × b centred on the origin, its border at y = 0, its inner points jittered by a third of a cell.</summary>
    public static void Rect(Quill pen, float a, float b, int nx, int nz, Func<float, float, float> height, Func<Vector3, Vector3, Vector4> colour, int seed, float jitter = 0.33f)
    {
        Field(pen, nz + 1, nx + 1, (i, j) =>
        {
            var edge = i == 0 || j == 0 || i == nz || j == nx;
            var x = a * (-0.5f + (j + (edge ? 0 : jitter * Jitter(i, j, seed))) / nx);
            var z = b * (-0.5f + (i + (edge ? 0 : jitter * Jitter(j, i, seed + 1))) / nz);
            return new Vector3(x, edge ? 0 : height(x, z), z);
        }, colour);
    }

    /// <summary>Keeps a point within a circle of <paramref name="radius"/> about the origin and under <paramref name="top"/>.</summary>
    public static Func<Vector3, Vector3> Within(float radius, float top) => p =>
    {
        var r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
        if (r > radius) p = p with { X = p.X * radius / r, Z = p.Z * radius / r };
        return p with { Y = MathF.Min(p.Y, top) };
    };

    /// <summary>Keeps a point within a rect of half-sizes <paramref name="hx"/>, <paramref name="hz"/> and under <paramref name="top"/>.</summary>
    public static Func<Vector3, Vector3> Within(float hx, float hz, float top) => p => new Vector3(Math.Clamp(p.X, -hx, hx), MathF.Min(p.Y, top), Math.Clamp(p.Z, -hz, hz));

    /// <summary>A broadleaf hobby tree: a trunk with flared roots, limbs branching <paramref name="depth"/> times, and a clump of leaves
    /// at every twig. <paramref name="crown"/> is the clumps' radius; <paramref name="leaves"/> per clump.</summary>
    public static void Broadleaf(Quill bark, Quill foliage, Random rng, Vector3 foot, float height, float girth, Func<Vector3, Vector4> barkColour,
        Func<Vector3, Random, Vector4> leaf, Func<Vector3, Vector3> confine, int seed, int depth = 3, float clump = 0.42f, int leaves = 34, float leafSize = 0.2f, float spread = 0.62f, float reach = 0.17f, float bole = 0.45f, bool single = true)
    {
        var lean = Flat(U(rng) * 6.28f) * 0.12f;
        var trunkTop = foot + Up * height * bole + lean * height * 0.3f;
        var trunk = new[] { foot - Up * 0.05f, foot + Up * girth * 1.2f, foot + Up * height * 0.18f + lean * height * 0.06f, trunkTop };
        Tube(bark, trunk, [girth * 1.75f, girth * 1.2f, girth, girth * 0.82f], 8, barkColour, seed, 0.22f);
        Roots(bark, rng, foot, girth, 5, barkColour, seed + 100);
        var tips = new List<(Vector3 At, Vector3 Dir)>();
        var limbs = 3 + rng.Next(2);
        var turn = U(rng) * 6.28f;
        for (var k = 0; k < limbs; k++)
        {
            var o = Flat(turn + 2 * MathF.PI * k / limbs + S(rng) * 0.3f);
            var d = Vector3.Normalize(Up * 0.9f + o * (0.8f + 0.4f * U(rng)));
            tips.AddRange(Branches(bark, rng, trunkTop - Up * height * 0.04f * k, d, height * reach, girth * 0.62f, depth - 1, barkColour, confine, spread, 0.35f, 0.72f, seed * 7 + k));
        }
        tips.AddRange(Branches(bark, rng, trunkTop, Up, height * reach * 1.05f, girth * 0.6f, depth - 1, barkColour, confine, spread * 0.8f, 0.4f, 0.7f, seed * 7 + 9));
        var top = tips.Max(t => t.At.Y);
        foreach (var (at, dir) in tips)
        {
            var c = confine(at + dir * clump * 0.4f);
            var s = clump * (0.75f + 0.45f * U(rng)) * (0.8f + 0.3f * Smooth(top - height * 0.4f, top, at.Y));
            Clump(foliage, rng, c, new Vector3(s, s * 0.8f, s), leaves, leafSize, leaf, confine, single: single);
        }
    }

    /// <summary>A conifer: a straight tapering trunk to the tip and whorls of drooping branches up it, each a twig with two needle
    /// sprays in a shallow V, longest at the foot of the crown.</summary>
    public static void Conifer(Quill bark, Quill needles, Random rng, Vector3 foot, float height, float radius, float girth, Func<Vector3, Vector4> barkColour,
        Vector4 dark, Vector4 light, Func<Vector3, Vector3> confine, int seed, float start = 0.16f, float step = 0.2f)
    {
        Tube(bark, [foot - Up * 0.05f, foot + Up * girth * 1.5f, foot + Up * height * 0.5f, foot + Up * height * 0.97f], [girth * 1.6f, girth * 1.05f, girth * 0.6f, 0], 6, barkColour, seed, 0.2f);
        Roots(bark, rng, foot, girth, 4, barkColour, seed + 100, 3.5f);
        var y0 = height * start;
        var whorl = 0;
        for (var y = y0; y < height * 0.97f; y += step * (0.85f + 0.3f * U(rng)), whorl++)
        {
            var t = (y - y0) / (height - y0);
            var length = radius * MathF.Pow(1 - t, 0.95f) + 0.12f;
            var count = t > 0.85f ? 4 : 6 + rng.Next(2);
            var turn = whorl * 2.4f + U(rng);
            for (var k = 0; k < count; k++)
            {
                var o = Flat(turn + 2 * MathF.PI * k / count + S(rng) * 0.25f);
                var l = length * (0.8f + 0.35f * U(rng));
                var root = foot + Up * y;
                var droop = 0.25f + 0.25f * U(rng);
                var spine = Enumerable.Range(0, 5).Select(i => { var u = i / 4f; return confine(root + o * l * u + Up * (l * 0.12f * u - l * droop * u * u)); }).ToList();
                if (l > 0.35f) Tube(bark, [spine[0], spine[2], spine[4]], [girth * 0.25f, girth * 0.14f, 0.008f], 3, barkColour, seed + whorl * 11 + k, 0.1f);
                var shade = 0.82f + 0.3f * t + 0.1f * S(rng);
                var b = Shade(dark, shade); var tip = Shade(light, shade);
                float W(float u) => l * (0.3f - 0.18f * u) + 0.04f;
                Frond(needles, spine, W, b, tip, 0.55f, 0.35f, 0.35f);
                Frond(needles, spine, u => W(u) * 0.8f, Shade(b, 0.9f), tip, 0.4f, 0.2f, -0.25f);
            }
        }
    }
}
