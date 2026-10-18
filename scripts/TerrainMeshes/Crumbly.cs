using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Crumbling and drifting ground. <see cref="Rock"/> (4" × 4", 2" high, Solid): a cracked, flaking sandstone outcrop of
/// stacked uneven layers split by deep cracks, its top layer flat at 2" to stand on, with flakes slid off its sides and scree at
/// its foot. <see cref="Dunes"/> (6" × 4", 1.5" high, Solid): three crescent dunes rising from a sand base, gentle windward
/// slopes, steep slip faces, crests at 1.5", wind ripples.</summary>
public static class Crumbly
{
    public static readonly Material Stone = new("sandstone", 0.92f), Scree = new("scree", 0.9f), Sand = new("sand", 0.95f), Grass = new("dry-grass", 0.9f, DoubleSided: true);

    public static Model Rock()
    {
        var model = new Model("crumbly-rock", round: false, new Vector3(4, 2, 4));
        var rng = new Random(481);
        var ochre = Rgb(0.6f, 0.52f, 0.42f); var pale = Rgb(0.8f, 0.72f, 0.58f); var dark = Rgb(0.3f, 0.24f, 0.19f); var grey = Rgb(0.55f, 0.52f, 0.48f);
        // Three blocks split by cracks, each a stack of layers; each layer's outline is the block's, wobbled and shrunk a little
        // with height, so the edges step back in flaky ledges.
        var blocks = new[] { (new Vector2(-0.55f, -0.45f), 1.15f, 2f, 1), (new Vector2(0.75f, -0.35f), 0.95f, 1.7f, 2), (new Vector2(0.05f, 0.95f), 0.95f, 1.45f, 3) };
        foreach (var (centre, radius, height, seed) in blocks)
        {
            const int layers = 8, sides = 13;
            var y0 = 0f;
            for (var l = 0; l < layers; l++)
            {
                var y1 = l == layers - 1 ? height : height * (l + 1) / layers + height / layers * 0.35f * ((float)rng.NextDouble() - 0.5f);
                var shrink = 1 - 0.3f * MathF.Pow(y0 / height, 1.3f);
                var shift = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.14f;
                var outline = Enumerable.Range(0, sides).Select(i =>
                {
                    var t = 2 * MathF.PI * (i + 0.3f * (float)rng.NextDouble()) / sides + seed;
                    var r = radius * shrink * (0.7f + 0.4f * Noise(MathF.Cos(t) * 2.2f + seed * 3, MathF.Sin(t) * 2.2f, l * 0.5f, 481)) * (0.88f + 0.2f * (float)rng.NextDouble());
                    return centre + shift + new Vector2(MathF.Cos(t), MathF.Sin(t)) * r;
                }).ToList();
                var shade = 0.82f + 0.24f * (float)rng.NextDouble();
                var (lo, hi) = (y0, y1);
                var last = l == layers - 1;
                Shapes.Slab(model[Stone], outline, y0, last ? y1 : y1 - 0.025f, (p, n) =>
                {
                    var c = n.Y > 0.5f ? Mix(ochre, pale, 0.6f) : Mix(dark, Mix(ochre, grey, 0.4f), Smooth(lo, hi, p.Y) * 0.8f + 0.2f);
                    return Shade(c, shade * (0.88f + 0.18f * Fbm(p * 3, seed)));
                }, topScale: 0.93f);
                // The bedding plane: a thin dark seam under the next layer.
                if (!last) Shapes.Slab(model[Scree], outline.Select(v => centre + shift + (v - centre - shift) * 0.9f).ToList(), y1 - 0.025f, y1, (_, _) => dark);
                y0 = y1;
            }
        }
        // Flakes: thin plates slid off the faces and leaning against them; scree in a skirt round the foot.
        for (var k = 0; k < 16; k++)
        {
            var (centre, radius, height, _) = blocks[k % 3];
            var t = (float)rng.NextDouble() * 6.3f; var o = new Vector3(MathF.Cos(t), 0, MathF.Sin(t));
            var at = new Vector3(centre.X, 0, centre.Y) + o * (radius * 0.95f); at.Y = 0.25f + 0.5f * (float)rng.NextDouble() * height * 0.5f;
            var s = 0.25f + 0.25f * (float)rng.NextDouble();
            Shapes.Box(model[Stone], Shapes.At(at, new Vector3(s, s * 1.2f, 0.06f), -t + MathF.PI / 2, 0.35f * (k % 2 == 0 ? 1 : -1), 0.2f), Shade(ochre, 0.85f + 0.2f * (float)rng.NextDouble()));
        }
        for (var k = 0; k < 70; k++)
        {
            var t = (float)rng.NextDouble() * 6.3f; var r = 1.4f + 0.55f * (float)rng.NextDouble();
            var s = 0.05f + 0.1f * (float)rng.NextDouble();
            var at = new Vector3(0.05f + MathF.Cos(t) * r, s * 0.3f, 0.05f + MathF.Sin(t) * r);
            if (MathF.Abs(at.X) > 1.92f || MathF.Abs(at.Z) > 1.92f) continue;
            Shapes.Box(model[Scree], Shapes.At(at, new Vector3(s * 1.4f, s * 0.5f, s), t, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f), Shade(Mix(ochre, pale, (float)rng.NextDouble()), 0.75f + 0.3f * (float)rng.NextDouble()));
        }
        return model;
    }

    public static Model Dunes()
    {
        var model = new Model("dunes", round: false, new Vector3(6, 1.5f, 4));
        var rng = new Random(501);
        // Each dune: (crest centre x, z), height, along-crest half length, windward and lee widths; the wind blows toward +x, so the
        // slip face is on +x and the horns curve downwind.
        var dunes = new[] { (new Vector2(-1.2f, -0.6f), 1.5f, 1.25f, 1.6f, 0.7f), (new Vector2(1.25f, 0.55f), 1.25f, 1.15f, 1.4f, 0.6f), (new Vector2(-0.6f, 1.15f), 0.95f, 0.75f, 1.0f, 0.45f) };
        float Dune(float x, float z, (Vector2 C, float H, float L, float W, float Lee) d)
        {
            var v = (z - d.C.Y) / d.L;
            if (MathF.Abs(v) >= 1) return 0;
            var crest = d.C.X + 0.55f * v * v;                                   // the crest bows downwind toward the horns
            var u = x - crest;
            var across = u < 0 ? 1 - MathF.Pow(-u / d.W, 1.6f) : 1 - MathF.Pow(u / d.Lee, 1.5f);
            if (across <= 0) return 0;
            return d.H * across * (1 - v * v) * (1 - 0.3f * v * v);
        }
        float Height(float x, float z)
        {
            var dist = MathF.Min(3 - MathF.Abs(x), 2 - MathF.Abs(z));
            var y = 0.12f + 0.12f * MathF.Log(dunes.Sum(d => MathF.Exp(Dune(x, z, d) / 0.12f))) + 0.01f * MathF.Sin(x * 11 + 2.5f * Noise(x * 0.8f, z * 0.8f, 0, 501));
            return MathF.Min(y, 1.5f) * Smooth(0, 0.4f, dist);
        }
        var sand = Rgb(0.82f, 0.72f, 0.52f); var shadow = Rgb(0.68f, 0.56f, 0.4f); var light = Rgb(0.9f, 0.82f, 0.64f);
        Shapes.Ground(model, Sand, 90, 60, Height, (p, n) =>
        {
            var c = Mix(sand, light, Smooth(0.9f, 1.4f, p.Y));
            c = Mix(c, shadow, Smooth(0.7f, 0.4f, n.Y) * 0.6f);
            return Shade(c, 0.94f + 0.05f * MathF.Sin(p.X * 11 + 2.5f * Noise(p.X * 0.8f, p.Z * 0.8f, 0, 501)) + 0.08f * Fbm(p.X * 2, p.Z * 2, 1, 502));
        });
        // A few tufts of dry grass in the hollows.
        for (var k = 0; k < 24; k++)
        {
            var x = ((float)rng.NextDouble() - 0.5f) * 5.4f; var z = ((float)rng.NextDouble() - 0.5f) * 3.4f;
            var y = Height(x, z);
            if (y > 0.5f) continue;
            Shapes.Tuft(model[Grass], rng, new Vector3(x, y, z), 7, 0.2f, 0.04f, 0.8f, Rgb(0.5f, 0.48f, 0.28f), Rgb(0.8f, 0.74f, 0.48f));
        }
        return model;
    }
}
