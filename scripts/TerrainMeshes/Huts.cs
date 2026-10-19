using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A group of small buildings (6" × 6", 4" high, Solid): three prefab habs on a packed-earth yard. A corrugated
/// half-cylinder hut with ribbed arches and a door end; a one-storey box hab and a two-storey one, both of raised riveted panels
/// on corner posts, with framed and shuttered windows, hazard-striped doors on grated steps, roof lips, vents and pipes; a ladder
/// and a water tank on the tall hab and a mast to the top; plank crates, drums and sandbags in the yard.</summary>
public static class Huts
{
    public static readonly Material Ground = new("yard", 0.95f), Panel = new("panel", 0.7f, Metallic: 0.1f), Trim = new("trim", 0.65f, Metallic: 0.25f),
        Glass = new("glass", 0.15f, Metallic: 0.2f), Lit = new("lit-window", 0.3f, Emissive: new Vector3(0.95f, 0.7f, 0.38f)), Crate = new("crate", 0.85f);
    const float Size = 6, Top = 4, Yard = 0.06f;

    static readonly Vector4 Olive = Rgb(0.46f, 0.49f, 0.34f), Sand = Rgb(0.74f, 0.66f, 0.48f), Slate = Rgb(0.52f, 0.58f, 0.63f), Steel = Rgb(0.42f, 0.43f, 0.45f),
        Hazard = Rgb(0.85f, 0.66f, 0.12f), Black = Rgb(0.12f, 0.12f, 0.12f), Rust = Rgb(0.45f, 0.26f, 0.13f), Dust = Rgb(0.5f, 0.43f, 0.32f);

    /// <summary>Paint worn at the foot: dust and rust creeping up from the ground, a little noise.</summary>
    static Vector4 Worn(Vector4 c, Vector3 p) =>
        Shade(Mix(Mix(c, Rust, Smooth(0.6f, 0.78f, Fbm(p * 5f, 421)) * 0.3f), Dust, Smooth(0.6f, Yard, p.Y) * 0.5f), 0.93f + 0.12f * Fbm(p * 4, 422));

    public static Model Build()
    {
        var model = new Model("huts", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(421);
        float R() => (float)rng.NextDouble();
        var earth = Rgb(0.46f, 0.4f, 0.31f); var gravel = Rgb(0.55f, 0.52f, 0.46f);
        Shapes.Ground(model, Ground, 30, 30, (x, z) => Yard * Smooth(0, 0.2f, 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z))) + 0.02f * (Fbm(x * 2, z * 2, 0, 423) - 0.5f) * Smooth(0, 0.3f, 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z))),
            (p, n) => Shade(Mix(earth, gravel, Smooth(0.45f, 0.6f, Fbm(p.X, p.Z, 0, 421))), 0.85f + 0.2f * Fbm(p.X * 4, p.Z * 4, 1, 422)));
        // Stones in the yard.
        for (var k = 0; k < 40; k++)
        {
            var p = new Vector3(-2.8f + 5.6f * R(), Yard, -2.8f + 5.6f * R());
            var s = 0.05f + 0.07f * R();
            Masonry.Block(model[Crate], p, new(s * 1.3f, s * 0.8f, s), Masonry.Turn(R() * 6, R() - 0.5f, R() - 0.5f), s * 0.2f, Shade(gravel, 0.75f + 0.3f * R()), rng, 0.3f);
        }
        Quonset(model, rng, new Vector3(-1.3f, Yard, -1.25f), 2.9f, 1.15f);
        Hab(model, rng, new Vector3(1.6f, Yard, -1.3f), new Vector3(2.3f, 1.55f, 2.1f), Sand, storeys: 1);
        Hab(model, rng, new Vector3(0.1f, Yard, 1.55f), new Vector3(2.0f, 2.9f, 1.9f), Slate, storeys: 2);
        TallHabRoof(model, rng, new Vector3(0.1f, Yard + 2.9f, 1.55f));
        Yardwork(model, rng);
        return model;
    }

    /// <summary>A half-cylinder hut along x: corrugated sheets (ridges and troughs round the arch), ribs over them, end walls of
    /// plates, a door and two framed windows at the +x end, a vent stack on the crown and sandbags along its foot.</summary>
    static void Quonset(Model model, Random rng, Vector3 at, float length, float radius)
    {
        float R() => (float)rng.NextDouble();
        const int arc = 18, flutes = 44;
        var panel = model[Panel]; var trim = model[Trim];
        for (var f = 0; f < flutes; f++)
        {
            float x0 = -length / 2 + length * f / flutes, x1 = x0 + length / flutes, xm = (x0 + x1) / 2;
            var sheet = (int)(f / (flutes / 4f));
            var c = Shade(Olive, 0.88f + 0.06f * sheet % 2 + 0.03f * (sheet % 3));
            for (var i = 0; i < arc; i++)
            {
                float a0 = MathF.PI * i / arc, a1 = MathF.PI * (i + 1) / arc;
                Vector3 P(float a, float x, float r) => at + new Vector3(x, r * MathF.Sin(a), r * MathF.Cos(a));
                var ridge = radius + 0.035f;
                // Two slopes per flute: up from the trough at x0 to the ridge at xm, down to the trough at x1.
                Masonry.Face(panel, [P(a0, x0, radius), P(a1, x0, radius), P(a1, xm, ridge), P(a0, xm, ridge)], P((a0 + a1) / 2, xm, radius) - (at with { X = at.X + xm }) - Vector3.UnitX * 0.05f, p => Worn(c, p));
                Masonry.Face(panel, [P(a0, xm, ridge), P(a1, xm, ridge), P(a1, x1, radius), P(a0, x1, radius)], P((a0 + a1) / 2, xm, radius) - (at with { X = at.X + xm }) + Vector3.UnitX * 0.05f, p => Worn(c, p));
            }
        }
        foreach (var r in new[] { 0, 2, 4 })   // ribs over the sheets
        {
            var x = -length / 2 + 0.12f + (length - 0.24f) * r / 4;
            for (var i = 0; i < arc; i++)
            {
                var a = MathF.PI * (i + 0.5f) / arc;
                Masonry.Block(trim, at + new Vector3(x, (radius + 0.06f) * MathF.Sin(a), (radius + 0.06f) * MathF.Cos(a)), new(0.1f, 0.06f, radius * MathF.PI / arc + 0.012f), Matrix4x4.CreateRotationX(-a + MathF.PI / 2), 0.015f, p => Worn(Shade(Olive, 0.7f), p), corners: false);
            }
        }
        foreach (var s in new[] { -1f, 1f })
        {
            var x = at.X + s * length / 2;
            // The end wall: a fan of plates, its rim a band.
            for (var i = 0; i < arc; i++)
            {
                float a0 = MathF.PI * i / arc, a1 = MathF.PI * (i + 1) / arc;
                Masonry.Face(trim, [new(x, at.Y, at.Z), new(x, at.Y + radius * MathF.Sin(a0), at.Z + radius * MathF.Cos(a0)), new(x, at.Y + radius * MathF.Sin(a1), at.Z + radius * MathF.Cos(a1))], new(s, 0, 0), p => Worn(Shade(Steel, 1.2f), p));
            }
            for (var k = -2; k <= 2; k++)   // plate seams
                Masonry.Block(trim, new(x + s * 0.01f, at.Y + radius * 0.45f, at.Z + k * 0.42f), new(0.03f, radius * 0.9f * MathF.Sqrt(MathF.Max(0.1f, 1 - k * k * 0.15f)), 0.04f), Matrix4x4.Identity, 0.01f, Shade(Steel, 0.9f), corners: false);
        }
        var end = at.X + length / 2;
        Door(model, rng, new Vector3(end, at.Y, at.Z), Vector3.UnitX, 0.5f, 0.95f);
        foreach (var z in new[] { -0.62f, 0.62f })
            Window(model, rng, new Vector3(end, at.Y + 0.42f, at.Z + z), Vector3.UnitX, 0.24f, 0.24f, Shade(Steel, 0.9f));
        Shapes.Prism(trim, Matrix4x4.CreateScale(0.1f, 0.4f, 0.1f) * Matrix4x4.CreateTranslation(at + new Vector3(-0.6f, radius - 0.05f, 0.1f)), 10, 1, p => Worn(Steel, p));
        Shapes.Prism(trim, Matrix4x4.CreateScale(0.17f, 0.08f, 0.17f) * Matrix4x4.CreateTranslation(at + new Vector3(-0.6f, radius + 0.35f, 0.1f)), 10, 0.4f, Shade(Steel, 0.8f));
        // Sandbags along the -z foot.
        for (var x = -length / 2 + 0.15f; x < length / 2 - 0.2f; x += 0.38f)
            Bag(model, rng, at + new Vector3(x + 0.19f, 0, -radius - 0.12f), 0.05f * (R() - 0.5f));
    }

    /// <summary>A sandbag: a pillow of cloth, tied at one end.</summary>
    static void Bag(Model model, Random rng, Vector3 at, float yaw)
    {
        var c = Shade(Mix(Rgb(0.62f, 0.56f, 0.4f), Rgb(0.5f, 0.48f, 0.35f), (float)rng.NextDouble()), 0.85f + 0.2f * (float)rng.NextDouble());
        Shapes.Blob(model[Crate], at + Vector3.UnitY * 0.08f, new Vector3(0.19f, 0.085f, 0.13f), 1, 0.1f, rng.Next(999), (q, n) => Shade(c, 0.85f + 0.2f * n.Y), yaw: yaw, shape: q => q with { Y = MathF.Max(q.Y, at.Y + 0.01f) });
    }

    /// <summary>A box hab with its foot centred at <paramref name="at"/>: corner posts, raised panels with recessed seams and rows of
    /// rivets on every face, a storey band, a roof with a lip, a door with steps on the front (−z), framed and shuttered windows,
    /// a wall-mounted cooling unit and a pipe run.</summary>
    static void Hab(Model model, Random rng, Vector3 at, Vector3 size, Vector4 paint, int storeys)
    {
        float R() => (float)rng.NextDouble();
        var panel = model[Panel]; var trim = model[Trim];
        var (hx, hz) = (size.X / 2, size.Z / 2);
        Masonry.Span(trim, at + new Vector3(-hx + 0.04f, 0, -hz + 0.04f), at + new Vector3(hx - 0.04f, size.Y, hz - 0.04f), 0, Shade(paint, 0.45f));
        Masonry.Span(trim, at + new Vector3(-hx - 0.06f, 0, -hz - 0.06f), at + new Vector3(hx + 0.06f, 0.14f, hz + 0.06f), 0.025f, p => Worn(Shade(Steel, 1.1f), p));
        var faces = new[] { (-Vector3.UnitZ, Vector3.UnitX, hx, hz), (Vector3.UnitZ, -Vector3.UnitX, hx, hz), (Vector3.UnitX, Vector3.UnitZ, hz, hx), (-Vector3.UnitX, -Vector3.UnitZ, hz, hx) };
        foreach (var (n, along, half, depth) in faces)
        {
            var rot = Masonry.Basis(along, Vector3.UnitY, n);
            var cols = Math.Max(2, (int)MathF.Round(2 * half / 0.55f));
            for (var s = 0; s < storeys; s++)
            {
                float y0 = 0.14f + (size.Y - 0.14f) * s / storeys, y1 = 0.14f + (size.Y - 0.14f) * (s + 1) / storeys;
                for (var i = 0; i < cols; i++)
                {
                    float u0 = -half + 2 * half * i / cols, u1 = u0 + 2 * half / cols;
                    var c = Shade(paint, 0.88f + 0.14f * R());
                    var centre = at + n * depth + along * ((u0 + u1) / 2) + Vector3.UnitY * ((y0 + y1) / 2);
                    Masonry.Block(panel, centre, new(u1 - u0 - 0.035f, y1 - y0 - 0.05f, 0.06f), rot, 0.018f, p => Worn(c, p), corners: false);
                    // Rivets along the panel's top and bottom.
                    foreach (var y in new[] { y0 + 0.06f, y1 - 0.06f })
                        for (var k = 0; k < 3; k++)
                            Masonry.Block(trim, at + n * (depth + 0.035f) + along * (u0 + (u1 - u0) * (k + 1) / 4) + Vector3.UnitY * y, new(0.025f, 0.025f, 0.02f), rot, 0, Shade(paint, 0.75f));
                }
            }
        }
        // Corner posts and the storey band.
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            Masonry.Block(trim, at + new Vector3(sx * hx, size.Y / 2 + 0.07f, sz * hz), new(0.14f, size.Y - 0.14f, 0.14f), Matrix4x4.Identity, 0.025f, p => Worn(Shade(paint, 0.65f), p), corners: false);
        for (var s = 1; s < storeys; s++)
            Masonry.Span(trim, at + new Vector3(-hx - 0.05f, size.Y * s / storeys - 0.05f, -hz - 0.05f), at + new Vector3(hx + 0.05f, size.Y * s / storeys + 0.05f, hz + 0.05f), 0.02f, Shade(paint, 0.65f));
        // The roof: a slab with a lip all round and a louvred vent box.
        var roof = at + Vector3.UnitY * size.Y;
        Masonry.Span(trim, roof + new Vector3(-hx - 0.1f, 0, -hz - 0.1f), roof + new Vector3(hx + 0.1f, 0.08f, hz + 0.1f), 0.025f, Shade(Steel, 0.9f));
        foreach (var (n, along, half, depth) in faces)
            Masonry.Block(trim, roof + n * (depth + 0.06f) + Vector3.UnitY * 0.13f, new(2 * half + 0.24f, 0.1f, 0.06f), Masonry.Basis(along, Vector3.UnitY, n), 0.015f, Shade(Steel, 0.8f), corners: false);
        var vent = roof + new Vector3(-hx + 0.5f, 0.08f, 0);
        Masonry.Block(trim, vent + Vector3.UnitY * 0.16f, new(0.5f, 0.32f, 0.45f), Matrix4x4.Identity, 0.025f, p => Worn(Steel, p));
        for (var k = 0; k < 3; k++)
            Masonry.Block(trim, vent + new Vector3(0, 0.08f + 0.09f * k, 0.24f), new(0.42f, 0.025f, 0.05f), Matrix4x4.CreateRotationX(0.5f), 0.006f, Shade(Steel, 0.55f), corners: false);
        // The door on the front, and windows round every storey (the door's bay left clear on the ground floor).
        Door(model, rng, at + new Vector3(0, 0, -hz), -Vector3.UnitZ, 0.62f, 1.05f);
        for (var s = 0; s < storeys; s++)
        {
            var y = 0.14f + (size.Y - 0.14f) * s / storeys + 0.6f;
            foreach (var (n, along, half, depth) in faces)
                foreach (var u in new[] { -half * 0.55f, half * 0.55f })
                {
                    if (s == 0 && n == -Vector3.UnitZ && MathF.Abs(u) < 0.6f) continue;
                    Window(model, rng, at + n * depth + along * u + Vector3.UnitY * y, n, 0.36f, 0.36f, Shade(paint, 0.6f));
                }
        }
        // A cooling unit on the +x face with a fan grille, and a pipe from it up to the roof.
        var unit = at + new Vector3(hx + 0.2f, 0.45f, hz * 0.2f);
        Masonry.Block(trim, unit, new(0.36f, 0.55f, 0.6f), Matrix4x4.Identity, 0.03f, p => Worn(Shade(Steel, 1.1f), p));
        Shapes.Prism(trim, Matrix4x4.CreateScale(0.2f, 0.03f, 0.2f) * Matrix4x4.CreateRotationZ(-MathF.PI / 2) * Matrix4x4.CreateTranslation(unit + new Vector3(0.18f, 0.02f, 0)), 12, 1, Black);
        for (var k = -2; k <= 2; k++)
            Masonry.Block(trim, unit + new Vector3(0.21f, 0.02f, k * 0.07f), new(0.02f, 0.4f, 0.02f), Matrix4x4.Identity, 0.005f, Shade(Steel, 0.8f), corners: false);
        Shapes.Chain(trim, [unit + new Vector3(0, 0.27f, 0.2f), unit + new Vector3(0, size.Y - 0.6f, 0.2f), unit + new Vector3(-0.2f, size.Y - 0.3f, 0.2f)], 0.045f, 0.045f, 6, p => Worn(Steel, p));
    }

    /// <summary>A door on a wall at <paramref name="foot"/> facing <paramref name="n"/>: a frame, a plated leaf with a vision slit,
    /// a hazard-striped head, a lamp over it, and a grated step.</summary>
    static void Door(Model model, Random rng, Vector3 foot, Vector3 n, float width, float height)
    {
        var trim = model[Trim]; var panel = model[Panel];
        var along = Vector3.Cross(Vector3.UnitY, n);
        var rot = Masonry.Basis(along, Vector3.UnitY, n);
        Vector3 P(float u, float y, float d) => foot + along * u + Vector3.UnitY * y + n * d;
        Masonry.Block(trim, P(0, 0.14f + height / 2, 0.03f), new(width, height, 0.05f), rot, 0.012f, p => Worn(Rgb(0.22f, 0.23f, 0.24f), p));
        Masonry.Block(trim, P(0, 0.14f + height * 0.72f, 0.06f), new(width * 0.5f, 0.06f, 0.02f), rot, 0.008f, Black, corners: false);
        foreach (var y in new[] { 0.35f, 0.62f })
            Masonry.Block(trim, P(0, 0.14f + height * y, 0.06f), new(width * 0.8f, 0.05f, 0.02f), rot, 0.008f, Rgb(0.3f, 0.31f, 0.32f), corners: false);
        foreach (var s in new[] { -1f, 1f })
            Masonry.Block(trim, P(s * (width / 2 + 0.04f), 0.14f + height / 2 + 0.04f, 0.05f), new(0.08f, height + 0.08f, 0.1f), rot, 0.015f, Shade(Steel, 0.8f), corners: false);
        Masonry.Block(panel, P(0, 0.14f + height + 0.09f, 0.06f), new(width + 0.16f, 0.1f, 0.12f), rot, 0.015f,
            p => (int)MathF.Floor(Vector3.Dot(p - foot, along) * 9 + p.Y * 9) % 2 == 0 ? Hazard : Black, corners: false);
        Masonry.Block(model[Lit], P(0, 0.14f + height + 0.24f, 0.08f), new(0.12f, 0.06f, 0.06f), rot, 0.01f, Rgb(1f, 0.85f, 0.55f), corners: false);
        Masonry.Block(trim, P(0, 0.07f, 0.24f), new(width + 0.2f, 0.06f, 0.36f), rot, 0.012f, p => Worn(Steel, p), corners: false);
        for (var k = -3; k <= 3; k++)   // the grating's bars
            Masonry.Block(trim, P(k * (width + 0.1f) / 7, 0.11f, 0.24f), new(0.025f, 0.02f, 0.34f), rot, 0.004f, Black, corners: false);
    }

    /// <summary>A window centred at <paramref name="at"/> on a wall facing <paramref name="n"/>: glass (some lit) behind a frame
    /// with a cross bar, a sill and a hood, and a shutter hinged open beside it.</summary>
    static void Window(Model model, Random rng, Vector3 at, Vector3 n, float w, float h, Vector4 frame)
    {
        var trim = model[Trim];
        var along = Vector3.Cross(Vector3.UnitY, n);
        var rot = Masonry.Basis(along, Vector3.UnitY, n);
        Vector3 P(float u, float y, float d) => at + along * u + Vector3.UnitY * y + n * d;
        var lit = rng.NextDouble() < 0.12;
        Masonry.Block(lit ? model[Lit] : model[Glass], P(0, 0, 0.03f), new(w, h, 0.03f), rot, 0, lit ? Rgb(0.95f, 0.78f, 0.5f) : Rgb(0.1f, 0.13f, 0.16f));
        foreach (var s in new[] { -1f, 1f })
        {
            Masonry.Block(trim, P(s * (w / 2 + 0.025f), 0, 0.06f), new(0.05f, h + 0.1f, 0.06f), rot, 0.01f, frame, corners: false);
            Masonry.Block(trim, P(0, s * (h / 2 + 0.025f), 0.06f), new(w, 0.05f, 0.06f), rot, 0.01f, frame, corners: false);
        }
        Masonry.Block(trim, P(0, 0, 0.055f), new(0.025f, h, 0.025f), rot, 0.005f, frame, corners: false);
        Masonry.Block(trim, P(0, 0, 0.055f), new(w, 0.025f, 0.025f), rot, 0.005f, frame, corners: false);
        Masonry.Block(trim, P(0, -h / 2 - 0.07f, 0.08f), new(w + 0.16f, 0.04f, 0.12f), rot, 0.01f, frame, corners: false);
        var side = rng.NextDouble() < 0.5 ? -1 : 1;
        var open = 0.5f + 0.9f * (float)rng.NextDouble();
        var hinge = P(side * (w / 2 + 0.06f), 0, 0.06f);
        var dir = Vector3.Normalize(along * side * MathF.Cos(open) + n * MathF.Sin(open));
        Masonry.Block(model[Panel], hinge + dir * w * 0.5f, new(0.025f, h + 0.06f, w), Masonry.Basis(Vector3.Cross(Vector3.UnitY, dir), Vector3.UnitY, dir), 0.008f, Shade(frame, 1.3f), corners: false);
    }

    /// <summary>The tall hab's roof: a water tank on legs with hoops, a ladder down the side, and the mast to the top with a lamp.</summary>
    static void TallHabRoof(Model model, Random rng, Vector3 roof)
    {
        var trim = model[Trim]; var panel = model[Panel];
        foreach (var (dx, dz) in new[] { (-0.33f, -0.3f), (0.33f, -0.3f), (-0.33f, 0.3f), (0.33f, 0.3f) })
            Masonry.Block(trim, roof + new Vector3(dx, 0.2f, dz), new(0.07f, 0.32f, 0.07f), Matrix4x4.Identity, 0.01f, Steel, corners: false);
        var tank = roof + new Vector3(0, 0.36f, 0);
        Shapes.Prism(panel, Matrix4x4.CreateScale(0.48f, 0.42f, 0.48f) * Matrix4x4.CreateTranslation(tank), 16, 1, p => Worn(Olive, p));
        Shapes.Prism(panel, Matrix4x4.CreateScale(0.5f, 0.1f, 0.5f) * Matrix4x4.CreateTranslation(tank + Vector3.UnitY * 0.42f), 16, 0.3f, Shade(Olive, 0.8f));
        foreach (var y in new[] { 0.08f, 0.33f })
            Shapes.Prism(trim, Matrix4x4.CreateScale(0.505f, 0.035f, 0.505f) * Matrix4x4.CreateTranslation(tank + Vector3.UnitY * y), 16, 1, Shade(Steel, 0.7f));
        // A ladder down the −x face.
        var x = roof.X - 1.0f - 0.08f;
        foreach (var dz in new[] { -0.15f, 0.15f })
            Masonry.Block(trim, new(x, (Yard + roof.Y + 0.3f) / 2, roof.Z + 0.35f + dz), new(0.035f, roof.Y + 0.3f - Yard, 0.035f), Matrix4x4.Identity, 0.008f, Steel, corners: false);
        for (var y = Yard + 0.2f; y < roof.Y + 0.2f; y += 0.2f)
            Masonry.Block(trim, new(x, y, roof.Z + 0.35f), new(0.03f, 0.03f, 0.3f), Matrix4x4.Identity, 0.006f, Shade(Steel, 0.9f), corners: false);
        var mast = roof + new Vector3(0.72f, 0.08f, 0.6f);
        Shapes.Limb(trim, mast, mast with { Y = Top - 0.06f }, 0.04f, 0.5f, 6, p => Worn(Steel, p));
        foreach (var y in new[] { 3.4f, 3.72f })
            Masonry.Block(trim, mast with { Y = y }, new(0.36f, 0.03f, 0.03f), Masonry.Turn(0.4f), 0.006f, Steel, corners: false);
        Shapes.Prism(model[Lit], Matrix4x4.CreateScale(0.04f, 0.06f, 0.04f) * Matrix4x4.CreateTranslation(mast with { Y = Top - 0.06f }), 6, 1, Rgb(1, 0.3f, 0.2f));
    }

    /// <summary>Plank crates, banded drums (one on its side) and a fuel can in the yard.</summary>
    static void Yardwork(Model model, Random rng)
    {
        float R() => (float)rng.NextDouble();
        var crate = model[Crate];
        var wood = Rgb(0.5f, 0.38f, 0.22f);
        foreach (var (at, s, yaw) in new[] { (new Vector3(-2.2f, Yard, 0.45f), 0.45f, 0.2f), (new Vector3(-1.72f, Yard, 0.6f), 0.36f, -0.3f), (new Vector3(-2.12f, Yard + 0.45f, 0.48f), 0.32f, 0.5f), (new Vector3(2.35f, Yard, 0.95f), 0.4f, 0.1f) })
        {
            var rot = Masonry.Turn(yaw);
            var c = Shade(wood, 0.85f + 0.2f * R());
            Masonry.Block(crate, at + Vector3.UnitY * s / 2, new(s * 0.94f), rot, 0.01f, Shade(c, 0.6f), corners: false);
            for (var k = 0; k < 3; k++)   // planks on every side, and a frame round each face
            {
                var y = at.Y + s * (k + 0.5f) / 3;
                foreach (var (dx, dz, w, d) in new[] { (0f, -1f, s, 0.03f), (0f, 1f, s, 0.03f), (-1f, 0f, 0.03f, s), (1f, 0f, 0.03f, s) })
                    Masonry.Block(crate, at with { Y = y } + Vector3.Transform(new Vector3(dx, 0, dz) * s * 0.49f, rot), new(w * 0.92f, s / 3 - 0.02f, d * 0.92f), rot, 0.01f, Shade(c, 0.9f + 0.15f * R()), corners: false);
            }
            Masonry.Block(crate, at + Vector3.UnitY * (s - 0.015f), new(s * 0.96f, 0.04f, s * 0.96f), rot, 0.012f, Shade(c, 1.1f), corners: false);
        }
        foreach (var (x, z, lying) in new[] { (1.95f, 0.15f, false), (2.3f, 0.3f, false), (2.1f, -0.15f + 0.6f, false), (-0.2f, -0.1f, true) })
        {
            var colour = R() < 0.5f ? Rgb(0.55f, 0.24f, 0.14f) : Rgb(0.3f, 0.36f, 0.26f);
            var m = lying ? Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(x + 0.2f, Yard + 0.15f, z) : Matrix4x4.CreateTranslation(x, Yard, z);
            Shapes.Prism(model[Panel], Matrix4x4.CreateScale(0.15f, 0.42f, 0.15f) * m, 12, 1, p => Worn(colour, p));
            foreach (var y in new[] { 0.1f, 0.3f })
                Shapes.Prism(model[Panel], Matrix4x4.CreateScale(0.157f, 0.03f, 0.157f) * Matrix4x4.CreateTranslation(0, y, 0) * m, 12, 1, Shade(colour, 0.75f));
            if (!lying) Shapes.Prism(model[Panel], Matrix4x4.CreateScale(0.155f, 0.05f, 0.155f) * Matrix4x4.CreateTranslation(0, 0.2f, 0) * m, 12, 1, Hazard);
        }
    }
}
