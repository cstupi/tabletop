using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The death-planet growths, each round, 6" across and 6" high, on a faceted base. <see cref="Deadly"/>: carnivorous and thorned
/// alien plants spaced on a sickly purple ground: pitcher plants with veined lips and hooded lids, snapping traps on stalks with teeth
/// round their jaws, barbed tendrils, spiked seed pods, rosettes of serrated blades and glowing spore bulbs. <see cref="Jungle"/>: a
/// thick jungle: palms with ringed trunks and drooping pinnate fronds, buttressed broadleaf trees with big-leaved crowns and hanging
/// vines, and a floor packed with elephant-ear leaves, ferns, spiky rosettes, tufts, mossy stones and leaf litter.</summary>
public static class Vegetation
{
    public static readonly Material Mat = new("mat", 0.9f), Flesh = new("plant", 0.6f), Leaf = new("leaf", 0.75f, DoubleSided: true),
        Thorn = new("thorn", 0.7f), Glow = new("spores", 0.4f, Emissive: new Vector3(0.55f, 0.75f, 0.15f)), Bark = new("bark", 0.9f), Canopy = new("canopy", 0.8f, DoubleSided: true),
        Stone = new("stone", 0.85f);
    const float D = 6, R = 3;

    static Vector3 At(float r, float t, float y = 0) => new(r * MathF.Cos(t), y, r * MathF.Sin(t));
    static float U(Random r) => (float)r.NextDouble();

    /// <summary>The faceted round base both pieces stand on; returns its ground height.</summary>
    static Func<float, float, float> Base(Quill pen, int seed, Func<Vector3, Vector3, Vector4> colour)
    {
        float Outline(float t) => R - 0.05f - 0.2f * Fbm(MathF.Cos(t) * 1.3f + 5, MathF.Sin(t) * 1.3f, 0, seed);
        float Ground(float x, float z) => 0.12f + 0.07f * Fbm(x * 0.9f, z * 0.9f, 2, seed + 3) + 0.02f * Noise(x * 6, z * 6, 1, seed + 4);
        Foliage.Disc(pen, 16, 64, Outline, (x, z, u) => Ground(x, z) * Smooth(1, 0.86f, u), colour, seed);
        return Ground;
    }

    static Vector2 Spot(Random r, float max) { var a = U(r) * 6.28f; var d = max * MathF.Sqrt(U(r)); return new Vector2(MathF.Cos(a), MathF.Sin(a)) * d; }

    public static Model Deadly()
    {
        var model = new Model("deadly-vegetation", round: true, new Vector3(D, D, D));
        var rng = new Random(281);
        Quill P(Material m) => Quill.Of(model, m, model.Inches);
        var (mat, flesh, leaf, thorn, glow, stone) = (P(Mat), P(Flesh), P(Leaf), P(Thorn), P(Glow), P(Stone));
        var confine = Foliage.Within(R - 0.06f, D - 0.05f);
        var purple = Rgb(0.3f, 0.2f, 0.26f); var bruise = Rgb(0.42f, 0.24f, 0.3f); var decay = Rgb(0.28f, 0.3f, 0.16f); var rim = Rgb(0.2f, 0.14f, 0.16f);
        var ground = Base(mat, 281, (p, n) =>
        {
            if (p.Y < 0.1f && n.Y < 0.9f) return Shade(rim, 0.85f + 0.3f * Noise(p.X * 5, p.Z * 5, 0, 9));
            return Shade(Mix(Mix(purple, bruise, Fbm(p.X, p.Z, 1, 282)), decay, Smooth(0.55f, 0.7f, Fbm(p.X * 1.5f + 3, p.Z * 1.5f, 2, 283))), 0.75f + 0.4f * Noise(p.X * 12, p.Z * 12, p.Y * 10, 284));
        });
        Vector3 On(Vector2 p) => new(p.X, ground(p.X, p.Y), p.Y);
        var skin = Rgb(0.36f, 0.5f, 0.18f); var lip = Rgb(0.55f, 0.1f, 0.18f); var maw = Rgb(0.2f, 0.03f, 0.06f); var tooth = Rgb(0.9f, 0.86f, 0.72f);
        Func<Vector3, Vector4> Veined(float y0, float y1, int k) => p => Shade(Mix(skin, lip, Smooth(y0, y1, p.Y)), 0.8f + 0.35f * Noise(p.X * 14, p.Y * 3, p.Z * 14, k));
        // Pitcher plants: flared, ribbed tubes, green at the foot, blood red at the lip, dark inside, with a hooded lid and leaves at the foot.
        foreach (var (r, t, h, k) in new[] { (1.7f, 0.3f, 1.9f, 0), (2.1f, 1.0f, 1.2f, 1), (0.4f, 3.4f, 2.4f, 2), (2.2f, 4.6f, 1.5f, 3), (0.8f, 5.2f, 1.0f, 4) })
        {
            var foot = On(new Vector2(r * MathF.Cos(t), r * MathF.Sin(t))) - Vector3.UnitY * 0.03f;
            var w = 0.2f + 0.02f * k;
            var lean = At(0.15f, t + 1);
            var spine = Enumerable.Range(0, 7).Select(i => { var u = i / 6f; return foot + lean * u * u + Vector3.UnitY * h * u; }).ToList();
            var radii = new[] { w * 0.9f, w * 1.15f, w * 1.3f, w * 1.2f, w * 1.15f, w * 1.4f, w * 1.75f };
            Foliage.Tube(flesh, spine, radii, 10, Veined(foot.Y + h * 0.3f, foot.Y + h * 0.8f, k), 290 + k, 0.12f);
            var mouth = spine[^1];
            Foliage.Tube(flesh, [mouth, mouth - Vector3.UnitY * 0.04f], [w * 1.75f, w * 1.5f], 10, _ => maw, 295 + k, 0.1f, cap: true);
            var up = Vector3.Normalize(Vector3.UnitY + At(0.45f, t)); var hoodLength = w * 3.2f;
            var hood = mouth - At(w * 1.6f, t) + up * hoodLength * 0.42f;
            Foliage.Leaf(leaf, hood, Vector3.Normalize(At(1, t) - Vector3.UnitY * 0.4f), up, hoodLength, w * 2.8f, Mix(lip, Rgb(0.85f, 0.32f, 0.26f), 0.45f));
            for (var l = 0; l < 5; l++)
            {
                var a = t + l * 1.26f + U(rng) * 0.4f;
                Foliage.Blade(leaf, foot + Vector3.UnitY * 0.05f, Vector3.Normalize(At(1, a) + Vector3.UnitY * 0.25f), 0.7f + 0.3f * U(rng), 0.24f, 0.15f, 0.25f, Shade(skin, 0.7f), Mix(skin, lip, 0.35f), 4, 0.6f);
            }
        }
        // Snapping traps: a curved stalk up to a head of two jaws open toward the viewer, teeth round each rim.
        foreach (var (r, t, h, k) in new[] { (0.9f, 1.6f, 3.6f, 0), (2.3f, 2.6f, 2.8f, 1), (1.4f, 5.6f, 4.4f, 2) })
        {
            var foot = On(new Vector2(r * MathF.Cos(t), r * MathF.Sin(t))); var lean = At(0.5f, t + 0.8f);
            var head = foot + lean + Vector3.UnitY * h;
            Foliage.Tube(flesh, [foot - Vector3.UnitY * 0.03f, foot + lean * 0.3f + Vector3.UnitY * h * 0.4f, foot + lean * 0.8f + Vector3.UnitY * h * 0.8f, head], [0.13f, 0.11f, 0.09f, 0.07f], 7,
                p => Shade(skin, 0.75f + 0.35f * Noise(p.X * 10, p.Y * 4, p.Z * 10, k)), 300 + k, 0.15f);
            for (var l = 0; l < 4; l++)
            {
                var a = t + l * 1.57f;
                Foliage.Blade(leaf, foot + Vector3.UnitY * 0.06f, Vector3.Normalize(At(1, a) + Vector3.UnitY * 0.2f), 0.8f, 0.28f, 0.12f, 0.2f, Shade(skin, 0.7f), skin, 4, 0.4f);
            }
            var face = -t;
            foreach (var j in new[] { -1f, 1f })
            {
                var rot = Matrix4x4.CreateRotationZ(j * 0.55f) * Matrix4x4.CreateRotationY(face);
                var centre = head + Vector3.Transform(new Vector3(0, j * 0.18f, 0), Matrix4x4.CreateRotationY(face));
                Shapes.Blob(model[Flesh], Vector3.Zero, new Vector3(0.55f, 0.18f, 0.45f), 1, 0.15f, 300 + k * 2 + (int)j,
                    (q, n) => n.Y * j > 0 ? Mix(skin, Rgb(0.5f, 0.55f, 0.2f), 0.4f) : lip, shape: q => centre + Vector3.Transform(q with { Y = j > 0 ? MathF.Max(q.Y, 0) : MathF.Min(q.Y, 0) }, rot));
                for (var i = 0; i < 11; i++)
                {
                    var a = MathF.PI * (i + 0.5f) / 11 - MathF.PI / 2;
                    var rimP = Vector3.Transform(new Vector3(0.52f * MathF.Cos(a), 0, 0.42f * MathF.Sin(a)), rot);
                    var tip = Vector3.Transform(new Vector3(0.56f * MathF.Cos(a), -j * 0.24f, 0.46f * MathF.Sin(a)), rot);
                    Shapes.Limb(model[Thorn], centre + rimP, centre + tip, 0.03f, 0, 4, tooth);
                }
            }
            Shapes.Blob(model[Flesh], head, new Vector3(0.38f, 0.12f, 0.32f), 1, 0.1f, 320 + k, (_, _) => maw, yaw: face);
        }
        // Barbed tendrils: curling up out of the ground, hooked thorns along them.
        var vine = Rgb(0.3f, 0.12f, 0.16f); var barb = Rgb(0.75f, 0.68f, 0.5f);
        for (var k = 0; k < 9; k++)
        {
            var spot = Spot(rng, 2.6f);
            var foot = On(spot) - Vector3.UnitY * 0.03f; var h = 1.4f + 2.2f * U(rng); var spin = U(rng) * 6;
            var pts = Enumerable.Range(0, 13).Select(i => { var u = i / 12f; return confine(foot + At(0.35f * u + 0.15f * MathF.Sin(u * 5), spin + u * 4.5f, h * MathF.Pow(u, 0.8f))); }).ToList();
            Foliage.Tube(thorn, pts, pts.Select((_, i) => 0.09f * (1 - i / 13f) + 0.012f).ToArray(), 6, p => Shade(vine, 0.75f + 0.4f * Noise(p.X * 9, p.Y * 9, p.Z * 9, k)), 340 + k, 0.2f);
            for (var i = 1; i < 12; i++)
            {
                var dir = Vector3.Normalize(pts[i + 1] - pts[i - 1]); var o = Vector3.Normalize(Vector3.Cross(dir, At(1, i * 2.3f)));
                var r0 = 0.09f * (1 - i / 13f) + 0.012f;
                Foliage.Tube(thorn, [pts[i] + o * r0 * 0.5f, pts[i] + o * (r0 + 0.12f) + dir * 0.03f, pts[i] + o * (r0 + 0.2f) + dir * 0.1f], [0.03f, 0.015f, 0], 4, _ => barb, 350 + i, 0.05f);
            }
        }
        // Spiked seed pods on stalks: faceted, ridged bulbs bristling with spines.
        var podSkin = Rgb(0.46f, 0.32f, 0.4f); var prickle = Rgb(0.82f, 0.76f, 0.6f);
        for (var k = 0; k < 7; k++)
        {
            var foot = On(Spot(rng, 2.6f)) - Vector3.UnitY * 0.02f; var h = 0.5f + 0.9f * U(rng); var bend = At(0.2f, U(rng) * 6.28f);
            var top = foot + bend + Vector3.UnitY * h;
            Foliage.Tube(flesh, [foot, foot + bend * 0.3f + Vector3.UnitY * h * 0.5f, top], [0.06f, 0.05f, 0.04f], 5, _ => skin, 360 + k, 0.1f);
            var size = 0.2f + 0.12f * U(rng);
            var centre = top + Vector3.UnitY * size * 0.9f;
            Foliage.Stone(flesh, centre, new Vector3(size * 0.85f, size * 1.15f, size * 0.85f), 370 + k, U(rng) * 6, (q, n) => Shade(Mix(podSkin, Rgb(0.6f, 0.2f, 0.25f), Smooth(-0.3f, 0.9f, n.Y)), 0.75f + 0.35f * Noise(q.X * 20, q.Y * 20, q.Z * 20, k)), 1, 0.2f);
            for (var s = 0; s < 18; s++)
            {
                var o = Foliage.Sphere(rng); if (o.Y < -0.5f) o = o with { Y = -o.Y };
                var from = centre + o * new Vector3(size * 0.8f, size * 1.1f, size * 0.8f);
                Foliage.Tube(thorn, [from, from + o * (0.12f + 0.1f * U(rng))], [0.022f, 0], 3, _ => prickle, 380 + s, 0);
            }
        }
        // Rosettes of serrated blades, red-edged.
        for (var k = 0; k < 8; k++)
        {
            var foot = On(Spot(rng, 2.7f));
            var n = 9 + rng.Next(4);
            for (var b = 0; b < n; b++)
            {
                var a = b * 6.28f / n + U(rng) * 0.3f; var l = 0.45f + 0.35f * U(rng);
                var dir = Vector3.Normalize(At(1, a) + Vector3.UnitY * (0.6f + 0.6f * U(rng)));
                var pts = Enumerable.Range(0, 5).Select(i => { var u = i / 4f; return foot + dir * l * u - Vector3.UnitY * 0.15f * l * u * u; }).ToList();
                Foliage.Frond(leaf, pts, u => 0.07f * (1 - u) + 0.02f, Shade(Mix(skin, purple, 0.3f), 0.8f), Mix(lip, skin, 0.3f), 0.25f, 0.1f, 0.3f);
            }
        }
        // Glowing spore bulbs on short stems, in clusters.
        for (var k = 0; k < 6; k++)
        {
            var c = Spot(rng, 2.4f);
            for (var b = 0; b < 4; b++)
            {
                var foot = On(c + new Vector2(MathF.Cos(b * 1.7f), MathF.Sin(b * 1.7f)) * 0.25f); var h = 0.3f + 0.5f * U(rng);
                Foliage.Tube(flesh, [foot - Vector3.UnitY * 0.02f, foot + Vector3.UnitY * h], [0.04f, 0.025f], 5, _ => skin, 390 + b, 0.1f);
                Foliage.Stone(glow, foot + Vector3.UnitY * (h + 0.12f), new Vector3(0.13f, 0.17f, 0.13f), 400 + k * 4 + b, 0, (_, n) => Shade(Rgb(0.82f, 0.95f, 0.4f), 0.85f + 0.15f * n.Y), 1, 0.12f);
            }
        }
        // Alien grass, stones and bone-pale litter between them.
        for (var k = 0; k < 110; k++)
            Foliage.Tuft(leaf, rng, On(Spot(rng, 2.8f)) - Vector3.UnitY * 0.01f, 6, 0.15f + 0.15f * U(rng), 0.03f, 0.5f, Rgb(0.25f, 0.2f, 0.24f), Mix(Rgb(0.45f, 0.5f, 0.3f), Rgb(0.55f, 0.3f, 0.4f), U(rng)));
        for (var k = 0; k < 14; k++)
        {
            var s = 0.06f + 0.1f * U(rng);
            Foliage.Stone(stone, On(Spot(rng, 2.7f)), new Vector3(s * 1.3f, s * 0.8f, s), 420 + k, U(rng) * 3, (q, n) => Shade(Rgb(0.38f, 0.36f, 0.4f), 0.75f + 0.35f * Noise(q.X * 20, q.Y * 20, q.Z * 20, k)), k % 3 == 0 ? 1 : 0);
        }
        return model;
    }

    public static Model Jungle()
    {
        var model = new Model("jungle", round: true, new Vector3(D, D, D));
        var rng = new Random(301);
        Quill P(Material m) => Quill.Of(model, m, model.Inches);
        var (mat, bark, leaf, canopy, flesh, stone) = (P(Mat), P(Bark), P(Leaf), P(Canopy), P(Flesh), P(Stone));
        var confine = Foliage.Within(R - 0.06f, D - 0.05f);
        var loam = Rgb(0.22f, 0.17f, 0.11f); var moss = Rgb(0.2f, 0.32f, 0.12f); var rim = Rgb(0.2f, 0.15f, 0.1f);
        var ground = Base(mat, 301, (p, n) =>
        {
            if (p.Y < 0.1f && n.Y < 0.9f) return Shade(rim, 0.85f + 0.3f * Noise(p.X * 5, p.Z * 5, 0, 9));
            return Shade(Mix(loam, moss, Smooth(0.4f, 0.6f, Fbm(p.X * 1.2f, p.Z * 1.2f, 1, 302))), 0.75f + 0.4f * Noise(p.X * 12, p.Z * 12, p.Y * 10, 303));
        });
        Vector3 On(Vector2 p) => new(p.X, ground(p.X, p.Y), p.Y);
        Vector4[] greens = [Rgb(0.16f, 0.36f, 0.12f), Rgb(0.22f, 0.44f, 0.14f), Rgb(0.13f, 0.3f, 0.11f), Rgb(0.28f, 0.48f, 0.16f)];
        var yellow = Rgb(0.5f, 0.52f, 0.18f); var dark = Rgb(0.08f, 0.19f, 0.08f);
        var barkA = Rgb(0.38f, 0.32f, 0.24f); var barkB = Rgb(0.5f, 0.46f, 0.38f);
        Func<Vector3, Vector4> BarkPaint(int k) => p => Shade(Mix(barkA, barkB, Smooth(0.3f, 0.75f, Fbm(p.X * 3, p.Y * 5, p.Z * 3, k))), 0.75f + 0.4f * Noise(p.X * 18, p.Y * 9, p.Z * 18, k + 1));
        var used = new List<Vector2>();
        // Palms: a ringed trunk curving up, a crown of long pinnate fronds drooping from it, a cluster of nuts under it.
        foreach (var (r, t, h, k) in new[] { (2.0f, 0.3f, 5.5f, 0), (1.9f, 2.4f, 4.8f, 1), (2.1f, 4.4f, 5.2f, 2) })
        {
            var spot = new Vector2(r * MathF.Cos(t), r * MathF.Sin(t)); used.Add(spot);
            var foot = On(spot); var lean = At(0.7f + 0.3f * U(rng), t + MathF.PI + 0.4f * (U(rng) - 0.5f));
            const int rings = 18;
            var pts = Enumerable.Range(0, rings + 1).Select(i => { var u = i / (float)rings; return foot - Vector3.UnitY * 0.05f + lean * u * u + Vector3.UnitY * (h - 0.9f) * u; }).ToList();
            Foliage.Tube(bark, pts, pts.Select((_, i) => (0.14f - 0.04f * i / rings) * (i % 2 == 0 ? 1.12f : 0.92f) + (i == 0 ? 0.08f : 0)).ToArray(), 7,
                p => Shade(Mix(Rgb(0.42f, 0.34f, 0.24f), Rgb(0.55f, 0.48f, 0.36f), Noise(0, p.Y * 6, 0, k)), 0.8f + 0.25f * Noise(p.X * 20, p.Y * 20, p.Z * 20, k)), 310 + k, 0.08f);
            var crown = pts[^1];
            var fronds = 10 + rng.Next(3);
            for (var f = 0; f < fronds; f++)
            {
                var a = 2 * MathF.PI * f / fronds + U(rng) * 0.4f; var o = At(1, a);
                var length = 1.5f + 0.6f * U(rng); var rise = 0.25f + 0.35f * U(rng);
                var spine = Enumerable.Range(0, 15).Select(i => { var u = i / 14f; return confine(crown + o * length * u + Vector3.UnitY * (rise * u - (rise + 0.9f) * u * u)); }).ToList();
                var green = Shade(greens[(f + k) % greens.Length], 0.85f + 0.3f * U(rng));
                var tip = Mix(green, yellow, 0.2f + 0.3f * U(rng));
                Foliage.Frond(leaf, spine, u => 0.5f * MathF.Sin(MathF.PI * (0.1f + 0.85f * u)) + 0.04f, Shade(green, 0.85f), tip, 0.9f, 1.1f, 0.25f, gap: 0.55f);
                Foliage.Tube(bark, [spine[0], spine[5], spine[10]], [0.03f, 0.02f, 0.008f], 3, _ => Rgb(0.4f, 0.42f, 0.2f), 320 + f, 0);
            }
            for (var n = 0; n < 5; n++)
                Foliage.Stone(bark, crown - Vector3.UnitY * 0.12f + At(0.12f, n * 1.3f), new Vector3(0.08f), 330 + k * 5 + n, 0, (_, nn) => Shade(Rgb(0.42f, 0.3f, 0.14f), 0.8f + 0.2f * nn.Y), 0, 0.15f);
        }
        // Broadleaf jungle trees: buttressed trunks, big leaves in clumps, vines hanging from the limbs.
        foreach (var (r, t, h, k) in new[] { (0.6f, 1.2f, 4.5f, 0), (1.1f, 3.9f, 3.9f, 1), (0.9f, 5.6f, 3.5f, 2) })
        {
            var spot = new Vector2(r * MathF.Cos(t), r * MathF.Sin(t)); used.Add(spot);
            var foot = On(spot);
            Foliage.Roots(bark, rng, foot, 0.32f, 6, BarkPaint(k * 3 + 1), 340 + k * 10, 3.2f);
            Foliage.Broadleaf(bark, canopy, rng, foot, h, 0.17f, BarkPaint(k * 3), (p, rr) => Foliage.LeafColour(rr, greens, yellow, dark, 0.04f, 0.15f), confine, 350 + k * 17,
                depth: 3, clump: 0.45f, leaves: 19, leafSize: 0.26f, spread: 0.75f, reach: 0.19f, bole: 0.5f, single: false);
            for (var v = 0; v < 6; v++)   // hanging vines with small leaves down them
            {
                var from = confine(foot + At(0.5f + 0.7f * U(rng), U(rng) * 6.28f) + Vector3.UnitY * (h * 0.62f + 0.3f * U(rng)));
                var drop = 1.2f + 1.8f * U(rng);
                var line = Enumerable.Range(0, 6).Select(i => { var u = i / 5f; return from + At(0.12f * MathF.Sin(u * 3 + v), v) - Vector3.UnitY * drop * u; }).ToList();
                Foliage.Tube(bark, line, line.Select(_ => 0.016f).ToArray(), 3, _ => Rgb(0.26f, 0.32f, 0.14f), 360 + v, 0.1f);
                for (var i = 1; i < 12; i++)
                {
                    var p = Vector3.Lerp(line[i / 2], line[Math.Min(i / 2 + 1, 5)], (i % 2) * 0.5f);
                    var n = Vector3.Normalize(Foliage.Sphere(rng) + Vector3.UnitY * 0.3f);
                    Foliage.Leaf(leaf, p, n, Vector3.Normalize(Vector3.Cross(n, Foliage.Sphere(rng))), 0.12f, 0.07f, Foliage.LeafColour(rng, greens, yellow, dark));
                }
            }
        }
        bool Clear(Vector2 p, float room) => used.All(q => Vector2.Distance(p, q) > room);
        // Undergrowth: elephant-ear leaves on stems, ferns and spiky rosettes, packed over the floor.
        for (var k = 0; k < 34; k++)
        {
            var spot = Spot(rng, 2.7f);
            if (!Clear(spot, 0.3f)) continue;
            var foot = On(spot);
            var leaves = 3 + rng.Next(3);
            for (var l = 0; l < leaves; l++)
            {
                var a = U(rng) * 6.28f; var o = At(1, a);
                var h = 0.3f + 0.8f * U(rng); var size = 0.45f + 0.4f * U(rng);
                var stem = foot + o * 0.12f + Vector3.UnitY * h;
                Foliage.Tube(flesh, [foot - Vector3.UnitY * 0.02f, foot + o * 0.06f + Vector3.UnitY * h * 0.6f, stem], [0.025f, 0.022f, 0.016f], 3, _ => Rgb(0.26f, 0.38f, 0.14f), 370 + l, 0.05f);
                var green = Shade(greens[rng.Next(greens.Length)], 0.85f + 0.3f * U(rng));
                var dir = Vector3.Normalize(o + Vector3.UnitY * (0.1f - 0.5f * U(rng)));
                var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
                var normal = Vector3.Normalize(Vector3.Cross(dir, side));
                // The blade, folded down either side of its midrib.
                var b0 = stem; var tip = stem + dir * size; var mid = stem + dir * size * 0.4f;
                var l1 = mid + side * size * 0.38f - normal * size * 0.12f; var l2 = mid - side * size * 0.38f - normal * size * 0.12f;
                var lobe1 = stem - dir * size * 0.08f + side * size * 0.18f; var lobe2 = stem - dir * size * 0.08f - side * size * 0.18f;
                leaf.Face(b0, l1, tip, normal, green, Shade(green, 0.9f), Shade(green, 1.1f));
                leaf.Face(b0, tip, l2, normal, green, Shade(green, 1.1f), Shade(green, 0.9f));
                leaf.Face(b0, lobe1, l1, normal, green, green, Shade(green, 0.9f));
                leaf.Face(b0, l2, lobe2, normal, green, Shade(green, 0.9f), green);
            }
        }
        var fernDark = Rgb(0.14f, 0.3f, 0.1f); var fernLight = Rgb(0.34f, 0.52f, 0.18f);
        for (var k = 0; k < 14; k++)
        {
            var spot = Spot(rng, 2.6f);
            if (!Clear(spot, 0.4f)) continue;
            var root = On(spot);
            var fronds = 7 + rng.Next(3);
            for (var f = 0; f < fronds; f++)
            {
                var o = At(1, f * 6.28f / fronds + U(rng) * 0.5f); var l = 0.6f + 0.4f * U(rng);
                var spine = Enumerable.Range(0, 8).Select(i => { var u = i / 7f; return root + o * l * u + Vector3.UnitY * (l * 0.9f * u - l * 0.8f * u * u); }).ToList();
                Foliage.Frond(leaf, spine, u => 0.14f * MathF.Sin(MathF.PI * (0.12f + 0.85f * u)) + 0.01f, fernDark, Shade(fernLight, 0.85f + 0.3f * U(rng)), 0.5f, 0.3f, 0.2f);
            }
        }
        for (var k = 0; k < 8; k++)
        {
            var spot = Spot(rng, 2.7f);
            if (!Clear(spot, 0.3f)) continue;
            var foot = On(spot);
            for (var b = 0; b < 12; b++)
            {
                var a = b * 0.52f + U(rng) * 0.3f;
                Foliage.Blade(leaf, foot, Vector3.Normalize(At(1, a) + Vector3.UnitY * (0.8f + 0.8f * U(rng))), 0.4f + 0.25f * U(rng), 0.08f, 0.05f, 0.15f, Shade(greens[1], 0.75f), Mix(greens[3], Rgb(0.6f, 0.2f, 0.2f), 0.3f), 3);
            }
        }
        for (var k = 0; k < 120; k++)
            Foliage.Tuft(leaf, rng, On(Spot(rng, 2.8f)) - Vector3.UnitY * 0.01f, 6, 0.15f + 0.15f * U(rng), 0.03f, 0.5f, Rgb(0.14f, 0.26f, 0.1f), Mix(Rgb(0.34f, 0.5f, 0.18f), Rgb(0.5f, 0.52f, 0.24f), U(rng) * 0.5f));
        var grey = Rgb(0.42f, 0.41f, 0.38f);
        for (var k = 0; k < 12; k++)
        {
            var s = 0.07f + 0.12f * U(rng);
            Foliage.Stone(stone, On(Spot(rng, 2.7f)), new Vector3(s * 1.3f, s * 0.8f, s), 420 + k, U(rng) * 3,
                (q, n) => Shade(Mix(grey, moss, Smooth(0.4f, 0.95f, n.Y) * 0.8f), 0.75f + 0.35f * Noise(q.X * 20, q.Y * 20, q.Z * 20, k)), k % 3 == 0 ? 1 : 0);
        }
        Vector4[] fallen = [Rgb(0.45f, 0.3f, 0.14f), Rgb(0.52f, 0.42f, 0.16f), Rgb(0.32f, 0.24f, 0.13f), Rgb(0.4f, 0.4f, 0.18f)];
        for (var k = 0; k < 160; k++)
            Foliage.Litter(leaf, rng, On(Spot(rng, 2.85f)) + Vector3.UnitY * 0.012f, 0.12f + 0.08f * U(rng), Shade(fallen[rng.Next(fallen.Length)], 0.8f + 0.3f * U(rng)));
        return model;
    }
}
