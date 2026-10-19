using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The mine field (6" × 6", 0.1" high), everything within its 0.1": churned, furrowed dirt strewn with clods and stones,
/// scorched blast craters with thrown-up rims, rows of disc mines (a casing with a chamfered rim, a pressure cap and a
/// carrying handle; a third sunk and tilted in mounded earth), and a perimeter of stakes strung with barbed wire, a red warning
/// plate on every other stake.</summary>
public static class Mines
{
    public static readonly Material Dirt = new("churned-dirt", 0.95f), Mine = new("mine", 0.55f, Metallic: 0.4f), Wire = new("wire", 0.5f, Metallic: 0.7f),
        Sign = new("warning", 0.6f), Stake = new("stake", 0.85f);
    const float Size = 6, Top = 0.1f;

    public static Model Build()
    {
        var model = new Model("mine-field", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(201);
        float R() => (float)rng.NextDouble();
        var pits = new[] { (new Vector2(-1.2f, 0.9f), 0.42f), (new Vector2(1.3f, -0.6f), 0.36f), (new Vector2(0.2f, 2.0f), 0.3f), (new Vector2(-0.6f, -1.7f), 0.4f), (new Vector2(2.1f, 1.5f), 0.25f) };
        float Height(float x, float z)
        {
            var edge = 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            // Furrows across the field, broken up, and churned lumps over them.
            var furrow = 0.008f * MathF.Sin(z * 9 + 2 * Noise(x * 1.5f, z * 1.5f, 3, 205)) * Smooth(0.3f, 0.7f, Noise(x * 0.8f, z * 0.8f, 4, 206));
            var y = 0.03f + 0.018f * Fbm(x * 2.5f, z * 2.5f, 0, 201) + 0.012f * Noise(x * 11, z * 11, 1, 202) + furrow;
            foreach (var (p, size) in pits)
            {
                var r = Vector2.Distance(new(x, z), p) / size;
                if (r < 1.7f) y += r < 1 ? -0.026f * (1 - r * r) : 0.022f * MathF.Sin((r - 1) / 0.7f * MathF.PI) * (0.7f + 0.6f * Noise(x * 8, z * 8, 2, 207));
            }
            return Math.Clamp(y, 0.003f, 0.08f) * Smooth(0, 0.12f, edge);
        }
        var soil = Rgb(0.42f, 0.33f, 0.23f); var dark = Rgb(0.27f, 0.21f, 0.15f); var burnt = Rgb(0.1f, 0.09f, 0.08f); var ash = Rgb(0.32f, 0.3f, 0.28f);
        float Scorch(float x, float z) => pits.Min(q => Vector2.Distance(new(x, z), q.Item1) / (q.Item2 * 1.6f));
        Shapes.Ground(model, Dirt, 52, 52, Height, (p, n) =>
        {
            var c = Mix(Shade(soil, 0.82f + 0.3f * Fbm(p.X * 3, p.Z * 3, 2, 203)), dark, Smooth(0.42f, 0.62f, Noise(p.X * 7, p.Z * 7, 3, 204)) * 0.7f);
            var s = Scorch(p.X, p.Z);
            return Mix(Mix(c, ash, Smooth(1.2f, 0.8f, s) * 0.4f), burnt, Smooth(0.75f, 0.3f, s));
        });
        // Clods and stones thrown over the field, thickest round the craters.
        for (var k = 0; k < 85; k++)
        {
            var (pit, size) = pits[k % pits.Length];
            var near = k % 3 != 0;
            var a = R() * MathF.Tau; var d = near ? size * (0.9f + 1.2f * R()) : 3;
            var p = near ? pit + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d : new Vector2(-2.8f + 5.6f * R(), -2.8f + 5.6f * R());
            if (MathF.Abs(p.X) > 2.85f || MathF.Abs(p.Y) > 2.85f || Height(p.X, p.Y) > 0.055f) continue;
            var s = 0.035f + 0.05f * R();
            var stone = R() < 0.3f;
            var c = stone ? Shade(Rgb(0.5f, 0.48f, 0.44f), 0.75f + 0.3f * R()) : Shade(Mix(soil, dark, R()), 0.8f + 0.25f * R());
            Masonry.Block(model[Dirt], new(p.X, Height(p.X, p.Y) + 0.008f, p.Y), new(s * 1.3f, 0.035f, s), Masonry.Turn(R() * 6, (R() - 0.5f) * 0.15f, (R() - 0.5f) * 0.15f), 0.008f, c, rng, 0.08f, corners: false);
        }
        // Mines in staggered rows.
        var olive = Rgb(0.36f, 0.39f, 0.24f); var grey = Rgb(0.4f, 0.41f, 0.38f); var capCol = Rgb(0.2f, 0.21f, 0.16f);
        for (var row = 0; row < 6; row++)
            for (var col = 0; col < 6; col++)
            {
                var x = -2.35f + col * 0.92f + (row % 2) * 0.46f + 0.15f * (R() - 0.5f);
                var z = -2.3f + row * 0.92f + 0.15f * (R() - 0.5f);
                if (x > 2.45f || pits.Any(p => Vector2.Distance(new(x, z), p.Item1) < p.Item2 + 0.2f)) continue;
                var sunk = R() < 0.35f;
                var body = R() < 0.3f ? grey : olive;
                var y = Height(x, z) - (sunk ? 0.02f : 0.003f);
                var tilt = sunk ? 0.3f * (R() - 0.5f) : 0;
                var spin = R() * 3;
                var m = Matrix4x4.CreateFromYawPitchRoll(spin, tilt, tilt * 0.5f) * Matrix4x4.CreateTranslation(x, y, z);
                Vector4 Paintwork(Vector3 p) => Shade(body, 0.85f + 0.2f * Noise(p.X * 25, p.Z * 25, 0, 205));
                Shapes.Prism(model[Mine], Matrix4x4.CreateScale(0.18f, 0.012f, 0.18f) * m, 14, 1, Paintwork);                                   // the base
                Shapes.Prism(model[Mine], Matrix4x4.CreateScale(0.18f, 0.008f, 0.18f) * Matrix4x4.CreateTranslation(0, 0.012f, 0) * m, 14, 0.88f, Paintwork);   // the chamfered rim
                Shapes.Prism(model[Mine], Matrix4x4.CreateScale(0.155f, 0.012f, 0.155f) * Matrix4x4.CreateTranslation(0, 0.02f, 0) * m, 14, 0.82f, Shade(body, 0.9f));
                Shapes.Prism(model[Mine], Matrix4x4.CreateScale(0.05f, 0.016f, 0.05f) * Matrix4x4.CreateTranslation(0, 0.03f, 0) * m, 8, 0.75f, capCol);
                if (!sunk)
                    Masonry.Block(model[Mine], Vector3.Transform(new Vector3(0.17f, 0.012f, 0), m), new(0.035f, 0.014f, 0.07f), Matrix4x4.CreateFromYawPitchRoll(spin, 0, 0), 0.004f, Rgb(0.78f, 0.62f, 0.12f), corners: false);
                else   // earth mounded round the sunk ones
                    for (var k = 0; k < 4; k++)
                    {
                        var a = R() * MathF.Tau; var at = new Vector2(x, z) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (0.17f + 0.04f * R());
                        var s = 0.05f + 0.03f * R();
                        Masonry.Block(model[Dirt], new(at.X, Height(at.X, at.Y) + 0.004f, at.Y), new(s * 1.3f, 0.03f, s), Masonry.Turn(a, 0, (R() - 0.5f) * 0.15f), 0.008f, Shade(soil, 0.75f + 0.25f * R()), rng, 0.06f, corners: false);
                    }
            }
        // The fence: stakes round the edge, two strands of barbed wire between them, a warning plate on every other stake.
        var wood = Rgb(0.38f, 0.29f, 0.19f); var red = Rgb(0.74f, 0.14f, 0.1f); var white = Rgb(0.9f, 0.88f, 0.8f);
        const float edge = 2.82f;
        var corners = new[] { new Vector2(-edge, -edge), new Vector2(edge, -edge), new Vector2(edge, edge), new Vector2(-edge, edge) };
        var n = 0;
        for (var side = 0; side < 4; side++)
        {
            var (a, b) = (corners[side], corners[(side + 1) % 4]);
            const int stakes = 6;
            var dir = Vector2.Normalize(b - a);
            for (var i = 0; i < stakes; i++, n++)
            {
                var p = Vector2.Lerp(a, b, (float)i / stakes); var q = Vector2.Lerp(a, b, (float)(i + 1) / stakes);
                Masonry.Block(model[Stake], new(p.X, Top / 2, p.Y), new(0.07f, Top, 0.07f), Masonry.Turn(R()), 0.012f, p3 => Shade(wood, 0.85f + 0.25f * Noise(p3.X * 40, p3.Z * 40, 0, 7)), corners: false);
                foreach (var h in new[] { 0.06f, 0.088f })
                {
                    var pts = Enumerable.Range(0, 13).Select(j => { var t = j / 12f; var v = Vector2.Lerp(p, q, t); return new Vector3(v.X, h - 0.018f * MathF.Sin(MathF.PI * t), v.Y); }).ToList();
                    // The strand as a thin ribbon, and barbs across it.
                    var across = new Vector3(-dir.Y, 0, dir.X) * 0.007f;
                    for (var j = 0; j + 1 < pts.Count; j++)
                        Masonry.Face(model[Wire], [pts[j] - across, pts[j + 1] - across, pts[j + 1] + across, pts[j] + across], Vector3.UnitY, _ => Rgb(0.46f, 0.45f, 0.43f));
                    for (var j = 1; j < 12; j += 2)
                    {
                        var barb = Vector3.Normalize(new Vector3(dir.X, 0, dir.Y) * 0.5f + new Vector3(-dir.Y, 0, dir.X)) * 0.025f;
                        Masonry.Face(model[Wire], [pts[j] - barb - Vector3.UnitY * 0.004f, pts[j] + barb - Vector3.UnitY * 0.004f, pts[j] + barb + Vector3.UnitY * 0.004f, pts[j] - barb + Vector3.UnitY * 0.004f], new Vector3(dir.X, 0, dir.Y), _ => Rgb(0.4f, 0.39f, 0.37f));
                    }
                }
                if (n % 2 == 0)
                {
                    // A plate on the stake facing out, tilted back so it reads from above: red, with a white band and a dark stripe.
                    var outward = new Vector3(dir.Y, 0, -dir.X);
                    if (Vector3.Dot(outward, new Vector3(p.X, 0, p.Y)) < 0) outward = -outward;
                    var centre = new Vector3(p.X, 0.06f, p.Y) + outward * 0.06f;
                    var yaw = MathF.Atan2(outward.X, outward.Z);
                    Masonry.Block(model[Sign], centre, new(0.34f, 0.075f, 0.012f), Masonry.Turn(yaw, -0.9f), 0.004f, red, corners: false);
                    Masonry.Block(model[Sign], centre + outward * 0.008f + Vector3.UnitY * 0.004f, new(0.26f, 0.02f, 0.008f), Masonry.Turn(yaw, -0.9f), 0, white);
                }
            }
        }
        return model;
    }
}
