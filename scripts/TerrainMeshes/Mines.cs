using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The mine field (6" × 6", 0.1" high): churned dirt with scorched blast pits, rows of disc mines, some half buried, a
/// fence of stakes and barbed wire round the edge, and red warning plates on the stakes.</summary>
public static class Mines
{
    public static readonly Material Dirt = new("churned-dirt", 0.95f), Mine = new("mine", 0.55f, Metallic: 0.5f), Wire = new("wire", 0.5f, Metallic: 0.7f),
        Sign = new("warning", 0.6f), Stake = new("stake", 0.85f);
    const float Size = 6, Top = 0.1f;

    public static Model Build()
    {
        var model = new Model("mine-field", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(201);
        var pits = new[] { new Vector2(-1.2f, 0.9f), new Vector2(1.3f, -0.6f), new Vector2(0.2f, 2.0f), new Vector2(-0.6f, -1.7f) };
        float Height(float x, float z)
        {
            var dist = 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var y = 0.025f + 0.02f * Fbm(x * 2.5f, z * 2.5f, 0, 201) + 0.012f * Noise(x * 9, z * 9, 1, 202);
            foreach (var p in pits)
            {
                var r = Vector2.Distance(new(x, z), p) / 0.38f;
                if (r < 1.6f) y += r < 1 ? -0.022f * (1 - r * r) : 0.018f * MathF.Sin((r - 1) / 0.6f * MathF.PI);
            }
            return MathF.Max(y, 0.003f) * Smooth(0, 0.15f, dist);
        }
        var soil = Rgb(0.4f, 0.31f, 0.21f); var dark = Rgb(0.26f, 0.2f, 0.14f); var burnt = Rgb(0.1f, 0.09f, 0.08f);
        Shapes.Ground(model, Dirt, 56, 56, Height, (p, n) =>
        {
            var c = Mix(Shade(soil, 0.85f + 0.3f * Fbm(p.X * 3, p.Z * 3, 2, 203)), dark, Smooth(0.45f, 0.6f, Noise(p.X * 7, p.Z * 7, 3, 204)) * 0.6f);
            var scorch = pits.Min(q => Vector2.Distance(new(p.X, p.Z), q)) / 0.6f;
            return Mix(c, burnt, Smooth(1, 0.4f, scorch));
        });
        // Mines: in staggered rows, olive discs with a pressure cap; a third half buried, tilted.
        var olive = Rgb(0.34f, 0.37f, 0.22f); var cap = Rgb(0.2f, 0.21f, 0.16f);
        for (var row = 0; row < 6; row++)
            for (var col = 0; col < 6; col++)
            {
                var x = -2.35f + col * 0.92f + (row % 2) * 0.46f + 0.15f * ((float)rng.NextDouble() - 0.5f);
                var z = -2.3f + row * 0.92f + 0.15f * ((float)rng.NextDouble() - 0.5f);
                if (x > 2.45f || pits.Any(p => Vector2.Distance(new(x, z), p) < 0.55f)) continue;
                var sunk = rng.NextDouble() < 0.35;
                var y = Height(x, z) - (sunk ? 0.03f : 0.004f);
                var tilt = sunk ? 0.25f * ((float)rng.NextDouble() - 0.5f) : 0;
                var spin = (float)rng.NextDouble() * 3;
                Shapes.Prism(model[Mine], Shapes.At(new Vector3(x, y, z), new Vector3(0.17f, 0.045f, 0.17f), spin, tilt, tilt * 0.5f), 12, 0.88f, p => Shade(olive, 0.85f + 0.2f * Noise(p.X * 20, p.Z * 20, 0, 205)));
                Shapes.Prism(model[Mine], Shapes.At(new Vector3(x, y + 0.045f, z), new Vector3(0.055f, 0.022f, 0.055f), spin, tilt, tilt * 0.5f), 8, 0.8f, cap);
                if (!sunk) Shapes.Box(model[Mine], Shapes.At(new Vector3(x, y + 0.03f, z) + new Vector3(MathF.Cos(spin), 0, MathF.Sin(spin)) * 0.13f, new Vector3(0.08f, 0.012f, 0.03f), -spin), Rgb(0.78f, 0.62f, 0.12f));
            }
        // The fence: stakes round the edge, two strands of barbed wire between them, and a warning plate on every other stake.
        var wood = Rgb(0.36f, 0.27f, 0.18f); var red = Rgb(0.72f, 0.14f, 0.1f); var white = Rgb(0.9f, 0.88f, 0.8f);
        const float edge = 2.82f;
        var corners = new[] { new Vector2(-edge, -edge), new Vector2(edge, -edge), new Vector2(edge, edge), new Vector2(-edge, edge) };
        var k = 0;
        for (var side = 0; side < 4; side++)
        {
            var (a, b) = (corners[side], corners[(side + 1) % 4]);
            const int stakes = 6;
            for (var i = 0; i < stakes; i++, k++)
            {
                var p = Vector2.Lerp(a, b, (float)i / stakes); var q = Vector2.Lerp(a, b, (float)(i + 1) / stakes);
                Shapes.Box(model[Stake], new Vector3(p.X - 0.035f, 0, p.Y - 0.035f), new Vector3(p.X + 0.035f, Top, p.Y + 0.035f), wood);
                // Barbed wire: a sagging strand with barbs, and a coil of loops beside it.
                foreach (var h in new[] { 0.062f, 0.09f })
                {
                    var pts = Enumerable.Range(0, 9).Select(j => { var t = j / 8f; var v = Vector2.Lerp(p, q, t); return new Vector3(v.X, h - 0.02f * MathF.Sin(MathF.PI * t), v.Y); }).ToList();
                    Shapes.Chain(model[Wire], pts, 0.008f, 0.008f, 4, Rgb(0.45f, 0.44f, 0.42f));
                    for (var j = 1; j < 8; j += 2)
                    {
                        var at = pts[j]; var dir = new Vector3(q.X - p.X, 0, q.Y - p.Y) / Vector2.Distance(p, q);
                        Shapes.Box(model[Wire], Shapes.At(at, new Vector3(0.05f, 0.01f, 0.008f), MathF.Atan2(-dir.Z, dir.X) + 1.2f), Rgb(0.4f, 0.39f, 0.37f));
                    }
                }
                if (k % 2 == 0)
                {
                    // A plate on the stake facing out: a red square with a white band, tilted back toward the sky so it reads from above.
                    var outward = new Vector3(p.X, 0, p.Y); outward = Vector3.Normalize(outward with { X = MathF.Abs(p.X) > edge - 0.01f ? p.X : 0, Z = MathF.Abs(p.Y) > edge - 0.01f ? p.Y : 0 });
                    var centre = new Vector3(p.X, 0.06f, p.Y) + outward * 0.06f;
                    var yaw = MathF.Atan2(outward.X, outward.Z);
                    var m = Shapes.At(centre, new Vector3(0.32f, 0.07f, 0.012f), yaw, -0.9f);
                    Shapes.Box(model[Sign], m, red);
                    Shapes.Box(model[Sign], Shapes.At(centre + outward * 0.008f, new Vector3(0.24f, 0.02f, 0.008f), yaw, -0.9f), white);
                }
            }
        }
        return model;
    }
}
