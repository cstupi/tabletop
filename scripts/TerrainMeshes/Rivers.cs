using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The rivers that are not the plain deep one, each 3" × 12" × 0.5" and drawn on <see cref="Water.River(Water.Style, Action{Model, Random, Func{Random, Vector3}})"/>.
/// <see cref="Toxic"/>: a poisoned green river between dead, stained banks, with scum, foam, dead reeds and a sunken drum.
/// <see cref="Stream"/>: a shallow clear stream over a pebble bed, with stepping stones and riffles, reeds and grass on its banks.</summary>
public static class Rivers
{
    static readonly Vector3 Size = new(3, 0.5f, 12);
    public static readonly Material Poison = new("poison", 0.12f, Alpha: 0.84f, Emissive: new Vector3(0.05f, 0.12f, 0.0f)), Clear = new("shallows", 0.08f, Alpha: 0.5f),
        Scum = new("scum", 0.7f), Dead = new("dead-reeds", 0.9f, DoubleSided: true), Reed = new("reeds", 0.85f, DoubleSided: true), Pebble = new("pebbles", 0.75f), Foam = new("foam", 0.5f), Drum = new("drum", 0.5f, Metallic: 0.6f);

    public static Model Toxic() => Water.River(new Water.Style("toxic-river", 241, Rgb(0.4f, 0.38f, 0.3f), Rgb(0.32f, 0.3f, 0.24f), Rgb(0.2f, 0.24f, 0.08f), Rgb(0.55f, 0.55f, 0.3f),
        Rgb(0.5f, 0.8f, 0.12f), Poison, Water.Level, 0.03f, Size), (model, rng, shore) =>
    {
        // Scum and foam: pale yellow-green rafts on the surface, thickest near the banks.
        for (var k = 0; k < 40; k++)
        {
            var x = ((float)rng.NextDouble() - 0.5f) * 1.9f; var z = -5.8f + 11.6f * (float)rng.NextDouble();
            var r = 0.1f + 0.18f * (float)rng.NextDouble();
            Shapes.Blob(model[Scum], new Vector3(x, Water.Level * 0.5f + 0.005f, z), new Vector3(r, 0.012f, r * (1 + (float)rng.NextDouble())), 1, 0.35f, 250 + k,
                (_, n) => Shade(Mix(Rgb(0.78f, 0.8f, 0.42f), Rgb(0.6f, 0.68f, 0.22f), (float)rng.NextDouble()), 0.9f + 0.15f * n.Y), yaw: (float)rng.NextDouble() * 3);
        }
        // Dead reeds, grey-brown and broken, and stained stones.
        for (var k = 0; k < 40; k++)
        {
            var p = shore(rng);
            if (k % 4 == 3)
            {
                var s = 0.08f + 0.07f * (float)rng.NextDouble();
                Shapes.Blob(model[Pebble], p with { Y = 0.1f }, new Vector3(s * 1.3f, s * 0.8f, s), 1, 0.3f, 300 + k, (q, n) => Shade(Rgb(0.36f, 0.36f, 0.3f), 0.8f + 0.3f * Fbm(q * 9, k)));
                continue;
            }
            for (var c = 0; c < 3; c++)
            {
                var root = p + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.15f, 0.1f, ((float)rng.NextDouble() - 0.5f) * 0.15f);
                Shapes.Tuft(model[Dead], rng, root, 5, 0.3f, 0.03f, 0.6f, Rgb(0.26f, 0.24f, 0.18f), Rgb(0.55f, 0.5f, 0.4f));
            }
        }
        // A drum half sunk in mid-stream, leaking.
        var foot = new Vector3(0.35f, 0.02f, 2.4f); var axis = Vector3.Normalize(new Vector3(0.4f, 0.5f, 0.7f));
        Shapes.Limb(model[Drum], foot, foot + axis * 0.42f, 0.15f, 1, 12, p => Mix(Rgb(0.8f, 0.62f, 0.12f), Rgb(0.45f, 0.24f, 0.12f), Smooth(0.45f, 0.75f, Fbm(p * 9, 2))));
        Shapes.Limb(model[Drum], foot + axis * 0.18f, foot + axis * 0.24f, 0.155f, 1, 12, Rgb(0.15f, 0.15f, 0.15f));
    });

    public static Model Stream() => Water.River(new Water.Style("stream", 261, Rgb(0.42f, 0.54f, 0.25f), Rgb(0.45f, 0.37f, 0.26f), Rgb(0.46f, 0.42f, 0.34f), Rgb(0.66f, 0.6f, 0.46f),
        Rgb(0.36f, 0.6f, 0.62f), Clear, Water.Level, 0.16f, Size), (model, rng, shore) =>
    {
        // The pebble bed under the shallow water, and riffles where the water runs over it.
        for (var k = 0; k < 260; k++)
        {
            var x = ((float)rng.NextDouble() - 0.5f) * 1.9f; var z = -5.9f + 11.8f * (float)rng.NextDouble();
            var s = 0.04f + 0.06f * (float)rng.NextDouble();
            var c = Shade(Mix(Rgb(0.6f, 0.57f, 0.5f), Rgb(0.45f, 0.4f, 0.33f), (float)rng.NextDouble()), 0.8f + 0.3f * (float)rng.NextDouble());
            Shapes.Blob(model[Pebble], new Vector3(x, 0.16f * 0.5f, z), new Vector3(s * 1.3f, s * 0.5f, s), 0, 0.2f, 400 + k, (_, _) => c, yaw: (float)rng.NextDouble() * 3);
        }
        // Stepping stones across, standing clear of the water, with foam on their upstream sides.
        for (var k = 0; k < 5; k++)
        {
            var at = new Vector3(-0.8f + 0.4f * k + 0.08f * ((float)rng.NextDouble() - 0.5f), 0.07f, -1.6f + 0.15f * ((float)rng.NextDouble() - 0.5f));
            var s = 0.17f + 0.04f * (float)rng.NextDouble();
            Shapes.Blob(model[Pebble], at, new Vector3(s, 0.1f, s * 0.9f), 1, 0.2f, 500 + k, (q, n) => Shade(Mix(Rgb(0.52f, 0.5f, 0.46f), Rgb(0.4f, 0.46f, 0.26f), Smooth(0.8f, 1, n.Y) * 0.4f), 0.85f + 0.25f * Fbm(q * 20, k)),
                shape: q => q with { Y = MathF.Min(q.Y, 0.165f) });
            Shapes.Blob(model[Foam], at + new Vector3(0, 0.05f, -0.16f), new Vector3(s * 1.1f, 0.012f, 0.08f), 1, 0.4f, 520 + k, (_, n) => Rgb(0.9f, 0.93f, 0.92f));
        }
        for (var k = 0; k < 18; k++)
        {
            var at = new Vector3(((float)rng.NextDouble() - 0.5f) * 1.6f, 0.123f, -5.6f + 11.2f * (float)rng.NextDouble());
            Shapes.Blob(model[Foam], at, new Vector3(0.12f + 0.1f * (float)rng.NextDouble(), 0.006f, 0.04f), 1, 0.4f, 540 + k, (_, n) => Rgb(0.86f, 0.9f, 0.9f), yaw: 0.2f * ((float)rng.NextDouble() - 0.5f));
        }
        // Reeds and grass on the banks.
        for (var k = 0; k < 30; k++)
        {
            var p = shore(rng);
            for (var c = 0; c < 3; c++)
            {
                var root = p + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.15f, 0.1f, ((float)rng.NextDouble() - 0.5f) * 0.15f);
                Shapes.Tuft(model[Reed], rng, root, 6, 0.3f, 0.03f, 0.5f, Rgb(0.3f, 0.4f, 0.18f), Rgb(0.6f, 0.64f, 0.32f));
            }
        }
    });
}
