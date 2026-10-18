using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Cultivated pieces, authored in inches. <see cref="Tall"/> (6" × 6", 1" high): a tall crop, maize in rows along x, each
/// stalk standing the full height with long arching leaves, a cob and a pale tassel. <see cref="Plantation"/> (6" × 6", 6" high):
/// an orchard in a regular grid, round-crowned fruit trees of one size on whitewashed trunks, grass strips under each row and
/// tilled soil between.</summary>
public static class Crops
{
    public static readonly Material Soil = new("soil", 0.95f), Stalk = new("stalk", 0.8f), Blade = new("crop-leaf", 0.8f, DoubleSided: true),
        Bark = new("bark", 0.9f), Crown = new("crown", 0.8f), Fruit = new("fruit", 0.5f), Grass = new("grass", 0.9f, DoubleSided: true);

    static void Furrows(Model model, int rows, float pitch, float depth, int seed)
    {
        var soil = Rgb(0.4f, 0.3f, 0.2f); var ridge = Rgb(0.5f, 0.38f, 0.26f);
        var half = model.Inches.X / 2;
        float Height(float x, float z)
        {
            var t = (z + half - 0.2f) / pitch; var dist = half - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            return (depth * 0.4f + depth * MathF.Pow(MathF.Sin(MathF.PI * (t - MathF.Floor(t) + 0.5f)), 2)) * Smooth(0, 0.12f, dist);
        }
        Shapes.Ground(model, Soil, 24, rows * 6, Height, (p, n) => Shade(Mix(soil, ridge, Smooth(depth * 0.5f, depth * 1.3f, p.Y)), 0.85f + 0.25f * Fbm(p.X * 3, p.Z * 3, 0, seed)));
    }

    public static Model Tall()
    {
        var model = new Model("tall-crops", round: false, new Vector3(6, 1, 6));
        var rng = new Random(321);
        const int rows = 11; const float pitch = 5.6f / rows;
        Furrows(model, rows, pitch, 0.04f, 321);
        var green = Rgb(0.3f, 0.5f, 0.16f); var dry = Rgb(0.62f, 0.6f, 0.3f); var tassel = Rgb(0.88f, 0.8f, 0.5f); var husk = Rgb(0.7f, 0.72f, 0.36f);
        for (var row = 0; row < rows; row++)
        {
            var z = -2.8f + (row + 0.5f) * pitch;
            for (var x = -2.75f; x <= 2.76f; x += 0.4f)
            {
                var root = new Vector3(x + 0.08f * ((float)rng.NextDouble() - 0.5f), 0.04f, z + 0.06f * ((float)rng.NextDouble() - 0.5f));
                var h = 0.88f + 0.12f * (float)rng.NextDouble();
                var ripe = (float)rng.NextDouble() * 0.5f;
                var top = root + new Vector3(0.05f * ((float)rng.NextDouble() - 0.5f), h - root.Y - 0.08f, 0.05f * ((float)rng.NextDouble() - 0.5f));
                Shapes.Limb(model[Stalk], root, top, 0.03f, 0.6f, 4, p => Mix(Shade(green, 0.8f), Mix(green, dry, ripe), (p.Y - 0.04f) / h));
                // Leaves: long blades leaving the stalk and arching down, alternating sides.
                for (var l = 0; l < 4; l++)
                {
                    var y = 0.22f + 0.15f * l; var at = Vector3.Lerp(root, top, y / h);
                    var a = l * 2.4f + (float)rng.NextDouble() * 0.8f; var o = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)); var side = new Vector3(-o.Z, 0, o.X);
                    var length = 0.42f - 0.05f * l;
                    var mid = at + o * length * 0.5f + Vector3.UnitY * 0.1f; var tip = at + o * length + Vector3.UnitY * -0.04f;
                    var c = Shade(Mix(green, dry, ripe + 0.1f * l), 0.85f + 0.25f * (float)rng.NextDouble());
                    model[Blade].Tri(at - side * 0.02f, mid - side * 0.035f, mid + side * 0.035f, c);
                    model[Blade].Tri(at - side * 0.02f, mid + side * 0.035f, at + side * 0.02f, c);
                    model[Blade].Tri(mid - side * 0.035f, tip, mid + side * 0.035f, Shade(c, 1.08f));
                }
                // The cob, against the stalk, and the tassel's spikes at the top.
                var cob = Vector3.Lerp(root, top, 0.55f); var away = new Vector3(MathF.Cos(x * 7), 0, MathF.Sin(x * 7));
                Shapes.Limb(model[Stalk], cob + away * 0.03f, cob + away * 0.07f + Vector3.UnitY * 0.16f, 0.035f, 0.5f, 5, husk);
                for (var s = 0; s < 4; s++)
                {
                    var a = s * 1.57f + x; var o = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                    model[Blade].Tri(top - new Vector3(-o.Z, 0, o.X) * 0.012f, top + new Vector3(-o.Z, 0, o.X) * 0.012f, top + o * 0.06f + Vector3.UnitY * (h - top.Y), tassel);
                }
            }
        }
        return model;
    }

    public static Model Plantation()
    {
        var model = new Model("plantation", round: false, new Vector3(6, 6, 6));
        var rng = new Random(341);
        const int rows = 3, cols = 3; const float pitch = 1.9f;
        Furrows(model, 9, 5.6f / 9, 0.05f, 341);
        var turf = Rgb(0.36f, 0.5f, 0.2f); var bark = Rgb(0.4f, 0.31f, 0.22f); var lime = Rgb(0.88f, 0.86f, 0.8f);
        var leaf = Rgb(0.24f, 0.46f, 0.16f); var orange = Rgb(0.95f, 0.55f, 0.12f);
        for (var row = 0; row < rows; row++)
        {
            var z = (row - 1) * pitch;
            // A grass strip under the row, the length of the piece.
            for (var i = 0; i < 40; i++)
            {
                var root = new Vector3(-2.7f + 5.4f * i / 39, 0.06f, z + 0.3f * ((float)rng.NextDouble() - 0.5f));
                Shapes.Tuft(model[Grass], rng, root, 5, 0.18f, 0.04f, 0.6f, Rgb(0.28f, 0.4f, 0.16f), Rgb(0.5f, 0.62f, 0.28f));
            }
            Shapes.Box(model[Soil], new Vector3(-2.8f, 0, z - 0.32f), new Vector3(2.8f, 0.07f, z + 0.32f), p => Shade(turf, 0.85f + 0.25f * Fbm(p.X * 3, 0, p.Z * 3, 342)));
            for (var col = 0; col < cols; col++)
            {
                var foot = new Vector3((col - 1) * pitch, 0.07f, z);
                var k = row * cols + col;
                // The trunk, whitewashed to knee height, forking into three limbs under the crown.
                Shapes.Limb(model[Bark], foot, foot + Vector3.UnitY * 1.9f, 0.14f, 0.7f, 8, p => p.Y < 0.6f ? lime : bark);
                for (var b = 0; b < 3; b++)
                {
                    var o = new Vector3(MathF.Cos(b * 2.1f + k), 0, MathF.Sin(b * 2.1f + k));
                    Shapes.Limb(model[Bark], foot + Vector3.UnitY * 1.8f, foot + Vector3.UnitY * 2.7f + o * 0.45f, 0.07f, 0.6f, 5, bark);
                }
                // The crown: one trimmed round head with a few lobes, and fruit on it.
                var centre = foot + Vector3.UnitY * 3.4f;
                Shapes.Blob(model[Crown], centre, new Vector3(0.68f, 0.85f, 0.68f), 2, 0.12f, 350 + k,
                    (q, n) => Shade(leaf, 0.65f + 0.35f * Fbm(q * 9, k) + 0.25f * MathF.Max(n.Y, 0)), frequency: 3);
                for (var l = 0; l < 4; l++)
                {
                    var o = new Vector3(MathF.Cos(l * 1.6f + k), 0.3f * (l % 2), MathF.Sin(l * 1.6f + k));
                    Shapes.Blob(model[Crown], centre + o * 0.42f + Vector3.UnitY * 0.15f, new Vector3(0.4f, 0.45f, 0.4f), 1, 0.15f, 360 + k * 4 + l,
                        (q, n) => Shade(leaf, 0.7f + 0.3f * Fbm(q * 9, k + l) + 0.25f * MathF.Max(n.Y, 0)));
                }
                for (var f = 0; f < 14; f++)
                {
                    var a = (float)rng.NextDouble() * 6.3f; var e = ((float)rng.NextDouble() - 0.3f) * 1.2f;
                    var dir = new Vector3(MathF.Cos(a) * MathF.Cos(e), MathF.Sin(e), MathF.Sin(a) * MathF.Cos(e));
                    Shapes.Blob(model[Fruit], centre + dir * new Vector3(0.72f, 0.88f, 0.72f), new Vector3(0.09f), 0, 0.05f, 380 + f, (_, n) => Shade(orange, 0.85f + 0.2f * n.Y));
                }
            }
        }
        return model;
    }
}
