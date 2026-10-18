using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The barricade (3" × 1", 1" high, Solid): a sandbag wall along x, bags laid in staggered courses on a dirt strip, and a
/// steel tank trap of three crossed girders at its end standing the full 1".</summary>
public static class Barricade
{
    public static readonly Material Dirt = new("dirt", 0.95f), Bag = new("sandbag", 0.95f), Steel = new("steel", 0.5f, Metallic: 0.7f);

    public static Model Build()
    {
        var model = new Model("barricade", round: false, new Vector3(3, 1, 1));
        var rng = new Random(461);
        Shapes.Ground(model, Dirt, 24, 8, (x, z) => 0.04f + 0.02f * Noise(x * 4, z * 4, 0, 461), (p, n) => Shade(Rgb(0.42f, 0.35f, 0.25f), 0.85f + 0.2f * Fbm(p.X * 5, p.Z * 5, 1, 462)));
        // Sandbags: pillow-shaped, 0.42" × 0.24" × 0.13", two rows deep at the bottom, one on top, each course offset.
        var khaki = Rgb(0.66f, 0.6f, 0.44f); var drab = Rgb(0.55f, 0.52f, 0.38f);
        var courses = new[] { (0.04f, new[] { -0.17f, 0.17f }, 0f), (0.16f, new[] { -0.17f, 0.17f }, 0.21f), (0.28f, new[] { -0.12f, 0.15f }, 0f), (0.4f, new[] { 0f }, 0.21f), (0.52f, new[] { 0f }, 0f) };
        var k = 0;
        foreach (var (y, rows, offset) in courses)
            foreach (var z in rows)
                for (var x = -1.42f + offset; x < 0.38f; x += 0.43f, k++)
                {
                    var c = Shade(Mix(khaki, drab, (float)rng.NextDouble()), 0.85f + 0.2f * (float)rng.NextDouble());
                    var at = new Vector3(x + 0.2f, y + 0.07f, z + 0.02f * ((float)rng.NextDouble() - 0.5f));
                    Shapes.Blob(model[Bag], at, new Vector3(0.22f, 0.075f, 0.14f), 1, 0.06f, 470 + k, (q, n) => Shade(c, 0.85f + 0.2f * n.Y),
                        smooth: true, yaw: 0.06f * ((float)rng.NextDouble() - 0.5f), shape: q => q with { Y = MathF.Max(q.Y, y + 0.005f) });
                    // The tied-off end, a darker crease.
                    Shapes.Box(model[Bag], Shapes.At(at + new Vector3(0.19f, 0.01f, 0), new Vector3(0.03f, 0.1f, 0.2f)), Shade(c, 0.7f));
                }
        // The tank trap: three girders crossed at the middle, splayed so each pair of ends rests on the ground.
        var centre = new Vector3(1.05f, 0.5f, 0);
        var rust = Rgb(0.4f, 0.27f, 0.18f); var grey = Rgb(0.32f, 0.32f, 0.33f);
        foreach (var axis in new[] { Vector3.Normalize(new Vector3(1, 1.2f, 0.4f)), Vector3.Normalize(new Vector3(-0.6f, 1.2f, 1)), Vector3.Normalize(new Vector3(-0.2f, 1.2f, -1)) })
        {
            var a = centre - axis * (0.56f / axis.Y); var b = centre + axis * (0.56f / axis.Y);   // the clamp cuts the ends flat on the ground and at the top
            Girder(model, a, b, p => Mix(grey, rust, Smooth(0.4f, 0.7f, Fbm(p * 6, 463))));
        }
        return model;
    }

    /// <summary>An I-section girder from a to b: a web and two flanges.</summary>
    static void Girder(Model model, Vector3 a, Vector3 b, Func<Vector3, Vector4> colour)
    {
        var d = b - a; var len = d.Length(); var y = d / len;
        var x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, y) + new Vector3(0.001f, 0, 0)); var z = Vector3.Cross(x, y);
        void Plate(Vector3 offset, Vector3 size)
        {
            var m = new Matrix4x4(x.X * size.X, x.Y * size.X, x.Z * size.X, 0, d.X, d.Y, d.Z, 0, z.X * size.Z, z.Y * size.Z, z.Z * size.Z, 0, 0, 0, 0, 1)
                    * Matrix4x4.CreateTranslation((a + b) / 2 + x * offset.X + z * offset.Z);
            Shapes.Box(model[Steel], m, colour);
        }
        Plate(new Vector3(0.06f, 0, 0), new Vector3(0.025f, 1, 0.14f));
        Plate(new Vector3(-0.06f, 0, 0), new Vector3(0.025f, 1, 0.14f));
        Plate(Vector3.Zero, new Vector3(0.12f, 1, 0.025f));
    }
}
