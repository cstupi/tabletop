using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The death marsh (6" × 6", 0.2" high): faceted black mud hummocks between pools of murky green-brown water with bubbles and
/// scum, reed clumps and bulrushes at the pools' edges, sedge tufts on the mud, and dead trees, bleached and twisted, fallen across the
/// mud with their snapped stumps standing and roots clawing out.</summary>
public static class Marsh
{
    public static readonly Material Mud = new("mud", 0.75f), Water = new("murk", 0.1f, Alpha: 0.86f), Wood = new("dead-wood", 0.9f),
        Reed = new("reeds", 0.85f, DoubleSided: true), Bubble = new("bubbles", 0.2f);
    const float Size = 6, Top = 0.2f, Level = 0.075f;

    static float U(Random r) => (float)r.NextDouble();

    public static Model Build()
    {
        var model = new Model("death-marsh", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(141);
        Quill P(Material m) => Quill.Of(model, m, model.Inches);
        var (mud, wood, reed, bubble) = (P(Mud), P(Wood), P(Reed), P(Bubble));
        float Height(float x, float z)
        {
            var dist = 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var lumps = Fbm(x * 0.85f + 7, z * 0.85f + 3, 0, 141);
            var y = 0.03f + 0.07f * Smooth(0.5f, 0.58f, lumps) + 0.02f * Smooth(0.6f, 0.72f, lumps) + 0.015f * Noise(x * 5, z * 5, 1, 142);
            y = y + (Level + 0.02f - y) * Smooth(0.9f, 0.3f, dist) * Smooth(Level + 0.04f, Level, y);
            return y * Smooth(0, 0.3f, 3 - MathF.Abs(x)) * Smooth(0, 0.3f, 3 - MathF.Abs(z));
        }
        var black = Rgb(0.13f, 0.11f, 0.08f); var wet = Rgb(0.22f, 0.18f, 0.12f); var moss = Rgb(0.3f, 0.33f, 0.14f); var sick = Rgb(0.42f, 0.4f, 0.2f);
        Foliage.Rect(mud, Size, Size, 40, 40, Height, (p, n) =>
        {
            var rim = 3 - MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Z)) < 0.25f;
            var c = p.Y < Level + 0.008f && !rim ? black : Mix(wet, Mix(moss, sick, Fbm(p.X * 1.3f, p.Z * 1.3f, 2, 143)), Smooth(Level + 0.01f, Level + 0.05f, p.Y));
            return Shade(c, 0.75f + 0.45f * Noise(p.X * 12, p.Z * 12, p.Y * 30, 144));
        }, 141, 0.35f);
        // The pools: one murky sheet over the footprint at the water line; the hummocks stand out of it.
        var murk = Rgb(0.2f, 0.26f, 0.14f); var scum = Rgb(0.42f, 0.46f, 0.18f);
        model[Water].Grid(31, 31, wrap: false, (i, j) => new Vector3(-2.7f + 5.4f * j / 30, Level, -2.7f + 5.4f * i / 30),
            (p, _) => { var q = model.Inch(p); return Mix(Shade(murk, 0.85f + 0.3f * Fbm(q.X * 2, q.Z * 2, 0, 145)), scum, Smooth(0.62f, 0.75f, Fbm(q.X * 1.4f + 9, q.Z * 1.4f, 0, 146)) * 0.8f); }, flipped: true);
        // Bubbles rising in the pools, in clusters.
        for (var k = 0; k < 70; k++)
        {
            var p = new Vector2(U(rng) * 5.4f - 2.7f, U(rng) * 5.4f - 2.7f);
            if (Height(p.X, p.Y) > Level - 0.01f) continue;
            for (var b = 0; b < 5; b++)
            {
                var r = 0.02f + 0.045f * U(rng);
                var at = new Vector3(p.X + (U(rng) - 0.5f) * 0.25f, Level - r * 0.2f, p.Y + (U(rng) - 0.5f) * 0.25f);
                Foliage.Stone(bubble, at, new Vector3(r, MathF.Min(r, 0.04f), r), 150 + k * 5 + b, 0, (_, n) => Shade(Rgb(0.55f, 0.6f, 0.32f), 0.75f + 0.35f * n.Y), 0, 0.05f);
            }
        }
        // Reed clumps and bulrushes where mud meets water, sedge tufts on the hummocks.
        var reedBase = Rgb(0.25f, 0.27f, 0.13f); var reedTip = Rgb(0.55f, 0.5f, 0.28f); var head = Rgb(0.3f, 0.2f, 0.12f);
        for (var k = 0; k < 900 && model[Reed].Triangles < 3600; k++)
        {
            var p = new Vector2(U(rng) * 5.6f - 2.8f, U(rng) * 5.6f - 2.8f);
            var h = Height(p.X, p.Y);
            if (h > Level - 0.005f && h < Level + 0.03f)
            {
                var root = new Vector3(p.X, Level - 0.01f, p.Y);
                Foliage.Tuft(reed, rng, root, 8, Top - Level, 0.05f, 0.5f, reedBase, Mix(reedTip, reedBase, U(rng) * 0.4f), bent: true);
                if (k % 4 == 0)
                {
                    var lean = Foliage.Flat(U(rng) * 6.28f) * 0.02f;
                    Foliage.Tube(reed, [root, root + lean + Vector3.UnitY * (Top - Level) * 0.75f, root + lean * 1.1f + Vector3.UnitY * (Top - Level - 0.002f)], [0.006f, 0.012f, 0], 4, p => p.Y > Level + 0.06f ? head : reedBase, k, 0.1f);
                }
            }
            else if (h > Level + 0.03f && k % 3 == 0)
                Foliage.Tuft(reed, rng, new Vector3(p.X, h - 0.005f, p.Y), 6, 0.08f, 0.03f, 0.9f, Rgb(0.28f, 0.28f, 0.14f), Mix(sick, Rgb(0.6f, 0.55f, 0.32f), U(rng)));
        }
        // Dead trees: bleached trunks fallen across the marsh, branches splayed on the mud, a snapped stump at each root.
        var bone = Rgb(0.62f, 0.6f, 0.52f); var rot = Rgb(0.32f, 0.29f, 0.24f);
        Func<Vector3, Vector4> Bark(int seed) => p => Shade(Mix(rot, bone, Fbm(p.X * 3, p.Z * 3, 0, seed)), 0.8f + 0.3f * Noise(p.X * 14, p.Y * 30, p.Z * 14, seed));
        foreach (var (root, angle, length, seed) in new[] { (new Vector2(-1.9f, -1.6f), 0.5f, 3.4f, 1), (new Vector2(1.9f, 1.2f), 3.6f, 2.8f, 2), (new Vector2(1.6f, -2.1f), 2.3f, 2.2f, 3) })
        {
            var dir = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            var side = new Vector3(-dir.Z, 0, dir.X);
            var start = new Vector3(root.X, 0.135f, root.Y);
            var pts = new List<Vector3>();
            for (var i = 0; i <= 8; i++) pts.Add(start + dir * length * i / 8 + side * 0.18f * MathF.Sin(i * 1.0f + seed) + Vector3.UnitY * (-0.03f * i / 8));
            Foliage.Tube(wood, pts, pts.Select((_, i) => 0.065f - 0.04f * i / 8).ToArray(), 7, Bark(seed), 160 + seed, 0.25f, cap: true);
            for (var b = 1; b < 8; b++)
            {
                if (rng.NextDouble() < 0.3) continue;
                var s = rng.NextDouble() < 0.5 ? 1 : -1;
                var a = (0.5f + 0.7f * U(rng)) * s;
                var out1 = dir * MathF.Cos(a) + side * MathF.Sin(a);
                var reach = 0.3f + 0.35f * U(rng);
                var mid = pts[b] + out1 * reach;
                var a2 = a + (U(rng) - 0.5f) * 1.6f;
                var tip = mid + (dir * MathF.Cos(a2) + side * MathF.Sin(a2)) * reach * 0.8f;
                Foliage.Tube(wood, [pts[b], mid with { Y = 0.115f }, tip with { Y = 0.1f }], [0.035f, 0.022f, 0.006f], 5, Bark(seed + b), 170 + b, 0.2f);
                var a3 = a - s * (0.6f + 0.5f * U(rng));
                Foliage.Tube(wood, [mid with { Y = 0.115f }, (mid + (dir * MathF.Cos(a3) + side * MathF.Sin(a3)) * reach * 0.5f) with { Y = 0.1f }], [0.018f, 0.005f], 4, Bark(seed + b + 40), 180 + b, 0.2f);
            }
            // The stump: splintered at the break, its roots clawing out over the mud.
            var stump = start - dir * 0.15f;
            Foliage.Tube(wood, [stump with { Y = 0 }, stump with { Y = Top * 0.6f }, stump with { Y = Top }], [0.17f, 0.13f, 0.1f], 8, Bark(seed + 10), 190 + seed, 0.35f, cap: true);
            for (var sp = 0; sp < 5; sp++)
            {
                var o = Foliage.Flat(sp * 1.25f + seed);
                Foliage.Tube(wood, [stump with { Y = Top * 0.8f } + o * 0.07f, stump with { Y = Top } + o * 0.08f], [0.025f, 0], 3, Bark(seed + 11), 195 + sp, 0);
            }
            for (var r = 0; r < 5; r++)
            {
                var a = angle + MathF.PI * (0.45f + 0.28f * r + 0.15f * U(rng));
                var o = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                Foliage.Tube(wood, [stump + o * 0.1f + Vector3.UnitY * 0.06f, stump + o * 0.32f + Vector3.UnitY * 0.04f, stump + o * 0.55f + side * 0.1f + Vector3.UnitY * 0.03f], [0.04f, 0.025f, 0.004f], 4, Bark(seed + 20 + r), 200 + r, 0.2f);
            }
        }
        return model;
    }
}
