using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Cultivated pieces, authored in inches. <see cref="Tall"/> (6" × 6", 1" high): maize in rows along x on ploughed ridges,
/// each plant a jointed stalk standing the full height with long arching leaves, a husked cob and a tassel, weeds and clods in
/// the furrows. <see cref="Plantation"/> (6" × 6", 6" high): an orchard in a regular grid, fruit trees of one size on whitewashed,
/// rooted trunks with branching limbs and trimmed leaf crowns, fruit among the leaves, grass strips under each row and tilled soil between.</summary>
public static class Crops
{
    public static readonly Material Soil = new("soil", 0.95f), Stalk = new("stalk", 0.8f), Blade = new("crop-leaf", 0.8f, DoubleSided: true),
        Bark = new("bark", 0.9f), Crown = new("crown", 0.8f, DoubleSided: true), Fruit = new("fruit", 0.5f), Grass = new("grass", 0.9f, DoubleSided: true),
        Stone = new("stone", 0.85f);

    static readonly Vector4 Earth = Rgb(0.4f, 0.3f, 0.2f), Ridge = Rgb(0.5f, 0.38f, 0.26f);

    /// <summary>Ploughed ground: ridges along x, <paramref name="rows"/> of them at <paramref name="pitch"/>, faceted, fading to the table at the border.</summary>
    static Func<float, float, float> Furrows(Quill soil, float half, int rows, float pitch, float depth, int seed)
    {
        float Height(float x, float z)
        {
            var t = (z + rows * pitch / 2) / pitch; var dist = half - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var ridge = MathF.Pow(MathF.Sin(MathF.PI * (t - MathF.Floor(t))), 2);
            return (depth * 0.45f + depth * ridge + depth * 0.35f * (Noise(x * 5, z * 5, 0, seed) - 0.5f)) * Smooth(0, 0.15f, dist);
        }
        Foliage.Rect(soil, half * 2, half * 2, 26, rows * 3, Height,
            (p, n) => Shade(Mix(Earth, Ridge, Smooth(depth * 0.6f, depth * 1.3f, p.Y)), 0.78f + 0.35f * Noise(p.X * 9, p.Z * 9, p.Y * 20, seed + 1)), seed, 0.3f);
        return Height;
    }

    public static Model Tall()
    {
        var model = new Model("tall-crops", round: false, new Vector3(6, 1, 6));
        var rng = new Random(321);
        var one = model.Inches;
        Quill P(Material m) => Quill.Of(model, m, one);
        var (soil, stalk, blade, grass, stone) = (P(Soil), P(Stalk), P(Blade), P(Grass), P(Stone));
        const int rows = 11; const float pitch = 5.6f / rows;
        var height = Furrows(soil, 3, rows, pitch, 0.045f, 321);
        var green = Rgb(0.32f, 0.48f, 0.17f); var deep = Rgb(0.2f, 0.34f, 0.12f); var dry = Rgb(0.64f, 0.58f, 0.3f); var tassel = Rgb(0.86f, 0.76f, 0.46f);
        var husk = Rgb(0.5f, 0.58f, 0.26f); var silk = Rgb(0.55f, 0.32f, 0.15f);
        for (var row = 0; row < rows; row++)
        {
            var z = -2.8f + (row + 0.5f) * pitch;
            for (var x = -2.72f; x <= 2.73f; x += 0.44f + 0.06f * (float)rng.NextDouble())
            {
                var foot = new Vector3(x, 0, z + 0.06f * ((float)rng.NextDouble() - 0.5f));
                foot.Y = height(foot.X, foot.Z) - 0.01f;
                var h = 0.9f + 0.09f * (float)rng.NextDouble();
                var ripe = (float)rng.NextDouble() * 0.45f;
                var lean = new Vector3(0.05f * ((float)rng.NextDouble() - 0.5f), 0, 0.05f * ((float)rng.NextDouble() - 0.5f));
                var top = foot + lean + Vector3.UnitY * (h - foot.Y - 0.1f);
                var stalkColour = Mix(Mix(green, dry, ripe * 1.4f), deep, 0.45f);
                Foliage.Tube(stalk, [foot, Vector3.Lerp(foot, top, 0.5f), top], [0.026f, 0.02f, 0.012f], 4,
                    p => Shade(stalkColour, 0.85f + 0.3f * (p.Y - foot.Y)), (int)(x * 100) + row, 0.1f);
                // Leaves: long blades leaving the stalk at its joints and arching over, alternating sides, the lowest drying.
                var turn = (float)rng.NextDouble() * 6.28f;
                for (var l = 0; l < 5; l++)
                {
                    var u = 0.14f + 0.15f * l;
                    var at = Vector3.Lerp(foot, top, u);
                    var a = turn + l * MathF.PI + ((float)rng.NextDouble() - 0.5f) * 0.9f;
                    var o = Foliage.Flat(a);
                    var length = (0.5f - 0.04f * l) * (0.85f + 0.3f * (float)rng.NextDouble());
                    var c = Shade(Mix(green, dry, ripe + (l < 2 ? 0.45f : 0) + 0.04f * l), 0.85f + 0.25f * (float)rng.NextDouble());
                    Foliage.Blade(blade, at, Vector3.Normalize(o + Vector3.UnitY * 0.55f), length, 0.065f, 0.06f, 0.22f + 0.1f * (float)rng.NextDouble(), Shade(c, 0.8f), c, 4, 0.5f);
                }
                // The cob in its husk against the stalk, silks at its tip, and the tassel's spikes at the top.
                var cob = Vector3.Lerp(foot, top, 0.55f); var away = Foliage.Flat(turn + 1.6f);
                var cobTip = cob + away * 0.06f + Vector3.UnitY * 0.12f;
                Foliage.Tube(stalk, [cob + away * 0.02f, cob + away * 0.05f + Vector3.UnitY * 0.08f, cobTip], [0.016f, 0.022f, 0.0f], 4, p => husk, row * 31 + (int)(x * 10), 0.15f);
                Foliage.Tuft(blade, rng, cobTip - Vector3.UnitY * 0.01f, 3, 0.05f, 0.012f, 0.6f, silk, Shade(silk, 1.2f));
                Foliage.Tube(stalk, [top, top + lean * 0.4f + Vector3.UnitY * (h - top.Y)], [0.006f, 0], 3, _ => tassel, row, 0.05f);
                for (var s = 0; s < 5; s++)
                {
                    var o = Foliage.Flat(s * 1.26f + x * 3);
                    var from = top + Vector3.UnitY * (h - top.Y) * 0.45f;
                    Foliage.Blade(blade, from, Vector3.Normalize(o + Vector3.UnitY * 0.4f), 0.08f, 0.012f, 0.01f, 0.04f, tassel, Shade(tassel, 1.1f), 1);
                }
            }
            // Weeds and clods in the furrow beside the row.
            for (var k = 0; k < 12; k++)
            {
                var p = new Vector3(-2.75f + 5.5f * (float)rng.NextDouble(), 0, z + pitch * 0.5f + 0.08f * ((float)rng.NextDouble() - 0.5f));
                if (MathF.Abs(p.Z) > 2.85f) continue;
                p.Y = height(p.X, p.Z);
                if (k % 3 == 0)
                {
                    var s = 0.03f + 0.03f * (float)rng.NextDouble();
                    Foliage.Stone(stone, p, new Vector3(s * 1.3f, s * 0.7f, s), 330 + row * 12 + k, (float)rng.NextDouble() * 3, (q, n) => Shade(Ridge, 0.8f + 0.3f * n.Y));
                }
                else Foliage.Tuft(grass, rng, p - Vector3.UnitY * 0.005f, 5, 0.1f + 0.08f * (float)rng.NextDouble(), 0.015f, 0.6f, Rgb(0.28f, 0.38f, 0.15f), Rgb(0.52f, 0.58f, 0.26f));
            }
        }
        return model;
    }

    public static Model Plantation()
    {
        var model = new Model("plantation", round: false, new Vector3(6, 6, 6));
        var rng = new Random(341);
        var one = model.Inches;
        Quill P(Material m) => Quill.Of(model, m, one);
        var (soil, bark, crown, fruit, grass, stone) = (P(Soil), P(Bark), P(Crown), P(Fruit), P(Grass), P(Stone));
        const int rows = 3, cols = 3; const float pitch = 1.9f;
        var tilled = Furrows(soil, 3, 9, 5.6f / 9, 0.05f, 341);
        var confine = Foliage.Within(2.95f, 2.95f, 5.95f);
        var turf = Rgb(0.34f, 0.46f, 0.2f); var turfDry = Rgb(0.5f, 0.5f, 0.28f);
        var barkA = Rgb(0.36f, 0.28f, 0.2f); var barkB = Rgb(0.46f, 0.41f, 0.34f); var lime = Rgb(0.86f, 0.85f, 0.79f);
        Vector4[] greens = [Rgb(0.22f, 0.42f, 0.14f), Rgb(0.28f, 0.48f, 0.16f), Rgb(0.19f, 0.36f, 0.13f), Rgb(0.32f, 0.5f, 0.18f)];
        var yellow = Rgb(0.55f, 0.55f, 0.2f); var dark = Rgb(0.12f, 0.24f, 0.09f);
        Vector4[] ripe = [Rgb(0.95f, 0.55f, 0.12f), Rgb(0.9f, 0.45f, 0.1f), Rgb(0.98f, 0.66f, 0.18f), Rgb(0.78f, 0.62f, 0.15f)];
        for (var row = 0; row < rows; row++)
        {
            var z = (row - 1) * pitch;
            // A grass strip under the row: a low turf bank, faceted, thick with tufts.
            float Bank(float x, float zz) => 0.07f + 0.02f * Noise(x * 4, zz * 4, 0, 342 + row);
            Foliage.Field(grass, 4, 41, (i, j) =>
            {
                var x = -2.82f + 5.64f * j / 40; var w = i is 0 or 3 ? 0.36f : 0.28f;
                var zz = z + (i < 2 ? -w : w) * (i is 1 or 2 ? 0.7f + 0.15f * Foliage.Jitter(i, j, 343) : 1);
                return new Vector3(x, i is 0 or 3 ? tilled(x, zz) * 0.8f : Bank(x, zz), zz);
            }, (p, n) => Shade(Mix(turf, turfDry, Noise(p.X * 3, p.Z * 3, 0, 344) * 0.6f), 0.8f + 0.3f * Noise(p.X * 12, p.Z * 12, 1, 345)));
            for (var i = 0; i < 70; i++)
            {
                var root = new Vector3(-2.75f + 5.5f * (float)rng.NextDouble(), 0, z + 0.5f * ((float)rng.NextDouble() - 0.5f));
                root.Y = Bank(root.X, root.Z) - 0.01f;
                Foliage.Tuft(grass, rng, root, 6, 0.14f + 0.1f * (float)rng.NextDouble(), 0.03f, 0.5f, Rgb(0.26f, 0.38f, 0.15f), Mix(Rgb(0.5f, 0.62f, 0.26f), Rgb(0.68f, 0.64f, 0.36f), (float)rng.NextDouble()));
            }
            for (var col = 0; col < cols; col++)
            {
                var k = row * cols + col;
                var foot = new Vector3((col - 1) * pitch + 0.08f * ((float)rng.NextDouble() - 0.5f), 0.06f, z);
                Func<Vector3, Vector4> paint = p => p.Y is > 0.16f and < 0.75f ? Shade(lime, 0.9f + 0.15f * Noise(p.X * 20, p.Y * 20, p.Z * 20, k))
                    : Shade(Mix(barkA, barkB, Fbm(p.X * 4, p.Y * 6, p.Z * 4, k)), 0.8f + 0.3f * Noise(p.X * 16, p.Y * 8, p.Z * 16, k + 1));
                var centreY = foot.Y + 3.0f; var head = new Vector3(0.8f, 0.85f, 0.8f);
                Foliage.Broadleaf(bark, crown, rng, foot, 4.4f, 0.12f, paint, (p, r) =>
                {
                    var c = Foliage.LeafColour(r, greens, yellow, dark);
                    return Shade(c, 0.8f + 0.25f * Smooth(centreY - 1, centreY + 1, p.Y));
                }, p => { var q = confine(p); var d = q - (foot with { Y = centreY }); var e = (d / head).Length(); return e > 1 ? foot with { Y = centreY } + d / e : q; },
                    seed: 350 + k * 13, depth: 2, clump: 0.36f, leaves: 10, leafSize: 0.15f, spread: 0.6f, reach: 0.12f, bole: 0.45f);
                Foliage.Clump(crown, rng, foot with { Y = centreY }, head, 480, 0.15f, (p, r) => Shade(Foliage.LeafColour(r, greens, yellow, dark), 0.8f + 0.25f * Smooth(centreY - 1, centreY + 1, p.Y)), single: true);
                // Fruit hanging just inside the crown's skin.
                for (var f = 0; f < 10; f++)
                {
                    var a = (float)rng.NextDouble() * 6.28f; var e = ((float)rng.NextDouble() - 0.35f) * 1.3f;
                    var dir = new Vector3(MathF.Cos(a) * MathF.Cos(e), MathF.Sin(e), MathF.Sin(a) * MathF.Cos(e));
                    var at = foot with { Y = centreY } + dir * head * 0.92f;
                    var c = ripe[(k + f) % ripe.Length];
                    Foliage.Stone(fruit, at, new Vector3(0.065f), 380 + k * 20 + f, a, (_, n) => Shade(c, 0.85f + 0.2f * n.Y), 0, 0.08f);
                }
                // Windfall and a few stones round the foot.
                for (var f = 0; f < 3; f++)
                {
                    var at = foot + Foliage.Flat((float)rng.NextDouble() * 6.28f) * (0.25f + 0.4f * (float)rng.NextDouble());
                    at.Y = Bank(at.X, at.Z) + 0.03f;
                    Foliage.Stone(fruit, at, new Vector3(0.06f, 0.05f, 0.06f), 400 + k * 3 + f, 0, (_, n) => Shade(ripe[f], 0.75f + 0.2f * n.Y), 0, 0.1f);
                }
            }
        }
        for (var s = 0; s < 18; s++)
        {
            var p = new Vector3(-2.7f + 5.4f * (float)rng.NextDouble(), 0, -2.7f + 5.4f * (float)rng.NextDouble());
            p.Y = tilled(p.X, p.Z);
            var size = 0.04f + 0.05f * (float)rng.NextDouble();
            Foliage.Stone(stone, p, new Vector3(size * 1.2f, size * 0.7f, size), 420 + s, (float)rng.NextDouble() * 3, (q, n) => Shade(Rgb(0.52f, 0.48f, 0.42f), 0.75f + 0.35f * n.Y));
        }
        return model;
    }
}
