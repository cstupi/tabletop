using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A low crop field: ploughed soil in ridges along x, and on each ridge a row of knee-high wheat, tufts of stalks green at the foot and
/// gold at the ear, leaning a little with the wind. Area terrain: models stand in it.</summary>
public static class Field
{
    public static readonly Material Soil = new("soil", 0.95f), Crop = new("crop", 0.8f, DoubleSided: true);
    const int Rows = 9;

    public static Model Build()
    {
        var model = new Model("field", round: false);
        var rng = new Random(71);
        var soil = Rgb(0.42f, 0.32f, 0.22f); var ridge = Rgb(0.5f, 0.39f, 0.27f);
        const float pitch = 0.94f / Rows;
        float Height(float z) { var t = (z + 0.47f) / pitch; return t is < 0 or > Rows ? 0.012f : 0.012f + 0.022f * MathF.Pow(MathF.Sin(MathF.PI * (t - MathF.Floor(t))), 2); }
        model[Soil].Grid(Rows * 6 + 3, 12, wrap: false, (i, j) =>
        {
            var z = -0.5f + i / (float)(Rows * 6 + 2); var x = -0.5f + j / 11f;
            var edge = MathF.Abs(x) > 0.49f || MathF.Abs(z) > 0.49f;
            return new Vector3(x, edge ? 0 : Height(z) + 0.004f * Noise(x * 30, z * 30, 0, 71), z);
        }, (p, n) => Shade(Mix(soil, ridge, Smooth(0.015f, 0.032f, p.Y)), 0.85f + 0.25f * Fbm(p.X * 20, p.Z * 20, 0, 72)), flipped: true);
        var stalk = Rgb(0.45f, 0.52f, 0.22f); var gold = Rgb(0.86f, 0.72f, 0.36f); var pale = Rgb(0.9f, 0.82f, 0.52f);
        var wind = new Vector3(0.25f, 0, 0.1f);
        for (var row = 0; row < Rows; row++)
        {
            var z = -0.47f + (row + 0.5f) * pitch;
            for (var x = -0.46f; x <= 0.46f; x += 0.042f)
            {
                var root = new Vector3(x + 0.01f * ((float)rng.NextDouble() - 0.5f), 0.025f, z + 0.012f * ((float)rng.NextDouble() - 0.5f));
                var ripe = Mix(gold, pale, (float)rng.NextDouble() * 0.6f);
                for (var s = 0; s < 5; s++)
                {
                    var a = (float)(rng.NextDouble() * 2 * MathF.PI);
                    var spread = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.012f;
                    var h = 0.4f + 0.16f * (float)rng.NextDouble();
                    var tip = root + spread * 2 + wind * h * (0.6f + 0.4f * (float)rng.NextDouble()) + Vector3.UnitY * (h - root.Y);
                    var side = Vector3.Normalize(new Vector3(-(tip - root).Z, 0, (tip - root).X) + new Vector3(0.001f, 0, 0)) * 0.005f;
                    var neck = Vector3.Lerp(root + spread, tip, 0.72f);
                    model[Crop].Tri(root + spread - side, root + spread + side, neck, stalk, stalk, Mix(stalk, ripe, 0.7f));
                    var ear = (tip - neck) * 1.1f; var earSide = side * 2.2f;
                    model[Crop].Quad(neck, neck + ear * 0.45f + earSide, neck + ear, neck + ear * 0.45f - earSide, ripe);
                }
            }
        }
        return model;
    }
}
