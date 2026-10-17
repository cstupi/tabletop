using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A wood on a mossy base disc: conifers of stacked cones and broadleaf trees of lumpy canopies on branching trunks,
/// spaced so models stand between them, with bushes, ferns, a fallen log and stones. Round; the rect forests share it.</summary>
public static class Woods
{
    public static readonly Material Floor = new("forest-floor", 0.95f), Bark = new("bark", 0.9f), Leaves = new("leaves", 0.8f),
        Needles = new("needles", 0.85f), Fern = new("fern", 0.85f, DoubleSided: true), Stone = new("stone", 0.85f);

    public static Model Build()
    {
        var model = new Model("woods", round: true);
        const int seed = 21;
        var rng = new Random(seed);
        var loam = Rgb(0.3f, 0.23f, 0.15f); var moss = Rgb(0.28f, 0.38f, 0.16f); var litter = Rgb(0.46f, 0.34f, 0.18f);
        model[Floor].Grid(9, 48, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / 48; var r = 0.5f * i / 8;
            var y = i == 8 ? 0 : 0.012f * (1 - r * r * 4) + 0.006f * Fbm(MathF.Cos(t) * r * 8, MathF.Sin(t) * r * 8, 0, seed);
            return new Vector3(r * MathF.Cos(t), y, r * MathF.Sin(t));
        }, (p, _) => Mix(Mix(loam, moss, Smooth(0.4f, 0.65f, Fbm(p.X * 7, p.Z * 7, 0, seed + 1))), litter, Smooth(0.55f, 0.75f, Fbm(p.X * 11 + 4, p.Z * 11, 1, seed + 2))));

        var spots = new List<Vector2>();
        for (var tries = 0; tries < 400 && spots.Count < 11; tries++)
        {
            var a = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.4f * MathF.Sqrt((float)rng.NextDouble());
            var p = new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            if (spots.All(q => Vector2.Distance(p, q) > 0.17f)) spots.Add(p);
        }
        var bark = Rgb(0.33f, 0.24f, 0.17f);
        for (var k = 0; k < spots.Count; k++)
        {
            var at = spots[k]; var h = 0.62f + 0.36f * (float)rng.NextDouble();
            var root = new Vector3(at.X, 0.01f, at.Y);
            if (k % 5 is 0 or 2 or 4) Conifer(model, rng, root, h, k); else Broadleaf(model, rng, root, h, k);
        }
        // Undergrowth: bushes and ferns where the trees leave room, a fallen log, a few stones.
        for (var k = 0; k < 9; k++)
        {
            var a = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.12f + 0.32f * (float)rng.NextDouble();
            var p = new Vector3(MathF.Cos(a) * r, 0.02f, MathF.Sin(a) * r);
            if (k % 2 == 0)
            {
                var size = 0.03f + 0.02f * (float)rng.NextDouble();
                Shapes.Blob(model[Leaves], p + new Vector3(0, size * 0.5f, 0), new Vector3(size * 1.3f, size, size * 1.3f), 1, 0.35f, seed * 7 + k,
                    (q, n) => Shade(Rgb(0.26f, 0.4f, 0.16f), 0.7f + 0.45f * Fbm(q * 40, k) + 0.15f * n.Y));
            }
            else Shapes.Tuft(model[Fern], rng, p, 7, 0.07f, 0.02f, 0.9f, Rgb(0.2f, 0.32f, 0.12f), Rgb(0.42f, 0.6f, 0.22f));
        }
        var log = Shapes.At(new Vector3(-0.05f, 0.022f, 0.3f), new Vector3(0.022f, 0.26f, 0.022f), yaw: 0.4f, pitch: 0, roll: MathF.PI / 2);
        Shapes.Prism(model[Bark], Matrix4x4.CreateTranslation(0, -0.5f, 0) * log, 7, 1, p => Shade(bark, 0.75f + 0.4f * Fbm(p * 50, 3)));
        for (var k = 0; k < 5; k++)
        {
            var a = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.2f + 0.25f * (float)rng.NextDouble(); var s = 0.015f + 0.015f * (float)rng.NextDouble();
            Shapes.Blob(model[Stone], new Vector3(MathF.Cos(a) * r, 0.005f, MathF.Sin(a) * r), new Vector3(s * 1.3f, s, s), 1, 0.3f, seed + 50 + k,
                (q, n) => Mix(Shade(Rgb(0.5f, 0.49f, 0.46f), 0.8f + 0.3f * Fbm(q * 60, k)), Rgb(0.36f, 0.45f, 0.2f), Smooth(0.6f, 0.95f, n.Y) * 0.6f));
        }
        return model;
    }

    static void Conifer(Model model, Random rng, Vector3 root, float h, int k)
    {
        var bark = Rgb(0.3f, 0.21f, 0.14f);
        Shapes.Prism(model[Bark], Shapes.At(root, new Vector3(0.016f, h * 0.3f, 0.016f)), 6, 0.7f, bark);
        var green = Shade(Rgb(0.17f, 0.33f, 0.19f), 0.85f + 0.3f * (float)rng.NextDouble());
        var width = 0.085f + 0.03f * (float)rng.NextDouble();
        const int tiers = 4;
        for (var i = 0; i < tiers; i++)
        {
            var baseY = h * (0.16f + 0.2f * i); var tierHeight = h - baseY > h * 0.42f ? h * 0.42f : h - baseY;
            if (i == tiers - 1) tierHeight = h - baseY;
            var r = width * (1 - 0.2f * i);
            var c = Shade(green, 0.85f + 0.08f * i);
            Shapes.Prism(model[Needles], Shapes.At(root with { Y = baseY }, new Vector3(r, tierHeight, r), yaw: (float)rng.NextDouble()), 9, 0,
                p => Mix(Shade(c, 0.7f), Shade(c, 1.15f), (p.Y - baseY) / tierHeight), phase: 0.3f * i);
        }
    }

    static void Broadleaf(Model model, Random rng, Vector3 root, float h, int k)
    {
        var bark = Rgb(0.36f, 0.27f, 0.19f);
        var trunk = h * 0.55f;
        Shapes.Prism(model[Bark], Shapes.At(root, new Vector3(0.022f, trunk, 0.022f)), 7, 0.55f, p => Shade(bark, 0.75f + 0.4f * Fbm(p * 40, k)));
        var tint = new[] { Rgb(0.3f, 0.48f, 0.2f), Rgb(0.38f, 0.52f, 0.2f), Rgb(0.26f, 0.42f, 0.22f), Rgb(0.5f, 0.5f, 0.2f) }[k % 4];
        var crown = 0.075f + 0.025f * (float)rng.NextDouble();
        for (var b = 0; b < 3; b++)
        {
            var a = 2 * MathF.PI * b / 3 + (float)rng.NextDouble();
            var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var from = root + Vector3.UnitY * trunk * 0.7f;
            Shapes.Prism(model[Bark], Shapes.At(from, new Vector3(0.008f, crown * 1.4f, 0.008f), yaw: -a + MathF.PI / 2, pitch: 0.75f), 5, 0.5f, bark);
        }
        var lobes = new[] { new Vector3(0, h - crown * 1.05f, 0), new Vector3(crown * 0.8f, h - crown * 1.6f, crown * 0.3f), new Vector3(-crown * 0.6f, h - crown * 1.7f, crown * 0.6f), new Vector3(-crown * 0.2f, h - crown * 1.5f, -crown * 0.85f) };
        for (var i = 0; i < lobes.Length; i++)
        {
            var c = root with { Y = 0 } + lobes[i];
            var r = crown * (i == 0 ? 1 : 0.8f + 0.15f * (float)rng.NextDouble());
            var limit = 0.5f - r * 1.1f; var flat = new Vector2(c.X, c.Z);
            if (flat.Length() > limit) { flat = Vector2.Normalize(flat) * limit; c = new Vector3(flat.X, c.Y, flat.Y); }
            Shapes.Blob(model[Leaves], c, new Vector3(r, r * 0.85f, r), 1, 0.28f, k * 13 + i,
                (p, n) => Shade(tint, 0.62f + 0.3f * Fbm(p * 30, k + i) + 0.25f * MathF.Max(n.Y, 0)));
        }
    }
}
