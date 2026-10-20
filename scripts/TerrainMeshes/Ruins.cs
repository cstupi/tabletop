using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A ruined gothic hab, authored in inches at its typical 6" × 6" × 4" and fitted into the unit box (the mesh is shared
/// with the 4" ruin): a flagstone floor at ground level (ruins are walk-in ground, an Area), two broken walls of chamfered ashlar
/// courses meeting at a corner pilaster, pointed-arch windows and a doorway with broken frames, a string course, a stub of upper
/// floor on girders with its broken half fallen in, rebar out of every break, a broken column, and heaps of rubble of individual
/// blocks, bricks and slabs at the wall feet.</summary>
public static class Ruins
{
    public static readonly Material Stonework = new("masonry", 0.88f), Floor = new("flagstones", 0.9f), Iron = new("iron", 0.55f, Metallic: 0.6f),
        Grit = new("rubble", 0.95f), Trim = new("dressed-stone", 0.85f);
    internal const int Seed = 31;
    internal const float W = 6, H = 4, Course = 0.25f, Gap = 0.028f, Thick = 0.32f, Level = 2.05f;

    static readonly Vector4 Stone = Rgb(0.5f, 0.49f, 0.47f), Dressed = Rgb(0.57f, 0.55f, 0.51f), Grime = Rgb(0.32f, 0.28f, 0.23f),
        Mortar = Rgb(0.27f, 0.25f, 0.23f), Moss = Rgb(0.23f, 0.31f, 0.13f), Gunmetal = Rgb(0.3f, 0.31f, 0.33f), Rust = Rgb(0.45f, 0.25f, 0.13f);

    /// <summary>A window or door in a wall: centred at <paramref name="At"/> along it, <paramref name="Half"/> wide each side, from
    /// <paramref name="Sill"/> up to where its pointed arch springs.</summary>
    internal readonly record struct Opening(float At, float Half, float Sill, float Spring)
    {
        public float Apex => Spring + 1.732f * Half;
        /// <summary>The opening's half-width at height y (the equilateral arch narrows to its point).</summary>
        public float HalfAt(float y) => y < Sill || y > Apex ? -1 : y <= Spring ? Half : MathF.Sqrt(MathF.Max(0, 4 * Half * Half - (y - Spring) * (y - Spring))) - Half;
    }

    internal sealed class Ctx(Model model, Random rng, bool overgrown)
    {
        public Model Model { get; } = model;
        public Random Rng { get; } = rng;
        public bool Overgrown { get; } = overgrown;
        public float R => (float)Rng.NextDouble();
        public List<(Vector3 Origin, Vector3 Normal, Vector3 Along, float Length, Func<float, float> Top)> Faces { get; } = [];   // the walls' faces, for ivy
    }

    public static Model Build() => Unit("ruins", overgrown: false);

    /// <summary>The same ruin taken over by growth: moss on the stone, ivy of many leaves spilling over the wall tops and down their
    /// faces, bushes, grass and a young tree in the rubble.</summary>
    public static Model Overgrown() => Unit("overgrown-ruins", overgrown: true);

    static Model Unit(string name, bool overgrown) => Masonry.Unit(Build(name, overgrown));

    internal static Vector4 Weather(Ctx c, Vector4 colour, Vector3 p, float grime = 1)
    {
        var low = Smooth(0.9f, 0f, p.Y) * grime;
        var o = Mix(colour, Grime, low * 0.55f);
        o = Shade(o, 0.93f + 0.12f * Fbm(p * 3.1f, Seed));
        if (c.Overgrown) o = Mix(o, Moss, Smooth(0.5f, 0.68f, Fbm(p * 1.7f, Seed + 5)) * (0.25f + 0.45f * Smooth(1.6f, 0, p.Y)));
        return o;
    }

    internal static Vector4 BlockPaint(Ctx c)
    {
        var shade = 0.8f + 0.28f * c.R;
        var warm = c.R - 0.5f;
        var col = Shade(Stone, shade) + new Vector4(0.03f * warm, 0.01f * warm, -0.02f * warm, 0);
        if (c.R < 0.06f) col = Shade(col, 0.55f);   // a scorched block
        return col;
    }

    static Model Build(string name, bool overgrown)
    {
        var model = new Model(name, round: false, new Vector3(W, H, W));
        var c = new Ctx(model, new Random(Seed), overgrown);
        FloorTiles(c);
        // The back wall (along x at the back) stands near full height at the corner, its windows broken, a breach to the right,
        // then a low run; the left wall (along z) falls away toward the front past a doorway.
        float Jag(float s, int salt, float amp) => amp * (Noise(s * 2.6f, salt, 0, Seed) - 0.5f) + amp * 0.9f * (MathF.Floor(Noise(s * 1.3f, salt, 3, Seed) * 4) / 4 - 0.4f);
        Wall(c, new(-2.95f, -2.7f), new(1, 0), 5.95f,
            s => s < 2.6f ? 3.95f - 0.12f * s * s + Jag(s, 1, 0.5f) : s < 3.1f ? 3.0f - 4.2f * (s - 2.6f) + Jag(s, 2, 0.3f) : s < 4.5f ? 0.75f + Jag(s, 3, 0.5f) : 1.55f - 0.25f * (s - 4.5f) + Jag(s, 4, 0.4f),
            [new(1.05f, 0.32f, 0.75f, 1.3f), new(2.2f, 0.32f, 0.75f, 1.3f), new(1.05f, 0.3f, 2.5f, 3.0f), new(2.2f, 0.28f, 2.5f, 2.95f), new(5.1f, 0.3f, 0.45f, 0.9f)]);
        Wall(c, new(-2.7f, -2.45f), new(0, 1), 5.4f,
            s => s < 1.9f ? 3.9f - 0.25f * s + Jag(s, 5, 0.45f) : 3.4f - 1.15f * (s - 1.9f) + Jag(s, 6, 0.5f),
            [new(1.0f, 0.32f, 0.75f, 1.3f), new(1.0f, 0.3f, 2.5f, 3.0f), new(3.15f, 0.42f, 0f, 1.15f)]);
        // The front-right corner: two low stubs.
        Wall(c, new(2.95f, 2.7f), new(-1, 0), 2.3f, s => 1.5f - 0.45f * s + Jag(s, 7, 0.4f), [new(1.1f, 0.28f, 0.4f, 0.8f)]);
        Wall(c, new(2.7f, 2.45f), new(0, -1), 2.0f, s => 1.15f - 0.4f * s + Jag(s, 8, 0.35f), []);
        Pilaster(c, new(-2.7f, -2.7f), 3.98f);
        Pilaster(c, new(-0.3f, -2.7f), 3.1f, along: true);
        Pilaster(c, new(-2.7f, -0.25f), 3.35f, along: false);
        Pilaster(c, new(2.7f, 2.7f), 1.35f);
        UpperFloor(c);
        Column(c, new(0.9f, 0.55f));
        // Rubble: under the breach, where the left wall fell, under the fallen floor, in the front corner, and loose pieces.
        foreach (var (at, r, h, n, yaw) in new[]
        {
            (new Vector2(0.75f, -2.05f), new Vector2(1.15f, 0.55f), 0.65f, 54, 0f), (new Vector2(-2.15f, 1.75f), new Vector2(0.55f, 1.0f), 0.5f, 36, 0.1f),
            (new Vector2(-0.55f, -0.15f), new Vector2(0.7f, 0.5f), 0.32f, 26, 0.4f), (new Vector2(2.15f, 2.1f), new Vector2(0.6f, 0.55f), 0.38f, 26, 0.7f),
            (new Vector2(-2.1f, -1.9f), new Vector2(0.45f, 0.4f), 0.25f, 12, 0.2f),
        })
            Masonry.Rubble(model[Stonework], model[Grit], c.Rng, new Vector3(at.X, 0, at.Y), r, h, overgrown ? n * 2 / 3 : n, 0.17f, rng => Weather(c, BlockPaint(c), new Vector3(0, 0.3f, 0)),
                Rgb(0.4f, 0.37f, 0.33f), yaw);
        for (var k = 0; k < (overgrown ? 14 : 26); k++)
        {
            var p = new Vector3(-2.2f + 4.8f * c.R, 0, -1.7f + 4.4f * c.R);
            var s = 0.1f + 0.1f * c.R;
            var col = BlockPaint(c);
            Masonry.Block(model[Stonework], p + Vector3.UnitY * (0.05f + s * 0.25f), new Vector3(s * 1.7f, s * 0.7f, s * 0.85f), Masonry.Turn(c.R * 6, (c.R - 0.5f) * 0.4f, (c.R - 0.5f) * 0.4f),
                s * 0.12f, q => Weather(c, col, q), c.Rng, 0.15f);
        }
        if (overgrown) Overgrowth(c);
        return model;
    }

    /// <summary>Flagstones on a dirt bed, all within 0.05" of the table: a grid of chamfered slabs each its own shade, some cracked in
    /// two, some sunk, some missing. The bed spans <paramref name="half"/> each side of the centre (the 6" square by default); the
    /// grid is <paramref name="columns"/> × <paramref name="rows"/> slabs of 0.59" from <paramref name="start"/>; a slab where
    /// <paramref name="keep"/> says no is left out (the floor fallen through).</summary>
    internal static void FloorTiles(Ctx c, Vector2? half = null, int columns = 10, int rows = 10, Vector2? start = null, Func<Vector2, bool>? keep = null)
    {
        var floor = c.Model[Floor];
        var dirt = Rgb(0.38f, 0.32f, 0.25f);
        var (hx, hz) = (half?.X ?? 3, half?.Y ?? 3);
        var (x0, z0) = (start?.X ?? -2.95f, start?.Y ?? -2.95f);
        Masonry.Face(floor, [new(-hx, 0.004f, -hz), new(hx, 0.004f, -hz), new(hx, 0.004f, hz), new(-hx, 0.004f, hz)], Vector3.UnitY, p => Shade(dirt, 0.8f + 0.3f * Fbm(p * 1.5f, Seed)));
        const float size = 5.9f / 10;
        for (var i = 0; i < columns; i++)
            for (var j = 0; j < rows; j++)
            {
                if (c.R < 0.13f) continue;
                var x = x0 + (i + 0.5f) * size; var z = z0 + (j + 0.5f) * size;
                if (keep is not null && !keep(new(x, z))) continue;
                var col = Shade(Rgb(0.45f, 0.44f, 0.42f), 0.78f + 0.3f * c.R) + new Vector4(0.02f * (c.R - 0.5f), 0, 0, 0);
                var top = c.R < 0.2f ? 0.03f : 0.045f;
                Vector4 Tile(Vector3 p) => Mix(Shade(col, 0.92f + 0.12f * Noise(p.X * 9, p.Z * 9, 0, 4)), dirt, 0.25f * Smooth(0.55f, 0.75f, Fbm(p * 2.5f, 9)));
                var yaw = (c.R - 0.5f) * 0.06f;
                if (c.R < 0.22f)   // cracked across, the halves a little apart
                {
                    var cut = (c.R - 0.5f) * 0.25f;
                    var w0 = size * (0.5f + cut) - 0.05f; var w1 = size - 0.05f - w0 - 0.035f;
                    Masonry.Block(floor, new(x - size / 2 + 0.025f + w0 / 2, top / 2, z), new(w0, top, size - 0.05f), Masonry.Turn(yaw), 0.012f, Tile);
                    Masonry.Block(floor, new(x + size / 2 - 0.025f - w1 / 2, top / 2 - 0.006f, z), new(w1, top - 0.006f, size - 0.06f), Masonry.Turn(yaw + 0.04f), 0.012f, Tile);
                }
                else Masonry.Block(floor, new(x, top / 2, z), new(size - 0.05f, top, size - 0.05f), Masonry.Turn(yaw), 0.014f, Tile);
            }
    }

    /// <summary>A wall from <paramref name="from"/> along <paramref name="dir"/>, <paramref name="length"/> long and <see cref="Thick"/>
    /// thick: a plinth course, then staggered courses of chamfered ashlar on a recessed mortar core, broken off along
    /// <paramref name="top"/> (s along the wall) with blocks missing and displaced near the break, its openings framed. The courses run up to
    /// <paramref name="tall"/>, the string course at <paramref name="level"/>; without <paramref name="corners"/> the course blocks
    /// leave out their corner facets (fewer triangles).</summary>
    internal static void Wall(Ctx c, Vector2 from, Vector2 dir, float length, Func<float, float> top, Opening[] openings, float tall = H, float level = Level, bool corners = true)
    {
        var nrm = new Vector2(-dir.Y, dir.X);
        var rot = Masonry.Basis(new(dir.X, 0, dir.Y), Vector3.UnitY, new(nrm.X, 0, nrm.Y));
        Vector3 P(float s, float y, float d) => new(from.X + dir.X * s + nrm.X * d, y, from.Y + dir.Y * s + nrm.Y * d);
        var part = c.Model[Stonework]; var core = c.Model[Grit];
        for (var face = -1; face <= 1; face += 2)
            c.Faces.Add((P(0, 0, face * Thick / 2), new Vector3(nrm.X, 0, nrm.Y) * face, new Vector3(dir.X, 0, dir.Y), length, top));
        // The plinth: one course of long, deeper blocks.
        for (var s = 0f; s < length - 0.05f;)
        {
            var len = MathF.Min(0.7f + 0.3f * c.R, length - s);
            var mid = s + len / 2;
            if (top(mid) > 0.1f && !openings.Any(o => o.Sill < 0.05f && MathF.Abs(mid - o.At) < o.Half))
            {
                var col = Weather(c, Shade(Dressed, 0.8f + 0.15f * c.R), P(mid, 0.1f, 0));
                Masonry.Block(part, P(mid, 0.14f, 0), new(len - Gap, 0.28f, Thick + 0.1f), rot, 0.035f, q => Weather(c, col, q), c.Rng);
            }
            s += len;
        }
        var row = 0;
        for (var y = 0.28f; y < tall; y += Course, row++)
        {
            var y1 = y + Course;
            var s = row % 2 == 0 ? 0 : -0.25f;
            var runs = new List<(float A, float B)>();
            while (s < length)
            {
                var len = 0.38f + 0.26f * c.R;
                var (s0, s1) = (MathF.Max(s, 0), MathF.Min(s + len, length));
                s += len;
                if (s1 - s0 < 0.1f) continue;
                // Clip the block around any opening crossing its course.
                var pieces = new List<(float, float)> { (s0, s1) };
                foreach (var o in openings)
                {
                    var half = MathF.Max(o.HalfAt(y + 0.02f), o.HalfAt(MathF.Min(y1, o.Apex) - 0.02f));
                    if (half <= 0) continue;
                    var (a, b) = (o.At - half - 0.02f, o.At + half + 0.02f);
                    pieces = pieces.SelectMany(p => p.Item2 <= a || p.Item1 >= b ? [p] : new[] { (p.Item1, a), (b, p.Item2) }).Where(p => p.Item2 - p.Item1 > 0.09f).ToList();
                }
                foreach (var (b0, b1) in pieces)
                {
                    var mid = (b0 + b1) / 2;
                    var t = top(mid) + 0.12f * (Noise(mid * 4, row, 1, Seed) - 0.5f);
                    if (y >= t) continue;
                    var nearTop = y1 > t;
                    if (nearTop && (t - y) < Course * 0.45f) { if (c.R < 0.2f) Rebar(c, P(mid, y, 0), new Vector3(0, 1, 0)); continue; }
                    // Missing blocks: rare low down, common near the break.
                    var missing = 0.008f + 0.2f * Smooth(0.6f, 0.05f, t - y1);
                    if (c.R < missing) { if (c.R < 0.25f) Rebar(c, P(mid, y + Course / 2, 0), new Vector3(dir.X, 0.2f, dir.Y) * (c.R < 0.5f ? 1 : -1)); continue; }
                    var col = BlockPaint(c);
                    var (len2, height) = (b1 - b0 - Gap, Course - Gap);
                    var d = Thick + 0.03f * (c.R - 0.4f);
                    var centre = P(mid, y + Course / 2, 0.025f * (c.R - 0.5f));
                    var turn = rot;
                    if (nearTop || (t - y1 < 0.3f && c.R < 0.25f))   // displaced: shifted and canted at the break
                    {
                        turn = Masonry.Turn((c.R - 0.5f) * 0.25f, (c.R - 0.5f) * 0.12f, (c.R - 0.5f) * 0.18f) * rot;
                        centre += new Vector3(nrm.X, 0, nrm.Y) * (c.R - 0.5f) * 0.1f + Vector3.UnitY * 0.01f;
                    }
                    if (c.R < 0.07f && len2 > 0.32f)   // cracked through: two pieces a hair apart
                    {
                        var cut = len2 * (0.35f + 0.3f * c.R);
                        Masonry.Block(part, centre + Vector3.TransformNormal(new Vector3(-len2 / 2 + cut / 2, 0, 0), rot), new(cut - 0.012f, height, d), turn, 0.03f, q => Weather(c, col, q), c.Rng, 0.04f, corners);
                        Masonry.Block(part, centre + Vector3.TransformNormal(new Vector3(cut / 2, -0.008f, 0), rot), new(len2 - cut - 0.012f, height - 0.01f, d), Masonry.Turn(0.03f) * turn, 0.03f, q => Weather(c, col, q), c.Rng, 0.04f, corners);
                    }
                    else Masonry.Block(part, centre, new(len2, height, d), turn, 0.032f, q => Weather(c, col, q), c.Rng, nearTop ? 0.06f : 0, corners);
                    if (runs.Count > 0 && b0 - runs[^1].B < 0.03f) runs[^1] = (runs[^1].A, b1); else runs.Add((b0, b1));
                }
            }
            // The core behind the course's joints, recessed so the mortar lines read; broken where blocks are missing.
            foreach (var (a, b) in runs)
                Masonry.Block(core, P((a + b) / 2, y + Course / 2, 0), new(b - a + 0.01f, Course + 0.01f, Thick - 0.07f), rot, 0, q => Mix(Mortar, Grime, Smooth(0.6f, 0, q.Y) * 0.4f));
        }
        // The string course at the upper floor's level, where the wall still stands that high.
        for (var s = 0f; s < length;)
        {
            var len = 0.6f + 0.25f * c.R; var mid = s + len / 2;
            if (top(mid) > level + 0.3f && top(s) > level + 0.2f && top(MathF.Min(s + len, length)) > level + 0.2f)
            {
                var col = Shade(Dressed, 0.82f + 0.15f * c.R);
                Masonry.Block(part, P(mid, level + 0.07f, 0), new(MathF.Min(len, length - s) - 0.02f, 0.14f, Thick + 0.14f), rot, 0.035f, q => Weather(c, col, q), c.Rng);
            }
            s += len;
        }
        foreach (var o in openings) Frame(c, o, P, rot, top, dir, nrm);
    }

    /// <summary>An opening's frame: a sill, jamb stones up both sides and voussoirs round the pointed arch, each lost where the wall
    /// has broken away above it, a few missing or slipped; a bent iron grille bar in the taller windows.</summary>
    static void Frame(Ctx c, Opening o, Func<float, float, float, Vector3> P, Matrix4x4 rot, Func<float, float> top, Vector2 dir, Vector2 nrm)
    {
        var part = c.Model[Trim];
        Vector4 Col() { var col = Shade(Dressed, 0.82f + 0.2f * c.R); return col; }
        bool Stands(float s, float y) => y < top(s) + 0.08f;
        if (o.Sill > 0.1f && Stands(o.At, o.Sill))
        {
            var col = Col();
            Masonry.Block(part, P(o.At, o.Sill - 0.06f, 0), new(2 * o.Half + 0.34f, 0.12f, Thick + 0.12f), rot, 0.03f, q => Weather(c, col, q), c.Rng);
        }
        for (var side = -1; side <= 1; side += 2)
            for (var y = MathF.Max(o.Sill, 0.28f); y < o.Spring - 0.05f; y += 0.25f)
            {
                var s = o.At + side * (o.Half + 0.08f);
                if (!Stands(s, y + 0.12f) || c.R < 0.08f) continue;
                var wide = (y - o.Sill) / 0.25f % 2 < 1 ? 0.2f : 0.14f;
                var col = Col();
                Masonry.Block(part, P(s + side * (wide / 2 - 0.07f), y + 0.12f, 0), new(wide, 0.23f, Thick + 0.08f), rot, 0.03f, q => Weather(c, col, q), c.Rng);
            }
        const int stones = 9;
        for (var k = 0; k < stones; k++)
        {
            // Voussoirs along the two arcs, from the springing to the point.
            var t = (k + 0.5f) / stones;
            var leftArc = t < 0.5f;
            var a = (leftArc ? t : 1 - t) * 2 * MathF.PI / 3;   // 0 at the springing, π/6·… up to the apex at 60°
            var centreS = o.At + (leftArc ? o.Half : -o.Half);
            var r = 2 * o.Half + 0.1f;
            var s = centreS - (leftArc ? 1 : -1) * r * MathF.Cos(a);
            var y = o.Spring + r * MathF.Sin(a);
            if (!Stands(s, y) || c.R < 0.07f) continue;
            var tangent = MathF.Atan2(MathF.Cos(a), MathF.Sin(a) * (leftArc ? 1 : -1));
            var roll = leftArc ? MathF.PI / 2 - a : -(MathF.PI / 2 - a);
            var col = Col();
            var slip = c.R < 0.15f ? 0.05f : 0;
            Masonry.Block(part, P(s, y - slip, 0.02f * (c.R - 0.5f)), new(r * 2 * MathF.PI / 3 / stones - 0.025f, 0.22f, Thick + 0.08f), Matrix4x4.CreateRotationZ(roll) * rot, 0.03f, q => Weather(c, col, q), c.Rng, 0.03f);
            _ = tangent;
        }
        if (o.Spring - o.Sill > 0.6f && o.Sill > 0.1f)   // an iron mullion, bent where the arch broke
        {
            var foot = P(o.At, o.Sill, 0);
            var head = MathF.Min(o.Apex, top(o.At) + 0.1f);
            var pts = new List<Vector3> { foot, foot + Vector3.UnitY * (head - o.Sill) * 0.6f };
            if (head < o.Apex - 0.2f) pts.Add(pts[1] + new Vector3(nrm.X, 0, nrm.Y) * 0.18f + Vector3.UnitY * 0.25f);
            else pts.Add(foot + Vector3.UnitY * (head - o.Sill));
            Shapes.Chain(c.Model[Iron], pts, 0.03f, 0.028f, 5, q => Mix(Gunmetal, Rust, Fbm(q * 6, 2)));
        }
    }

    /// <summary>A short bar of rusty rebar out of a break.</summary>
    internal static void Rebar(Ctx c, Vector3 at, Vector3 dir) => Masonry.Rebar(c.Model[Iron], c.Rng, at, dir + new Vector3(c.R - 0.5f, 0, c.R - 0.5f) * 0.5f, 0.3f + 0.25f * c.R, 0.022f);

    /// <summary>A pilaster: a stack of dressed blocks standing proud of the wall (a corner one proud of both), on a deep base, with a
    /// capital at the floor level if it reaches it, broken at <paramref name="height"/>.</summary>
    internal static void Pilaster(Ctx c, Vector2 at, float height, bool? along = null, float level = Level)
    {
        var part = c.Model[Trim];
        var size = along switch { true => new Vector2(0.44f, Thick + 0.16f), false => new Vector2(Thick + 0.16f, 0.44f), _ => new Vector2(0.52f, 0.52f) };
        var col0 = Shade(Dressed, 0.85f);
        Masonry.Block(part, new(at.X, 0.17f, at.Y), new(size.X + 0.12f, 0.34f, size.Y + 0.12f), Matrix4x4.Identity, 0.04f, q => Weather(c, col0, q), c.Rng);
        var y = 0.34f; var k = 0;
        while (y < height - 0.1f)
        {
            var h = MathF.Min(k % 2 == 0 ? 0.3f : 0.2f, height - y);
            var col = Shade(Dressed, 0.8f + 0.2f * c.R);
            var last = y + h >= height - 0.1f;
            var turn = last ? Masonry.Turn((c.R - 0.5f) * 0.2f, (c.R - 0.5f) * 0.1f, (c.R - 0.5f) * 0.1f) : Matrix4x4.Identity;
            var shrink = k % 2 == 0 ? 0 : 0.03f;
            Masonry.Block(part, new(at.X, y + h / 2, at.Y), new(size.X - shrink, h - 0.025f, size.Y - shrink), turn, 0.035f, q => Weather(c, col, q), c.Rng, last ? 0.12f : 0);
            if (MathF.Abs(y + h - level) < 0.22f && height > level + 0.4f)
            {
                var colc = Shade(Dressed, 0.95f);
                Masonry.Block(part, new(at.X, y + h + 0.08f, at.Y), new(size.X + 0.14f, 0.16f, size.Y + 0.14f), Matrix4x4.Identity, 0.04f, q => Weather(c, colc, q), c.Rng);
                y += 0.16f;
            }
            y += h; k++;
        }
        if (c.R < 0.8f) Rebar(c, new(at.X + 0.1f, height - 0.05f, at.Y + 0.08f), Vector3.UnitY);
    }

    /// <summary>The upper floor's stub in the back-left corner: a rockcrete slab with a broken edge on girders out of both walls,
    /// floor plates on it, rebar out of the break, and its fallen half tilted down onto the rubble.</summary>
    static void UpperFloor(Ctx c)
    {
        var part = c.Model[Stonework]; var iron = c.Model[Iron];
        var slabCol = Rgb(0.42f, 0.41f, 0.39f);
        // The slab's outline: square in the corner, broken along a jagged line from the back wall to the left wall.
        var outline = new List<Vector2> { new(-2.54f, -2.54f), new(-0.42f, -2.54f) };
        var rng = c.Rng;
        for (var k = 1; k < 12; k++)
        {
            var t = k / 12f;
            var a = t * MathF.PI / 2;
            var r = 2.05f + 0.35f * (Noise(t * 9, 1, 1, Seed) - 0.5f) + 0.25f * (k % 2 == 0 ? 1 : -1) * (float)rng.NextDouble();
            outline.Add(new Vector2(-2.54f, -2.54f) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r with { } * new Vector2(1.05f, 0.95f));
        }
        outline.Add(new(-2.54f, -0.45f));
        Shapes.Slab(part, outline, Level - 0.16f, Level, (p, n) => Weather(c, Shade(slabCol, n.Y > 0.5f ? 0.95f : 0.8f), p, 0.3f));
        // Girders under it, one past the break.
        foreach (var (a, b) in new[] { (new Vector3(-2.55f, Level - 0.26f, -1.6f), new Vector3(-0.15f, Level - 0.26f, -1.6f)), (new Vector3(-1.6f, Level - 0.26f, -2.55f), new Vector3(-1.6f, Level - 0.26f, -0.75f)) })
            Masonry.Girder(iron, a, b, 0.16f, 0.2f, q => Mix(Gunmetal, Rust, Smooth(0.45f, 0.7f, Fbm(q * 3, 11))));
        // Plates on the slab.
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
            {
                var p = new Vector2(-2.3f + i * 0.5f, -2.3f + j * 0.5f);
                if (Vector2.Distance(p, new(-2.54f, -2.54f)) > 1.75f || c.R < 0.15f) continue;
                var col = Shade(Rgb(0.44f, 0.43f, 0.41f), 0.8f + 0.3f * c.R);
                Masonry.Block(part, new(p.X, Level + 0.02f, p.Y), new(0.46f, 0.04f, 0.46f), Masonry.Turn((c.R - 0.5f) * 0.05f), 0.012f, col);
            }
        // Rebar out of the broken edge.
        for (var k = 2; k < outline.Count - 1; k += 1)
        {
            var e = outline[k]; var out2 = Vector2.Normalize(e - new Vector2(-2.54f, -2.54f));
            Masonry.Rebar(iron, rng, new(e.X, Level - 0.08f, e.Y), new Vector3(out2.X, 0.05f - 0.4f * c.R, out2.Y), 0.3f + 0.3f * c.R);
        }
        // The fallen half: a broken slab tilted from under the edge down onto the rubble, and a smaller piece flat beside it.
        var fallen = new List<Vector2>();
        for (var k = 0; k < 9; k++)
        {
            var a = k / 9f * MathF.Tau;
            fallen.Add(new Vector2(MathF.Cos(a) * 0.75f, MathF.Sin(a) * 0.45f) * (0.8f + 0.35f * c.R));
        }
        var tilt = Matrix4x4.CreateRotationZ(0.62f) * Matrix4x4.CreateRotationY(-0.75f) * Matrix4x4.CreateTranslation(-0.55f, 0.75f, -0.4f);
        var fallenPart = new Part(Stonework, v => v);
        Shapes.Slab(fallenPart, fallen, -0.08f, 0.08f, (p, n) => slabCol);
        for (var i = 0; i < fallenPart.Indices.Count; i += 3)
        {
            var q = new[] { 0, 1, 2 }.Select(j => Vector3.Transform(fallenPart.Positions[fallenPart.Indices[i + j]], tilt)).ToArray();
            part.Tri(q[0], q[1], q[2], Weather(c, Shade(slabCol, 0.9f), q[0], 0.4f), Weather(c, Shade(slabCol, 0.9f), q[1], 0.4f), Weather(c, Shade(slabCol, 0.9f), q[2], 0.4f));
        }
        for (var k = 0; k < 4; k++)
        {
            var at = Vector3.Transform(new Vector3(-0.5f + 0.33f * k, 0.08f, 0.3f * (c.R - 0.5f)), tilt);
            Masonry.Rebar(iron, rng, at, Vector3.Transform(new Vector3(0, 0, 1), Matrix4x4.CreateRotationY(-0.75f)) + Vector3.UnitY * 0.3f, 0.3f);
        }
        Masonry.Girder(iron, new(-0.2f, 0.12f, 0.5f), new(-1.3f, 1.05f, -0.35f), 0.15f, 0.18f, q => Mix(Gunmetal, Rust, Smooth(0.4f, 0.65f, Fbm(q * 3, 12))), 0.3f);
    }

    /// <summary>A broken column: a stepped base, octagonal drums up to a break, the top drum slipped, and one fallen on the floor.</summary>
    internal static void Column(Ctx c, Vector2 at)
    {
        var part = c.Model[Trim];
        var col0 = Shade(Dressed, 0.85f);
        Masonry.Block(part, new(at.X, 0.12f, at.Y), new(0.7f, 0.24f, 0.7f), Matrix4x4.Identity, 0.04f, q => Weather(c, col0, q));
        Masonry.Block(part, new(at.X, 0.3f, at.Y), new(0.56f, 0.12f, 0.56f), Matrix4x4.Identity, 0.03f, q => Weather(c, col0, q));
        var y = 0.36f;
        for (var k = 0; k < 4; k++)
        {
            var h = 0.34f; var last = false;
            var col = Shade(Dressed, 0.82f + 0.18f * c.R);
            var off = last ? new Vector3(0.06f, 0, -0.04f) : Vector3.Zero;
            var m = Matrix4x4.CreateScale(0.22f, h - 0.02f, 0.22f) * (last ? Masonry.Turn(0.2f, 0.12f, -0.1f) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(new Vector3(at.X, y, at.Y) + off);
            Shapes.Prism(part, m, 8, 1, q => Weather(c, col, q), MathF.PI / 8);
            Shapes.Prism(part, Matrix4x4.CreateScale(0.235f, 0.04f, 0.235f) * Matrix4x4.CreateTranslation(at.X, y - 0.02f, at.Y), 8, 0.92f, q => Weather(c, Shade(col, 0.85f), q), MathF.PI / 8);
            y += h;
        }
        // The break: a jagged chunk of the next drum, slipped on the last.
        var colb = Shade(Dressed, 0.85f);
        Masonry.Block(part, new(at.X + 0.05f, y + 0.12f, at.Y - 0.03f), new(0.4f, 0.26f, 0.38f), Masonry.Turn(0.4f, 0.15f, -0.12f), 0.05f, q => Weather(c, colb, q), c.Rng, 0.3f);
        Masonry.Block(part, new(at.X - 0.1f, y + 0.3f, at.Y + 0.05f), new(0.2f, 0.18f, 0.22f), Masonry.Turn(1.1f, 0.3f, 0.2f), 0.04f, q => Weather(c, colb, q), c.Rng, 0.35f);
        Shapes.Prism(part, Matrix4x4.CreateScale(0.22f, 0.36f, 0.22f) * Matrix4x4.CreateRotationZ(MathF.PI / 2 - 0.08f) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(at.X + 0.75f, 0.2f, at.Y + 0.35f),
            8, 1, q => Weather(c, Shade(Dressed, 0.8f), q), 0.2f);
    }

    public static readonly Material Ivy = new("ivy", 0.8f, DoubleSided: true), Bark = new("bark", 0.9f);

    /// <summary>Ivy vines down both faces of the standing walls, each a stem with leaves; bushes of leaves, grass tufts and a young
    /// tree in the rubble.</summary>
    static void Overgrowth(Ctx c)
    {
        var leaf = c.Model[Ivy]; var bark = c.Model[Bark];
        var dark = Rgb(0.17f, 0.3f, 0.11f); var light = Rgb(0.38f, 0.52f, 0.18f); var yellow = Rgb(0.5f, 0.55f, 0.2f);
        Vector4 LeafColour() { var t = c.R; return Shade(t < 0.15f ? Mix(light, yellow, c.R) : Mix(dark, light, t), 0.8f + 0.3f * c.R); }
        foreach (var (origin, normal, along, length, top) in c.Faces)
        {
            var vines = (int)(length * (1.0f + 0.6f * c.R));
            for (var v = 0; v < vines; v++)
            {
                var s = 0.15f + (length - 0.3f) * c.R;
                if (top(s) < 0.8f) continue;
                var p = origin + along * s + Vector3.UnitY * (top(s) - 0.1f - 0.6f * c.R * c.R) + normal * 0.04f;
                var steps = (int)(5 + 10 * c.R);
                for (var k = 0; k < steps && p.Y > 0.1f; k++)
                {
                    var next = p + Vector3.UnitY * -0.24f + along * (c.R - 0.5f) * 0.2f;
                    var sNext = Vector3.Dot(next - origin, along);
                    if (sNext < 0.05f || sNext > length - 0.05f) break;
                    var w = Vector3.Cross(normal, Vector3.Normalize(next - p)) * 0.014f;   // the stem: a thin ribbon on the stone
                    leaf.Tri(p - w, p + w, next + w, Rgb(0.28f, 0.22f, 0.15f)); leaf.Tri(p - w, next + w, next - w, Rgb(0.28f, 0.22f, 0.15f));
                    for (var l = 0; l < 6; l++)
                    {
                        var off = along * (c.R - 0.5f) * 0.26f + Vector3.UnitY * -0.24f * c.R + normal * (0.02f + 0.05f * c.R);
                        var n = Vector3.Normalize(normal * 1.4f + new Vector3(c.R - 0.5f, c.R - 0.3f, c.R - 0.5f));
                        Masonry.Leaf(leaf, p + off, n, Vector3.UnitY * (c.R < 0.3f ? -1 : 1) + along * (c.R - 0.5f), 0.07f + 0.05f * c.R, LeafColour());
                    }
                    p = next;
                }
            }
        }
        // Bushes: clusters of leaves round a few twigs.
        for (var k = 0; k < 9; k++)
        {
            var centre = new Vector3(-2.2f + 4.6f * c.R, 0, -1.8f + 4.4f * c.R);
            var r = 0.25f + 0.2f * c.R;
            for (var l = 0; l < 64; l++)
            {
                var d = Vector3.Normalize(new Vector3(c.R - 0.5f, c.R * 0.9f, c.R - 0.5f) + new Vector3(0, 0.05f, 0));
                var p = centre + d * r * (0.55f + 0.45f * c.R) * new Vector3(1.2f, 1, 1.2f) + Vector3.UnitY * 0.04f;
                Masonry.Leaf(leaf, p, d, Vector3.UnitY + d, 0.07f + 0.04f * c.R, LeafColour());
            }
        }
        // Grass tufts between the flagstones and on the rubble.
        for (var k = 0; k < 70; k++)
            Shapes.Tuft(leaf, c.Rng, new Vector3(-2.7f + 5.4f * c.R, 0.02f, -2.4f + 5.2f * c.R), 6, 0.22f + 0.15f * c.R, 0.03f, 0.35f, Rgb(0.25f, 0.36f, 0.13f), Rgb(0.55f, 0.6f, 0.28f));
        // A young tree out of the left wall's rubble: a leaning trunk, forked limbs, and leaves in clumps at the twig ends.
        var root = new Vector3(-2.0f, 0.1f, 1.5f);
        var trunk = new[] { root, root + new Vector3(0.1f, 0.9f, -0.05f), root + new Vector3(0.3f, 1.8f, -0.2f), root + new Vector3(0.38f, 2.5f, -0.3f) };
        Vector4 BarkColour(Vector3 q) => Shade(Rgb(0.36f, 0.29f, 0.21f), 0.8f + 0.25f * Noise(q.X * 8, q.Y * 8, q.Z * 8, 3));
        Shapes.Chain(bark, trunk, 0.13f, 0.05f, 7, BarkColour);
        var tips = new List<Vector3>();
        void Branch(Vector3 from, Vector3 dir, float len, float r, int depth)
        {
            var to = from + dir * len;
            Shapes.Limb(bark, from, to, r, 0.65f, 5, BarkColour);
            if (depth == 0) { tips.Add(to); return; }
            for (var b = 0; b < 2; b++)
            {
                var turn = Vector3.Normalize(dir + new Vector3(c.R - 0.5f, 0.35f + 0.3f * c.R, c.R - 0.5f) * 1.1f);
                Branch(to, turn, len * 0.72f, r * 0.65f, depth - 1);
            }
        }
        foreach (var (i, d) in new[] { (1, new Vector3(-0.8f, 0.6f, 0.4f)), (2, new Vector3(0.8f, 0.5f, 0.3f)), (2, new Vector3(-0.3f, 0.6f, -0.8f)), (3, new Vector3(0.2f, 1, 0.1f)), (3, new Vector3(0.5f, 0.7f, -0.6f)) })
            Branch(trunk[i], Vector3.Normalize(d), 0.55f, 0.05f, 2);
        foreach (var tip in tips)
            for (var l = 0; l < 32; l++)
            {
                var d = Vector3.Normalize(new Vector3(c.R - 0.5f, c.R - 0.35f, c.R - 0.5f));
                var p = tip + d * 0.28f * MathF.Sqrt(c.R);
                Masonry.Leaf(leaf, p, Vector3.Normalize(d + Vector3.UnitY * 0.6f), Vector3.Normalize(d + new Vector3(c.R - 0.5f, 0, c.R - 0.5f)), 0.09f + 0.05f * c.R, LeafColour());
            }
    }
}
