using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Crumbling and drifting ground. <see cref="Rock"/> (4" × 4", 2" high, Solid): three sandstone blocks split by gaps, each
/// a stack of thin broken slabs shifted and set back on one another, jointed and chipped, the tallest flat at 2" to stand on;
/// slabs slid off and leaning at the foot, gravel round it, on a sandy base. <see cref="Dunes"/> (6" × 4", 1.5" high, Solid):
/// three crescent dunes, gentle windward slopes, steep slip faces, crests at 1.5", modelled wind ripples on the windward sides,
/// weathered sandstone breaking through between them, stones and dry grass in the hollows.</summary>
public static class Crumbly
{
    public static readonly Material Stone = new("sandstone", 0.92f), Scree = new("scree", 0.9f), Sand = new("sand", 0.95f), Grass = new("dry-grass", 0.9f, DoubleSided: true);

    public static Model Rock()
    {
        var model = new Model("crumbly-rock", round: false, new Vector3(4, 2, 4));
        var rng = new Random(481);
        var pen = new Pen(model[Stone]);
        var layers = new[] { Rgb(0.62f, 0.52f, 0.4f), Rgb(0.56f, 0.45f, 0.34f), Rgb(0.66f, 0.57f, 0.44f), Rgb(0.52f, 0.42f, 0.33f), Rgb(0.6f, 0.5f, 0.42f) };
        var dark = Rgb(0.32f, 0.25f, 0.2f);
        Func<Vector3, Vector3, int, Vector4> Paint(int seed) => (p, n, layer) =>
        {
            var c = layers[(layer * 3 + seed) % layers.Length];
            c = Mix(c, dark, Smooth(0.35f, 0.02f, p.Y) * 0.45f);
            return Shade(c, 0.92f + 0.14f * Fbm(p * 1.3f, seed));
        };
        Vector4 Loose(Vector3 p, Vector3 n) => Shade(layers[(int)(Fbm(p * 5, 490) * 11) % layers.Length], 0.82f);
        // Three blocks split by gaps, each a stack of thin, broken, shifted slabs; the tallest's top flat at 2".
        foreach (var (x, z, rx, rz, h, seed) in new[] { (-0.5f, -0.4f, 1.15f, 1.0f, 2f, 1), (0.85f, -0.3f, 0.85f, 0.75f, 1.6f, 2), (0.05f, 0.95f, 0.95f, 0.7f, 1.3f, 3) })
            Crag.Stack(pen, new Crag.Strata
            {
                Centre = new Vector2(x, z), Base = -0.05f, Height = h, Radii = new Vector2(rx, rz), Yaw = seed * 1.3f, Layers = (int)(h * 4.2f), Cuts = 6,
                LayerCuts = 4, Cracks = 3, Chips = 10, Columns = 60, RowStep = 0.11f, Taper = 0.28f, Lumps = 0.2f, Inset = 0.2f, Groove = 0.12f, Lip = 0.05f,
                Shift = 0.17f, Dip = 0.1f, DipToward = seed, Seed = 480 + seed,
            }, Paint(seed));
        // Slabs slid off the faces and leaning against them, flakes, then gravel in a skirt round the foot.
        for (var k = 0; k < 14; k++)
        {
            var t = (float)rng.NextDouble() * MathF.Tau; var d = 1.25f + 0.35f * (float)rng.NextDouble();
            var at = new Vector3(0.1f + MathF.Cos(t) * d, 0, 0.05f + MathF.Sin(t) * d);
            var s = 0.3f + 0.35f * (float)rng.NextDouble();
            at.Y = s * 0.25f;
            Crag.Chunk(pen, rng, at, new Vector3(s * 1.3f, s * 0.28f, s), -t, 0.35f + 0.4f * (float)rng.NextDouble(), 0.2f * ((float)rng.NextDouble() - 0.5f), Loose, 14);
        }
        Vector3? Skirt(Random r)
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = 1.15f + 0.75f * MathF.Sqrt((float)r.NextDouble());
            var at = new Vector2(0.1f + MathF.Cos(t) * d * 1.05f, MathF.Sin(t) * d);
            return MathF.Abs(at.X) > 1.88f || MathF.Abs(at.Y) > 1.88f ? null : new Vector3(at.X, 0.06f, at.Y);
        }
        Crag.Scree(pen, rng, 200, r => Skirt(r) is { } at ? (at, 0.04f + 0.14f * MathF.Pow((float)r.NextDouble(), 2.2f)) : null, Loose);
        var earth = Rgb(0.5f, 0.4f, 0.28f); var sand = Rgb(0.68f, 0.58f, 0.42f);
        Crag.Base(model, Scree, Vector3.One, new Vector2(0.05f, 0), t => 1.92f + 0.15f * (Noise(MathF.Cos(t) * 1.5f, MathF.Sin(t) * 1.5f, 2, 482) - 0.5f) + 0.25f * MathF.Pow(MathF.Abs(MathF.Sin(2 * t)), 3),
            14, 96, 0.22f, (x, z) => 0.06f + 0.02f * Fbm(x * 3, z * 3, 0, 483),
            (p, n) => Shade(Mix(earth, sand, Smooth(0.35f, 0.65f, Fbm(p.X * 1.5f, p.Z * 1.5f, 0, 484))), 0.85f + 0.25f * Fbm(p.X * 10, p.Z * 10, 1, 485)));
        Crag.Tufts(model[Grass], Vector3.One, rng, 30, r => Skirt(r), 0.2f, Rgb(0.45f, 0.42f, 0.22f), Rgb(0.78f, 0.7f, 0.45f));
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
        float Dunes(float x, float z) => 0.12f + 0.12f * MathF.Log(dunes.Sum(d => MathF.Exp(Dune(x, z, d) / 0.12f)));
        // Wind ripples: asymmetric (a long stoss side, a short steep lee), wandering, across the wind; flattened out on the slip faces.
        float Ripple(float x, float z)
        {
            var lee = Smooth(-0.6f, -1.4f, (Dunes(x + 0.04f, z) - Dunes(x - 0.04f, z)) / 0.08f);
            var u = (x + 0.3f * MathF.Sin(z * 1.4f) + 0.6f * Noise(x * 0.7f, z * 0.9f, 0, 503)) / 0.26f;
            var f = u - MathF.Floor(u);
            var saw = f < 0.72f ? Smooth(0, 0.72f, f) : Smooth(1, 0.72f, f);
            return 0.022f * saw * (1 - lee) * (0.6f + 0.4f * Noise(x * 1.3f, z * 1.3f, 1, 504));
        }
        float Height(float x, float z)
        {
            var dist = MathF.Min(3 - MathF.Abs(x), 2 - MathF.Abs(z));
            return MathF.Min(Dunes(x, z) + Ripple(x, z), 1.5f) * Smooth(0, 0.4f, dist);
        }
        var sand = Rgb(0.78f, 0.69f, 0.52f); var shadow = Rgb(0.66f, 0.53f, 0.37f); var light = Rgb(0.88f, 0.79f, 0.6f); var grit = Rgb(0.58f, 0.48f, 0.36f);
        Shapes.Ground(model, Sand, 120, 80, Height, (p, n) =>
        {
            var up = Vector3.Normalize(n / model.Inches).Y;
            var c = Mix(sand, light, Smooth(0.9f, 1.4f, p.Y));
            c = Mix(c, shadow, Smooth(0.8f, 0.45f, up) * 0.6f);
            c = Mix(c, grit, Smooth(0.25f, 0.05f, p.Y) * Smooth(0.45f, 0.7f, Fbm(p.X * 2, p.Z * 2, 3, 505)) * 0.6f);   // coarse grit in the hollows
            return Shade(c, 0.9f + 0.6f * Ripple(p.X, p.Z) / 0.022f * 0.12f + 0.1f * Fbm(p.X * 2, p.Z * 2, 1, 502));
        });
        // Weathered sandstone breaking through the sand between the dunes, broken stone round them, dry grass in the hollows.
        var pen = new Pen(model[Stone]);
        var layers = new[] { Rgb(0.62f, 0.5f, 0.38f), Rgb(0.56f, 0.44f, 0.33f), Rgb(0.66f, 0.55f, 0.42f) };
        Func<Vector3, Vector3, int, Vector4> Rock(int seed) => (p, n, layer) => Shade(layers[(layer + seed) % layers.Length], 0.9f + 0.14f * Fbm(p * 1.5f, seed));
        Vector4 Loose(Vector3 p, Vector3 n) => Shade(layers[(int)(Fbm(p * 5, 506) * 7) % layers.Length], 0.82f);
        foreach (var (x, z, rx, rz, h, seed) in new[] { (2.25f, -1.25f, 0.55f, 0.4f, 0.75f, 1), (-2.35f, 1.35f, 0.45f, 0.35f, 0.6f, 2), (0.35f, -1.45f, 0.35f, 0.3f, 0.45f, 3) })
            Crag.Stack(pen, new Crag.Strata
            {
                Centre = new Vector2(x, z), Base = Height(x, z) - 0.3f, Height = Height(x, z) + h, Radii = new Vector2(rx, rz), Yaw = seed, Pitch = 0.12f, Layers = 3,
                Cuts = 8, LayerCuts = 2, Cracks = 1, Chips = 4, Columns = 36, RowStep = 0.1f, Taper = 0.25f, Inset = 0.12f, Groove = 0.1f, Shift = 0.1f, Seed = 510 + seed,
            }, Rock(seed));
        Crag.Scree(pen, rng, 70, r =>
        {
            var x = ((float)r.NextDouble() - 0.5f) * 5.6f; var z = ((float)r.NextDouble() - 0.5f) * 3.6f;
            return Height(x, z) > 0.35f ? null : (new Vector3(x, Height(x, z), z), 0.04f + 0.1f * MathF.Pow((float)r.NextDouble(), 2));
        }, Loose);
        Crag.Tufts(model[Grass], Vector3.One, rng, 40, r =>
        {
            var x = ((float)r.NextDouble() - 0.5f) * 5.5f; var z = ((float)r.NextDouble() - 0.5f) * 3.5f;
            return Height(x, z) > 0.4f ? null : new Vector3(x, Height(x, z), z);
        }, 0.22f, Rgb(0.5f, 0.46f, 0.26f), Rgb(0.82f, 0.74f, 0.48f));
        return model;
    }
}
