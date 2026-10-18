using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A blast crater: a raised rim of churned earth round a scorched bowl, with debris and twisted metal. Round.</summary>
public static class Crater
{
    public static readonly Material Metal = new("metal", 0.4f, Metallic: 0.8f), Earth = new("earth", 0.95f), Stone = new("debris", 0.85f);

    public static Model Build()
    {
        var model = new Model("crater", round: true);
        var rng = new Random(111);
        const int segments = 72, rings = 26;
        float Rim(float t) => 0.39f + 0.025f * (Noise(MathF.Cos(t) * 3 + 5, MathF.Sin(t) * 3 + 5, 0, 111) - 0.5f);
        float Crest(float t) => 0.78f + 0.22f * Fbm(MathF.Cos(t) * 2 + 1, MathF.Sin(t) * 2 + 1, 0, 112);
        float Height(float r, float t)
        {
            var rim = Rim(t);
            if (r >= rim) return Crest(t) * MathF.Pow(Smooth(0.5f, rim, r), 1.4f);
            var bowl = Smooth(0.08f, rim, r);
            return 0.06f + (Crest(t) - 0.06f) * MathF.Pow(bowl, 2.2f);
        }
        var soil = Rgb(0.45f, 0.36f, 0.25f); var churned = Rgb(0.55f, 0.45f, 0.32f); var scorched = Rgb(0.2f, 0.17f, 0.14f); var ash = Rgb(0.11f, 0.1f, 0.09f);
        model[Earth].Grid(rings + 1, segments, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments; var r = 0.5f * i / rings;
            r += i is > 0 and < rings ? 0.006f * (Noise(t * 6, i, 0, 113) - 0.5f) : 0;
            return new Vector3(r * MathF.Cos(t), Height(r, t), r * MathF.Sin(t));
        }, (p, n) =>
        {
            var r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            var c = r > 0.36f ? Mix(churned, soil, Smooth(0.4f, 0.5f, r)) : Mix(ash, scorched, Smooth(0.08f, 0.3f, r));
            c = Mix(c, churned, Smooth(0.3f, 0.38f, r) * Smooth(0.44f, 0.38f, r));
            return Shade(c, 0.8f + 0.35f * Fbm(p.X * 16, p.Z * 16, p.Y * 4, 114));
        });
        for (var k = 0; k < 14; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = k < 9 ? Rim(t) + 0.03f * ((float)rng.NextDouble() - 0.5f) : 0.1f + 0.18f * (float)rng.NextDouble();
            var s = 0.02f + 0.025f * (float)rng.NextDouble();
            var p = new Vector3(r * MathF.Cos(t), Height(r, t), r * MathF.Sin(t));
            Shapes.Blob(model[Stone], p, new Vector3(s, s * 1.6f, s * 0.9f), 0, 0.3f, 120 + k, (q, n) => Shade(Rgb(0.45f, 0.42f, 0.38f), 0.65f + 0.35f * Fbm(q * 40, k)), yaw: t);
        }
        for (var k = 0; k < 4; k++)   // twisted metal in the bowl
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.05f + 0.2f * (float)rng.NextDouble();
            var p = new Vector3(r * MathF.Cos(t), Height(r, t) + 0.05f, r * MathF.Sin(t));
            Shapes.Box(model[Metal], Shapes.At(p, new Vector3(0.008f, 0.14f, 0.05f), t, 0.6f + 0.4f * (float)rng.NextDouble(), 0.3f), Rgb(0.3f, 0.27f, 0.25f));
        }
        return model;
    }
}
