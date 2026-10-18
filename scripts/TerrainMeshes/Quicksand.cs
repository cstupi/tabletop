using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Quicksand (4" × 4", 0.1" high): a pit of smooth, wet, darker sand sagging toward its middle in concentric ripple rings,
/// inside a lip of dry rippled sand; a crate sinking at a tilt, a rifle and a dead branch going under, and a helmet half swallowed.</summary>
public static class Quicksand
{
    public static readonly Material Sand = new("sand", 0.9f), Wet = new("wet-sand", 0.6f), Wood = new("crate", 0.85f), Metal = new("metal", 0.5f, Metallic: 0.6f);
    const float Size = 4, Top = 0.1f;

    public static Model Build()
    {
        var model = new Model("quicksand", round: false, new Vector3(Size, Top, Size));
        var centre = new Vector2(0.1f, -0.05f);
        float Pit(float x, float z)   // 0 at the middle, 1 at the pit's edge, wobbling
        {
            var d = new Vector2(x, z) - centre; var t = MathF.Atan2(d.Y, d.X);
            return d.Length() / (1.45f + 0.18f * Noise(MathF.Cos(t) * 1.5f + 4, MathF.Sin(t) * 1.5f + 4, 0, 221));
        }
        float Height(float x, float z)
        {
            var dist = 2 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var r = Pit(x, z);
            float y;
            if (r < 1) y = 0.012f + 0.022f * r * r + 0.004f * MathF.Sin(r * 26) * (0.4f + 0.6f * r);                    // the sag and its rings
            else y = 0.034f + 0.03f * Smooth(1, 1.15f, r) - 0.012f * Smooth(1.15f, 1.5f, r)                          // a lip, then dry sand
                     + 0.004f * MathF.Sin(x * 9 + 2 * Noise(x, z, 1, 222) + z * 3);                                    // wind ripples
            return y * Smooth(0, 0.15f, dist);
        }
        var dry = Rgb(0.78f, 0.66f, 0.45f); var damp = Rgb(0.56f, 0.45f, 0.3f); var wet = Rgb(0.38f, 0.3f, 0.2f);
        Shapes.Ground(model, Sand, 64, 64, (x, z) => Pit(x, z) < 0.98f ? MathF.Min(Height(x, z), 0.01f) : Height(x, z),
            (p, n) => Shade(Mix(dry, damp, Smooth(1.25f, 1, Pit(p.X, p.Z))), 0.9f + 0.15f * Fbm(p.X * 4, p.Z * 4, 0, 223)));
        // The wet pit itself, a glossy surface over the sag with darker troughs between its rings.
        model[Wet].Grid(28, 72, wrap: true, (i, j) =>
        {
            var t = 2 * MathF.PI * j / 72;
            var lo = 0f; var hi = 1.6f;   // find the pit's edge along this ray
            for (var s = 0; s < 18; s++) { var m = (lo + hi) / 2; if (Pit(centre.X + MathF.Cos(t) * m, centre.Y + MathF.Sin(t) * m) < 1) lo = m; else hi = m; }
            var r = lo * i / 27;
            var x = centre.X + MathF.Cos(t) * r; var z = centre.Y + MathF.Sin(t) * r;
            return new Vector3(x, Height(x, z), z);
        }, (p, n) =>
        {
            var r = Pit(model.Inch(p).X, model.Inch(p).Z);
            return Shade(Mix(wet, damp, Smooth(0.5f, 1, r) * 0.7f), 0.85f + 0.25f * Smooth(-0.3f, 0.6f, MathF.Sin(r * 26)));
        });
        // The crate: a corner and an edge above the sand, the rest under it.
        var plank = Rgb(0.5f, 0.38f, 0.22f);
        var crate = Shapes.At(new Vector3(-0.55f, 0.0f, 0.35f), new Vector3(0.7f, 0.7f, 0.7f), 0.5f, 0.6f, 0.45f);
        Shapes.Box(model[Wood], crate, p => Shade(plank, 0.85f + 0.2f * MathF.Floor(Noise(p.X * 12, p.Z * 12, 0, 224) * 3) / 3));
        Shapes.Box(model[Wood], Shapes.At(new Vector3(-0.55f, 0.0f, 0.35f), new Vector3(0.74f, 0.1f, 0.74f), 0.5f, 0.6f, 0.45f), Rgb(0.32f, 0.24f, 0.14f));
        // The rifle, stock up; a branch; the helmet.
        var gun = Rgb(0.2f, 0.2f, 0.21f);
        Shapes.Box(model[Metal], Shapes.At(new Vector3(0.75f, 0.03f, -0.45f), new Vector3(1.2f, 0.05f, 0.08f), 0.7f, 0, 0.05f), gun);
        Shapes.Box(model[Wood], Shapes.At(new Vector3(1.25f, 0.045f, -0.85f), new Vector3(0.36f, 0.07f, 0.12f), 0.7f, 0, 0.12f), Rgb(0.42f, 0.28f, 0.16f));
        Shapes.Chain(model[Wood], [new Vector3(-0.3f, 0.02f, -0.9f), new Vector3(-0.75f, 0.05f, -1.1f), new Vector3(-1.2f, 0.07f, -1.05f), new Vector3(-1.5f, 0.06f, -1.35f)], 0.05f, 0.015f, 6, Rgb(0.4f, 0.33f, 0.24f));
        Shapes.Chain(model[Wood], [new Vector3(-0.85f, 0.055f, -1.08f), new Vector3(-1.0f, 0.06f, -0.75f), new Vector3(-1.05f, 0.05f, -0.55f)], 0.025f, 0.01f, 5, Rgb(0.4f, 0.33f, 0.24f));
        Shapes.Blob(model[Metal], new Vector3(0.55f, 0.0f, 0.75f), new Vector3(0.24f, 0.08f, 0.26f), 2, 0.02f, 225, (q, n) => Shade(Rgb(0.3f, 0.34f, 0.24f), 0.85f + 0.2f * n.Y), smooth: true);
        Shapes.Limb(model[Metal], new Vector3(0.55f, 0.005f, 0.75f), new Vector3(0.55f, 0.012f, 0.75f), 0.3f, 1, 16, Rgb(0.26f, 0.3f, 0.2f));
        return model;
    }
}
