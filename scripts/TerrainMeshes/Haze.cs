using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A toxic cloud: a stained, pooled ground disc under translucent smooth billows, low and wide at the foot, smaller and
/// higher above, so the column reads as a soft volume and shows what stands inside it.</summary>
public static class Haze
{
    public static readonly Material Ground = new("stain", 0.7f), Cloud = new("smog", 1f, Alpha: 0.24f);

    public static Model Build()
    {
        var model = new Model("haze", round: true);
        var rng = new Random(121);
        var stain = Rgb(0.36f, 0.36f, 0.2f); var pool = Rgb(0.5f, 0.58f, 0.2f); var dirt = Rgb(0.32f, 0.28f, 0.2f);
        model[Ground].Grid(9, 48, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / 48; var r = 0.5f * i / 8;
            return new Vector3(r * MathF.Cos(t), i == 8 ? 0 : 0.004f, r * MathF.Sin(t));
        }, (p, _) =>
        {
            var r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            var c = Mix(stain, pool, Smooth(0.55f, 0.7f, Fbm(p.X * 8, p.Z * 8, 0, 121)));
            return Shade(Mix(c, dirt, Smooth(0.35f, 0.5f, r)), 0.85f + 0.25f * Fbm(p.X * 20, p.Z * 20, 1, 122));
        });
        var smog = Rgb(0.72f, 0.78f, 0.45f);
        var layers = new[] { (7, 0.14f, 0.3f, 0.17f, 0.17f), (6, 0.38f, 0.22f, 0.15f, 0.18f), (4, 0.62f, 0.14f, 0.14f, 0.16f), (2, 0.82f, 0.06f, 0.12f, 0.12f) };
        var k = 0;
        foreach (var (count, y, ring, radius, height) in layers)
            for (var i = 0; i < count; i++, k++)
            {
                var t = 2 * MathF.PI * i / count + (float)rng.NextDouble();
                var r = ring * (0.7f + 0.3f * (float)rng.NextDouble());
                var s = radius * (0.8f + 0.3f * (float)rng.NextDouble());
                var c = new Vector3(r * MathF.Cos(t), y + 0.04f * (float)rng.NextDouble(), r * MathF.Sin(t));
                var flat = new Vector2(c.X, c.Z); var limit = 0.5f - s;
                if (flat.Length() > limit) { flat = Vector2.Normalize(flat) * limit; c = new Vector3(flat.X, c.Y, flat.Y); }
                Shapes.Blob(model[Cloud], c, new Vector3(s, height, s), 2, 0.22f, 130 + k,
                    (p, n) => Shade(smog, 0.85f + 0.2f * MathF.Max(n.Y, 0)), smooth: true, yaw: t);
            }
        return model;
    }
}
