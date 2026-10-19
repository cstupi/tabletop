using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A low crop field (authored in inches at 6" × 6", 1" high, drawn in the unit box): ploughed soil in faceted ridges along x,
/// and on each ridge a row of knee-high wheat, clumps of stalks green at the foot and gold at the ear, each ear a plait of grains with
/// whiskers, leaning a little with the wind; leaves at the foot, weeds and clods between the rows. Area terrain: models stand in it.</summary>
public static class Field
{
    public static readonly Material Soil = new("soil", 0.95f), Crop = new("crop", 0.8f, DoubleSided: true), Stone = new("stone", 0.85f);
    const int Rows = 9;
    static readonly Vector3 Drawn = new(6, 1, 6);

    public static Model Build()
    {
        var model = new Model("field", round: false);
        var rng = new Random(71);
        var soil = Quill.Of(model, Soil, Drawn); var crop = Quill.Of(model, Crop, Drawn); var stone = Quill.Of(model, Stone, Drawn);
        var earth = Rgb(0.42f, 0.32f, 0.22f); var ridge = Rgb(0.52f, 0.4f, 0.27f);
        const float pitch = 5.64f / Rows;
        float Height(float x, float z)
        {
            var t = (z + 2.82f) / pitch; var dist = 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var y = t is < 0 or > Rows ? 0.06f : 0.06f + 0.11f * MathF.Pow(MathF.Sin(MathF.PI * (t - MathF.Floor(t))), 2);
            return (y + 0.03f * (Noise(x * 5, z * 5, 0, 71) - 0.5f)) * Smooth(0, 0.12f, dist);
        }
        Foliage.Rect(soil, 6, 6, 26, Rows * 4, Height, (p, n) => Shade(Mix(earth, ridge, Smooth(0.08f, 0.15f, p.Y)), 0.78f + 0.35f * Noise(p.X * 9, p.Z * 9, p.Y * 10, 72)), 71, 0.3f);
        var stalk = Rgb(0.45f, 0.52f, 0.22f); var gold = Rgb(0.84f, 0.68f, 0.32f); var pale = Rgb(0.9f, 0.8f, 0.5f); var leaf = Rgb(0.38f, 0.5f, 0.2f);
        var wind = Vector3.Normalize(new Vector3(0.9f, 0, 0.35f));
        for (var row = 0; row < Rows; row++)
        {
            var z = -2.82f + (row + 0.5f) * pitch;
            for (var x = -2.78f; x <= 2.78f; x += 0.17f + 0.05f * (float)rng.NextDouble())
            {
                var root = new Vector3(x + 0.04f * ((float)rng.NextDouble() - 0.5f), 0, z + 0.07f * ((float)rng.NextDouble() - 0.5f));
                root.Y = Height(root.X, root.Z) - 0.01f;
                var ripe = Mix(gold, pale, (float)rng.NextDouble() * 0.6f);
                var stalks = 4;
                for (var s = 0; s < stalks; s++)
                {
                    var o = Foliage.Flat((float)rng.NextDouble() * 6.28f);
                    var foot = root + o * 0.025f * (float)rng.NextDouble();
                    var h = 0.4f + 0.14f * (float)rng.NextDouble();
                    var lean = wind * (0.06f + 0.05f * (float)rng.NextDouble()) + o * 0.05f;
                    var neck = foot + lean * 0.7f + Vector3.UnitY * h * 0.72f;
                    var tip = foot + lean * 1.6f + Vector3.UnitY * h;
                    var side = Vector3.Normalize(Vector3.Cross(tip - foot, Vector3.UnitY) + new Vector3(1e-4f, 0, 0));
                    crop.Face(foot - side * 0.006f, foot + side * 0.006f, neck + side * 0.002f, Vector3.Cross(side, neck - foot), stalk, stalk, Mix(stalk, ripe, 0.7f));
                    // The ear: three pairs of grains plaited up it, and whiskers past its tip.
                    var axis = tip - neck; var normal = Vector3.Cross(side, axis);
                    for (var g = 0; g < 2; g++)
                    {
                        var a = neck + axis * (g / 2f); var b = neck + axis * ((g + 1) / 2f);
                        var w = 0.016f * (1 - 0.2f * g);
                        var c = Shade(ripe, 0.9f + 0.08f * g);
                        crop.Face(a, a + axis * 0.25f + side * w, b, normal, c, Shade(c, 1.1f), c);
                        crop.Face(a, b, a + axis * 0.25f - side * w, normal, c, c, Shade(c, 1.1f));
                    }
                    crop.Face(tip - side * 0.004f, tip + side * 0.004f, tip + axis * 0.55f + lean * 0.3f, normal, ripe, ripe, pale);
                }
                // Two leaves at the clump's foot.
                for (var l = 0; l < 2; l++)
                {
                    var o = Foliage.Flat((float)rng.NextDouble() * 6.28f);
                    Foliage.Blade(crop, root + Vector3.UnitY * 0.04f, Vector3.Normalize(o + Vector3.UnitY * 0.6f), 0.22f, 0.03f, 0.05f, 0.12f, Shade(leaf, 0.8f), Mix(leaf, gold, 0.4f), 3);
                }
            }
            // Weeds and clods in the furrow.
            for (var k = 0; k < 8; k++)
            {
                var p = new Vector3(-2.75f + 5.5f * (float)rng.NextDouble(), 0, z + pitch * 0.5f);
                if (MathF.Abs(p.Z) > 2.85f) continue;
                p.Y = Height(p.X, p.Z);
                if (k % 2 == 0)
                {
                    var s = 0.03f + 0.03f * (float)rng.NextDouble();
                    Foliage.Stone(stone, p, new Vector3(s * 1.3f, s * 0.8f, s), 80 + row * 8 + k, (float)rng.NextDouble() * 3, (q, n) => Shade(ridge, 0.75f + 0.35f * n.Y));
                }
                else Foliage.Tuft(crop, rng, p, 5, 0.12f, 0.015f, 0.6f, Rgb(0.26f, 0.36f, 0.14f), Rgb(0.5f, 0.56f, 0.24f));
            }
        }
        return model;
    }
}
