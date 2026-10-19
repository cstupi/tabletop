using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A toxic cloud on its own blasted base, authored in inches at its entry (8" across, 6" high): a faceted, stained ground
/// with sunk pools of glowing sludge rimmed by cracked crust plates, broken vent pipes and a toppled leaking drum, rubble and dead
/// tufts, under translucent billows, low and wide at the foot, smaller and higher above, so the column reads as a soft volume and
/// shows what stands inside it.</summary>
public static class Haze
{
    public static readonly Material Ground = new("stain", 0.85f), Pool = new("sludge-pool", 0.25f, Emissive: new Vector3(0.06f, 0.1f, 0.0f)),
        Crust = new("crust", 0.9f), Pipe = new("rusted-pipe", 0.6f, Metallic: 0.5f), Debris = new("debris", 0.88f),
        Dead = new("dead-grass", 0.9f, DoubleSided: true), Cloud = new("smog", 1f, Alpha: 0.24f);

    const float D = 8, Top = 6, R = D / 2;
    static readonly (Vector2 At, float Radius)[] Pools = [(new(-1.2f, 0.8f), 0.75f), (new(1.3f, -0.6f), 0.55f), (new(0.4f, 1.9f), 0.4f), (new(-0.6f, -1.8f), 0.45f)];

    /// <summary>How far into a pool a point is, 0 outside, 1 at its middle.</summary>
    static float InPool(float x, float z) =>
        Pools.Max(p => Smooth(p.Radius, p.Radius * 0.55f, Vector2.Distance(new Vector2(x, z), p.At) * (0.9f + 0.2f * Noise(x * 3, z * 3, 0, 7))));

    public static Model Build()
    {
        var model = new Model("haze", round: true, new Vector3(D, Top, D));
        var rng = new Random(121);
        var stain = Rgb(0.34f, 0.33f, 0.2f); var dirt = Rgb(0.3f, 0.26f, 0.19f); var burnt = Rgb(0.26f, 0.24f, 0.18f);
        var ground = Quill.Of(model, Ground, model.Inches);
        float Outline(float t) => R * (0.94f + 0.05f * MathF.Sin(t * 5 + 1) + 0.02f * MathF.Sin(t * 11));
        Foliage.Disc(ground, 14, 72, Outline, (x, z, u) =>
        {
            var lip = Smooth(1, 0.85f, u);                                     // a bevelled edge down to the table
            var lumps = 0.05f + 0.07f * Fbm(x * 1.4f, z * 1.4f, 0, 123);
            return lip * (lumps - 0.06f * InPool(x, z));
        }, (p, n) =>
        {
            var c = Mix(stain, dirt, Smooth(0.45f, 0.65f, Fbm(p.X * 0.8f, p.Z * 0.8f, 0, 121)));
            c = Mix(c, burnt, Smooth(0.5f, 0.2f, MathF.Sqrt(p.X * p.X + p.Z * p.Z) / R) * 0.35f);   // scorched toward the vents
            c = Mix(c, Rgb(0.42f, 0.46f, 0.16f), Smooth(0.2f, 0.6f, InPool(p.X, p.Z) + 0.3f) * 0.6f);   // stained around the pools
            return Shade(c, 0.85f + 0.25f * Fbm(p.X * 4, p.Z * 4, 1, 122));
        }, 125);

        // The pools: a glossy sludge skin just under the ground's rim, with crust plates broken around it.
        var pool = Quill.Of(model, Pool, model.Inches);
        var glow = Rgb(0.45f, 0.62f, 0.12f); var murk = Rgb(0.25f, 0.34f, 0.08f);
        var crust = new Pen(model[Crust]);
        foreach (var (at, radius) in Pools)
        {
            Foliage.Disc(pool, 4, 24, t => radius * (0.85f + 0.1f * MathF.Sin(t * 3 + at.X)), (x, z, u) => 0.03f + 0.012f * Noise(x * 6, z * 6, 1, 9),
                (p, n) => Mix(glow, murk, Smooth(0.3f, 0.9f, Vector2.Distance(new Vector2(p.X, p.Z), at) / radius)), 140 + (int)(at.X * 10));
            for (var k = 0; k < 9; k++)
            {
                var a = (float)(rng.NextDouble() * Math.Tau); var r = radius * (0.95f + 0.3f * (float)rng.NextDouble());
                var c = new Vector3(at.X + MathF.Cos(a) * r, 0.07f, at.Y + MathF.Sin(a) * r);
                var size = new Vector3(0.28f + 0.2f * (float)rng.NextDouble(), 0.05f, 0.2f + 0.15f * (float)rng.NextDouble());
                Crag.Chunk(crust, rng, c, size, a, ((float)rng.NextDouble() - 0.5f) * 0.4f, ((float)rng.NextDouble() - 0.5f) * 0.4f,
                    (p, n) => Shade(Mix(Rgb(0.5f, 0.48f, 0.3f), Rgb(0.4f, 0.44f, 0.18f), (float)rng.NextDouble()), 0.8f + 0.25f * MathF.Max(n.Y, 0)), 9);
            }
        }

        // Vents: broken pipe stubs, and a toppled drum leaking into the largest pool.
        var pipe = Quill.Of(model, Pipe, model.Inches);
        var rust = Rgb(0.42f, 0.24f, 0.13f); var steel = Rgb(0.36f, 0.36f, 0.34f);
        Vector4 Rusted(Vector3 p) => Shade(Mix(steel, rust, Smooth(0.35f, 0.65f, Fbm(p * 5, 131))), 0.85f + 0.2f * Fbm(p * 11, 132));
        foreach (var (x, z, h, r) in new[] { (0.2f, 0.1f, 0.85f, 0.17f), (-2.3f, -0.4f, 0.55f, 0.13f), (2.1f, 1.4f, 0.4f, 0.12f) })
        {
            var lean = new Vector3(0.12f * MathF.Sign(x), 1, 0.08f);
            Foliage.Tube(pipe, [new Vector3(x, 0, z), new Vector3(x, 0, z) + Vector3.Normalize(lean) * h], [r, r * 0.95f], 9, Rusted, 133 + (int)(x * 10), 0.05f, cap: false);
            Foliage.Tube(pipe, [new Vector3(x, 0, z), new Vector3(x, 0.08f, z)], [r * 1.35f, r * 1.3f], 9, Rusted, 134, 0.05f, cap: true);   // the flange
        }
        Foliage.Tube(pipe, [new Vector3(-2.05f, 0.24f, 1.25f), new Vector3(-1.6f, 0.24f, 0.95f)], [0.24f, 0.24f], 12,
            p => Shade(Mix(Rgb(0.52f, 0.46f, 0.12f), rust, Smooth(0.4f, 0.7f, Fbm(p * 4, 135))), 0.9f), 136, 0.02f, cap: true);

        // Rubble and dead growth.
        var debris = new Pen(model[Debris]);
        Crag.Scree(debris, rng, 46, g =>
        {
            var a = (float)(g.NextDouble() * Math.Tau); var r = R * 0.85f * MathF.Sqrt((float)g.NextDouble());
            var (x, z) = (MathF.Cos(a) * r, MathF.Sin(a) * r);
            return InPool(x, z) > 0.05f ? null : (new Vector3(x, 0.06f, z), 0.08f + 0.18f * (float)g.NextDouble());
        }, (p, n) => Shade(Mix(Rgb(0.42f, 0.4f, 0.36f), Rgb(0.3f, 0.27f, 0.22f), Fbm(p * 3, 137)), 0.8f + 0.3f * MathF.Max(n.Y, 0)));
        var dead = Quill.Of(model, Dead, model.Inches);
        for (var k = 0; k < 34; k++)
        {
            var a = (float)(rng.NextDouble() * Math.Tau); var r = R * 0.9f * MathF.Sqrt((float)rng.NextDouble());
            var (x, z) = (MathF.Cos(a) * r, MathF.Sin(a) * r);
            if (InPool(x, z) > 0.05f) continue;
            Foliage.Tuft(dead, rng, new Vector3(x, 0.05f, z), 7, 0.25f + 0.2f * (float)rng.NextDouble(), 0.025f, 0.6f, Rgb(0.3f, 0.27f, 0.16f), Rgb(0.6f, 0.55f, 0.36f), bent: true);
        }

        // The smog.
        var smog = Rgb(0.72f, 0.78f, 0.45f);
        var layers = new[] { (7, 0.14f, 0.3f, 0.17f, 0.17f), (6, 0.38f, 0.22f, 0.15f, 0.18f), (4, 0.62f, 0.14f, 0.14f, 0.16f), (2, 0.82f, 0.06f, 0.12f, 0.12f) };
        var cloud = new Random(121);
        var kk = 0;
        foreach (var (count, y, ring, radius, height) in layers)
            for (var i = 0; i < count; i++, kk++)
            {
                var t = 2 * MathF.PI * i / count + (float)cloud.NextDouble();
                var r = ring * (0.7f + 0.3f * (float)cloud.NextDouble());
                var s = radius * (0.8f + 0.3f * (float)cloud.NextDouble());
                var c = new Vector3(r * MathF.Cos(t), y + 0.04f * (float)cloud.NextDouble(), r * MathF.Sin(t));
                var flat = new Vector2(c.X, c.Z); var limit = 0.5f - s;
                if (flat.Length() > limit) { flat = Vector2.Normalize(flat) * limit; c = new Vector3(flat.X, c.Y, flat.Y); }
                Shapes.Blob(model[Cloud], c * model.Inches, new Vector3(s, height, s) * model.Inches, 2, 0.22f, 130 + kk,
                    (p, n) => Shade(smog, 0.85f + 0.2f * MathF.Max(n.Y, 0)), smooth: true, yaw: t);
            }
        return model;
    }
}
