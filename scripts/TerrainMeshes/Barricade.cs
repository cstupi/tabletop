using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The barricade (3" × 1", 1" high, Solid): a sandbag wall along x on a strip of churned dirt, each bag a faceted cloth
/// pillow with tied ears, laid in staggered courses and held by timber stakes; a steel tank trap of three crossed, rusting
/// I-beams at its end standing the full 1"; a coil of barbed wire along the top.</summary>
public static class Barricade
{
    public static readonly Material Dirt = new("dirt", 0.95f), Bag = new("sandbag", 0.95f), Steel = new("steel", 0.5f, Metallic: 0.5f), Timber = new("timber", 0.85f);

    public static Model Build()
    {
        var model = new Model("barricade", round: false, new Vector3(3, 1, 1));
        var rng = new Random(461);
        float R() => (float)rng.NextDouble();
        Shapes.Ground(model, Dirt, 30, 10, (x, z) => 0.04f + 0.025f * Noise(x * 5, z * 5, 0, 461) + 0.015f * Noise(x * 13, z * 13, 1, 462),
            (p, n) => Shade(Rgb(0.42f, 0.35f, 0.25f), 0.8f + 0.25f * Fbm(p.X * 6, p.Z * 6, 1, 462)));
        for (var k = 0; k < 18; k++)   // clods and stones
        {
            var s = 0.03f + 0.04f * R();
            Masonry.Block(model[Dirt], new(-1.45f + 2.9f * R(), 0.05f, -0.45f + 0.9f * R()), new(s * 1.3f, s * 0.8f, s), Masonry.Turn(R() * 6, R() - 0.5f, R() - 0.5f), s * 0.25f, Shade(Rgb(0.45f, 0.39f, 0.3f), 0.75f + 0.3f * R()), rng, 0.3f);
        }
        var khaki = Rgb(0.66f, 0.6f, 0.44f); var drab = Rgb(0.52f, 0.5f, 0.36f);
        var courses = new[] { (0.05f, new[] { -0.18f, 0.18f }, 0f), (0.17f, new[] { -0.17f, 0.17f }, 0.2f), (0.29f, new[] { -0.12f, 0.14f }, 0f), (0.41f, new[] { 0.01f }, 0.2f), (0.53f, new[] { 0f }, 0f) };
        foreach (var (y, rows, offset) in courses)
            foreach (var z in rows)
                for (var x = -1.44f + offset; x < 0.36f; x += 0.42f)
                {
                    var c = Shade(Mix(khaki, drab, R()), 0.82f + 0.22f * R());
                    Sandbag(model, rng, new Vector3(x + 0.2f, y, z + 0.03f * (R() - 0.5f)), 0.08f * (R() - 0.5f), c);
                }
        // Timber stakes driven in along the front, holding the bags.
        foreach (var x in new[] { -1.3f, -0.7f, -0.1f })
        {
            var wood = Shade(Rgb(0.42f, 0.32f, 0.21f), 0.85f + 0.2f * R());
            Masonry.Block(model[Timber], new(x, 0.3f, 0.36f), new(0.07f, 0.6f, 0.07f), Masonry.Turn(0, 0.08f, (R() - 0.5f) * 0.1f), 0.012f, p => Shade(wood, 0.85f + 0.2f * Noise(p.Y * 20, p.X * 9, 0, 3)));
        }
        // The tank trap: three I-beams crossed at the middle, splayed so their ends rest on the ground; the clamp trims them flat.
        var centre = new Vector3(1.0f, 0.5f, 0);
        var rust = Rgb(0.45f, 0.26f, 0.14f); var grey = Rgb(0.36f, 0.36f, 0.37f);
        Vector4 Metal(Vector3 p) => Mix(grey, rust, Smooth(0.38f, 0.65f, Fbm(p * 7, 463)));
        foreach (var (axis, spin) in new[] { (Vector3.Normalize(new Vector3(1, 1.2f, 0.4f)), 0.3f), (Vector3.Normalize(new Vector3(-0.6f, 1.2f, 1)), 1.2f), (Vector3.Normalize(new Vector3(-0.2f, 1.2f, -1)), 2.1f) })
        {
            var a = centre - axis * (0.56f / axis.Y); var b = centre + axis * (0.56f / axis.Y);
            Masonry.Girder(model[Steel], a, b, 0.13f, 0.13f, Metal, spin);
        }
        Masonry.Block(model[Steel], centre, new(0.2f, 0.2f, 0.2f), Masonry.Turn(0.4f, 0.3f, 0.2f), 0.02f, Metal);   // the welded plate at the cross
        // A coil of barbed wire along the top of the bags.
        var wire = new List<Vector3>();
        for (var t = 0f; t < 1; t += 1 / 220f)
        {
            var x = -1.35f + 1.6f * t; var a = t * 26 * MathF.PI;
            wire.Add(new Vector3(x + 0.03f * MathF.Sin(a), 0.75f + 0.09f * MathF.Sin(a), 0.09f * MathF.Cos(a)));
        }
        Shapes.Chain(model[Steel], wire, 0.008f, 0.008f, 3, Rgb(0.42f, 0.41f, 0.4f));
        for (var k = 0; k < wire.Count; k += 7)
            Masonry.Block(model[Steel], wire[k], new(0.04f, 0.008f, 0.008f), Masonry.Turn(R() * 3, R() * 3, 0), 0, Rgb(0.4f, 0.39f, 0.37f));
        return model;
    }

    /// <summary>A sandbag lying along x at <paramref name="at"/> (its underside): a flat-shaded pillow, fuller in the middle, its
    /// ends gathered into tied ears, with a seam round its waist.</summary>
    static void Sandbag(Model model, Random rng, Vector3 at, float yaw, Vector4 colour)
    {
        var bag = model[Bag];
        var turn = Matrix4x4.CreateRotationY(yaw);
        Shapes.Blob(bag, at + Vector3.UnitY * 0.06f, new Vector3(0.2f, 0.07f, 0.14f), 2, 0.07f, rng.Next(9999), (q, n) => Shade(colour, 0.86f + 0.16f * n.Y + 0.08f * Noise(q.X * 30, q.Y * 30, q.Z * 30, 5)),
            smooth: true, yaw: yaw, shape: q =>
            {
                var local = Vector3.Transform(q - at, Matrix4x4.CreateRotationY(-yaw));
                var end = MathF.Abs(local.X) / 0.2f;
                var pinch = 1 - 0.35f * Smooth(0.6f, 1, end);   // the ends gathered in
                local = new Vector3(local.X, (local.Y - 0.06f) * pinch + 0.06f, local.Z * pinch);
                local.Y = MathF.Max(local.Y, 0.005f);
                return at + Vector3.Transform(local, turn);
            });
        foreach (var s in new[] { -1f, 1f })
        {
            var ear = at + Vector3.Transform(new Vector3(s * 0.215f, 0.06f, 0), turn);
            Masonry.Block(bag, ear, new(0.05f, 0.05f, 0.07f), Matrix4x4.CreateRotationZ(s * 0.4f) * turn, 0.012f, Shade(colour, 0.85f), rng, 0.25f);
        }
    }
}
