using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The rivers that are not the plain deep one, each 3" × 12" × 0.5" and drawn on <see cref="Water.River(Water.Style, Action{Water.Banks})"/>.
/// <see cref="Toxic"/>: a poisoned green river between dead, stained banks, with scum rafts, dead broken reeds, stained stones and a sunken drum.
/// <see cref="Stream"/>: a shallow clear stream over a pebble bed, with stepping stones and riffles, reeds and grass on its banks.</summary>
public static class Rivers
{
    static readonly Vector3 Size = new(3, 0.5f, 12);
    public static readonly Material Poison = new("poison", 0.12f, Alpha: 0.84f, Emissive: new Vector3(0.05f, 0.12f, 0.0f)), Clear = new("shallows", 0.08f, Alpha: 0.5f),
        Scum = new("scum", 0.7f), Dead = new("dead-reeds", 0.9f, DoubleSided: true), Reed = new("reeds", 0.85f, DoubleSided: true), Pebble = new("pebbles", 0.75f), Foam = new("foam", 0.5f), Drum = new("drum", 0.5f, Metallic: 0.6f);

    static float U(Random r) => (float)r.NextDouble();

    public static Model Toxic() => Water.River(new Water.Style("toxic-river", 241, Rgb(0.34f, 0.32f, 0.23f), Rgb(0.26f, 0.23f, 0.18f), Rgb(0.2f, 0.24f, 0.08f), Rgb(0.5f, 0.5f, 0.28f),
        Rgb(0.42f, 0.68f, 0.1f), Poison, Water.Level, 0.03f, Size), b =>
    {
        var rng = b.Rng;
        // Scum rafts on the surface: flat irregular plates, pale yellow-green, thickest near the banks.
        var scum = b[Scum];
        for (var k = 0; k < 46; k++)
        {
            var (at, inward) = b.Shore(rng);
            var c = at + inward * (0.05f + 0.6f * U(rng) * U(rng)); c.Y = b.Level + 0.004f;
            var r = 0.06f + 0.14f * U(rng);
            var colour = Shade(Mix(Rgb(0.76f, 0.78f, 0.4f), Rgb(0.56f, 0.64f, 0.2f), U(rng)), 0.85f + 0.2f * U(rng));
            var n = 7;
            var pts = Enumerable.Range(0, n).Select(i => { var a = i * 6.28f / n; return c + Foliage.Flat(a) * r * (0.6f + 0.6f * Noise(i, k, 0, 250)) + Vector3.UnitY * 0.008f * Noise(i, k, 1, 251); }).ToArray();
            for (var i = 0; i < n; i++) scum.Face(c + Vector3.UnitY * 0.01f, pts[i], pts[(i + 1) % n], Vector3.UnitY, colour, Shade(colour, 0.9f), Shade(colour, 0.9f));
        }
        // Dead reeds, grey-brown and snapped, and stained stones.
        var stone = b[Pebble]; var dead = b[Dead];
        for (var k = 0; k < 50; k++)
        {
            var (at, inward) = b.Shore(rng);
            if (k % 4 == 3)
            {
                var s = 0.06f + 0.1f * U(rng);
                Foliage.Stone(stone, at + inward * s * 0.4f, new Vector3(s * 1.3f, s * 0.8f, s), 300 + k, U(rng) * 3,
                    (q, n) => Shade(Mix(Rgb(0.38f, 0.37f, 0.32f), Rgb(0.45f, 0.5f, 0.18f), Smooth(b.Level + 0.04f, b.Level, q.Y)), 0.75f + 0.3f * Noise(q.X * 30, q.Y * 30, q.Z * 30, k)), k % 8 == 3 ? 1 : 0);
                continue;
            }
            var root = at + inward * 0.06f; root.Y = MathF.Min(root.Y, b.Level - 0.01f);
            Foliage.Tuft(dead, rng, root, 6, 0.2f + 0.2f * U(rng), 0.04f, 0.7f, Rgb(0.24f, 0.22f, 0.16f), Rgb(0.55f, 0.5f, 0.4f), bent: true);
        }
        for (var k = 0; k < 130; k++)
        {
            var (at, inward) = b.Shore(rng);
            var p = at - inward * (0.1f + 0.35f * U(rng)); p.Y = b.Height(p.X, p.Z) - 0.005f;
            if (p.Y < b.Level + 0.02f) continue;
            Foliage.Tuft(dead, rng, p, 5, 0.08f + 0.06f * U(rng), 0.02f, 0.7f, Rgb(0.3f, 0.28f, 0.2f), Rgb(0.6f, 0.55f, 0.38f));
        }
        // A drum half sunk in mid-stream, leaking.
        var drum = b[Drum];
        var foot = new Vector3(0.35f, 0.02f, 2.4f); var axis = Vector3.Normalize(new Vector3(0.4f, 0.5f, 0.7f));
        Func<Vector3, Vector4> paint = p => Mix(Rgb(0.8f, 0.62f, 0.12f), Rgb(0.45f, 0.24f, 0.12f), Smooth(0.45f, 0.75f, Fbm(p * 9, 2)));
        Foliage.Tube(drum, [foot, foot + axis * 0.42f], [0.15f, 0.15f], 12, paint, 7, 0.03f, cap: true);
        Foliage.Tube(drum, [foot + axis * 0.17f, foot + axis * 0.24f], [0.157f, 0.157f], 12, _ => Rgb(0.15f, 0.15f, 0.15f), 8, 0.0f);
    });

    public static Model Stream() => Water.River(new Water.Style("stream", 261, Rgb(0.34f, 0.45f, 0.2f), Rgb(0.43f, 0.35f, 0.24f), Rgb(0.46f, 0.42f, 0.34f), Rgb(0.64f, 0.58f, 0.44f),
        Rgb(0.36f, 0.6f, 0.62f), Clear, Water.Level, 0.16f, Size), b =>
    {
        var rng = b.Rng;
        Water.Dress(b, 30, 50, 160, false);
        // The pebble bed under the shallow water.
        var pebble = b[Pebble];
        for (var k = 0; k < 160; k++)
        {
            var x = (U(rng) - 0.5f) * 2.0f; var z = -5.9f + 11.8f * U(rng);
            var y = b.Height(x, z);
            if (y > b.Level - 0.01f) continue;
            var s = 0.035f + 0.05f * U(rng);
            var c = Shade(Mix(Rgb(0.6f, 0.57f, 0.5f), Rgb(0.45f, 0.4f, 0.33f), U(rng)), 0.8f + 0.3f * U(rng));
            Foliage.Stone(pebble, new Vector3(x, y, z), new Vector3(s * 1.3f, s * 0.6f, s), 400 + k, U(rng) * 3, (_, n) => Shade(c, 0.85f + 0.2f * n.Y));
        }
        // Stepping stones across, standing clear of the water, with foam on their upstream sides.
        var foam = b[Foam];
        for (var k = 0; k < 5; k++)
        {
            var at = new Vector3(-0.8f + 0.4f * k + 0.08f * (U(rng) - 0.5f), b.Level - 0.08f, -1.6f + 0.15f * (U(rng) - 0.5f));
            var s = 0.15f + 0.04f * U(rng);
            Foliage.Stone(pebble, at, new Vector3(s, 0.16f, s * 0.9f), 500 + k, U(rng) * 3, (q, n) => Shade(Mix(Rgb(0.52f, 0.5f, 0.46f), Rgb(0.4f, 0.46f, 0.26f), Smooth(0.8f, 1, n.Y) * 0.4f), 0.8f + 0.3f * Noise(q.X * 20, q.Y * 20, q.Z * 20, k)), 1, 0.25f);
            for (var f = 0; f < 6; f++)
            {
                var c = at + new Vector3((f - 2.5f) * s * 0.35f, 0.08f + 0.004f, -s * 0.95f - 0.03f * U(rng));
                c.Y = b.Level + 0.004f;
                Foliage.Leaf(foam, c, Vector3.UnitY, Vector3.UnitX, 0.09f, 0.05f, Rgb(0.9f, 0.93f, 0.92f));
            }
        }
        for (var k = 0; k < 40; k++)
        {
            var at = new Vector3((U(rng) - 0.5f) * 1.4f, b.Level + 0.003f, -5.6f + 11.2f * U(rng));
            Foliage.Leaf(foam, at, Vector3.UnitY, Vector3.Normalize(new Vector3(0.15f * (U(rng) - 0.5f), 0, 1)), 0.12f + 0.12f * U(rng), 0.03f, Rgb(0.86f, 0.9f, 0.9f));
        }
    });
}
