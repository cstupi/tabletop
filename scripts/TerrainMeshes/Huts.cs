using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A group of small buildings (6" × 6", 4" high, Solid): three prefab habs on a packed-earth yard. A ribbed half-cylinder
/// hut, a low box hab with a sloped roof, and a two-storey hab with a water tank and a mast reaching the top; doors, shuttered
/// windows, vents, and crates and drums between them.</summary>
public static class Huts
{
    public static readonly Material Ground = new("yard", 0.95f), Panel = new("panel", 0.7f, Metallic: 0.3f), Trim = new("trim", 0.6f, Metallic: 0.4f),
        Glass = new("glass", 0.15f, Metallic: 0.2f), Lit = new("lit-window", 0.3f, Emissive: new Vector3(0.95f, 0.7f, 0.38f)), Crate = new("crate", 0.85f);
    const float Size = 6, Top = 4;

    static readonly Vector4 Olive = Rgb(0.42f, 0.45f, 0.32f), Sand = Rgb(0.68f, 0.6f, 0.44f), Slate = Rgb(0.42f, 0.47f, 0.52f), Steel = Rgb(0.32f, 0.33f, 0.35f), Hazard = Rgb(0.85f, 0.66f, 0.12f);

    public static Model Build()
    {
        var model = new Model("huts", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(421);
        var earth = Rgb(0.46f, 0.4f, 0.31f); var gravel = Rgb(0.55f, 0.52f, 0.46f);
        Shapes.Ground(model, Ground, 24, 24, (x, z) => 0.06f * Smooth(0, 0.2f, 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z))),
            (p, n) => Shade(Mix(earth, gravel, Smooth(0.45f, 0.6f, Fbm(p.X, p.Z, 0, 421))), 0.85f + 0.2f * Fbm(p.X * 4, p.Z * 4, 1, 422)));
        Quonset(model, new Vector3(-1.35f, 0.06f, -1.25f), 2.9f, 1.15f);
        Box(model, rng, new Vector3(1.55f, 0.06f, -1.35f), new Vector3(2.4f, 1.55f, 2.0f), Sand, storeys: 1);
        Box(model, rng, new Vector3(0.05f, 0.06f, 1.55f), new Vector3(2.0f, 2.9f, 1.9f), Slate, storeys: 2);
        // The water tank on the tall hab's roof and the mast to the top.
        var roof = new Vector3(0.05f, 0.06f + 2.9f, 1.55f);
        foreach (var (dx, dz) in new[] { (-0.35f, -0.3f), (0.35f, -0.3f), (-0.35f, 0.3f), (0.35f, 0.3f) })
            Shapes.Box(model[Trim], new Vector3(roof.X - 0.45f + dx - 0.04f, roof.Y, roof.Z + dz - 0.04f) + new Vector3(0.45f, 0, 0), new Vector3(roof.X - 0.45f + dx + 0.04f, roof.Y + 0.35f, roof.Z + dz + 0.04f) + new Vector3(0.45f, 0, 0), Steel);
        Shapes.Prism(model[Panel], Shapes.At(roof + new Vector3(0, 0.35f, 0), new Vector3(0.5f, 0.45f, 0.5f)), 12, 1, p => Shade(Olive, 0.9f + 0.15f * MathF.Floor((p.Y - roof.Y) * 8) % 2));
        Shapes.Prism(model[Panel], Shapes.At(roof + new Vector3(0, 0.8f, 0), new Vector3(0.52f, 0.12f, 0.52f)), 12, 0.3f, Shade(Olive, 0.8f));
        var mast = roof + new Vector3(0.75f, 0, 0.6f);
        Shapes.Limb(model[Trim], mast, mast with { Y = Top }, 0.04f, 0.5f, 5, Steel);
        foreach (var y in new[] { 3.4f, 3.75f }) Shapes.Box(model[Trim], new Vector3(mast.X - 0.18f, y, mast.Z - 0.015f), new Vector3(mast.X + 0.18f, y + 0.03f, mast.Z + 0.015f), Steel);
        Shapes.Prism(model[Lit], Shapes.At(mast with { Y = Top - 0.06f }, new Vector3(0.04f, 0.06f, 0.04f)), 6, 1, Rgb(1, 0.3f, 0.2f));
        // Crates and drums in the yard.
        var wood = Rgb(0.5f, 0.38f, 0.22f);
        foreach (var (at, s, yaw) in new[] { (new Vector3(-2.2f, 0.06f, 0.45f), 0.45f, 0.2f), (new Vector3(-1.72f, 0.06f, 0.55f), 0.35f, -0.3f), (new Vector3(-2.05f, 0.51f, 0.48f), 0.32f, 0.5f), (new Vector3(2.3f, 0.06f, 1.0f), 0.4f, 0.1f) })
        {
            Shapes.Box(model[Crate], Shapes.At(at + Vector3.UnitY * s / 2, new Vector3(s), yaw), p => Shade(wood, 0.85f + 0.15f * Noise(p.X * 9, p.Y * 9, p.Z * 9, 7)));
            Shapes.Box(model[Crate], Shapes.At(at + Vector3.UnitY * s / 2, new Vector3(s * 1.02f, s * 0.12f, s * 1.02f), yaw), Shade(wood, 0.7f));
        }
        foreach (var (x, z) in new[] { (1.95f, 1.55f), (2.3f, 1.65f), (2.15f, 1.95f) })
        {
            Shapes.Prism(model[Panel], Shapes.At(new Vector3(x, 0.06f, z), new Vector3(0.15f, 0.42f, 0.15f)), 10, 1, Rgb(0.55f, 0.24f, 0.14f));
            Shapes.Prism(model[Panel], Shapes.At(new Vector3(x, 0.2f, z), new Vector3(0.155f, 0.05f, 0.155f)), 10, 1, Hazard);
        }
        return model;
    }

    /// <summary>A half-cylinder hut along x from <paramref name="at"/> (its centre at ground), corrugated in ribs, with end walls,
    /// a door between two windows at the +x end, and a vent on its crown.</summary>
    static void Quonset(Model model, Vector3 at, float length, float radius)
    {
        const int arc = 14;
        for (var i = 0; i < arc; i++)
        {
            float a0 = MathF.PI * i / arc, a1 = MathF.PI * (i + 1) / arc;
            Vector3 P(float a, float x) => at + new Vector3(x, radius * MathF.Sin(a), radius * MathF.Cos(a));
            for (var r = 0; r < 8; r++)
            {
                float x0 = -length / 2 + length * r / 8, x1 = x0 + length / 8;
                var c = Shade(Olive, (r % 2 == 0 ? 0.95f : 0.85f) * (0.85f + 0.2f * MathF.Sin(a0 + 0.5f)));
                Shapes.Oriented(model[Panel], [P(a0, x0), P(a1, x0), P(a1, x1), P(a0, x1)], P((a0 + a1) / 2, (x0 + x1) / 2) - (at with { X = at.X + (x0 + x1) / 2 }), _ => c);
            }
            foreach (var s in new[] { -1f, 1f })   // the end walls, a fan
            {
                var x = s * length / 2;
                Shapes.Oriented(model[Trim], [at + new Vector3(x, 0, 0), P(a0, x), P(a1, x)], new Vector3(s, 0, 0), _ => Shade(Steel, 1.2f));
            }
        }
        foreach (var r in new[] { 0, 2, 4, 6, 8 })   // rib bands
        {
            var x = -length / 2 + length * r / 8;
            for (var i = 0; i < arc; i++)
            {
                float a0 = MathF.PI * i / arc, a1 = MathF.PI * (i + 1) / arc;
                Shapes.Box(model[Trim], Shapes.At(at + new Vector3(x, radius * MathF.Sin((a0 + a1) / 2), radius * MathF.Cos((a0 + a1) / 2)) * 1, new Vector3(0.06f, 0.06f, radius * MathF.PI / arc + 0.01f), 0, -(a0 + a1) / 2, 0), Shade(Olive, 0.65f));
            }
        }
        var end = at.X + length / 2;
        Shapes.Box(model[Trim], new Vector3(end, at.Y, at.Z - 0.25f), new Vector3(end + 0.04f, at.Y + 0.8f, at.Z + 0.25f), Rgb(0.25f, 0.27f, 0.25f));
        Shapes.Box(model[Panel], new Vector3(end, at.Y + 0.8f, at.Z - 0.3f), new Vector3(end + 0.05f, at.Y + 0.86f, at.Z + 0.3f), Hazard);
        Shapes.Box(model[Glass], new Vector3(end, at.Y + 0.4f, at.Z + 0.45f), new Vector3(end + 0.03f, at.Y + 0.65f, at.Z + 0.7f), Rgb(0.12f, 0.15f, 0.18f));
        Shapes.Box(model[Glass], new Vector3(end, at.Y + 0.4f, at.Z - 0.7f), new Vector3(end + 0.03f, at.Y + 0.65f, at.Z - 0.45f), Rgb(0.12f, 0.15f, 0.18f));
        Shapes.Prism(model[Trim], Shapes.At(at + new Vector3(-0.5f, radius - 0.05f, 0), new Vector3(0.1f, 0.3f, 0.1f)), 8, 1, Steel);
        Shapes.Prism(model[Trim], Shapes.At(at + new Vector3(-0.5f, radius + 0.25f, 0), new Vector3(0.16f, 0.05f, 0.16f)), 8, 0.5f, Steel);
    }

    /// <summary>A box hab centred at <paramref name="at"/> (its foot) of <paramref name="size"/>: panelled walls with seams, a plinth,
    /// a roof sloped down to the front (−z) with an overhang, a door on the front, windows with shutters on every side.</summary>
    static void Box(Model model, Random rng, Vector3 at, Vector3 size, Vector4 paint, int storeys)
    {
        var (hx, hz) = (size.X / 2, size.Z / 2);
        Shapes.Box(model[Trim], at + new Vector3(-hx - 0.05f, 0, -hz - 0.05f), at + new Vector3(hx + 0.05f, 0.12f, hz + 0.05f), Shade(Steel, 1.2f));
        var panels = (int)MathF.Round(size.X / 0.5f);
        for (var i = 0; i < panels; i++)   // front and back panels, each its own shade, seams between
        {
            float x0 = -hx + size.X * i / panels + 0.012f, x1 = -hx + size.X * (i + 1) / panels - 0.012f;
            var c = Shade(paint, 0.9f + 0.12f * (float)rng.NextDouble());
            Shapes.Box(model[Panel], at + new Vector3(x0, 0.12f, -hz), at + new Vector3(x1, size.Y, hz), c);
        }
        Shapes.Box(model[Trim], at + new Vector3(-hx + 0.01f, 0.12f, -hz + 0.02f), at + new Vector3(hx - 0.01f, size.Y, hz - 0.02f), Shade(paint, 0.6f));   // seams
        for (var s = 1; s < storeys; s++)
            Shapes.Box(model[Trim], at + new Vector3(-hx - 0.03f, size.Y * s / storeys - 0.04f, -hz - 0.03f), at + new Vector3(hx + 0.03f, size.Y * s / storeys + 0.04f, hz + 0.03f), Shade(paint, 0.7f));
        // The roof: a slab sloping to the front, overhanging all round, with a vent box.
        var roof = at + Vector3.UnitY * size.Y;
        Shapes.Box(model[Trim], Shapes.At(roof + new Vector3(0, 0.08f, 0), new Vector3(size.X + 0.25f, 0.08f, size.Z + 0.3f), 0, 0.1f, 0), Shade(Steel, 0.9f));
        Shapes.Box(model[Trim], roof + new Vector3(-hx + 0.25f, 0.1f, -0.2f), roof + new Vector3(-hx + 0.65f, 0.38f, 0.2f), Steel);
        // Door (front, −z) with a hazard-striped frame and a step.
        Shapes.Box(model[Trim], at + new Vector3(-0.28f, 0.12f, -hz - 0.04f), at + new Vector3(0.28f, 1.1f, -hz), Rgb(0.2f, 0.21f, 0.22f));
        Shapes.Box(model[Panel], at + new Vector3(-0.34f, 1.1f, -hz - 0.05f), at + new Vector3(0.34f, 1.18f, -hz), Hazard);
        Shapes.Box(model[Trim], at + new Vector3(-0.36f, 0, -hz - 0.3f), at + new Vector3(0.36f, 0.12f, -hz), Shade(Steel, 1.1f));
        // Windows on each storey, every side, with shutters; some lit.
        for (var s = 0; s < storeys; s++)
        {
            var y0 = size.Y * s / storeys + 0.55f; var y1 = y0 + 0.4f;
            foreach (var (n, along, half) in new[] { (-Vector3.UnitZ, Vector3.UnitX, hx), (Vector3.UnitZ, Vector3.UnitX, hx), (Vector3.UnitX, Vector3.UnitZ, hz), (-Vector3.UnitX, Vector3.UnitZ, hz) })
            {
                var depth = n.X != 0 ? hx : hz;
                foreach (var u in new[] { -half * 0.55f, half * 0.55f })
                {
                    if (s == 0 && n == -Vector3.UnitZ && MathF.Abs(u) < 0.6f) continue;
                    var centre = at + n * depth + along * u;
                    var lit = rng.NextDouble() < 0.25;
                    Vector3 Lo(float a, float y, float d) => centre + along * a + Vector3.UnitY * y + n * d;
                    Shapes.Box(model[lit ? Lit : Glass], Vector3.Min(Lo(-0.18f, y0, 0), Lo(0.18f, y1, 0.02f)), Vector3.Max(Lo(-0.18f, y0, 0), Lo(0.18f, y1, 0.02f)), lit ? Rgb(0.95f, 0.78f, 0.5f) : Rgb(0.1f, 0.13f, 0.17f));
                    Shapes.Box(model[Trim], Vector3.Min(Lo(-0.22f, y1, 0), Lo(0.22f, y1 + 0.06f, 0.06f)), Vector3.Max(Lo(-0.22f, y1, 0), Lo(0.22f, y1 + 0.06f, 0.06f)), Shade(paint, 0.6f));
                    Shapes.Box(model[Trim], Vector3.Min(Lo(-0.22f, y0 - 0.04f, 0), Lo(0.22f, y0, 0.05f)), Vector3.Max(Lo(-0.22f, y0 - 0.04f, 0), Lo(0.22f, y0, 0.05f)), Shade(paint, 0.6f));
                }
            }
        }
    }
}
