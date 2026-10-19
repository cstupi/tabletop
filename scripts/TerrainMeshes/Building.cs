using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The large building (6" × 6", 8" high, Solid): a three-storey gothic hab block of chamfered rockcrete ashlar on a plinth.
/// Stepped buttresses with pinnacles divide each face into three bays; string courses mark the floors; pointed-arch windows with
/// stone frames, iron mullions and dark glass (some lit, some smashed with frame stones lost) on the upper storeys, slit windows
/// below; an arched bronze door up three steps; a shell-hole in one wall with its rubble below; a downpipe; a cornice and
/// crenellated parapet; and on the roof a water tank, louvred vents, pipes, a hatch and a vox mast to the top.</summary>
public static class Building
{
    public static readonly Material Stone = new("stone", 0.85f), Trim = new("trim", 0.8f), Roof = new("roof", 0.9f),
        Metal = new("metal", 0.45f, Metallic: 0.7f), Glass = new("glass", 0.12f, Metallic: 0.2f), Lit = new("lit-window", 0.3f, Emissive: new Vector3(0.95f, 0.7f, 0.38f));

    const float Size = 6, Top = 8, Half = 2.5f, Plinth = 0.45f, Cornice = 7.0f, Parapet = 7.55f;
    static readonly float[] Floors = [Plinth, 2.75f, 5.05f, Cornice];
    static readonly float[] Buttresses = [-0.85f, 0.85f];
    static readonly Vector4 Rockcrete = Rgb(0.7f, 0.67f, 0.62f), Dark = Rgb(0.52f, 0.5f, 0.47f), Grime = Rgb(0.3f, 0.27f, 0.23f), Slate = Rgb(0.28f, 0.29f, 0.31f),
        Steel = Rgb(0.34f, 0.35f, 0.37f), Rust = Rgb(0.45f, 0.26f, 0.14f), Bronze = Rgb(0.5f, 0.38f, 0.2f);

    /// <summary>One face of the block: u along it (−Half to Half), y up, d out from the wall's face.</summary>
    readonly record struct Side(Vector3 Normal, Vector3 Along)
    {
        public Vector3 P(float u, float y, float d) => Normal * (Half + d) + Along * u + Vector3.UnitY * y;
        public Matrix4x4 Rot => Masonry.Basis(Along, Vector3.UnitY, Normal);
    }

    static readonly Side[] Sides = [new(Vector3.UnitZ, Vector3.UnitX), new(Vector3.UnitX, -Vector3.UnitZ), new(-Vector3.UnitZ, -Vector3.UnitX), new(-Vector3.UnitX, Vector3.UnitZ)];

    static Vector4 Weathered(Vector4 c, Vector3 p) => Shade(Mix(c, Grime, Smooth(1.4f, 0f, p.Y) * 0.6f), 0.93f + 0.12f * Fbm(p * 1.3f, 41));

    public static Model Build()
    {
        var model = new Model("building", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(41);
        float R() => (float)rng.NextDouble();
        var stone = model[Stone]; var trim = model[Trim];
        // The core: the block's mass behind its facing, and the plinth round it.
        Masonry.Span(model[Roof], new(-Half + 0.04f, 0, -Half + 0.04f), new(Half - 0.04f, Cornice, Half - 0.04f), 0, p => Weathered(Rgb(0.36f, 0.35f, 0.33f), p));
        for (var i = 0; i < 4; i++)
        {
            var side = Sides[i];
            for (var u = -Half - 0.3f; u < Half + 0.3f - 0.05f;)
            {
                var len = MathF.Min(0.8f + 0.4f * R(), Half + 0.3f - u);
                var col = Shade(Dark, 0.9f + 0.15f * R());
                if (!(i == 0 && MathF.Abs(u + len / 2) < 0.75f))
                    Masonry.Block(trim, side.P(u + len / 2, Plinth / 2, 0.12f), new(len - 0.03f, Plinth, 0.5f), side.Rot, 0.05f, p => Weathered(col, p), rng);
                u += len;
            }
        }
        var hole = (Side: 3, U: 0.1f, Y: 3.9f, R: 0.75f);
        for (var i = 0; i < 4; i++) Face(model, rng, i, hole);
        // The shell-hole's rubble at the foot of its wall.
        Masonry.Rubble(stone, model[Roof], rng, Sides[hole.Side].P(hole.U, 0, 0.75f), new(0.8f, 0.45f), 0.45f, 34, 0.2f,
            r => Shade(Rockcrete, 0.75f + 0.3f * (float)r.NextDouble()), Rgb(0.4f, 0.37f, 0.33f), 0);
        Door(model, rng, Sides[0]);
        Roofline(model, rng);
        return model;
    }

    /// <summary>A face: ashlar courses between the buttresses, string courses at the floors, windows in every bay above the ground
    /// floor and slits on it (but the door bay at the front), the buttresses, a downpipe on the right-hand face, and the shell-hole.</summary>
    static void Face(Model model, Random rng, int index, (int Side, float U, float Y, float R) hole)
    {
        float R() => (float)rng.NextDouble();
        var side = Sides[index];
        var stone = model[Stone]; var trim = model[Trim];
        var bays = new[] { -1.7f, 0f, 1.7f };
        var windows = new List<(float U, float Half, float Sill, float Spring)>();
        for (var f = 0; f < 3; f++)
            foreach (var u in bays)
            {
                if (index == 0 && f == 0 && u == 0) continue;
                if (f == 0) windows.Add((u, 0.13f, Floors[0] + 0.7f, Floors[0] + 1.45f));
                else windows.Add((u, 0.28f, Floors[f] + 0.45f, Floors[f] + 1.2f));
            }
        if (index == 0) windows.Add((0, 0.5f, Plinth, 1.9f));   // the doorway
        bool InHole(float u, float y) => index == hole.Side && MathF.Pow((u - hole.U) / hole.R, 2) + MathF.Pow((y - hole.Y) / (hole.R * 0.85f), 2) < 1 + 0.35f * (Noise(u * 3, y * 3, index, 7) - 0.5f);
        // Ashlar courses, the joints showing the core behind.
        var row = 0;
        for (var y = Plinth; y < Cornice - 0.01f; row++)
        {
            var h = MathF.Min(0.46f, Cornice - y);
            if (Floors.Any(f => f > Plinth && MathF.Abs(f - y) < 0.01f)) { y += 0.0f; }
            for (var u = -Half + (row % 2 == 0 ? 0 : -0.45f); u < Half;)
            {
                var len = 0.75f + 0.35f * R();
                var (a, b) = (MathF.Max(u, -Half + 0.02f), MathF.Min(u + len, Half - 0.02f));
                u += len;
                var pieces = new List<(float, float)> { (a, b) };
                foreach (var w in windows)
                {
                    var apex = w.Spring + 1.732f * w.Half;
                    if (y + h <= w.Sill || y >= apex) continue;
                    var reach = w.Half + 0.17f;
                    pieces = pieces.SelectMany(p => p.Item2 <= w.U - reach || p.Item1 >= w.U + reach ? [p] : new[] { (p.Item1, w.U - reach), (w.U + reach, p.Item2) }).ToList();
                }
                foreach (var bt in Buttresses.Append(-Half).Append(Half))
                    pieces = pieces.SelectMany(p => p.Item2 <= bt - 0.24f || p.Item1 >= bt + 0.24f ? [p] : new[] { (p.Item1, bt - 0.24f), (bt + 0.24f, p.Item2) }).ToList();
                foreach (var (p0, p1) in pieces.Where(p => p.Item2 - p.Item1 > 0.12f))
                {
                    var mid = (p0 + p1) / 2;
                    if (InHole(mid, y + h / 2)) continue;
                    var col = Shade(Rockcrete, 0.85f + 0.2f * R()) + new Vector4(0.02f * (R() - 0.5f), 0, -0.02f * (R() - 0.5f), 0);
                    var near = index == hole.Side && Vector2.Distance(new(mid, y + h / 2), new(hole.U, hole.Y)) < hole.R * 1.4f;
                    var turn = near ? Masonry.Turn((R() - 0.5f) * 0.12f, (R() - 0.5f) * 0.08f, (R() - 0.5f) * 0.1f) * side.Rot : side.Rot;
                    Masonry.Block(stone, side.P(mid, y + h / 2, 0.02f * R()), new(p1 - p0 - 0.035f, h - 0.035f, 0.16f), turn, 0.035f, p => Weathered(col, p), rng, near ? 0.12f : 0, corners: near);
                }
            }
            y += h;
        }
        // String courses at the upper floors, between the buttresses.
        foreach (var f in Floors.Skip(1).Take(2))
            foreach (var (a, b) in new[] { (-Half, -0.85f), (-0.85f, 0.85f), (0.85f, Half) })
            {
                var col = Shade(Dark, 1.05f);
                Masonry.Block(trim, side.P((a + b) / 2, f, 0.08f), new(b - a - 0.4f, 0.16f, 0.3f), side.Rot, 0.04f, p => Weathered(col, p), corners: false);
            }
        foreach (var w in windows.Where(w => !(index == 0 && w.Half > 0.45f)))
            Window(model, rng, side.P, side.Rot, w.U, w.Half, w.Sill, w.Spring, broken: R() < 0.3f, lit: R() < 0.15f);
        // Buttresses: stepping back at each floor, a weathering slope on each step and a pinnacle above the parapet.
        foreach (var u in Buttresses)
        {
            float[] depth = [0.36f, 0.28f, 0.2f];
            for (var f = 0; f < 3; f++)
            {
                var (y0, y1) = (f == 0 ? 0 : Floors[f], Floors[f + 1]);
                var col = Shade(Dark, 0.95f + 0.1f * R());
                for (var y = y0; y < y1 - 0.01f; y += 0.6f)
                {
                    var yy = MathF.Min(y + 0.6f, y1);
                    Masonry.Block(trim, side.P(u, (y + yy) / 2, depth[f] / 2 - 0.02f), new(0.46f - 0.04f * f, yy - y - 0.02f, depth[f]), side.Rot, 0.04f, p => Weathered(col, p), rng, corners: false);
                }
                // The slope: a block tilted back over the step.
                Masonry.Block(trim, side.P(u, y1 + 0.02f, depth[f] / 2), new(0.5f - 0.04f * f, 0.12f, depth[f] + 0.06f), Matrix4x4.CreateRotationX(-0.35f) * side.Rot, 0.03f, Shade(Dark, 1.1f));
            }
            Pinnacle(model, side.P(u, Cornice, 0.12f));
        }
        // A downpipe down the right-hand face, with brackets and a shoe at the foot.
        if (index == 1)
        {
            var top = side.P(1.25f, Cornice - 0.1f, 0.14f); var foot = side.P(1.25f, 0.5f, 0.14f);
            Shapes.Limb(model[Metal], foot, top, 0.08f, 1, 8, p => Mix(Steel, Rust, Smooth(0.5f, 0.7f, Fbm(p * 2.5f, 43))));
            Shapes.Limb(model[Metal], foot, foot + side.Normal * 0.2f - Vector3.UnitY * 0.3f, 0.08f, 1, 8, p => Mix(Steel, Rust, 0.5f));
            for (var y = 1.0f; y < Cornice - 0.3f; y += 1.1f)
                Masonry.Block(model[Metal], side.P(1.25f, y, 0.1f), new(0.26f, 0.06f, 0.2f), side.Rot, 0.01f, Shade(Steel, 0.8f));
            for (var y = 1.6f; y < Cornice - 0.6f; y += 2.3f)   // a horizontal run into the wall
                Shapes.Limb(model[Metal], side.P(1.25f, y, 0.14f), side.P(-0.5f, y, 0.14f), 0.05f, 1, 6, p => Mix(Steel, Rust, Smooth(0.5f, 0.7f, Fbm(p * 2.5f, 44))));
        }
    }

    /// <summary>A pointed-arch window on a face (P(u, y, d)): a stone sill, jambs and voussoirs standing proud, a dark (or lit) pane
    /// with an iron mullion and transom. A broken one has its pane smashed to shards, its bars bent and frame stones lost.</summary>
    public static void Window(Model model, Random rng, Func<float, float, float, Vector3> P, Matrix4x4 rot, float u, float half, float sill, float spring, bool broken, bool lit)
    {
        float R() => (float)rng.NextDouble();
        var trim = model[Trim];
        var apex = spring + 1.732f * half;
        var stoneCol = Shade(Rgb(0.6f, 0.58f, 0.53f), 0.9f + 0.1f * R());
        Vector4 Col(Vector3 p) => Weathered(stoneCol, p);
        Masonry.Block(trim, P(u, sill - 0.07f, 0.06f), new(2 * half + 0.4f, 0.14f, 0.32f), rot, 0.03f, Col, corners: false);
        var jamb = spring - sill;
        for (var s = -1; s <= 1; s += 2)
        {
            var pieces = jamb > 1.0f ? 2 : 1;
            for (var k = 0; k < pieces; k++)
            {
                if (broken && R() < 0.25f) continue;
                var y0 = sill + jamb * k / pieces; var y1 = sill + jamb * (k + 1) / pieces;
                Masonry.Block(trim, P(u + s * (half + 0.09f), (y0 + y1) / 2, 0.06f), new(0.18f, y1 - y0 - 0.02f, 0.24f), rot, 0.03f, Col, corners: false);
            }
        }
        var stones = half < 0.2f ? 4 : 5;
        for (var k = 0; k < stones; k++)
        {
            var t = (k + 0.5f) / stones; var left = t < 0.5f;
            var a = (left ? t : 1 - t) * 2 * MathF.PI / 3;   // 0 at the springing to 60° at the apex
            var r = 2 * half + 0.09f;
            var cu = u + (left ? half : -half) - (left ? 1 : -1) * r * MathF.Cos(a);
            var cy = spring + r * MathF.Sin(a);
            if (broken && R() < 0.3f) continue;
            var roll = left ? MathF.PI / 2 - a : -(MathF.PI / 2 - a);
            Masonry.Block(trim, P(cu, cy, 0.06f), new(r * 2 * MathF.PI / 3 / stones - 0.025f, 0.19f, 0.24f), Matrix4x4.CreateRotationZ(roll) * rot, 0.03f, Col, corners: false);
        }
        // The pane: the opening's outline (a rectangle under an equilateral arch) at the wall's face.
        var outline = new List<Vector2> { new(u - half, sill), new(u + half, sill), new(u + half, spring) };
        for (var k = 1; k < 6; k++) { var a = k / 6f * MathF.PI / 3; outline.Add(new(u - half + 2 * half * MathF.Cos(a), spring + 2 * half * MathF.Sin(a))); }
        outline.Add(new(u, apex));
        for (var k = 5; k >= 1; k--) { var a = k / 6f * MathF.PI / 3; outline.Add(new(u + half - 2 * half * MathF.Cos(a), spring + 2 * half * MathF.Sin(a))); }
        outline.Add(new(u - half, spring));
        var centre = new Vector2(u, (sill + spring) / 2);
        var pane = broken ? model[Roof] : lit ? model[Lit] : model[Glass];
        var paneCol = broken ? Rgb(0.07f, 0.06f, 0.06f) : lit ? Rgb(0.95f, 0.78f, 0.5f) : Rgb(0.1f, 0.14f, 0.17f);
        for (var k = 0; k < outline.Count; k++)
        {
            var (a, b) = (outline[k], outline[(k + 1) % outline.Count]);
            Masonry.Face(pane, [P(centre.X, centre.Y, -0.02f), P(a.X, a.Y, -0.02f), P(b.X, b.Y, -0.02f)], P(0, 0, 1) - P(0, 0, 0), _ => paneCol);
        }
        var metal = model[Metal]; var iron = Rgb(0.22f, 0.22f, 0.23f);
        if (broken)
        {
            // Shards left in the frame, and the bars bent in.
            var glass = model[Glass];
            for (var k = 0; k < 7; k++)
            {
                var e = outline[rng.Next(outline.Count)]; var inward = Vector2.Normalize(centre - e);
                var w = 0.08f + 0.08f * R();
                var side2 = new Vector2(-inward.Y, inward.X) * w;
                var tip = e + inward * (0.12f + 0.2f * R());
                Masonry.Face(glass, [P(e.X - side2.X, e.Y - side2.Y, 0), P(e.X + side2.X, e.Y + side2.Y, 0), P(tip.X, tip.Y, 0)], P(0, 0, 1) - P(0, 0, 0), _ => Rgb(0.16f, 0.2f, 0.22f));
            }
            Shapes.Chain(metal, [P(u, sill, 0.02f), P(u, (sill + spring) / 2, 0.02f), P(u + 0.12f, spring, 0.2f)], 0.025f, 0.025f, 5, p => Mix(iron, Rust, 0.6f));
        }
        else
        {
            Masonry.Block(metal, P(u, (sill + apex) / 2 - 0.03f, 0.0f), new(0.05f, apex - sill - 0.08f, 0.06f), rot, 0.01f, iron);
            Masonry.Block(metal, P(u, spring - 0.05f, 0.0f), new(2 * half, 0.05f, 0.06f), rot, 0.01f, iron);
        }
    }

    /// <summary>A pinnacle on a buttress's head: a panelled shaft and a spire.</summary>
    static void Pinnacle(Model model, Vector3 foot)
    {
        var trim = model[Trim];
        Masonry.Block(trim, foot + Vector3.UnitY * 0.35f, new(0.36f, 0.7f, 0.36f), Matrix4x4.Identity, 0.04f, Shade(Dark, 1.05f));
        Masonry.Block(trim, foot + Vector3.UnitY * 0.74f, new(0.44f, 0.08f, 0.44f), Matrix4x4.Identity, 0.02f, Shade(Dark, 1.15f));
        Shapes.Prism(trim, Matrix4x4.CreateScale(0.24f, 0.2f, 0.24f) * Matrix4x4.CreateTranslation(foot + Vector3.UnitY * 0.78f), 4, 0, Shade(Dark, 1.1f), MathF.PI / 4);
    }

    /// <summary>The doorway at the front: an arched opening of dressed stone, a pair of bronze-plated doors with studs, a plaque
    /// over them and three steps.</summary>
    static void Door(Model model, Random rng, Side side)
    {
        var trim = model[Trim]; var metal = model[Metal];
        const float half = 0.5f, spring = 1.9f;
        Window(model, rng, side.P, side.Rot, 0, half, Plinth, spring, broken: false, lit: false);
        for (var s = -1; s <= 1; s += 2)
        {
            Masonry.Block(metal, side.P(s * half / 2, (Plinth + spring) / 2, 0.02f), new(half - 0.03f, spring - Plinth - 0.02f, 0.06f), side.Rot, 0.015f, p => Mix(Bronze, Rgb(0.3f, 0.42f, 0.36f), 0.3f * Fbm(p * 4, 45)));
            for (var k = 0; k < 3; k++)
            {
                var y = Plinth + 0.25f + k * 0.45f;
                Masonry.Block(metal, side.P(s * half / 2, y + 0.18f, 0.06f), new(half - 0.16f, 0.32f, 0.04f), side.Rot, 0.012f, Shade(Bronze, 1.1f));
                foreach (var dx in new[] { -0.14f, 0.14f })
                    Masonry.Block(metal, side.P(s * half / 2 + dx, y + 0.04f, 0.07f), new(0.04f, 0.04f, 0.03f), side.Rot, 0.008f, Shade(Bronze, 1.3f));
            }
        }
        Masonry.Block(trim, side.P(0, spring + 1.1f, 0.12f), new(0.9f, 0.3f, 0.08f), side.Rot, 0.02f, Shade(Bronze, 0.9f));
        Masonry.Block(trim, side.P(0, spring + 1.1f, 0.16f), new(0.28f, 0.34f, 0.06f), side.Rot, 0.02f, Shade(Rgb(0.82f, 0.78f, 0.68f), 0.9f));
        for (var k = 0; k < 3; k++)
            Masonry.Block(trim, side.P(0, Plinth * (k + 0.5f) / 3, 0.25f + (2 - k) * 0.17f), new(1.5f - 0.1f * k, Plinth / 3, 0.34f), side.Rot, 0.03f, p => Weathered(Shade(Dark, 1.05f), p), rng);
    }

    /// <summary>The cornice, the crenellated parapet, the roof deck and its fittings, and the mast to the top.</summary>
    static void Roofline(Model model, Random rng)
    {
        float R() => (float)rng.NextDouble();
        var trim = model[Trim]; var metal = model[Metal]; var roof = model[Roof];
        foreach (var side in Sides)
        {
            Masonry.Block(trim, side.P(0, Cornice + 0.1f, 0.08f), new(2 * Half + 0.36f, 0.2f, 0.34f), side.Rot, 0.04f, Shade(Dark, 1.05f));
            for (var u = -Half + 0.12f; u < Half - 0.1f; u += 0.5f)
            {
                var merlon = ((int)MathF.Round((u + Half) / 0.5f)) % 2 == 0;
                var h = merlon ? Parapet - Cornice - 0.2f : 0.22f;
                var col = Shade(Dark, 0.92f + 0.12f * R());
                Masonry.Block(trim, side.P(u + 0.25f, Cornice + 0.2f + h / 2, -0.12f), new(0.48f, h, 0.24f), side.Rot, 0.035f, col, corners: false);
            }
        }
        Masonry.Span(roof, new(-Half + 0.2f, Cornice, -Half + 0.2f), new(Half - 0.2f, Cornice + 0.12f, Half - 0.2f), 0.02f, p => Shade(Slate, 0.9f + 0.15f * Fbm(p * 2, 46)));
        var deck = Cornice + 0.12f;
        Vector4 Metalwork(Vector3 p) => Mix(Steel, Rust, Smooth(0.5f, 0.72f, Fbm(p * 2.2f, 47)));
        // The water tank on four legs.
        var tank = new Vector3(-1.1f, deck, -1.0f);
        foreach (var (dx, dz) in new[] { (-0.45f, -0.45f), (0.45f, -0.45f), (0.45f, 0.45f), (-0.45f, 0.45f) })
            Masonry.Block(metal, tank + new Vector3(dx, 0.2f, dz), new(0.08f, 0.4f, 0.08f), Matrix4x4.Identity, 0.01f, Metalwork);
        Shapes.Prism(metal, Matrix4x4.CreateScale(0.62f, 0.5f, 0.62f) * Matrix4x4.CreateTranslation(tank + Vector3.UnitY * 0.4f), 14, 1, Metalwork);
        Shapes.Prism(metal, Matrix4x4.CreateScale(0.64f, 0.18f, 0.64f) * Matrix4x4.CreateTranslation(tank + Vector3.UnitY * 0.9f), 14, 0.2f, Metalwork);
        foreach (var y in new[] { 0.5f, 0.78f })
            Shapes.Prism(metal, Matrix4x4.CreateScale(0.65f, 0.04f, 0.65f) * Matrix4x4.CreateTranslation(tank + Vector3.UnitY * y), 14, 1, Shade(Steel, 0.7f));
        // Two louvred vents and a run of pipe between them; a hatch.
        foreach (var (x, z) in new[] { (0.9f, -1.2f), (1.2f, 0.6f) })
        {
            var at = new Vector3(x, deck, z);
            Masonry.Block(metal, at + Vector3.UnitY * 0.25f, new(0.6f, 0.5f, 0.5f), Matrix4x4.Identity, 0.03f, Metalwork);
            for (var k = 0; k < 4; k++)
                Masonry.Block(metal, at + new Vector3(0, 0.12f + 0.1f * k, 0.27f), new(0.5f, 0.03f, 0.06f), Matrix4x4.CreateRotationX(0.5f), 0.008f, Shade(Steel, 0.6f));
            Masonry.Block(metal, at + Vector3.UnitY * 0.53f, new(0.68f, 0.06f, 0.58f), Matrix4x4.Identity, 0.015f, Shade(Steel, 0.9f));
        }
        Shapes.Chain(metal, [new(0.9f, deck + 0.15f, -0.95f), new(0.9f, deck + 0.15f, 0.2f), new(1.2f, deck + 0.15f, 0.35f)], 0.06f, 0.06f, 6, Metalwork);
        Masonry.Block(trim, new(-0.6f, deck + 0.08f, 1.1f), new(0.7f, 0.16f, 0.7f), Matrix4x4.Identity, 0.03f, Shade(Dark, 1.0f));
        Masonry.Block(metal, new(-0.6f, deck + 0.19f, 1.1f), new(0.56f, 0.06f, 0.56f), Matrix4x4.Identity, 0.015f, Metalwork);
        // The vox mast in the corner: a tapered pole, two crossbars and a lamp at the very top.
        var foot = new Vector3(1.6f, deck, 1.6f);
        Masonry.Block(metal, foot + Vector3.UnitY * 0.08f, new(0.3f, 0.16f, 0.3f), Matrix4x4.Identity, 0.02f, Metalwork);
        Shapes.Limb(metal, foot, foot with { Y = Top - 0.06f }, 0.05f, 0.5f, 6, Metalwork);
        foreach (var y in new[] { 7.65f, 7.85f })
            Masonry.Block(metal, foot with { Y = y }, new(0.5f, 0.03f, 0.03f), Masonry.Turn(0.7f), 0.006f, Shade(Steel, 0.8f));
        Shapes.Prism(model[Lit], Matrix4x4.CreateScale(0.04f, 0.06f, 0.04f) * Matrix4x4.CreateTranslation(foot with { Y = Top - 0.06f }), 6, 1, Rgb(1, 0.3f, 0.2f));
    }
}
