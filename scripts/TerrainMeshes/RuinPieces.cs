using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;
using static Tabletop.TerrainMeshes.Ruins;

namespace Tabletop.TerrainMeshes;

/// <summary>The ruins planet's own pieces, built from <see cref="Ruins"/>' masonry (ashlar courses, framed pointed-arch openings,
/// pilasters, flagstones, rebar, girders, rubble of individual blocks), each an inches model at its catalog entry's size:
/// <see cref="Corner"/> (`ruined-corner`), <see cref="Wall"/> (`ruined-wall`) and <see cref="Field"/> (`rubble-field`).</summary>
public static class RuinPieces
{
    static readonly Vector4 SlabColour = Rgb(0.42f, 0.41f, 0.39f), Gunmetal = Rgb(0.3f, 0.31f, 0.33f), Rust = Rgb(0.45f, 0.25f, 0.13f), Dust = Rgb(0.4f, 0.37f, 0.33f);

    /// <summary>A broken wall top's jaggedness along a wall: fine noise plus stepped course-sized drops.</summary>
    static float Jag(float s, int salt, float amp, int seed) =>
        amp * (Noise(s * 2.6f, salt, 0, seed) - 0.5f) + amp * 0.9f * (MathF.Floor(Noise(s * 1.3f, salt, 3, seed) * 4) / 4 - 0.4f);

    static Vector4 IronPaint(Vector3 q) => Mix(Gunmetal, Rust, Smooth(0.45f, 0.7f, Fbm(q * 3, 11)));

    static void Heap(Ctx c, Vector2 at, Vector2 r, float h, int n, float yaw = 0, float size = 0.17f) =>
        Masonry.Rubble(c.Model[Stonework], c.Model[Grit], c.Rng, new Vector3(at.X, 0, at.Y), r, h, n, size, _ => Weather(c, BlockPaint(c), new Vector3(0, 0.3f, 0)), Dust, yaw);

    /// <summary>Loose blocks on the floor inside <paramref name="min"/>–<paramref name="max"/>.</summary>
    static void Loose(Ctx c, int count, Vector2 min, Vector2 max)
    {
        for (var k = 0; k < count; k++)
        {
            var p = new Vector3(min.X + (max.X - min.X) * c.R, 0, min.Y + (max.Y - min.Y) * c.R);
            var s = 0.1f + 0.1f * c.R;
            var col = BlockPaint(c);
            Masonry.Block(c.Model[Stonework], p + Vector3.UnitY * (0.05f + s * 0.25f), new Vector3(s * 1.7f, s * 0.7f, s * 0.85f), Masonry.Turn(c.R * 6, (c.R - 0.5f) * 0.4f, (c.R - 0.5f) * 0.4f),
                s * 0.12f, q => Weather(c, col, q), c.Rng, 0.15f);
        }
    }

    /// <summary>A broken slab of rockcrete <paramref name="thick"/> thick, about <paramref name="radius"/> across, cracked in two
    /// along a jagged line, the halves a little apart and each canted its own way, turned by <paramref name="pose"/> (from its
    /// centre): a bevelled top with plate seams, a broken edge of uneven points, rebar out of the crack and the edge.</summary>
    static void Fallen(Ctx c, Vector2 radius, float thick, Matrix4x4 pose, int bars)
    {
        var part = c.Model[Stonework];
        var crack = new[] { new Vector2(-1.05f, 0), new Vector2(-0.4f, 0.12f * (c.R - 0.5f) * 2), new Vector2(0.1f, -0.15f * c.R), new Vector2(0.55f, 0.14f * c.R), new Vector2(1.05f, 0) };
        for (var half = 0; half < 2; half++)
        {
            var side = half == 0 ? 1 : -1;
            var outline = new List<Vector2>();
            for (var k = 0; k <= 6; k++)
            {
                var a = side * k / 6f * MathF.PI;
                var r = k is 0 or 6 ? 1 : 0.72f + 0.38f * c.R;
                outline.Add(new Vector2(MathF.Cos(a) * radius.X * r, MathF.Sin(a) * radius.Y * r));
            }
            outline.AddRange(crack.Skip(1).Take(3).Select(p => p * radius.X));   // back along the crack from −x to +x
            var loose = new Part(Stonework, v => v);
            var tone = 0.85f + 0.2f * c.R;
            Shapes.Slab(loose, outline, -thick / 2, thick / 2, (p, n) => Shade(SlabColour, (n.Y > 0.5f ? 1f : n.Y < -0.5f ? 0.6f : 0.78f) * tone), 0.9f);
            var apart = Matrix4x4.CreateRotationX(side * (0.06f + 0.08f * c.R)) * Matrix4x4.CreateRotationY(side * 0.05f) * Matrix4x4.CreateTranslation(0, side * 0.02f, side * 0.045f) * pose;
            for (var i = 0; i < loose.Indices.Count; i += 3)
            {
                var q = new[] { 0, 1, 2 }.Select(j => Vector3.Transform(loose.Positions[loose.Indices[i + j]], apart)).ToArray();
                var col = loose.Colours[loose.Indices[i]];
                part.Tri(q[0], q[1], q[2], Weather(c, col, q[0], 0.4f), Weather(c, col, q[1], 0.4f), Weather(c, col, q[2], 0.4f));
            }
            // Seams between the floor plates cast in its top.
            for (var s = -1; s <= 1; s += 2)
            {
                var x = s * radius.X * 0.35f;
                var a = Vector3.Transform(new Vector3(x, thick / 2 + 0.004f, 0.03f * side), apart); var b = Vector3.Transform(new Vector3(x, thick / 2 + 0.004f, side * radius.Y * 0.62f), apart);
                var along = Vector3.Normalize(b - a); var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, apart));
                Masonry.Block(part, (a + b) / 2, new Vector3(0.03f, 0.012f, Vector3.Distance(a, b)), Masonry.Basis(Vector3.Cross(up, along), up, along), 0, Shade(SlabColour, 0.55f), corners: false);
            }
            for (var k = 0; k < bars; k++)
            {
                var e = k == 0 ? crack[1 + c.Rng.Next(3)] * radius.X : outline[c.Rng.Next(outline.Count)];
                var at = Vector3.Transform(new Vector3(e.X, 0, e.Y) * 0.95f, apart);
                var dir = Vector3.TransformNormal(k == 0 ? new Vector3(0, 0, -side) : Vector3.Normalize(new Vector3(e.X, 0, e.Y)), apart) + Vector3.UnitY * 0.2f;
                Masonry.Rebar(c.Model[Iron], c.Rng, at, dir, 0.2f + 0.2f * c.R);
            }
        }
    }

    /// <summary>An upper floor's stub in a corner: a rockcrete slab at <paramref name="level"/> out from <paramref name="corner"/>
    /// (the walls' inner corner) along <paramref name="u"/> and <paramref name="v"/>, broken along a jagged arc about
    /// <paramref name="reach"/> out, on girders out of both walls, floor plates on it and rebar out of the break.</summary>
    static void UpperFloor(Ctx c, Vector2 corner, Vector2 u, Vector2 v, float level, float reach)
    {
        Vector2 W(float a, float b) => corner + u * a + v * b;
        var part = c.Model[Stonework]; var iron = c.Model[Iron];
        var local = new List<Vector2> { new(0, 0), new(reach + 0.05f, 0) };
        for (var k = 1; k < 12; k++)
        {
            var t = k / 12f; var a = t * MathF.PI / 2;
            var r = reach + 0.35f * (Noise(t * 9, 1, 1, 61) - 0.5f) + 0.25f * (k % 2 == 0 ? 1 : -1) * c.R;
            local.Add(new Vector2(MathF.Cos(a) * r * 1.05f, MathF.Sin(a) * r * 0.95f));
        }
        local.Add(new(0, reach - 0.1f));
        var outline = local.Select(p => W(p.X, p.Y)).ToList();
        Shapes.Slab(part, outline, level - 0.16f, level, (p, n) => Weather(c, Shade(SlabColour, n.Y > 0.5f ? 0.95f : 0.8f), p, 0.3f));
        foreach (var (a, b) in new[] { (W(-0.02f, 0.95f), W(reach + 0.35f, 0.95f)), (W(0.95f, -0.02f), W(0.95f, reach * 0.6f)) })
            Masonry.Girder(iron, new(a.X, level - 0.26f, a.Y), new(b.X, level - 0.26f, b.Y), 0.16f, 0.2f, IronPaint);
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
            {
                var p = new Vector2(0.24f + i * 0.5f, 0.24f + j * 0.5f);
                if (p.Length() > reach - 0.35f || c.R < 0.15f) continue;
                var w = W(p.X, p.Y);
                var col = Shade(Rgb(0.44f, 0.43f, 0.41f), 0.8f + 0.3f * c.R);
                Masonry.Block(part, new(w.X, level + 0.02f, w.Y), new(0.46f, 0.04f, 0.46f), Masonry.Turn((c.R - 0.5f) * 0.05f), 0.012f, col);
            }
        for (var k = 2; k < outline.Count - 1; k++)
        {
            var e = outline[k]; var out2 = Vector2.Normalize(e - corner);
            Masonry.Rebar(iron, c.Rng, new(e.X, level - 0.08f, e.Y), new Vector3(out2.X, 0.05f - 0.4f * c.R, out2.Y), 0.3f + 0.3f * c.R);
        }
    }

    /// <summary>A two-storey ruined corner, 6" × 6" and 5" high at the corner (an Area: models stand on the floor inside): a
    /// flagstone floor at ground level, two broken walls of ashlar courses meeting at the back right under a corner pilaster,
    /// three rows of pointed-arch windows and a doorway, both walls stepping down where they broke; a stub of first floor on girders,
    /// joists of the floor above it out of the walls (one hanging), the low foundations of the two fallen walls, rubble heaps
    /// under the breaks and a fallen slab.</summary>
    public static Model Corner()
    {
        const int seed = 61; const float level = 2.35f, height = 5;
        var model = new Model("ruined-corner", round: false, new Vector3(6, height, 6));
        var c = new Ctx(model, new Random(seed), overgrown: false);
        FloorTiles(c, keep: p => Vector2.Distance(p, new(-0.95f, -2.0f)) > 0.8f);
        Opening[] Rows(float at, bool door = false) =>
        [
            door ? new(at, 0.4f, 0, 1.12f) : new(at, 0.3f, 0.75f, 1.3f), new(at, 0.28f, 2.85f, 3.3f), new(at, 0.22f, 4.0f, 4.3f),
        ];
        // The back wall (along −x from the corner): near full height for 1.9", down to the first floor, a breach, a low run.
        Ruins.Wall(c, new(2.95f, -2.7f), new(-1, 0), 5.9f,
            s => s < 1.9f ? 4.92f - 0.08f * s + Jag(s, 1, 0.35f, seed)
                : s < 2.5f ? 4.77f - 4.0f * (s - 1.9f) + Jag(s, 2, 0.25f, seed)
                : s < 3.5f ? 2.3f + Jag(s, 3, 0.35f, seed)
                : s < 4.0f ? 2.3f - 3.2f * (s - 3.5f) + Jag(s, 4, 0.2f, seed)
                : 0.7f + 0.25f * (s - 4.0f) + Jag(s, 5, 0.3f, seed),
            [.. Rows(1.0f), .. Rows(2.3f).Take(2), new(3.2f, 0.4f, 0, 1.1f)], height, level, corners: false);
        // The right wall (along +z from the corner): full height for 1.7", then down in two steps toward the front.
        Ruins.Wall(c, new(2.7f, -2.45f), new(0, 1), 5.4f,
            s => s < 1.7f ? 4.87f - 0.1f * s + Jag(s, 6, 0.35f, seed)
                : s < 2.3f ? 4.7f - 3.5f * (s - 1.7f) + Jag(s, 7, 0.25f, seed)
                : s < 3.6f ? 2.55f - 0.3f * (s - 2.3f) + Jag(s, 8, 0.35f, seed)
                : 2.2f - 0.8f * (s - 3.6f) + Jag(s, 9, 0.35f, seed),
            [.. Rows(0.9f), .. Rows(2.4f).Take(2), new(4.2f, 0.3f, 0.75f, 1.3f)], height, level, corners: false);
        // The foundations of the fallen left and front walls.
        Ruins.Wall(c, new(-2.7f, -2.45f), new(0, 1), 5.4f, s => 0.75f + 0.2f * MathF.Sin(s * 1.7f) + Jag(s, 10, 0.3f, seed), [], 1.2f, level, corners: false);
        Ruins.Wall(c, new(2.45f, 2.7f), new(-1, 0), 5.0f, s => 0.85f - 0.1f * s + Jag(s, 11, 0.3f, seed), [], 1.2f, level, corners: false);
        Pilaster(c, new(2.7f, -2.7f), 5.1f, level: level);
        Pilaster(c, new(1.3f, -2.7f), 4.65f, along: true, level: level);
        Pilaster(c, new(2.7f, -0.8f), 4.55f, along: false, level: level);
        Pilaster(c, new(-2.7f, -2.7f), 1.1f, level: level);
        UpperFloor(c, new(2.54f, -2.54f), new(-1, 0), new(0, 1), level, 2.3f);
        // Joists of the floor above: girders out of the back wall at 4.15", broken short, the last one hanging from its end.
        var iron = model[Iron];
        foreach (var (x, len) in new[] { (2.2f, 1.6f), (1.55f, 1.15f), (0.9f, 0.7f) })
        {
            Masonry.Girder(iron, new(x, 4.15f, -2.6f), new(x, 4.15f, -2.6f + len), 0.13f, 0.17f, IronPaint);
            Masonry.Rebar(iron, c.Rng, new(x, 4.2f, -2.6f + len), new Vector3(0, -0.2f, 1), 0.25f);
        }
        Masonry.Girder(iron, new(2.55f, 4.15f, -1.35f), new(2.55f, 4.0f, -0.4f), 0.13f, 0.17f, IronPaint);
        Masonry.Girder(iron, new(2.55f, 4.0f, -0.4f), new(2.3f, 2.55f, 0.25f), 0.13f, 0.17f, IronPaint, 0.2f);
        // A floor plate left on the joists in the corner.
        Masonry.Block(model[Stonework], new(2.0f, 4.27f, -2.15f), new(1.0f, 0.06f, 0.7f), Masonry.Turn(0.04f, 0, 0.03f), 0.015f, q => Weather(c, Shade(SlabColour, 0.95f), q, 0.2f), c.Rng, 0.05f);
        Heap(c, new(-0.95f, -2.0f), new(1.05f, 0.55f), 0.7f, 50);
        Heap(c, new(2.05f, 2.05f), new(0.55f, 0.8f), 0.5f, 34, 0.3f);
        Heap(c, new(0.1f, -0.25f), new(0.7f, 0.5f), 0.35f, 26, 0.5f);
        Heap(c, new(-2.1f, 1.6f), new(0.55f, 0.6f), 0.3f, 22, 0.9f);
        Fallen(c, new(0.75f, 0.45f), 0.16f, Matrix4x4.CreateRotationZ(-0.55f) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(0.25f, 0.6f, 0.1f), 3);
        Loose(c, 14, new(-2.2f, -1.5f), new(2.2f, 2.2f));
        return model;
    }

    /// <summary>A ruined wall section, 6" long, 4" high and 1.5" deep with what fell from it (a Solid: it blocks, and its drawn top
    /// reaches the volume's 4"): ashlar courses on a plinth broken along a jagged top that never falls far, two rows of
    /// pointed-arch windows and a doorway with broken frames, a string course, pilasters at both ends and one in the middle up to
    /// the full height, rebar out of the breaks, a girder of the fallen floor still lodged in it, and rubble heaps along both feet.</summary>
    public static Model Wall()
    {
        const int seed = 71;
        var model = new Model("ruined-wall", round: false, new Vector3(6, 4, 1.5f));
        var c = new Ctx(model, new Random(seed), overgrown: false);
        Ruins.Wall(c, new(-2.95f, 0), new(1, 0), 5.9f,
            s => s < 1.0f ? 3.6f + Jag(s, 1, 0.5f, seed)
                : s < 1.6f ? 3.6f - 1.2f * (s - 1.0f) + Jag(s, 2, 0.4f, seed)
                : s < 2.4f ? 2.88f + 1.2f * (s - 1.6f) + Jag(s, 3, 0.4f, seed)
                : s < 4.6f ? 3.97f + Jag(s, 4, 0.3f, seed)
                : 3.9f - 0.9f * (s - 4.6f) + Jag(s, 5, 0.5f, seed),
            [new(1.2f, 0.3f, 0.75f, 1.3f), new(2.5f, 0.3f, 0.75f, 1.3f), new(5.0f, 0.42f, 0, 1.15f),
             new(1.2f, 0.28f, 2.5f, 3.0f), new(2.5f, 0.28f, 2.5f, 3.0f), new(5.0f, 0.28f, 2.5f, 3.0f)], 4, Level, corners: false);
        Pilaster(c, new(-2.72f, 0), 3.75f, along: true);
        Pilaster(c, new(0.95f, 0), 4.3f, along: true);
        Pilaster(c, new(2.72f, 0), 3.05f, along: true);
        Masonry.Girder(model[Iron], new(-0.45f, 2.0f, 0.12f), new(0.55f, 0.12f, 0.66f), 0.14f, 0.17f, IronPaint, 0.3f);
        Heap(c, new(-1.6f, 0.47f), new(0.75f, 0.26f), 0.45f, 28);
        Heap(c, new(1.9f, -0.48f), new(0.85f, 0.26f), 0.5f, 30, 0.1f);
        Heap(c, new(-0.3f, -0.5f), new(0.5f, 0.23f), 0.3f, 16, -0.1f);
        Heap(c, new(2.45f, 0.5f), new(0.45f, 0.24f), 0.35f, 15);
        Loose(c, 8, new(-2.6f, -0.6f), new(2.6f, 0.6f));
        return model;
    }

    /// <summary>A collapsed building's footprint, 6" × 4" and at most 1" high (an Area of rubble): the flagstone floor showing in
    /// patches, the stumps of its four walls a course or three high with gaps, two big heaps of rubble and two small ones, broken
    /// floor slabs fallen across them with rebar out of their edges, girders lying where they dropped, a fallen column drum and
    /// loose blocks.</summary>
    public static Model Field()
    {
        const int seed = 81;
        var model = new Model("rubble-field", round: false, new Vector3(6, 1, 4));
        var c = new Ctx(model, new Random(seed), overgrown: false);
        Vector2[] heaps = [new(-1.2f, -0.25f), new(1.45f, 0.45f)];
        FloorTiles(c, new Vector2(3, 2), 10, 6, new Vector2(-2.95f, -1.77f),
            p => Fbm(p.X * 0.9f, 0, p.Y * 0.9f, seed) > 0.42f && heaps.All(h => Vector2.Distance(p, h) > 0.95f));
        // The wall stumps: runs a course or three high, with gaps where they fell to the floor.
        float Stump(float s, int salt) => 0.45f + 0.4f * Noise(s * 0.7f, salt, 5, seed) + Jag(s, salt, 0.3f, seed);
        foreach (var (from, dir, runs, salt) in new (Vector2, Vector2, (float A, float B)[], int)[]
        {
            (new(-2.78f, -1.78f), new(1, 0), [(0, 1.9f), (2.5f, 4.6f), (5.1f, 5.56f)], 1),
            (new(2.78f, 1.78f), new(-1, 0), [(0, 2.6f), (3.4f, 4.2f), (4.9f, 5.56f)], 2),
            (new(-2.78f, 1.55f), new(0, -1), [(0, 1.2f), (2.0f, 3.1f)], 3),
            (new(2.78f, -1.55f), new(0, 1), [(0, 3.1f)], 4),
        })
            foreach (var (a, b) in runs)
                Ruins.Wall(c, from + dir * a, dir, b - a, s => Stump(s + a, salt), [], 1, 9, corners: false);
        // Each big heap is a cluster of overlapping mounds, densely covered in blocks, bricks and slabs.
        Heap(c, heaps[0], new(0.85f, 0.6f), 0.82f, 60, 0.2f, 0.21f);
        Heap(c, heaps[0] + new Vector2(-0.75f, 0.4f), new(0.55f, 0.45f), 0.5f, 26, 1.0f, 0.19f);
        Heap(c, heaps[0] + new Vector2(0.65f, -0.45f), new(0.5f, 0.4f), 0.45f, 22, -0.6f, 0.18f);
        Heap(c, heaps[1], new(0.75f, 0.55f), 0.72f, 46, -0.4f, 0.21f);
        Heap(c, heaps[1] + new Vector2(0.55f, 0.5f), new(0.45f, 0.35f), 0.4f, 18, 0.3f, 0.18f);
        Heap(c, new(0.3f, 1.15f), new(0.55f, 0.35f), 0.38f, 20, 0.1f);
        Heap(c, new(2.15f, -1.15f), new(0.45f, 0.38f), 0.33f, 16, 0.6f);
        Fallen(c, new(0.7f, 0.45f), 0.14f, Matrix4x4.CreateRotationZ(0.32f) * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(-0.5f, 0.55f, 0.35f), 3);
        Fallen(c, new(0.7f, 0.5f), 0.13f, Matrix4x4.CreateRotationX(-0.28f) * Matrix4x4.CreateRotationY(-0.9f) * Matrix4x4.CreateTranslation(1.0f, 0.42f, -0.55f), 2);
        Fallen(c, new(0.5f, 0.4f), 0.12f, Matrix4x4.CreateRotationZ(-0.12f) * Matrix4x4.CreateTranslation(-2.0f, 0.16f, 0.95f), 2);
        var iron = model[Iron];
        Masonry.Girder(iron, new(-2.45f, 0.1f, 0.85f), new(-0.25f, 0.68f, -0.25f), 0.14f, 0.17f, IronPaint, 0.25f);
        Masonry.Girder(iron, new(0.55f, 0.1f, -1.3f), new(2.45f, 0.38f, -0.2f), 0.14f, 0.17f, IronPaint, -0.3f);
        var drum = Shade(Rgb(0.57f, 0.55f, 0.51f), 0.82f);
        Shapes.Prism(model[Trim], Matrix4x4.CreateScale(0.22f, 0.4f, 0.22f) * Matrix4x4.CreateRotationZ(MathF.PI / 2 - 0.06f) * Matrix4x4.CreateRotationY(1.1f) * Matrix4x4.CreateTranslation(0.0f, 0.22f, -1.05f),
            8, 1, q => Weather(c, drum, q), 0.2f);
        Loose(c, 20, new(-2.5f, -1.5f), new(2.5f, 1.5f));
        return model;
    }
}
