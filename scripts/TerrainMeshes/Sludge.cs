using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>Industrial sludge. <see cref="Field"/> (6" × 6", 0.5" high): pools of glowing green-yellow sludge in cracked grey waste
/// ground, each ringed by a pale chemical crust, with leaking drums standing and tipped over. <see cref="Canal"/> (2" × 12", 0.5"
/// high): a concrete channel along z with rusted steel copings, expansion joints, an outfall pipe and a thick sludge flow.</summary>
public static class Sludge
{
    public static readonly Material Ground = new("waste-ground", 0.9f), Goo = new("sludge", 0.55f, Emissive: new Vector3(0.07f, 0.1f, 0.0f)),
        Crust = new("crust", 0.95f), Weed = new("dead-weeds", 0.9f, DoubleSided: true), Drum = new("drum", 0.5f, Metallic: 0.6f), Concrete = new("concrete", 0.85f), Rust = new("rusted-steel", 0.6f, Metallic: 0.5f);

    static readonly Vector4 Toxic = Rgb(0.42f, 0.5f, 0.1f), Bile = Rgb(0.78f, 0.82f, 0.16f), Waste = Rgb(0.46f, 0.43f, 0.38f), Ash = Rgb(0.24f, 0.23f, 0.21f),
        Salt = Rgb(0.82f, 0.8f, 0.62f), RustRed = Rgb(0.45f, 0.24f, 0.12f);

    static Vector4 SludgeColour(Vector3 q, int seed) => Mix(Mix(Shade(Toxic, 0.75f + 0.3f * Fbm(q.X * 2, q.Z * 2, 0, seed)), Rgb(0.3f, 0.3f, 0.12f), Smooth(0.55f, 0.7f, Fbm(q.X * 1.5f + 3, q.Z * 1.5f, 2, seed)) * 0.6f), Bile, Smooth(0.62f, 0.75f, Fbm(q.X * 3 + 5, q.Z * 3, 1, seed)) * 0.8f);

    public static Model Field()
    {
        const float Size = 6, Top = 0.5f, Level = 0.09f;
        var model = new Model("sludge-field", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(161);
        float Pool(float x, float z) => Fbm(x * 0.7f + 2, z * 0.7f + 11, 0, 161) + 0.05f;   // below 0.47: sludge
        float Height(float x, float z)
        {
            var dist = 3 - MathF.Max(MathF.Abs(x), MathF.Abs(z));
            var p = Pool(x, z);
            var rim = 0.06f * Smooth(0.44f, 0.475f, p) * Smooth(0.53f, 0.49f, p);                      // the crust raised round each pool
            var y = 0.035f + 0.1f * Smooth(0.45f, 0.5f, p) + rim + 0.025f * Fbm(x * 3, z * 3, 2, 162);
            return dist < 0.4f ? MathF.Max(y, Level + 0.03f) * Smooth(0, 0.2f, dist) : y;
        }
        Foliage.Rect(Quill.Of(model, Ground, model.Inches), Size, Size, 40, 40, Height, (p, n) =>
        {
            var pool = Pool(p.X, p.Z);
            var crack = Smooth(0.47f, 0.5f, Noise(p.X * 5, p.Z * 5, 3, 163)) * Smooth(0.53f, 0.5f, Noise(p.X * 5, p.Z * 5, 3, 163));
            var c = Mix(Shade(Waste, 0.85f + 0.25f * Fbm(p.X * 4, p.Z * 4, 0, 164)), Ash, crack * 0.8f);
            c = Mix(c, Salt, Smooth(0.43f, 0.465f, pool) * Smooth(0.54f, 0.5f, pool) * (0.6f + 0.4f * Fbm(p.X * 8, p.Z * 8, 5, 165)));
            return Shade(Mix(c, Toxic, Smooth(0.47f, 0.42f, pool) * 0.6f), 0.85f + 0.25f * Noise(p.X * 14, p.Z * 14, p.Y * 20, 168));
        }, 161, 0.35f);
        model[Goo].Grid(41, 41, wrap: false, (i, j) =>
        {
            var x = -2.7f + 5.4f * j / 40; var z = -2.7f + 5.4f * i / 40;
            return new Vector3(x, Level + 0.006f * Noise(x * 6, z * 6, 0, 166), z);
        }, (p, _) => SludgeColour(model.Inch(p), 167), flipped: true);
        // Bubbles and skins on the sludge.
        for (var k = 0; k < 160 && model[Goo].Triangles < 4600; k++)
        {
            var x = (float)rng.NextDouble() * 5.2f - 2.6f; var z = (float)rng.NextDouble() * 5.2f - 2.6f;
            if (Pool(x, z) > 0.42f) continue;
            var r = 0.04f + 0.08f * (float)rng.NextDouble();
            Foliage.Stone(Quill.Of(model, Goo, model.Inches), new Vector3(x, Level, z), new Vector3(r, r * 0.8f, r), 170 + k, 0, (q, n) => Shade(Bile, 0.85f + 0.25f * n.Y), 1, 0.08f);
        }
        // Rubble, crust flakes and dead weeds on the waste ground.
        var rubble = Quill.Of(model, Crust, model.Inches);
        for (var k = 0; k < 200; k++)
        {
            var x = (float)rng.NextDouble() * 5.6f - 2.8f; var z = (float)rng.NextDouble() * 5.6f - 2.8f;
            var pool = Pool(x, z);
            if (pool < 0.45f) continue;
            var y = Height(x, z);
            if (k % 3 == 0)
            {
                var s = 0.03f + 0.08f * (float)rng.NextDouble();
                Foliage.Stone(rubble, new Vector3(x, y, z), new Vector3(s * 1.3f, s * 0.6f, s), 500 + k, (float)rng.NextDouble() * 3,
                    (q, n) => Shade(pool < 0.52f ? Salt : Mix(Waste, Ash, 0.3f), 0.75f + 0.35f * n.Y), k % 9 == 0 ? 1 : 0);
            }
            else if (k % 3 == 1 && pool < 0.53f)
            {
                var o = Foliage.Flat((float)rng.NextDouble() * 6.28f); var s = 0.08f + 0.1f * (float)rng.NextDouble();
                var c = Shade(Salt, 0.85f + 0.2f * (float)rng.NextDouble());
                var tilt = Vector3.Normalize(Vector3.UnitY + Foliage.Sphere(rng) * 0.3f);
                Foliage.Leaf(rubble, new Vector3(x, y + 0.01f, z), tilt, Vector3.Normalize(Vector3.Cross(tilt, new Vector3(-o.Z, 0, o.X))), s, s * 0.8f, c);
            }
            else Foliage.Tuft(Quill.Of(model, Weed, model.Inches), rng, new Vector3(x, y - 0.005f, z), 5, 0.1f + 0.1f * (float)rng.NextDouble(), 0.025f, 0.8f, Rgb(0.24f, 0.22f, 0.16f), Rgb(0.5f, 0.46f, 0.3f));
        }
        // Drums: standing in a group, one tipped into a pool and spilling.
        var drums = new[] { Rgb(0.8f, 0.62f, 0.12f), Rgb(0.2f, 0.36f, 0.6f), Rgb(0.6f, 0.22f, 0.14f) };
        foreach (var (at, tipped, k) in new[] { (new Vector2(-1.9f, 1.8f), false, 0), (new Vector2(-1.55f, 2.05f), false, 1), (new Vector2(-1.8f, 1.45f), false, 2), (new Vector2(1.7f, -1.9f), false, 0), (new Vector2(0.4f, 0.6f), true, 1), (new Vector2(2.1f, 1.6f), true, 2) })
        {
            var colour = drums[k];
            var y = Height(at.X, at.Y);
            if (!tipped) Barrel(model, new Vector3(at.X, y - 0.02f, at.Y), Vector3.UnitY, colour, rng);
            else
            {
                var dir = Vector3.Normalize(new Vector3(0.6f, 0, 0.8f * (k == 1 ? 1 : -1)));
                Barrel(model, new Vector3(at.X, MathF.Max(y, Level) + 0.15f, at.Y) - dir * 0.2f, dir, colour, rng);
                Shapes.Blob(model[Goo], new Vector3(at.X, Level, at.Y) + dir * 0.35f, new Vector3(0.32f, 0.03f, 0.22f), 1, 0.2f, 190 + k, (q, n) => SludgeColour(model.Inch(q), 191), yaw: MathF.Atan2(dir.Z, dir.X));
            }
        }
        return model;
    }

    /// <summary>A steel drum, 0.3" across and 0.45" long, from its base centre along <paramref name="axis"/>, with rolling hoops,
    /// rust and a hazard band.</summary>
    static void Barrel(Model model, Vector3 foot, Vector3 axis, Vector4 colour, Random rng)
    {
        var top = foot + axis * 0.42f;
        var rust = (float)rng.NextDouble();
        Shapes.Limb(model[Drum], foot, top, 0.15f, 1, 12, p => Mix(colour, RustRed, Smooth(0.5f, 0.8f, Fbm(p.X * 9, p.Y * 9, p.Z * 9, 7)) * (0.4f + 0.6f * rust)));
        foreach (var t in new[] { 0.3f, 0.7f })
            Shapes.Limb(model[Drum], foot + axis * (t * 0.42f - 0.012f), foot + axis * (t * 0.42f + 0.012f), 0.158f, 1, 12, Shade(colour, 0.7f));
        Shapes.Limb(model[Drum], foot + axis * 0.17f, foot + axis * 0.24f, 0.153f, 1, 12, p => Rgb(0.85f, 0.7f, 0.1f));
        Shapes.Limb(model[Drum], top, top + axis * 0.015f, 0.14f, 1, 12, Shade(colour, 0.8f));
    }

    /// <summary>A cast concrete block between <paramref name="min"/> and <paramref name="max"/>: every face but the bottom split into cells about
    /// <paramref name="cell"/> wide, spalled in and out a little, its top edges chipped back where the noise says so.</summary>
    static void Cast(Quill pen, Vector3 min, Vector3 max, float cell, Func<Vector3, Vector4> colour, int seed)
    {
        var centre = (min + max) / 2;
        Vector3 Chip(Vector3 v)
        {
            var onTop = MathF.Abs(v.Y - max.Y) < 1e-4f;
            var onSide = MathF.Abs(v.X - min.X) < 1e-4f || MathF.Abs(v.X - max.X) < 1e-4f || MathF.Abs(v.Z - min.Z) < 1e-4f || MathF.Abs(v.Z - max.Z) < 1e-4f;
            if (!onTop || !onSide) return v;
            var bite = Smooth(0.55f, 0.8f, Noise(v.X * 9, v.Y * 9, v.Z * 9, seed)) * 0.05f;
            var inward = Vector3.Normalize((centre - v) with { Y = 0 });
            return v + (inward - Vector3.UnitY) * bite;
        }
        var size = max - min;
        var faces = new (Vector3 Origin, Vector3 U, Vector3 V, Vector3 N)[]
        {
            (min with { Y = max.Y }, new(size.X, 0, 0), new(0, 0, size.Z), Vector3.UnitY),
            (min, new(size.X, 0, 0), new(0, size.Y, 0), -Vector3.UnitZ), (min with { Z = max.Z }, new(size.X, 0, 0), new(0, size.Y, 0), Vector3.UnitZ),
            (min, new(0, 0, size.Z), new(0, size.Y, 0), -Vector3.UnitX), (min with { X = max.X }, new(0, 0, size.Z), new(0, size.Y, 0), Vector3.UnitX),
        };
        foreach (var (o, u, v, n) in faces)
        {
            int nu = Math.Max(1, (int)MathF.Round(u.Length() / cell)), nv = Math.Max(1, (int)MathF.Round(v.Length() / cell));
            Vector3 P(int i, int j)
            {
                var p = o + u * i / nu + v * j / nv;
                var interior = i > 0 && i < nu && j > 0 && j < nv;
                if (interior) p += n * 0.012f * (Noise(p.X * 7, p.Y * 7, p.Z * 7, seed + 1) * 2 - 1) + (u / nu * 0.25f * Foliage.Jitter(i, j, seed)) + (v / nv * 0.25f * Foliage.Jitter(j, i, seed));
                return Chip(p);
            }
            for (var i = 0; i < nu; i++)
                for (var j = 0; j < nv; j++)
                    pen.Quad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), n, colour);
        }
    }

    public static Model Canal()
    {
        const float Width = 2, Length = 12, Top = 0.5f, Level = 0.26f, Wall = 0.32f, Floor = 0.08f, Coping = 0.42f;
        var model = new Model("sludge-canal", round: false, new Vector3(Width, Top, Length));
        var rng = new Random(181);
        var concrete = Rgb(0.56f, 0.55f, 0.51f); var stain = Rgb(0.32f, 0.33f, 0.2f);
        Func<Vector3, Vector4> Face(int seed) => p => Mix(Shade(concrete, 0.85f + 0.2f * Fbm(p.X * 3, p.Y * 6, p.Z * 3, seed)), stain, Smooth(Level + 0.06f, Level - 0.02f, p.Y) * 0.85f);
        // The channel: a floor and two walls, each wall a run of cast sections with dark joints between them.
        Shapes.Box(model[Concrete], new Vector3(-0.7f, 0, -6), new Vector3(0.7f, Floor, 6), Face(1));
        const int sections = 6;
        foreach (var side in new[] { -1f, 1f })
            for (var i = 0; i < sections; i++)
            {
                float z0 = -6 + 12f * i / sections + (i == 0 ? 0 : 0.02f), z1 = -6 + 12f * (i + 1) / sections - (i == sections - 1 ? 0 : 0.02f);
                var (x0, x1) = side < 0 ? (-1f, -1 + Wall) : (1 - Wall, 1f);
                Cast(Quill.Of(model, Concrete, model.Inches), new Vector3(x0, 0, z0), new Vector3(x1, Coping, z1), 0.2f, Face(10 + i), 10 + i);
                // Rust streaks down the inner face under the coping's bolts.
                var inner = side < 0 ? x1 : x0;
                for (var s = 0; s < 2; s++)
                {
                    var z = z0 + (z1 - z0) * (0.3f + 0.4f * s) + 0.2f * ((float)rng.NextDouble() - 0.5f);
                    Shapes.Box(model[Rust], new Vector3(inner - 0.006f * side - 0.003f, Level + 0.02f, z - 0.03f), new Vector3(inner - 0.006f * side + 0.003f, Coping - 0.01f, z + 0.03f), p => Mix(RustRed, Rgb(0.3f, 0.18f, 0.1f), Smooth(Coping, Level, p.Y)));
                }
            }
        // Steel copings along both tops, rusted, with posts and a low rail.
        foreach (var side in new[] { -1f, 1f })
        {
            var inner = side < 0 ? -1 + Wall : 1 - Wall;
            var (x0, x1) = side < 0 ? (inner - 0.06f, inner + 0.02f) : (inner - 0.02f, inner + 0.06f);
            Shapes.Box(model[Rust], new Vector3(x0, Coping - 0.06f, -6), new Vector3(x1, Coping + 0.02f, 6), p => Mix(Rgb(0.4f, 0.38f, 0.36f), RustRed, Fbm(p.X * 4, 0, p.Z * 1.5f, 3)));
            var rail = side * 0.88f;
            for (var z = -5.5f; z <= 5.6f; z += 1.1f)
                Shapes.Box(model[Rust], new Vector3(rail - 0.02f, Coping + 0.02f, z - 0.02f), new Vector3(rail + 0.02f, Top, z + 0.02f), Rgb(0.42f, 0.28f, 0.16f));
            Shapes.Box(model[Rust], new Vector3(rail - 0.018f, Top - 0.025f, -6), new Vector3(rail + 0.018f, Top, 6), Rgb(0.7f, 0.56f, 0.12f));
        }
        // The flow: thick, slow, glowing, with skins and bubbles; a heaped surface from bank to bank.
        model[Goo].Grid(97, 9, wrap: false, (i, j) =>
        {
            var x = -0.68f + 1.36f * j / 8; var z = -6 + 12f * i / 96;
            return new Vector3(x, Level + 0.02f * Fbm(x * 3, z * 1.5f, 0, 182) + 0.012f * (1 - MathF.Abs(x) / 0.7f), z);
        }, (p, _) => SludgeColour(model.Inch(p) * new Vector3(2, 1, 0.5f), 183), flipped: true);
        foreach (var z in new[] { -6f, 6f })
            Shapes.Oriented(model[Goo], [new(-0.68f, Floor, z), new(0.68f, Floor, z), new(0.68f, Level, z), new(-0.68f, Level, z)], new Vector3(0, 0, z), _ => Shade(Toxic, 0.7f));
        for (var k = 0; k < 60; k++)
        {
            var x = ((float)rng.NextDouble() - 0.5f) * 1.1f; var z = -5.8f + 11.6f * (float)rng.NextDouble();
            var r = 0.03f + 0.06f * (float)rng.NextDouble();
            Foliage.Stone(Quill.Of(model, Goo, model.Inches), new Vector3(x, Level + 0.015f, z), new Vector3(r, r * 0.8f, r), 200 + k, 0, (q, n) => Shade(Bile, 0.85f + 0.25f * n.Y), 1, 0.08f);
        }
        // Weeds in the wall joints and along the copings, chipped concrete fallen in the flow.
        var weed = Quill.Of(model, Weed, model.Inches);
        foreach (var side in new[] { -1f, 1f })
            for (var i = 1; i < sections; i++)
            {
                var z = -6 + 12f * i / sections;
                Foliage.Tuft(weed, rng, new Vector3(side * (1 - Wall * 0.5f), Coping, z), 7, 0.1f, 0.04f, 0.9f, Rgb(0.26f, 0.3f, 0.14f), Rgb(0.5f, 0.52f, 0.26f));
                Foliage.Tuft(weed, rng, new Vector3(side * (1 - Wall) + side * -0.01f, Level, z + 0.05f), 5, 0.1f, 0.03f, 0.9f, Rgb(0.22f, 0.24f, 0.1f), Rgb(0.42f, 0.46f, 0.2f));
            }
        var chips = Quill.Of(model, Concrete, model.Inches);
        for (var k = 0; k < 14; k++)
        {
            var s = 0.05f + 0.08f * (float)rng.NextDouble();
            var at = new Vector3(((float)rng.NextDouble() - 0.5f) * 1.1f, Level - s * 0.3f, -5.6f + 11.2f * (float)rng.NextDouble());
            Foliage.Stone(chips, at, new Vector3(s * 1.3f, s * 0.8f, s), 220 + k, (float)rng.NextDouble() * 3, (q, n) => Shade(Mix(concrete, stain, 0.5f), 0.75f + 0.35f * n.Y), 0, 0.2f);
        }
        // An outfall pipe through the left wall, its spill fanning out on the flow.
        var pipe = new Vector3(-1.02f, 0.3f, 1.4f);
        Shapes.Limb(model[Rust], pipe, pipe + new Vector3(0.42f, 0, 0), 0.1f, 1, 12, p => Mix(Rgb(0.36f, 0.34f, 0.3f), RustRed, Fbm(p * 8, 4)));
        Shapes.Limb(model[Rust], pipe + new Vector3(0.36f, 0, 0), pipe + new Vector3(0.42f, 0, 0), 0.125f, 1, 12, Rgb(0.3f, 0.2f, 0.14f));
        Shapes.Blob(model[Goo], new Vector3(-0.5f, Level + 0.02f, 1.4f), new Vector3(0.22f, 0.035f, 0.3f), 1, 0.2f, 260, (q, n) => Shade(Bile, 1.05f));
        Shapes.Limb(model[Goo], pipe + new Vector3(0.42f, -0.02f, 0), new Vector3(-0.5f, Level + 0.02f, 1.4f), 0.06f, 1.4f, 8, Bile);
        // A drum caught against the right wall.
        Barrel(model, new Vector3(0.45f, Level + 0.04f, -3.2f), Vector3.Normalize(new Vector3(0.3f, 0.15f, 1)), Rgb(0.3f, 0.36f, 0.3f), rng);
        return model;
    }
}
