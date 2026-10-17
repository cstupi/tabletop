using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A two-storey stone block with a lower front wing: coursed masonry on a plinth, corner pilasters, a string course,
/// framed windows (a few lit), a metal door under a hood with a step, a cornice and parapet, and roof vents, a hatch and a mast.
/// Reads as a hab block or bunker and as a stone house.</summary>
public static class Building
{
    public static readonly Material Stone = new("stone", 0.85f), Trim = new("trim", 0.8f), Roof = new("roof", 0.9f),
        Metal = new("metal", 0.45f, Metallic: 0.7f), Glass = new("glass", 0.12f, Metallic: 0.2f), Lit = new("lit-window", 0.3f, Emissive: new Vector3(0.95f, 0.7f, 0.38f));

    const float Plinth = 0.035f, MainTop = 0.9f, WingTop = 0.5f, Back = -0.44f, Split = 0.14f, Front = 0.44f, Side = 0.44f, WingSide = 0.36f;
    static readonly Vector4 Wall = Rgb(0.66f, 0.61f, 0.53f), Dark = Rgb(0.5f, 0.47f, 0.43f), Slate = Rgb(0.3f, 0.31f, 0.33f), Steel = Rgb(0.38f, 0.4f, 0.42f);

    public static Model Build()
    {
        var model = new Model("building", round: false);
        var rng = new Random(41);
        Shapes.Box(model[Trim], new Vector3(-0.49f, 0, -0.49f), new Vector3(0.49f, Plinth, 0.49f), Dark);
        Courses(model, rng, new(-Side, Plinth, Back), new(Side, MainTop, Split));
        Courses(model, rng, new(-WingSide, Plinth, Split - 0.01f), new(WingSide, WingTop, Front));
        // Pilasters at the main block's corners and the wing's front corners; the string course at the upper floor.
        foreach (var (x, z) in new[] { (-Side, Back), (Side, Back), (-Side, Split), (Side, Split) })
            Shapes.Box(model[Trim], new Vector3(x - 0.03f, Plinth, z - 0.03f), new Vector3(x + 0.03f, MainTop - 0.03f, z + 0.03f), p => Shade(Dark, 0.92f + 0.12f * Fbm(p * 30, 1)));
        foreach (var x in new[] { -WingSide, WingSide })
            Shapes.Box(model[Trim], new Vector3(x - 0.025f, Plinth, Front - 0.025f), new Vector3(x + 0.025f, WingTop - 0.02f, Front + 0.025f), Dark);
        Shapes.Box(model[Trim], new Vector3(-Side - 0.012f, 0.455f, Back - 0.012f), new Vector3(Side + 0.012f, 0.475f, Split + 0.012f), Dark);
        // Cornice and parapets, roof decks.
        Shapes.Box(model[Trim], new Vector3(-0.465f, MainTop - 0.035f, -0.465f), new Vector3(0.465f, MainTop, Split + 0.025f), Dark);
        Parapet(model, -0.465f, 0.465f, -0.465f, Split + 0.025f, MainTop, 1f, 0.03f);
        Shapes.Box(model[Roof], new Vector3(-0.435f, MainTop, -0.435f), new Vector3(0.435f, MainTop + 0.012f, Split - 0.005f), Slate);
        Shapes.Box(model[Trim], new Vector3(-WingSide - 0.02f, WingTop - 0.025f, Split), new Vector3(WingSide + 0.02f, WingTop, Front + 0.03f), Dark);
        Parapet(model, -WingSide - 0.02f, WingSide + 0.02f, Split, Front + 0.03f, WingTop, WingTop + 0.05f, 0.022f, back: false);
        Shapes.Box(model[Roof], new Vector3(-WingSide + 0.005f, WingTop, Split), new Vector3(WingSide - 0.005f, WingTop + 0.01f, Front + 0.005f), Slate);
        // Windows: two floors round the main block, the upper one also over the wing; the wing's front and sides on the ground floor.
        var lower = (0.16f, 0.36f); var upper = (0.58f, 0.78f);
        foreach (var x in new[] { -0.27f, 0f, 0.27f }) { Window(model, rng, new(x, 0, Back), -Vector3.UnitZ, lower); Window(model, rng, new(x, 0, Back), -Vector3.UnitZ, upper); Window(model, rng, new(x, 0, Split), Vector3.UnitZ, upper); }
        foreach (var sx in new[] { -1f, 1f })
            foreach (var z in new[] { -0.29f, -0.02f }) { Window(model, rng, new(sx * Side, 0, z), Vector3.UnitX * sx, lower); Window(model, rng, new(sx * Side, 0, z), Vector3.UnitX * sx, upper); }
        foreach (var x in new[] { -0.24f, 0.24f }) Window(model, rng, new(x, 0, Front), Vector3.UnitZ, (0.17f, 0.35f));
        foreach (var sx in new[] { -1f, 1f }) Window(model, rng, new(sx * WingSide, 0, 0.3f), Vector3.UnitX * sx, (0.17f, 0.35f));
        // The door: a steel leaf with a frame, a hood over it and a step before it.
        Shapes.Box(model[Metal], new Vector3(-0.065f, Plinth, Front), new Vector3(0.065f, 0.33f, Front + 0.006f), p => Shade(Rgb(0.3f, 0.36f, 0.36f), p.Y > 0.18f && p.Y < 0.2f ? 0.7f : 1));
        Shapes.Box(model[Metal], new Vector3(-0.004f, Plinth + 0.01f, Front + 0.006f), new Vector3(0.004f, 0.32f, Front + 0.009f), Steel);
        Shapes.Box(model[Trim], new Vector3(-0.085f, Plinth, Front), new Vector3(-0.065f, 0.35f, Front + 0.018f), Dark);
        Shapes.Box(model[Trim], new Vector3(0.065f, Plinth, Front), new Vector3(0.085f, 0.35f, Front + 0.018f), Dark);
        Shapes.Box(model[Trim], new Vector3(-0.11f, 0.35f, Front - 0.005f), new Vector3(0.11f, 0.37f, Front + 0.05f), Slate);
        Shapes.Box(model[Trim], new Vector3(-0.1f, Plinth, Front), new Vector3(0.1f, Plinth + 0.02f, 0.49f), Dark);
        // Roof: two vents, a hatch, a run of pipe and a mast.
        foreach (var (x, z) in new[] { (-0.25f, -0.25f), (0.2f, -0.3f) })
        {
            Shapes.Prism(model[Metal], Shapes.At(new Vector3(x, MainTop + 0.012f, z), new Vector3(0.035f, 0.05f, 0.035f)), 10, 1, Steel);
            Shapes.Prism(model[Metal], Shapes.At(new Vector3(x, MainTop + 0.062f, z), new Vector3(0.045f, 0.012f, 0.045f)), 10, 0.6f, Shade(Steel, 0.8f));
        }
        Shapes.Box(model[Metal], new Vector3(0.1f, MainTop + 0.012f, -0.08f), new Vector3(0.26f, MainTop + 0.05f, 0.04f), Shade(Steel, 0.9f));
        Shapes.Box(model[Trim], new Vector3(-0.3f, MainTop + 0.012f, -0.1f), new Vector3(-0.16f, MainTop + 0.045f, 0.0f), Dark);
        Shapes.Box(model[Metal], new Vector3(-0.41f, MainTop + 0.012f, -0.36f), new Vector3(-0.39f, MainTop + 0.032f, 0.08f), Shade(Steel, 0.7f));
        Shapes.Prism(model[Metal], Shapes.At(new Vector3(0.35f, MainTop + 0.012f, -0.35f), new Vector3(0.006f, 0.99f - MainTop - 0.012f, 0.006f)), 5, 0.4f, Steel);
        return model;
    }

    /// <summary>A block faced in courses: horizontal bands 0.055 high, each its own shade, so the masonry reads at table distance.</summary>
    static void Courses(Model model, Random rng, Vector3 min, Vector3 max)
    {
        for (var y = min.Y; y < max.Y - 1e-4f; y += 0.055f)
        {
            var top = MathF.Min(y + 0.055f, max.Y);
            var shade = 0.9f + 0.14f * (float)rng.NextDouble();
            Shapes.Box(model[Stone], new Vector3(min.X, y, min.Z), new Vector3(max.X, top, max.Z), p => Shade(Wall, shade * (0.92f + 0.12f * Fbm(p * 18, 2))));
        }
    }

    static void Parapet(Model model, float x0, float x1, float z0, float z1, float y0, float y1, float t, bool back = true)
    {
        Shapes.Box(model[Trim], new Vector3(x0, y0, z1 - t), new Vector3(x1, y1, z1), Dark);
        if (back) Shapes.Box(model[Trim], new Vector3(x0, y0, z0), new Vector3(x1, y1, z0 + t), Dark);
        Shapes.Box(model[Trim], new Vector3(x0, y0, z0), new Vector3(x0 + t, y1, z1), Dark);
        Shapes.Box(model[Trim], new Vector3(x1 - t, y0, z0), new Vector3(x1, y1, z1), Dark);
    }

    /// <summary>A window on the face through <paramref name="at"/> facing <paramref name="normal"/>: glass with a mullion, jambs, sill and lintel.</summary>
    static void Window(Model model, Random rng, Vector3 at, Vector3 normal, (float Bottom, float Top) rows)
    {
        var tangent = new Vector3(-normal.Z, 0, normal.X);
        const float half = 0.05f;
        Vector3 P(float t, float y, float n) => new Vector3(at.X, 0, at.Z) + tangent * t + Vector3.UnitY * y + normal * n;
        void Slab(Material m, float t0, float t1, float y0, float y1, float n0, float n1, Vector4 c)
        {
            var (a, b) = (P(t0, y0, n0), P(t1, y1, n1));
            Shapes.Box(model[m], Vector3.Min(a, b), Vector3.Max(a, b), c);
        }
        var lit = rng.NextDouble() < 0.22;
        Slab(lit ? Lit : Glass, -half, half, rows.Bottom, rows.Top, -0.004f, 0.004f, lit ? Rgb(0.95f, 0.78f, 0.5f) : Rgb(0.1f, 0.13f, 0.17f));
        Slab(Trim, -0.004f, 0.004f, rows.Bottom, rows.Top, 0, 0.008f, Dark);
        Slab(Trim, -half - 0.014f, -half, rows.Bottom, rows.Top, -0.002f, 0.012f, Dark);
        Slab(Trim, half, half + 0.014f, rows.Bottom, rows.Top, -0.002f, 0.012f, Dark);
        Slab(Trim, -half - 0.02f, half + 0.02f, rows.Bottom - 0.018f, rows.Bottom, -0.002f, 0.022f, Dark);
        Slab(Trim, -half - 0.018f, half + 0.018f, rows.Top, rows.Top + 0.022f, -0.002f, 0.016f, Dark);
    }
}
