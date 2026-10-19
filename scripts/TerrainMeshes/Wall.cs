using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The wall or fence (6" × 0.5", 1.5" high, Solid): a fieldstone wall along x on a footing. Five dressed pillars with
/// caps stand to the full height; between them, panels of random-coursed rough stones (each its own size, shade and set,
/// chipped at the corners) under a row of coping stones on edge; moss low down; one panel broken down, its stones fallen at
/// its foot.</summary>
public static class Wall
{
    public static readonly Material Stone = new("stone", 0.88f), Mortar = new("mortar", 0.95f);
    const float Length = 6, Height = 1.5f, Depth = 0.5f, Body = 0.34f, Footing = 0.1f, Coping = 1.12f;

    public static Model Build()
    {
        var model = new Model("wall", round: false, new Vector3(Length, Height, Depth));
        var rng = new Random(51);
        float R() => (float)rng.NextDouble();
        var stone = Rgb(0.6f, 0.57f, 0.51f); var dressed = Rgb(0.66f, 0.63f, 0.57f); var moss = Rgb(0.3f, 0.39f, 0.17f);
        var grime = Rgb(0.33f, 0.3f, 0.25f); var mortar = Rgb(0.3f, 0.28f, 0.26f);
        Vector4 Weather(Vector4 c, Vector3 p) =>
            Shade(Mix(Mix(c, grime, Smooth(0.5f, 0, p.Y) * 0.5f), moss, Smooth(0.52f, 0.7f, Fbm(p * 3.2f, 52)) * Smooth(0.75f, 0.1f, p.Y) * 0.85f), 0.93f + 0.12f * Fbm(p * 4, 53));
        // The footing: long flat stones the length of the wall.
        for (var x = -Length / 2; x < Length / 2 - 0.05f;)
        {
            var len = MathF.Min(0.6f + 0.4f * R(), Length / 2 - x);
            var c = Shade(stone, 0.75f + 0.2f * R());
            Masonry.Block(model[Stone], new(x + len / 2, Footing / 2, 0), new(len - 0.025f, Footing, Depth - 0.04f), Matrix4x4.Identity, 0.025f, p => Weather(c, p), rng, 0.04f);
            x += len;
        }
        float[] pillars = [-2.8f, -1.4f, 0, 1.4f, 2.8f];
        foreach (var px in pillars)
        {
            var w = 0.38f;
            for (var y = Footing; y < 1.3f - 0.01f;)
            {
                var h = MathF.Min(0.3f, 1.3f - y);
                var c = Shade(dressed, 0.85f + 0.18f * R());
                Masonry.Block(model[Stone], new(px, y + h / 2, 0), new(w - 0.02f * (int)(y / 0.3f % 2), h - 0.02f, Depth - 0.1f), Matrix4x4.Identity, 0.03f, p => Weather(c, p), rng);
                y += h;
            }
            Masonry.Block(model[Stone], new(px, 1.35f, 0), new(w + 0.08f, 0.1f, Depth - 0.02f), Matrix4x4.Identity, 0.025f, Shade(dressed, 0.82f));
            Shapes.Prism(model[Stone], Matrix4x4.CreateScale(0.3f, Height - 1.4f, 0.3f) * Matrix4x4.CreateTranslation(px, 1.4f, 0), 4, 0.25f, Shade(dressed, 0.78f), MathF.PI / 4);
        }
        for (var panel = 0; panel < pillars.Length - 1; panel++)
        {
            float x0 = pillars[panel] + 0.19f, x1 = pillars[panel + 1] - 0.19f;
            var broken = panel == 2;
            float Top(float x) => broken ? 0.45f + 0.5f * MathF.Pow(MathF.Abs((x - (x0 + x1) / 2) / ((x1 - x0) / 2)), 2.2f) + 0.1f * Noise(x * 9, 0, 0, 54) : Coping;
            Masonry.Span(model[Mortar], new(x0, Footing, -Body / 2 + 0.04f), new(x1, broken ? 0.55f : Coping - 0.02f, Body / 2 - 0.04f), 0, mortar);
            for (var y = Footing; y < Coping - 0.04f;)
            {
                var h = MathF.Min(0.13f + 0.08f * R(), Coping - y);
                for (var x = x0 - 0.12f * R(); x < x1;)
                {
                    var len = 0.16f + 0.2f * R();
                    var (a, b) = (MathF.Max(x, x0), MathF.Min(x + len, x1));
                    x += len;
                    if (b - a < 0.06f) continue;
                    var mid = (a + b) / 2;
                    if (y + h * 0.5f > Top(mid)) continue;
                    var c = Shade(Mix(stone, Rgb(0.55f, 0.5f, 0.42f), R()), 0.78f + 0.28f * R());
                    var set = Masonry.Turn((R() - 0.5f) * 0.1f, (R() - 0.5f) * 0.08f, (R() - 0.5f) * 0.1f);
                    Masonry.Block(model[Stone], new(mid, y + h / 2, (R() - 0.5f) * 0.03f), new(b - a - 0.02f, h - 0.018f, Body + 0.03f * R()), set, 0.03f, p => Weather(c, p), rng, 0.12f);
                }
                y += h;
            }
            // Coping: stones on edge along the top, where the panel stands.
            for (var x = x0; x < x1 - 0.04f;)
            {
                var w = MathF.Min(0.1f + 0.05f * R(), x1 - x);
                if (Top(x + w / 2) >= Coping - 0.01f)
                {
                    var c = Shade(stone, 0.8f + 0.25f * R());
                    Masonry.Block(model[Stone], new(x + w / 2, Coping + 0.1f, 0), new(w - 0.012f, 0.2f + 0.03f * R(), Body + 0.04f), Masonry.Turn(0, 0, (R() - 0.5f) * 0.15f), 0.022f, p => Weather(c, p), rng, 0.1f);
                }
                x += w;
            }
            if (broken)   // the fallen stones, along the foot on both sides
                for (var k = 0; k < 14; k++)
                {
                    var side = k % 2 == 0 ? 1 : -1;
                    var s = 0.09f + 0.06f * R();
                    var p = new Vector3(x0 + 0.1f + (x1 - x0 - 0.2f) * R(), Footing + s * 0.35f, side * (0.1f + 0.1f * R()));
                    var c = Shade(stone, 0.75f + 0.3f * R());
                    Masonry.Block(model[Stone], p, new(s * 1.6f, s * 0.8f, s), Masonry.Turn(R() * 3, (R() - 0.5f) * 0.8f, (R() - 0.5f) * 0.8f), s * 0.18f, q => Weather(c, q), rng, 0.2f);
                }
        }
        return model;
    }
}
