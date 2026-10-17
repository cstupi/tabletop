using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A dry-stone wall along x on a footing: four panels of staggered courses between capped posts, coping on top, moss
/// low down, and one panel broken with its blocks fallen beside it. Fitted long and thin (a wall) or short and deep (a barricade).</summary>
public static class Wall
{
    public static readonly Material Stone = new("stone", 0.88f), Mortar = new("mortar", 0.95f);

    public static Model Build()
    {
        var model = new Model("wall", round: false);
        var rng = new Random(51);
        var stone = Rgb(0.6f, 0.57f, 0.5f); var moss = Rgb(0.34f, 0.42f, 0.2f); var dark = Rgb(0.42f, 0.4f, 0.36f);
        Shapes.Box(model[Stone], new Vector3(-0.5f, 0, -0.46f), new Vector3(0.5f, 0.06f, 0.46f), p => Shade(Mix(dark, moss, 0.25f * Fbm(p * 20, 1)), 0.9f + 0.15f * Fbm(p * 30, 2)));
        float[] posts = [-0.455f, -0.2275f, 0, 0.2275f, 0.455f];
        foreach (var x in posts)
        {
            Shapes.Box(model[Stone], new Vector3(x - 0.04f, 0.06f, -0.4f), new Vector3(x + 0.04f, 0.93f, 0.4f), p => Shade(stone, 0.85f + 0.15f * Fbm(p * 25, 3)));
            Shapes.Box(model[Stone], new Vector3(x - 0.05f, 0.93f, -0.46f), new Vector3(x + 0.05f, 1, 0.46f), Shade(dark, 1.1f));
        }
        const float courses = 5, bottom = 0.06f, coping = 0.85f, course = (coping - bottom) / courses;
        for (var panel = 0; panel < posts.Length - 1; panel++)
        {
            float x0 = posts[panel] + 0.04f, x1 = posts[panel + 1] - 0.04f;
            var broken = panel == 2;
            float notchFrom = x0 + 0.05f, notchTo = x1 - 0.04f, notchBelow = 0.4f;
            Shapes.Box(model[Mortar], new Vector3(x0, bottom, -0.27f), new Vector3(x1, broken ? notchBelow : coping, 0.27f), Rgb(0.3f, 0.29f, 0.27f));
            if (broken)
            {
                Shapes.Box(model[Mortar], new Vector3(x0, notchBelow, -0.27f), new Vector3(notchFrom, coping, 0.27f), Rgb(0.3f, 0.29f, 0.27f));
                Shapes.Box(model[Mortar], new Vector3(notchTo, notchBelow, -0.27f), new Vector3(x1, coping, 0.27f), Rgb(0.3f, 0.29f, 0.27f));
            }
            for (var row = 0; row < courses; row++)
            {
                var y0 = bottom + row * course; var y1 = y0 + course - 0.008f;
                var x = x0 - (row % 2 == 0 ? 0 : 0.03f);
                while (x < x1 - 0.005f)
                {
                    var length = 0.05f + 0.045f * (float)rng.NextDouble();
                    var (b0, b1) = (MathF.Max(x, x0), MathF.Min(x + length, x1));
                    x += length + 0.006f;
                    if (b1 - b0 < 0.01f) continue;
                    if (broken && y0 > notchBelow - 0.01f && b1 > notchFrom && b0 < notchTo) continue;
                    if (broken && y0 > notchBelow - 0.17f && b1 > notchFrom + 0.03f && b0 < notchTo - 0.03f && rng.NextDouble() < 0.5) continue;
                    var out0 = 0.02f * (float)rng.NextDouble(); var out1 = 0.02f * (float)rng.NextDouble();
                    var mossy = Smooth(0.35f, 0.1f, y0) * (0.4f + 0.6f * (float)rng.NextDouble());
                    var c = Shade(Mix(stone, moss, mossy), 0.82f + 0.25f * (float)rng.NextDouble());
                    Shapes.Box(model[Stone], new Vector3(b0, y0, -0.3f - out0), new Vector3(b1, y1, 0.3f + out1), p => Shade(c, 0.94f + 0.1f * Fbm(p * 40, row)));
                }
            }
            if (!broken) Shapes.Box(model[Stone], new Vector3(x0 - 0.005f, coping, -0.34f), new Vector3(x1 + 0.005f, 0.9f, 0.34f), Shade(dark, 1.05f + 0.05f * panel));
            else
            {
                Shapes.Box(model[Stone], new Vector3(x0 - 0.005f, coping, -0.34f), new Vector3(notchFrom, 0.9f, 0.34f), Shade(dark, 1.05f));
                Shapes.Box(model[Stone], new Vector3(notchTo, coping, -0.34f), new Vector3(x1 + 0.005f, 0.9f, 0.34f), Shade(dark, 1.05f));
                for (var k = 0; k < 7; k++)   // the fallen blocks, on the footing on both sides
                {
                    var side = k % 2 == 0 ? 1 : -1;
                    var p = new Vector3(notchFrom + (notchTo - notchFrom) * (float)rng.NextDouble(), 0.06f + 0.04f * (k / 2 % 2), side * (0.4f + 0.04f * (float)rng.NextDouble()));
                    Shapes.Box(model[Stone], Shapes.At(p, new Vector3(0.06f, 0.08f, 0.1f), (float)rng.NextDouble() * 2, ((float)rng.NextDouble() - 0.5f) * 0.8f, ((float)rng.NextDouble() - 0.5f) * 0.8f), Shade(stone, 0.8f + 0.2f * (float)rng.NextDouble()));
                }
            }
        }
        return model;
    }
}
