using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Water in a shallow basin: grassy banks rising from the footprint's edge to a lip, muddy inner banks down to a dark
/// bed, and a translucent rippled surface between them, with reeds and stones at the shore. <see cref="River"/> runs along z
/// with open ends (a river is fitted 3" × 12"); <see cref="Lake"/> is round, with lily pads.</summary>
public static class Water
{
    public static readonly Material Bank = new("bank", 0.95f), Surface = new("water", 0.06f, Alpha: 0.78f), Reed = new("reeds", 0.85f, DoubleSided: true), Stone = new("stone", 0.85f), Pad = new("lily", 0.7f);
    public const float Level = 0.24f;
    const float Lip = 0.42f;

    static readonly Vector4 Grass = Rgb(0.4f, 0.5f, 0.24f), Mud = Rgb(0.42f, 0.34f, 0.24f), Bed = Rgb(0.2f, 0.2f, 0.17f), Sand = Rgb(0.6f, 0.53f, 0.38f), Blue = Rgb(0.28f, 0.47f, 0.52f);

    /// <summary>Bank height by distance d from the footprint's edge inward, d in [0, 0.5], with the open water starting at <paramref name="shore"/>.</summary>
    static float Profile(float d, float shore)
    {
        var lipAt = 0.07f;
        if (d <= lipAt) return 0.36f * Smooth(0, lipAt, d);
        if (d >= shore) return 0.03f;
        return 0.03f + 0.33f * Smooth(shore, lipAt, d);
    }

    static Vector4 BankColour(Vector3 p, Vector3 n, int seed)
    {
        var c = p.Y > Level + 0.04f ? Mix(Mud, Grass, Smooth(Level + 0.04f, Level + 0.1f, p.Y)) : Mix(Bed, Mix(Sand, Mud, 0.5f), Smooth(0.03f, Level, p.Y));
        return Shade(c, 0.82f + 0.3f * Fbm(p.X * 18, p.Z * 18, p.Y, seed));
    }

    static Vector4 Ripple(Vector3 p) => Shade(Blue, 0.9f + 0.2f * Fbm(p.X * 9, p.Z * 9, 0, 3));

    public static Model River()
    {
        var model = new Model("water", round: false);
        var rng = new Random(81);
        const int along = 64, across = 22;
        float Shore(float z) => 0.15f + 0.06f * Fbm(z * 5, 0, 1, 81);   // how far in from either edge the open water begins
        float Y(float x, float z) { var d = 0.5f - MathF.Abs(x); return Profile(d, Shore(z * (x < 0 ? 1 : -1) + (x < 0 ? 0 : 3))); }
        model[Bank].Grid(along + 1, across + 1, wrap: false, (i, j) =>
        {
            var z = -0.5f + (float)i / along; var x = -0.5f + (float)j / across;
            return new Vector3(x, Y(x, z), z);
        }, (p, n) => BankColour(p, n, 81), flipped: true);
        foreach (var z in new[] { -0.5f, 0.5f })   // the cut ends, down to the table
            for (var j = 0; j < across; j++)
            {
                float x0 = -0.5f + (float)j / across, x1 = -0.5f + (float)(j + 1) / across;
                var q = new[] { new Vector3(x0, 0, z), new Vector3(x1, 0, z), new Vector3(x1, Y(x1, z), z), new Vector3(x0, Y(x0, z), z) };
                Shapes.Oriented(model[Bank], q, new Vector3(0, 0, z), _ => Shade(Mud, 0.7f));
            }
        model[Surface].Grid(along + 1, 9, wrap: false, (i, j) =>
        {
            var z = -0.5f + (float)i / along; var x = -0.43f + 0.86f * j / 8;
            return new Vector3(x, Level + 0.006f * (Noise(x * 40, z * 40, 0, 5) - 0.5f), z);
        }, (p, _) => Ripple(p), flipped: true);
        foreach (var z in new[] { -0.5f, 0.5f })
            Shapes.Oriented(model[Surface], [new(-0.43f, 0.03f, z), new(0.43f, 0.03f, z), new(0.43f, Level, z), new(-0.43f, Level, z)], new Vector3(0, 0, z), _ => Shade(Blue, 0.8f));
        Shoreline(model, rng, k => { var z = -0.46f + 0.92f * (float)rng.NextDouble(); var sx = k % 2 == 0 ? 1 : -1; var d = Shore(z * -sx + (sx > 0 ? 3 : 0)) - 0.01f; return new Vector3(sx * (0.5f - d), 0, z); }, 18);
        return model;
    }

    public static Model Lake()
    {
        var model = new Model("lake", round: true);
        var rng = new Random(91);
        const int segments = 72, rings = 18;
        float Shore(float t) => 0.12f + 0.08f * Fbm(MathF.Cos(t) * 1.5f + 2, MathF.Sin(t) * 1.5f + 2, 0, 91);
        model[Bank].Grid(rings + 1, segments, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments; var r = 0.5f * i / rings;
            return new Vector3(r * MathF.Cos(t), Profile(0.5f - r, Shore(t)), r * MathF.Sin(t));
        }, (p, n) => BankColour(p, n, 91));
        model[Surface].Grid(10, segments, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments; var r = 0.44f * i / 9;
            return new Vector3(r * MathF.Cos(t), Level + 0.006f * (Noise(r * MathF.Cos(t) * 40, r * MathF.Sin(t) * 40, 0, 5) - 0.5f), r * MathF.Sin(t));
        }, (p, _) => Ripple(p));
        Shoreline(model, rng, k => { var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.5f - Shore(t) + 0.01f; return new Vector3(r * MathF.Cos(t), 0, r * MathF.Sin(t)); }, 16);
        for (var k = 0; k < 9; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.1f + 0.2f * (float)rng.NextDouble(); var s = 0.022f + 0.014f * (float)rng.NextDouble();
            Shapes.Prism(model[Pad], Shapes.At(new Vector3(r * MathF.Cos(t), Level + 0.004f, r * MathF.Sin(t)), new Vector3(s, 0.006f, s)), 9, 1,
                Shade(Rgb(0.3f, 0.48f, 0.2f), 0.85f + 0.3f * (float)rng.NextDouble()), phase: t);
        }
        return model;
    }

    /// <summary>Reed beds and stones along the shore, at points <paramref name="at"/> gives (its Y ignored).</summary>
    static void Shoreline(Model model, Random rng, Func<int, Vector3> at, int count)
    {
        for (var k = 0; k < count; k++)
        {
            var p = at(k);
            if (k % 3 == 2)
            {
                var s = 0.025f + 0.02f * (float)rng.NextDouble();
                Shapes.Blob(model[Stone], p with { Y = Level - 0.02f }, new Vector3(s, s * 1.4f, s), 1, 0.3f, 100 + k,
                    (q, n) => Shade(Mix(Rgb(0.5f, 0.49f, 0.46f), Rgb(0.32f, 0.42f, 0.24f), Smooth(0.6f, 0.95f, n.Y) * 0.5f), 0.8f + 0.3f * Fbm(q * 50, k)));
                continue;
            }
            for (var c = 0; c < 3; c++)
            {
                var root = p + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.04f, Level - 0.03f, ((float)rng.NextDouble() - 0.5f) * 0.04f);
                Shapes.Tuft(model[Reed], rng, root, 6, 0.55f, 0.01f, 0.12f, Rgb(0.32f, 0.4f, 0.2f), Rgb(0.6f, 0.62f, 0.34f));
            }
        }
    }
}
