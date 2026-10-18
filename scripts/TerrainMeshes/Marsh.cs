using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The death marsh (6" × 6", 0.2" high): black mud hummocks between pools of murky green-brown water with bubbles and scum,
/// reed beds at the pools' edges, and dead trees, bleached and twisted, fallen across the mud with their stumps standing.</summary>
public static class Marsh
{
    public static readonly Material Mud = new("mud", 0.75f), Water = new("murk", 0.1f, Alpha: 0.86f), Wood = new("dead-wood", 0.9f),
        Reed = new("reeds", 0.85f, DoubleSided: true), Bubble = new("bubbles", 0.2f);
    const float Size = 6, Top = 0.2f, Level = 0.075f;

    public static Model Build()
    {
        var model = new Model("death-marsh", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(141);
        float Height(float x, float z)
        {
            var dist = 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var lumps = Fbm(x * 0.85f + 7, z * 0.85f + 3, 0, 141);
            var y = 0.03f + 0.07f * Smooth(0.5f, 0.58f, lumps) + 0.02f * Smooth(0.6f, 0.72f, lumps) + 0.01f * Noise(x * 4, z * 4, 1, 142);
            y = y + (Level + 0.02f - y) * Smooth(0.9f, 0.3f, dist) * Smooth(Level + 0.04f, Level, y);
            return y * Smooth(0, 0.3f, 3 - MathF.Abs(x)) * Smooth(0, 0.3f, 3 - MathF.Abs(z));
        }
        var black = Rgb(0.13f, 0.11f, 0.08f); var wet = Rgb(0.22f, 0.18f, 0.12f); var moss = Rgb(0.3f, 0.33f, 0.14f); var sick = Rgb(0.42f, 0.4f, 0.2f);
        Shapes.Ground(model, Mud, 60, 60, Height, (p, n) =>
        {
            var rim = 3 - MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Z)) < 0.25f;
            var c = p.Y < Level + 0.008f && !rim ? black : Mix(wet, Mix(moss, sick, Fbm(p.X * 1.3f, p.Z * 1.3f, 2, 143)), Smooth(Level + 0.01f, Level + 0.05f, p.Y));
            return Shade(c, 0.8f + 0.35f * Fbm(p.X * 5, p.Z * 5, 3, 144));
        });
        // The pools: one murky sheet over the footprint at the water line; the hummocks stand out of it.
        var murk = Rgb(0.2f, 0.26f, 0.14f); var scum = Rgb(0.42f, 0.46f, 0.18f);
        model[Water].Grid(31, 31, wrap: false, (i, j) => new Vector3(-2.7f + 5.4f * j / 30, Level, -2.7f + 5.4f * i / 30),
            (p, _) => { var q = model.Inch(p); return Mix(Shade(murk, 0.85f + 0.3f * Fbm(q.X * 2, q.Z * 2, 0, 145)), scum, Smooth(0.62f, 0.75f, Fbm(q.X * 1.4f + 9, q.Z * 1.4f, 0, 146)) * 0.8f); }, flipped: true);
        // Bubbles rising in the pools, in clusters.
        for (var k = 0; k < 90 && model[Bubble].Triangles < 1600; k++)
        {
            var p = new Vector2((float)rng.NextDouble() * 5.4f - 2.7f, (float)rng.NextDouble() * 5.4f - 2.7f);
            if (Height(p.X, p.Y) > Level - 0.01f) continue;
            for (var b = 0; b < 4; b++)
            {
                var r = 0.025f + 0.05f * (float)rng.NextDouble();
                var at = new Vector3(p.X + ((float)rng.NextDouble() - 0.5f) * 0.25f, Level - r * 0.3f, p.Y + ((float)rng.NextDouble() - 0.5f) * 0.25f);
                Shapes.Blob(model[Bubble], at, new Vector3(r, MathF.Min(r, 0.05f), r), 1, 0.05f, 150 + k * 4 + b, (_, n) => Shade(Rgb(0.55f, 0.6f, 0.32f), 0.8f + 0.3f * n.Y), smooth: true);
            }
        }
        // Reed beds where mud meets water.
        var reedBase = Rgb(0.25f, 0.27f, 0.13f); var reedTip = Rgb(0.55f, 0.5f, 0.28f);
        for (var k = 0; k < 600 && model[Reed].Triangles < 2600; k++)
        {
            var p = new Vector2((float)rng.NextDouble() * 5.6f - 2.8f, (float)rng.NextDouble() * 5.6f - 2.8f);
            var h = Height(p.X, p.Y);
            if (h < Level - 0.005f || h > Level + 0.03f) continue;
            for (var c = 0; c < 3; c++)
            {
                var root = new Vector3(p.X + ((float)rng.NextDouble() - 0.5f) * 0.2f, Level - 0.01f, p.Y + ((float)rng.NextDouble() - 0.5f) * 0.2f);
                Shapes.Tuft(model[Reed], rng, root, 7, Top - Level, 0.035f, 0.9f, reedBase, reedTip);
            }
        }
        // Dead trees: bleached trunks fallen across the marsh, branches splayed on the mud, a broken stump at each root.
        var bone = Rgb(0.62f, 0.6f, 0.52f); var rot = Rgb(0.32f, 0.29f, 0.24f);
        Func<Vector3, Vector4> Bark(int seed) => p => Shade(Mix(rot, bone, Fbm(p.X * 3, p.Z * 3, 0, seed)), 0.85f + 0.2f * Noise(p.X * 9, p.Z * 9, 1, seed));
        foreach (var (root, angle, length, seed) in new[] { (new Vector2(-1.9f, -1.6f), 0.5f, 3.4f, 1), (new Vector2(1.9f, 1.2f), 3.6f, 2.8f, 2), (new Vector2(1.6f, -2.1f), 2.3f, 2.2f, 3) })
        {
            var dir = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            var side = new Vector3(-dir.Z, 0, dir.X);
            var start = new Vector3(root.X, 0.135f, root.Y);
            // The trunk wanders as it lies, a little thinner each segment.
            var pts = new List<Vector3>();
            for (var i = 0; i <= 6; i++) pts.Add(start + dir * length * i / 6 + side * 0.18f * MathF.Sin(i * 1.3f + seed) + Vector3.UnitY * (-0.03f * i / 6));
            Shapes.Chain(model[Wood], pts, 0.065f, 0.025f, 6, Bark(seed));
            for (var b = 1; b < 6; b++)
            {
                if (rng.NextDouble() < 0.25) continue;
                var s = rng.NextDouble() < 0.5 ? 1 : -1;
                var a = (0.5f + 0.7f * (float)rng.NextDouble()) * s;
                var out1 = dir * MathF.Cos(a) + side * MathF.Sin(a);
                var reach = 0.35f + 0.35f * (float)rng.NextDouble();
                var mid = pts[b] + out1 * reach;
                var a2 = a + (float)(rng.NextDouble() - 0.5) * 1.6f;
                var tip = mid + (dir * MathF.Cos(a2) + side * MathF.Sin(a2)) * reach * 0.8f;
                Shapes.Chain(model[Wood], [pts[b], mid with { Y = 0.115f }, tip with { Y = 0.1f }], 0.04f, 0.012f, 5, Bark(seed + b));
                var a3 = a - s * (0.6f + 0.5f * (float)rng.NextDouble());
                Shapes.Chain(model[Wood], [mid with { Y = 0.115f }, (mid + (dir * MathF.Cos(a3) + side * MathF.Sin(a3)) * reach * 0.5f) with { Y = 0.1f }], 0.022f, 0.01f, 4, Bark(seed + b + 40));
            }
            // The stump: jagged at the break, its roots clawing out over the mud.
            var stump = start - dir * 0.15f;
            Shapes.Prism(model[Wood], Shapes.At(stump with { Y = 0 }, new Vector3(0.16f, Top, 0.16f)), 7, 0.55f, Bark(seed + 10));
            for (var r = 0; r < 4; r++)
            {
                var a = angle + MathF.PI * (0.55f + 0.3f * r + 0.15f * (float)rng.NextDouble());
                var o = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                Shapes.Chain(model[Wood], [stump + o * 0.1f + Vector3.UnitY * 0.1f, stump + o * 0.32f + Vector3.UnitY * 0.06f, stump + o * 0.55f + side * 0.1f + Vector3.UnitY * 0.06f], 0.04f, 0.012f, 4, Bark(seed + 20 + r));
            }
        }
        return model;
    }
}
