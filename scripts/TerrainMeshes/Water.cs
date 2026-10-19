using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Water between banks, authored in inches. Each bank is a flocked turf top bevelled down to the table at the footprint's edge
/// and broken off at the shore in a crumbling earth cut down to the bed; the bed slopes to the middle under a translucent rippled
/// surface. Stones, earth clods and reed clumps line the shore; tufts stand on the turf; pebbles lie on the bed.
/// <see cref="River"/> runs along z with open ends (fitted 3" × 12"); <see cref="Lake"/> is round, with lily pads.</summary>
public static class Water
{
    public static readonly Material Bank = new("bank", 0.95f), Surface = new("water", 0.06f, Alpha: 0.78f), Reed = new("reeds", 0.85f, DoubleSided: true),
        Stone = new("stone", 0.85f), Pad = new("lily", 0.7f, DoubleSided: true);
    /// <summary>The water line, as a fraction of the piece's height.</summary>
    public const float Level = 0.24f;

    static readonly Vector4 Grass = Rgb(0.33f, 0.43f, 0.19f), Mud = Rgb(0.4f, 0.31f, 0.21f), Bed = Rgb(0.22f, 0.21f, 0.17f), Sand = Rgb(0.58f, 0.51f, 0.37f), Blue = Rgb(0.26f, 0.44f, 0.5f);

    /// <summary>How a river looks: its bank and water colours, its surface material, where its water stands and its bed lies (fractions of
    /// its height), and its authored inches when it has its own.</summary>
    public sealed record Style(string Name, int Seed, Vector4 Grass, Vector4 Mud, Vector4 Bed, Vector4 Sand, Vector4 Water, Material Surface, float Level, float Floor, Vector3? Inches = null);

    static readonly Style Deep = new("water", 81, Grass, Mud, Bed, Sand, Blue, Surface, Level, 0.03f);

    /// <summary>A built bank for a piece's dressing: pens on its materials, its ground height, and random shore points.</summary>
    public sealed class Banks(Model model, Vector3 size, Random rng, Func<float, float, float> height, Func<Random, (Vector3 At, Vector3 Inward)> shore, float level, float floor)
    {
        public Model Model { get; } = model;
        public Vector3 Size { get; } = size;
        public Random Rng { get; } = rng;
        /// <summary>The ground's height at (x, z), in inches.</summary>
        public Func<float, float, float> Height { get; } = height;
        /// <summary>A random point at the foot of the earth cut, and the direction into the water there.</summary>
        public Func<Random, (Vector3 At, Vector3 Inward)> Shore { get; } = shore;
        public float Level { get; } = level;
        public float Floor { get; } = floor;
        public Quill this[Material m] => Quill.Of(Model, m, Size);
    }

    static float U(Random r) => (float)r.NextDouble();

    /// <summary>The bank's height at distance <paramref name="d"/> in from the footprint's edge, with the cut's foot at <paramref name="shore"/>.</summary>
    static float Profile(float d, float shore, float top, float floor, float level, float centre, float noise)
    {
        if (d <= 0) return 0;
        if (d < 0.14f) return top * Smooth(0, 0.14f, d) * (0.85f + 0.15f * noise);       // the bevel down to the table
        if (d < shore - 0.06f) return top * (0.9f + 0.18f * noise);                        // the turf
        if (d < shore) return level + (top - level) * (0.6f + 0.4f * noise) * (shore - d) / 0.06f;   // the cut
        var u = Math.Clamp((d - shore) / MathF.Max(centre - shore, 0.05f), 0, 1);
        return level - 0.03f - (level - 0.03f - floor) * Smooth(0, 0.6f, u) + 0.012f * noise;  // the bed
    }

    static Vector4 BankColour(Vector3 p, Vector3 n, Style s, float level, float top)
    {
        Vector4 c;
        if (p.Y < level - 0.005f) c = Mix(s.Bed, Mix(s.Sand, s.Mud, 0.4f), Smooth(level * 0.3f, level, p.Y));
        else if (n.Y < 0.75f && p.Y < top * 0.95f && p.Y > top * 0.25f) c = Shade(s.Mud, 0.75f + 0.25f * Noise(p.Y * 40, p.X * 9, p.Z * 9, s.Seed));   // earth cut and bevel
        else c = Mix(s.Mud, Mix(s.Grass, Rgb(0.55f, 0.52f, 0.3f), Smooth(0.55f, 0.75f, Fbm(p.X * 2, p.Z * 2, 1, s.Seed)) * 0.5f), Smooth(level + 0.01f, level + 0.05f, p.Y));
        return Shade(c, 0.8f + 0.35f * Noise(p.X * 12, p.Z * 12, p.Y * 12, s.Seed + 7));
    }

    static Vector4 Ripple(Vector3 p, Vector4 water) => Shade(water, 0.9f + 0.2f * Fbm(p.X * 3, p.Z * 3, 0, 3));

    public static Model River() => River(Deep, b => Dress(b, 34, 70, 140, true));

    /// <summary>A river along z, open at both ends, authored in inches at <see cref="Style.Inches"/> (or drawn at 3" × 0.5" × 12" in the unit box);
    /// <paramref name="decorate"/> adds its shore and water details.</summary>
    public static Model River(Style s, Action<Banks> decorate)
    {
        var model = new Model(s.Name, round: false, s.Inches);
        var size = s.Inches ?? new Vector3(3, 0.5f, 12);
        var rng = new Random(s.Seed);
        var (half, len) = (size.X / 2, size.Z);
        float level = s.Level * size.Y, floor = s.Floor * size.Y, top = MathF.Min(0.44f * size.Y, level + 0.1f);
        float Shore(float z, int side) => 0.42f + 0.16f * Fbm(z * 0.6f + side * 7, 0, 1, s.Seed) + 0.05f * Noise(z * 3.1f, side, 2, s.Seed);
        float Grain(float x, float z) => Noise(x * 6, z * 6, 0, s.Seed + 1);
        float Height(float x, float z)
        {
            var side = x < 0 ? 0 : 1; var d = half - MathF.Abs(x);
            return Profile(d, Shore(z, side), top, floor, level, half, Grain(x, z));
        }
        // Columns across: per side, offsets in from the edge tied to the shore so the cut is one sharp step.
        float[] Offsets(float shore) => [0, 0.07f, 0.14f, 0.14f + (shore - 0.2f) * 0.5f, shore - 0.06f, shore - 0.025f, shore, shore + 0.1f, shore + (half - shore) * 0.45f, half];
        const int along = 72;
        var bank = Quill.Of(model, Bank, size);
        Vector3 At(int i, int j)
        {
            var z = -len / 2 + len * i / along;
            var count = Offsets(0.5f).Length;
            var (side, k) = j < count ? (0, j) : (1, 2 * count - 2 - j);
            var shore = Shore(z, side);
            var d = Offsets(shore)[k];
            var jag = k is 4 or 5 && i > 0 && i < along ? 0.04f * Foliage.Jitter(i, j, s.Seed) : 0;
            var zj = k is > 0 and < 9 && i > 0 && i < along ? len / along * 0.3f * Foliage.Jitter(j, i, s.Seed + 2) : 0;
            d += jag;
            var x = side == 0 ? -half + d : half - d;
            var y = Profile(d, shore, top, floor, level, half, Grain(x, z + zj));
            if (k == 5) y = level + (top - level) * (0.25f + 0.5f * Noise(z * 4, side, 0, s.Seed + 3));   // a crumbling lip part way down the cut
            return new Vector3(x, y, z + zj);
        }
        var cols = Offsets(0.5f).Length * 2 - 1;
        Foliage.Field(bank, along + 1, cols, At, (p, n) => BankColour(p, n, s, level, top));
        foreach (var (row, sign) in new[] { (0, -1f), (along, 1f) })   // the cut ends, down to the table
            for (var j = 0; j < cols - 1; j++)
            {
                var (a, b) = (At(row, j), At(row, j + 1));
                bank.Quad(a with { Y = 0 }, b with { Y = 0 }, b, a, new Vector3(0, 0, sign), _ => Shade(s.Mud, 0.65f));
            }
        // The water: a rippled sheet from bank to bank, and its cut ends.
        var water = Quill.Of(model, s.Surface, size);
        var w = half - 0.3f;
        model[s.Surface].Grid(along + 1, 9, wrap: false, (i, j) =>
        {
            var z = -len / 2 + len * i / along; var x = -w + 2 * w * j / 8;
            return new Vector3(x, level + 0.006f * (Noise(x * 8, z * 8, 0, 5) - 0.5f), z) * water.S;
        }, (p, _) => Ripple(p / water.S, s.Water), flipped: true);
        foreach (var z in new[] { -len / 2, len / 2 })
            water.Quad(new Vector3(-w, floor, z), new Vector3(w, floor, z), new Vector3(w, level, z), new Vector3(-w, level, z), new Vector3(0, 0, z), _ => Shade(s.Water, 0.8f));
        decorate(new Banks(model, size, rng, Height, r =>
        {
            var z = -len / 2 + 0.15f + (len - 0.3f) * U(r); var side = r.NextDouble() < 0.5 ? 0 : 1;
            var d = Shore(z, side) + 0.02f;
            var x = side == 0 ? -half + d : half - d;
            return (new Vector3(x, Height(x, z), z), new Vector3(side == 0 ? 1 : -1, 0, 0));
        }, level, floor));
        return model;
    }

    public static Model Lake()
    {
        var model = new Model("lake", round: true);
        var size = new Vector3(8, 0.5f, 8);
        var rng = new Random(91);
        var s = Deep with { Seed = 91 };
        float level = Level * size.Y, floor = 0.03f * size.Y, top = level + 0.1f, radius = 4;
        float Shore(float t) => 0.55f + 0.35f * Fbm(MathF.Cos(t) * 1.5f + 2, MathF.Sin(t) * 1.5f + 2, 0, 91) + 0.06f * Noise(MathF.Cos(t) * 6, MathF.Sin(t) * 6, 1, 92);
        float Grain(float x, float z) => Noise(x * 6, z * 6, 0, 93);
        float Height(float x, float z) { var r = MathF.Sqrt(x * x + z * z); return Profile(radius - r, Shore(MathF.Atan2(z, x)), top, floor, level, radius, Grain(x, z)); }
        float[] Offsets(float shore) => [0, 0.07f, 0.14f, 0.14f + (shore - 0.2f) * 0.5f, shore - 0.06f, shore - 0.025f, shore, shore + 0.2f, shore + 0.8f, shore + 1.6f, 2.6f, radius];
        const int segments = 96;
        var bank = Quill.Of(model, Bank, size);
        Foliage.Field(bank, Offsets(1).Length, segments, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments + (i is > 0 and < 11 ? 0.3f / segments * Foliage.Jitter(i, j, 94) : 0);
            var shore = Shore(t);
            var d = Offsets(shore)[i] + (i is 4 or 5 ? 0.04f * Foliage.Jitter(j, i, 95) : 0);
            if (i < 11) d = MathF.Min(d, radius - 0.3f);
            var r = radius - d;
            var (x, z) = (r * MathF.Cos(t), r * MathF.Sin(t));
            var y = i == 5 ? level + (top - level) * (0.25f + 0.5f * Noise(t * 5, 0, 0, 96)) : Profile(d, shore, top, floor, level, radius, Grain(x, z));
            return new Vector3(x, y, z);
        }, (p, n) => BankColour(p, n, s, level, top), wrap: true);
        var water = Quill.Of(model, Surface, size);
        model[Surface].Grid(12, segments, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments; var r = (radius - 0.35f) * i / 11;
            var (x, z) = (r * MathF.Cos(t), r * MathF.Sin(t));
            return new Vector3(x, level + 0.006f * (Noise(x * 8, z * 8, 0, 5) - 0.5f), z) * water.S;
        }, (p, _) => Ripple(p / water.S, Blue));
        var banks = new Banks(model, size, rng, Height, r =>
        {
            var t = U(r) * 6.28f; var d = Shore(t) + 0.02f;
            var o = new Vector3(MathF.Cos(t), 0, MathF.Sin(t));
            var at = o * (radius - d);
            return (at with { Y = Height(at.X, at.Z) }, -o);
        }, level, floor);
        Dress(banks, 40, 90, 160, true);
        // Lily pads: notched discs on the water, a few with flowers.
        var pad = banks[Pad];
        for (var k = 0; k < 16; k++)
        {
            var t = U(rng) * 6.28f; var r = 0.8f + 1.8f * U(rng); var sz = 0.14f + 0.1f * U(rng);
            var c = new Vector3(r * MathF.Cos(t), level + 0.008f, r * MathF.Sin(t));
            var colour = Shade(Rgb(0.3f, 0.48f, 0.2f), 0.85f + 0.3f * U(rng));
            var notch = U(rng) * 6.28f;
            for (var i = 0; i < 9; i++)
            {
                float a0 = notch + 0.35f + (6.28f - 0.7f) * i / 9, a1 = notch + 0.35f + (6.28f - 0.7f) * (i + 1) / 9;
                pad.Face(c, c + new Vector3(MathF.Cos(a0), 0.02f, MathF.Sin(a0)) * sz, c + new Vector3(MathF.Cos(a1), 0.02f, MathF.Sin(a1)) * sz, Vector3.UnitY, colour, Shade(colour, 1.1f), Shade(colour, 1.1f));
            }
            if (k % 4 == 0)
                for (var p = 0; p < 6; p++)
                {
                    var o = Foliage.Flat(p * 1.05f + t);
                    pad.Face(c + Vector3.UnitY * 0.01f, c + o * 0.06f + new Vector3(-o.Z, 0, o.X) * 0.03f + Vector3.UnitY * 0.05f, c + o * 0.06f - new Vector3(-o.Z, 0, o.X) * 0.03f + Vector3.UnitY * 0.05f, o, Rgb(0.95f, 0.88f, 0.9f), Rgb(0.95f, 0.75f, 0.85f), Rgb(0.95f, 0.75f, 0.85f));
                }
        }
        return model;
    }

    /// <summary>The usual shore: stones and clods at the cut's foot, reed clumps at the water's edge, tufts on the turf and pebbles on the bed.</summary>
    public static void Dress(Banks b, int stones, int reeds, int tufts, bool pebbles)
    {
        var rng = b.Rng; var stone = b[Stone]; var reed = b[Reed];
        var grey = Rgb(0.42f, 0.41f, 0.38f); var moss = Rgb(0.36f, 0.42f, 0.22f);
        for (var k = 0; k < stones; k++)
        {
            var (at, inward) = b.Shore(rng);
            var s = 0.05f + 0.09f * U(rng);
            var earth = k % 3 == 0;
            Foliage.Stone(stone, at + inward * s * 0.3f + Vector3.UnitY * s * 0.1f, new Vector3(s * 1.3f, s * 0.8f, s), 600 + k, U(rng) * 3,
                (q, n) => earth ? Shade(Rgb(0.38f, 0.29f, 0.2f), 0.75f + 0.3f * n.Y) : Shade(Mix(grey, moss, Smooth(0.6f, 1, n.Y) * 0.5f), 0.75f + 0.3f * Noise(q.X * 30, q.Y * 30, q.Z * 30, k)), k % 4 == 0 ? 1 : 0);
        }
        var reedBase = Rgb(0.3f, 0.36f, 0.17f); var reedTip = Rgb(0.6f, 0.6f, 0.32f); var head = Rgb(0.36f, 0.24f, 0.14f);
        var tall = b.Size.Y * 0.95f - b.Level;
        for (var k = 0; k < reeds; k++)
        {
            var (at, inward) = b.Shore(rng);
            var root = at + inward * (0.05f + 0.1f * U(rng)) - Vector3.UnitY * 0.01f;
            root.Y = MathF.Min(root.Y, b.Level - 0.01f);
            Foliage.Tuft(reed, rng, root, 7, MathF.Min(tall, 0.25f + 0.3f * U(rng)), 0.04f, 0.25f, reedBase, Mix(reedTip, reedBase, U(rng) * 0.4f), bent: true);
            if (k % 3 == 0)   // a bulrush
            {
                var h = MathF.Min(tall, 0.35f + 0.1f * U(rng));
                var lean = Foliage.Flat(U(rng) * 6.28f) * 0.04f;
                Foliage.Tube(reed, [root, root + lean * 0.7f + Vector3.UnitY * h * 0.7f], [0.008f, 0.006f], 3, _ => reedBase, k, 0.05f);
                Foliage.Tube(reed, [root + lean * 0.7f + Vector3.UnitY * h * 0.7f, root + lean + Vector3.UnitY * h * 0.92f, root + lean * 1.05f + Vector3.UnitY * h], [0.022f, 0.02f, 0.0f], 5, _ => head, k + 1, 0.1f);
            }
        }
        var grass = Rgb(0.32f, 0.42f, 0.18f); var straw = Rgb(0.6f, 0.58f, 0.3f);
        for (var k = 0; k < tufts; k++)
        {
            var (at, inward) = b.Shore(rng);
            var p = at - inward * (0.1f + 0.35f * U(rng)); p.Y = b.Height(p.X, p.Z) - 0.005f;
            if (p.Y < b.Level + 0.02f) continue;
            Foliage.Tuft(reed, rng, p, 6, 0.08f + 0.08f * U(rng), 0.025f, 0.5f, grass, Mix(straw, grass, U(rng) * 0.5f));
        }
        if (!pebbles) return;
        for (var k = 0; k < 60; k++)
        {
            var (at, inward) = b.Shore(rng);
            var p = at + inward * (0.1f + 0.6f * U(rng)); p.Y = b.Height(p.X, p.Z);
            var s = 0.025f + 0.035f * U(rng);
            var c = Shade(Mix(Rgb(0.5f, 0.47f, 0.42f), Rgb(0.36f, 0.32f, 0.27f), U(rng)), 0.85f + 0.3f * U(rng));
            Foliage.Stone(stone, p, new Vector3(s * 1.3f, s * 0.6f, s), 700 + k, U(rng) * 3, (_, n) => Shade(c, 0.85f + 0.2f * n.Y));
        }
    }
}
