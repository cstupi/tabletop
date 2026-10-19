using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A wood on a flocked area base (authored in inches at 6" across and 6" high, drawn in the unit box): broadleaf trees
/// on rooted trunks with branching limbs and clump foliage of small leaves, conifers of drooping needle sprays, spaced so models
/// stand between them; ferns, bushes, grass tufts, stones, a fallen log and leaf litter on the floor. Round.</summary>
public static class Woods
{
    public static readonly Material Floor = new("forest-floor", 0.95f), Bark = new("bark", 0.9f), Leaves = new("leaves", 0.8f, DoubleSided: true),
        Needles = new("needles", 0.85f, DoubleSided: true), Fern = new("fern", 0.85f, DoubleSided: true), Stone = new("stone", 0.85f);

    static readonly Vector3 Drawn = new(6, 6, 6);
    const float R = 3, Top = 6;

    public static Model Build()
    {
        var model = new Model("woods", round: true);
        const int seed = 21;
        var rng = new Random(seed);
        var floor = Quill.Of(model, Floor, Drawn); var bark = Quill.Of(model, Bark, Drawn); var leaves = Quill.Of(model, Leaves, Drawn);
        var needles = Quill.Of(model, Needles, Drawn); var fern = Quill.Of(model, Fern, Drawn); var stone = Quill.Of(model, Stone, Drawn);
        var confine = Foliage.Within(R - 0.08f, Top - 0.04f);

        // The base: an irregular flocked slab with a bevelled earth rim.
        float Outline(float t) => R - 0.05f - 0.22f * Fbm(MathF.Cos(t) * 1.3f + 5, MathF.Sin(t) * 1.3f, 0, seed);
        float Ground(float x, float z) => 0.12f + 0.06f * Fbm(x * 0.9f, z * 0.9f, 2, seed + 3) + 0.015f * Noise(x * 6, z * 6, 1, seed + 4);
        var loam = Rgb(0.29f, 0.22f, 0.15f); var moss = Rgb(0.3f, 0.4f, 0.17f); var straw = Rgb(0.5f, 0.46f, 0.26f); var litter = Rgb(0.42f, 0.3f, 0.17f);
        var rim = Rgb(0.26f, 0.2f, 0.14f);
        Foliage.Disc(floor, 16, 64, Outline, (x, z, u) => Ground(x, z) * Smooth(1, 0.86f, u), (p, n) =>
        {
            if (p.Y < 0.1f && n.Y < 0.9f) return Shade(rim, 0.85f + 0.3f * Noise(p.X * 5, p.Z * 5, 0, 9));
            var c = Mix(Mix(loam, moss, Smooth(0.35f, 0.6f, Fbm(p.X * 1.4f, p.Z * 1.4f, 0, seed + 1))), straw, Smooth(0.55f, 0.75f, Fbm(p.X * 2.3f + 4, p.Z * 2.3f, 1, seed + 2)) * 0.6f);
            c = Mix(c, litter, Smooth(0.6f, 0.8f, Fbm(p.X * 3.1f, p.Z * 3.1f + 2, 3, seed + 5)) * 0.7f);
            return Shade(c, 0.78f + 0.4f * Noise(p.X * 14, p.Z * 14, p.Y, seed + 6));
        }, seed);

        // Trees: broadleaves and conifers, spaced so a model's base fits between their trunks.
        var barkA = Rgb(0.32f, 0.25f, 0.19f); var barkB = Rgb(0.44f, 0.4f, 0.35f);
        Func<Vector3, Vector4> BarkPaint(int k) => p => Shade(Mix(barkA, barkB, Smooth(0.3f, 0.75f, Fbm(p.X * 3, p.Y * 5, p.Z * 3, k))), 0.75f + 0.4f * Noise(p.X * 18, p.Y * 9, p.Z * 18, k + 1));
        Vector4[][] canopies =
        [
            [Rgb(0.25f, 0.4f, 0.14f), Rgb(0.31f, 0.46f, 0.16f), Rgb(0.2f, 0.35f, 0.13f)],
            [Rgb(0.35f, 0.47f, 0.15f), Rgb(0.42f, 0.5f, 0.17f), Rgb(0.29f, 0.42f, 0.14f)],
            [Rgb(0.22f, 0.37f, 0.17f), Rgb(0.27f, 0.43f, 0.2f), Rgb(0.18f, 0.31f, 0.15f)],
            [Rgb(0.4f, 0.45f, 0.15f), Rgb(0.47f, 0.48f, 0.16f), Rgb(0.33f, 0.4f, 0.14f)],
        ];
        var yellow = Rgb(0.56f, 0.52f, 0.2f); var dark = Rgb(0.13f, 0.22f, 0.09f);
        var trees = new (Vector2 At, bool Conifer, float Height)[]
        {
            (new(-1.15f, -1.0f), false, 5.7f), (new(1.35f, -1.05f), true, 5.9f), (new(0.75f, 1.3f), false, 5.0f), (new(-1.55f, 1.35f), true, 4.6f),
            (new(-0.1f, 0.15f), false, 3.2f),
        };
        for (var k = 0; k < trees.Length; k++)
        {
            var (at, conifer, h) = trees[k];
            var foot = new Vector3(at.X, Ground(at.X, at.Y) - 0.02f, at.Y);
            if (conifer)
                Foliage.Conifer(bark, needles, rng, foot, h, 1.3f + 0.12f * (k % 3), 0.11f, BarkPaint(k * 3), Rgb(0.16f, 0.3f, 0.17f), Rgb(0.32f, 0.48f, 0.24f), confine, seed + k * 17, step: 0.24f);
            else
            {
                var greens = canopies[k % canopies.Length];
                Foliage.Broadleaf(bark, leaves, rng, foot, h, 0.15f, BarkPaint(k * 3), (p, r) =>
                {
                    var c = Foliage.LeafColour(r, greens, yellow, dark);
                    return Shade(c, 0.78f + 0.3f * Smooth(foot.Y + h * 0.35f, foot.Y + h, p.Y));
                }, confine, seed + k * 17, depth: 3, clump: 0.38f, leaves: 44, leafSize: 0.13f);
            }
        }

        // The floor: ferns and bushes where the trees leave room, grass tufts, stones, leaf litter and a fallen log.
        bool Clear(Vector2 p, float room) => trees.All(t => Vector2.Distance(t.At, p) > room);
        Vector2 Spot(Random r, float max) { var a = (float)r.NextDouble() * 6.28f; var d = max * MathF.Sqrt((float)r.NextDouble()); return new Vector2(MathF.Cos(a), MathF.Sin(a)) * d; }
        Vector3 On(Vector2 p) => new(p.X, Ground(p.X, p.Y), p.Y);
        var fernDark = Rgb(0.18f, 0.32f, 0.12f); var fernLight = Rgb(0.4f, 0.56f, 0.2f);
        for (var k = 0; k < 9; k++)
        {
            var p = Spot(rng, 2.5f);
            if (!Clear(p, 0.45f)) continue;
            var root = On(p);
            var fronds = 6 + rng.Next(3);
            for (var f = 0; f < fronds; f++)
            {
                var o = Foliage.Flat(f * 6.28f / fronds + (float)rng.NextDouble() * 0.5f); var l = 0.45f + 0.25f * (float)rng.NextDouble();
                var spine = Enumerable.Range(0, 7).Select(i => { var u = i / 6f; return root + o * l * u + Foliage.Up * (l * 0.9f * u - l * 0.75f * u * u); }).ToList();
                Foliage.Frond(fern, spine, u => 0.11f * MathF.Sin(MathF.PI * (0.15f + 0.85f * u)) + 0.01f, fernDark, Shade(fernLight, 0.85f + 0.3f * (float)rng.NextDouble()), 0.5f, 0.25f, 0.2f);
            }
        }
        for (var k = 0; k < 7; k++)
        {
            var p = Spot(rng, 2.5f);
            if (!Clear(p, 0.4f)) continue;
            var s = 0.18f + 0.12f * (float)rng.NextDouble();
            var greens = canopies[(k + 1) % canopies.Length];
            Foliage.Clump(leaves, rng, On(p) + Foliage.Up * s * 0.6f, new Vector3(s * 1.3f, s, s * 1.3f), 40, 0.14f, (_, r) => Foliage.LeafColour(r, greens, yellow, dark), confine);
        }
        var grass = Rgb(0.32f, 0.38f, 0.16f); var grassTip = Rgb(0.62f, 0.6f, 0.32f);
        for (var k = 0; k < 140; k++)
        {
            var p = Spot(rng, 2.75f);
            Foliage.Tuft(fern, rng, On(p) - Foliage.Up * 0.01f, 6, 0.16f + 0.12f * (float)rng.NextDouble(), 0.025f, 0.45f, grass, Mix(grassTip, grass, (float)rng.NextDouble() * 0.5f));
        }
        var grey = Rgb(0.5f, 0.49f, 0.46f); var lichen = Rgb(0.42f, 0.46f, 0.26f);
        for (var k = 0; k < 14; k++)
        {
            var p = Spot(rng, 2.7f);
            if (!Clear(p, 0.3f)) continue;
            var s = 0.06f + 0.1f * (float)rng.NextDouble();
            Foliage.Stone(stone, On(p), new Vector3(s * 1.3f, s * 0.8f, s), seed + 50 + k, (float)rng.NextDouble() * 3,
                (q, n) => Shade(Mix(grey, lichen, Smooth(0.5f, 0.95f, n.Y) * 0.6f), 0.75f + 0.35f * Noise(q.X * 20, q.Y * 20, q.Z * 20, k)), k % 3 == 0 ? 1 : 0);
        }
        var fallen = new[] { Rgb(0.5f, 0.3f, 0.13f), Rgb(0.58f, 0.45f, 0.16f), Rgb(0.38f, 0.26f, 0.15f), Rgb(0.62f, 0.52f, 0.22f), Rgb(0.45f, 0.4f, 0.2f) };
        for (var k = 0; k < 200; k++)
        {
            var p = Spot(rng, 2.8f);
            Foliage.Litter(fern, rng, On(p) + Foliage.Up * 0.012f, 0.09f + 0.05f * (float)rng.NextDouble(), Shade(fallen[rng.Next(fallen.Length)], 0.8f + 0.3f * (float)rng.NextDouble()));
        }
        // A fallen log, broken at both ends, with a branch stub and moss on its top.
        var logA = new Vector3(0.95f, 0, 1.75f); var logB = new Vector3(2.35f, 0, -0.05f);
        var log = Enumerable.Range(0, 5).Select(i => { var u = i / 4f; var p = Vector3.Lerp(logA, logB, u); return new Vector3(p.X, Ground(p.X, p.Z) + 0.09f - 0.02f * u, p.Z) + Foliage.Flat(1.2f) * 0.06f * MathF.Sin(u * 5); }).ToList();
        var mossy = Rgb(0.32f, 0.4f, 0.17f);
        Foliage.Tube(bark, log, [0.12f, 0.115f, 0.11f, 0.1f, 0.09f], 7, p => Mix(BarkPaint(91)(p), mossy, Smooth(0.15f, 0.24f, p.Y) * 0.6f), seed + 90, 0.25f, cap: true);
        Foliage.Tube(bark, [log[0] + Vector3.Normalize(logA - logB) * 0.02f, log[0]], [0.12f, 0.12f], 7, _ => Rgb(0.6f, 0.48f, 0.32f), seed + 90, 0.25f, cap: true);
        Foliage.Tube(bark, [log[2], log[2] + new Vector3(0.12f, 0.12f, 0.2f), log[2] + new Vector3(0.18f, 0.2f, 0.36f)], [0.04f, 0.03f, 0.01f], 4, BarkPaint(92), seed + 93);
        return model;
    }
}
