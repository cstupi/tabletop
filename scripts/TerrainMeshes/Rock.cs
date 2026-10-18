using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A large rock (Solid, impassable): one big boulder whose top is cut flat at y = 1, the volume's top, two
/// smaller ones against it and scattered stones, banded in grey-brown strata with lichen on the faces that look up.</summary>
public static class Rock
{
    public static readonly Material Stone = new("rock", 0.82f);

    public static Model Build()
    {
        var model = new Model("rock", round: false);
        var rng = new Random(61);
        var grey = Rgb(0.42f, 0.42f, 0.41f); var brown = Rgb(0.4f, 0.36f, 0.31f); var dark = Rgb(0.26f, 0.25f, 0.24f); var lichen = Rgb(0.55f, 0.58f, 0.32f);
        Func<Vector3, Vector3, Vector4> Colour(int seed) => (p, n) =>
        {
            var band = Fbm(p.Y * 9 + 0.4f * Fbm(p * 4, seed), 0.5f, 0.5f, seed);
            var c = Mix(grey, brown, Smooth(0.4f, 0.7f, band));
            c = Mix(c, dark, Smooth(0.25f, 0.02f, p.Y) * 0.7f);
            c = Mix(c, lichen, Smooth(0.7f, 0.92f, n.Y) * Smooth(0.45f, 0.7f, Fbm(p * 14, seed + 1)) * 0.85f);
            return Shade(c, 0.86f + 0.22f * Fbm(p * 25, seed + 2));
        };
        Vector3 Sunk(Vector3 q) => q with { Y = MathF.Max(q.Y, 0) };
        Shapes.Blob(model[Stone], new Vector3(-0.06f, 0.4f, 0.04f), new Vector3(0.42f, 0.72f, 0.38f), 2, 0.3f, 3, Colour(3),
            yaw: 0.4f, shape: q => Sunk(q with { Y = MathF.Min(q.Y, 1) }), frequency: 2.4f);
        Shapes.Blob(model[Stone], new Vector3(0.3f, 0.16f, -0.25f), new Vector3(0.2f, 0.33f, 0.18f), 1, 0.32f, 5, Colour(5), yaw: 1.1f, shape: Sunk, frequency: 2.4f);
        Shapes.Blob(model[Stone], new Vector3(0.24f, 0.08f, 0.3f), new Vector3(0.16f, 0.22f, 0.15f), 1, 0.32f, 7, Colour(7), yaw: 2.3f, shape: Sunk, frequency: 2.4f);
        Shapes.Blob(model[Stone], new Vector3(-0.33f, 0.04f, -0.33f), new Vector3(0.1f, 0.12f, 0.09f), 1, 0.3f, 9, Colour(9), yaw: 0.2f, shape: Sunk);
        for (var k = 0; k < 9; k++)
        {
            var a = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.4f + 0.08f * (float)rng.NextDouble(); var s = 0.025f + 0.025f * (float)rng.NextDouble();
            Shapes.Blob(model[Stone], new Vector3(MathF.Cos(a) * r, s * 0.4f, MathF.Sin(a) * r), new Vector3(s * 1.2f, s, s), 0, 0.3f, 20 + k, Colour(20 + k), yaw: a, shape: Sunk);
        }
        return model;
    }
}
