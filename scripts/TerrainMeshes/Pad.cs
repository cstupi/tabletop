using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The flat ground pieces. <see cref="Plaza"/>: a chamfered landing pad, a bevelled slab decked in panels, with hazard
/// chevrons round the edge, a ring and bars marking the centre, grates and corner lights. <see cref="Crater"/>: a blast crater,
/// a raised rim of churned earth round a scorched bowl, with debris and twisted metal.</summary>
public static class Pad
{
    public static readonly Material Concrete = new("plate", 0.75f, Metallic: 0.15f), Marking = new("marking", 0.6f), Metal = new("metal", 0.4f, Metallic: 0.8f),
        Light = new("light", 0.3f, Emissive: new Vector3(1f, 0.62f, 0.2f)), Earth = new("earth", 0.95f), Stone = new("debris", 0.85f);
    const float Deck = 0.3f, Inner = 0.4f, Outer = 0.49f, Chamfer = 0.08f;

    public static Model Plaza()
    {
        var model = new Model("pad", round: false);
        var rng = new Random(101);
        var plate = Rgb(0.46f, 0.47f, 0.48f); var edge = Rgb(0.36f, 0.37f, 0.38f);
        // The slab: an octagon, its side bevelled from the ground to the deck.
        var outline = Octagon(Outer, Chamfer); var top = Octagon(Outer - 0.025f, Chamfer * 0.9f);
        for (var i = 0; i < 8; i++)
        {
            var (a, b) = (outline[i], outline[(i + 1) % 8]); var (c, d) = (top[(i + 1) % 8], top[i]);
            var mid = (a + b) / 2;
            Shapes.Oriented(model[Concrete], [new(a.X, 0, a.Y), new(b.X, 0, b.Y), new(b.X, Deck - 0.04f, b.Y), new(a.X, Deck - 0.04f, a.Y)], new Vector3(mid.X, 0, mid.Y), _ => Shade(edge, 0.85f));
            Shapes.Oriented(model[Concrete], [new(a.X, Deck - 0.04f, a.Y), new(b.X, Deck - 0.04f, b.Y), new(c.X, Deck, c.Y), new(d.X, Deck, d.Y)], new Vector3(mid.X, 1, mid.Y), _ => edge);
            Shapes.Oriented(model[Concrete], [new(0, Deck, 0), new(d.X, Deck, d.Y), new(c.X, Deck, c.Y)], Vector3.UnitY, _ => Shade(edge, 1.1f));
        }
        // Deck panels, 4 × 4, each its own shade, standing proud of the slab so the seams read.
        const int panels = 4; const float size = 2 * Inner / panels;
        for (var i = 0; i < panels; i++)
            for (var j = 0; j < panels; j++)
            {
                var (x, z) = (-Inner + i * size, -Inner + j * size);
                var c = Shade(plate, 0.9f + 0.16f * (float)rng.NextDouble());
                Shapes.Box(model[Concrete], new Vector3(x + 0.004f, Deck, z + 0.004f), new Vector3(x + size - 0.004f, Deck + 0.012f, z + size - 0.004f), p => Shade(c, 0.94f + 0.1f * Fbm(p * 30, i * 4 + j)));
            }
        // Hazard chevrons on the border, alternate yellow and black.
        var yellow = Rgb(0.88f, 0.7f, 0.16f); var black = Rgb(0.12f, 0.12f, 0.12f);
        const float y = Deck + 0.002f, band0 = Inner + 0.012f, band1 = Outer - 0.04f, stripe = 0.04f;
        for (var side = 0; side < 4; side++)
        {
            var rot = Matrix4x4.CreateRotationY(side * MathF.PI / 2);
            var n = 0;
            for (var u = -band1 + Chamfer; u < band1 - Chamfer - 0.001f; u += stripe, n++)
            {
                var u1 = MathF.Min(u + stripe, band1 - Chamfer);
                var q = new[] { new Vector3(u, y, band0), new Vector3(u1, y, band0), new Vector3(u1 + 0.02f, y, band1), new Vector3(u + 0.02f, y, band1) }.Select(v => Vector3.Transform(v, rot)).ToArray();
                Shapes.Oriented(model[Marking], q, Vector3.UnitY, _ => n % 2 == 0 ? yellow : black);
            }
        }
        // Centre marking: a ring and two bars with a crossbar, raised a hair above the panels.
        const float mark = Deck + 0.0135f;
        var white = Rgb(0.88f, 0.88f, 0.84f);
        for (var i = 0; i < 40; i++)
        {
            var (t0, t1) = (2 * MathF.PI * i / 40, 2 * MathF.PI * (i + 1) / 40);
            Vector3 R(float r, float t) => new(r * MathF.Cos(t), mark, r * MathF.Sin(t));
            Shapes.Oriented(model[Marking], [R(0.22f, t0), R(0.25f, t0), R(0.25f, t1), R(0.22f, t1)], Vector3.UnitY, _ => yellow);
        }
        foreach (var (x0, z0, x1, z1) in new[] { (-0.1f, -0.11f, -0.065f, 0.11f), (0.065f, -0.11f, 0.1f, 0.11f), (-0.065f, -0.016f, 0.065f, 0.016f) })
            Shapes.Oriented(model[Marking], [new(x0, mark, z0), new(x1, mark, z0), new(x1, mark, z1), new(x0, mark, z1)], Vector3.UnitY, _ => white);
        // Grates and lights.
        foreach (var (gx, gz) in new[] { (-0.3f, 0.3f), (0.3f, -0.3f) })
        {
            Shapes.Box(model[Metal], new Vector3(gx - 0.06f, Deck + 0.012f, gz - 0.04f), new Vector3(gx + 0.06f, Deck + 0.016f, gz + 0.04f), Rgb(0.18f, 0.18f, 0.19f));
            for (var k = 0; k < 6; k++)
                Shapes.Box(model[Metal], new Vector3(gx - 0.055f + k * 0.02f, Deck + 0.016f, gz - 0.035f), new Vector3(gx - 0.045f + k * 0.02f, Deck + 0.022f, gz + 0.035f), Rgb(0.42f, 0.43f, 0.44f));
        }
        foreach (var (lx, lz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var at = new Vector3(lx * 0.425f, Deck, lz * 0.425f);
            Shapes.Prism(model[Metal], Shapes.At(at, new Vector3(0.022f, 0.03f, 0.022f)), 8, 1, Rgb(0.3f, 0.3f, 0.32f));
            Shapes.Prism(model[Light], Shapes.At(at + new Vector3(0, 0.03f, 0), new Vector3(0.016f, 0.03f, 0.016f)), 8, 0.6f, Rgb(1f, 0.75f, 0.35f));
        }
        return model;
    }

    static Vector2[] Octagon(float r, float c) =>
        [new(r, -r + c), new(r, r - c), new(r - c, r), new(-r + c, r), new(-r, r - c), new(-r, -r + c), new(-r + c, -r), new(r - c, -r)];

    public static Model Crater()
    {
        var model = new Model("crater", round: true);
        var rng = new Random(111);
        const int segments = 72, rings = 26;
        float Rim(float t) => 0.39f + 0.025f * (Noise(MathF.Cos(t) * 3 + 5, MathF.Sin(t) * 3 + 5, 0, 111) - 0.5f);
        float Crest(float t) => 0.78f + 0.22f * Fbm(MathF.Cos(t) * 2 + 1, MathF.Sin(t) * 2 + 1, 0, 112);
        float Height(float r, float t)
        {
            var rim = Rim(t);
            if (r >= rim) return Crest(t) * MathF.Pow(Smooth(0.5f, rim, r), 1.4f);
            var bowl = Smooth(0.08f, rim, r);
            return 0.06f + (Crest(t) - 0.06f) * MathF.Pow(bowl, 2.2f);
        }
        var soil = Rgb(0.45f, 0.36f, 0.25f); var churned = Rgb(0.55f, 0.45f, 0.32f); var scorched = Rgb(0.2f, 0.17f, 0.14f); var ash = Rgb(0.11f, 0.1f, 0.09f);
        model[Earth].Grid(rings + 1, segments, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / segments; var r = 0.5f * i / rings;
            r += i is > 0 and < rings ? 0.006f * (Noise(t * 6, i, 0, 113) - 0.5f) : 0;
            return new Vector3(r * MathF.Cos(t), Height(r, t), r * MathF.Sin(t));
        }, (p, n) =>
        {
            var r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            var c = r > 0.36f ? Mix(churned, soil, Smooth(0.4f, 0.5f, r)) : Mix(ash, scorched, Smooth(0.08f, 0.3f, r));
            c = Mix(c, churned, Smooth(0.3f, 0.38f, r) * Smooth(0.44f, 0.38f, r));
            return Shade(c, 0.8f + 0.35f * Fbm(p.X * 16, p.Z * 16, p.Y * 4, 114));
        });
        for (var k = 0; k < 14; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = k < 9 ? Rim(t) + 0.03f * ((float)rng.NextDouble() - 0.5f) : 0.1f + 0.18f * (float)rng.NextDouble();
            var s = 0.02f + 0.025f * (float)rng.NextDouble();
            var p = new Vector3(r * MathF.Cos(t), Height(r, t), r * MathF.Sin(t));
            Shapes.Blob(model[Stone], p, new Vector3(s, s * 1.6f, s * 0.9f), 0, 0.3f, 120 + k, (q, n) => Shade(Rgb(0.45f, 0.42f, 0.38f), 0.65f + 0.35f * Fbm(q * 40, k)), yaw: t);
        }
        for (var k = 0; k < 4; k++)   // twisted metal in the bowl
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.05f + 0.2f * (float)rng.NextDouble();
            var p = new Vector3(r * MathF.Cos(t), Height(r, t) + 0.05f, r * MathF.Sin(t));
            Shapes.Box(model[Metal], Shapes.At(p, new Vector3(0.008f, 0.14f, 0.05f), t, 0.6f + 0.4f * (float)rng.NextDouble(), 0.3f), Rgb(0.3f, 0.27f, 0.25f));
        }
        return model;
    }
}
