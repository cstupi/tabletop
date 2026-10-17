using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A ruined building: a cracked flagstone floor at ground level (ruins are walk-in ground, an Area), broken walls on
/// two sides with window and door openings and jagged tops, a corner pilaster, a stub of upper floor on its joists, a broken
/// pillar, rebar from the breaks and rubble at the wall feet.</summary>
public static class Ruins
{
    public static readonly Material Masonry = new("masonry", 0.88f), Floor = new("flagstones", 0.9f), Iron = new("iron", 0.55f, Metallic: 0.6f), Timber = new("timber", 0.85f);
    const int Seed = 31;
    const float Column = 0.034f;

    /// <summary>A wall from <paramref name="from"/> to <paramref name="to"/> on the ground, built of vertical strips <see cref="Column"/>
    /// wide whose tops follow <paramref name="top"/> (u in [0, 1] along the wall) and which skip each opening (u0, u1, y0, y1).</summary>
    static void Wall(Model model, Random rng, Vector2 from, Vector2 to, float thickness, Func<float, float> top, (float U0, float U1, float Y0, float Y1)[] openings)
    {
        var length = Vector2.Distance(from, to); var dir = (to - from) / length;
        var yaw = -MathF.Atan2(dir.Y, dir.X);
        var columns = (int)MathF.Ceiling(length / Column);
        var stone = Rgb(0.6f, 0.57f, 0.52f); var grime = Rgb(0.38f, 0.36f, 0.33f);
        for (var c = 0; c < columns; c++)
        {
            float u0 = (float)c / columns, u1 = (float)(c + 1) / columns, u = (u0 + u1) / 2;
            var height = top(u) + 0.03f * ((float)rng.NextDouble() - 0.5f);
            var cuts = new List<(float, float)> { (0, height) };
            foreach (var o in openings.Where(o => u > o.U0 && u < o.U1))
                cuts = cuts.SelectMany(s => o.Y1 <= s.Item1 || o.Y0 >= s.Item2 ? new[] { s } : new[] { (s.Item1, o.Y0), (o.Y1, s.Item2) }).Where(s => s.Item2 - s.Item1 > 0.01f).ToList();
            var shade = 0.88f + 0.2f * (float)rng.NextDouble();
            var mid = Vector2.Lerp(from, to, u); var width = length / columns + 0.001f;
            var depth = thickness * (0.9f + 0.2f * (float)rng.NextDouble());
            foreach (var (y0, y1) in cuts)
                Shapes.Box(model[Masonry], Shapes.At(new Vector3(mid.X, (y0 + y1) / 2, mid.Y), new Vector3(width, y1 - y0, depth), yaw),
                    p => Shade(Mix(grime, stone, Smooth(0, 0.25f, p.Y) * (0.7f + 0.3f * Fbm(p * 25, Seed))), shade));
            if (rng.NextDouble() < 0.14 && height > 0.2f)   // rebar out of the break
            {
                var lean = ((float)rng.NextDouble() - 0.5f) * 0.6f;
                Shapes.Box(model[Iron], Shapes.At(new Vector3(mid.X, height + 0.03f, mid.Y), new Vector3(0.004f, 0.08f, 0.004f), yaw, lean, lean * 0.5f), Rgb(0.4f, 0.27f, 0.2f));
            }
        }
    }

    public static Model Build()
    {
        var model = new Model("ruins", round: false);
        var rng = new Random(Seed);
        // Ground: dirt under a grid of flagstones, some missing, each a thin slab of its own shade.
        var dirt = Rgb(0.4f, 0.34f, 0.26f);
        model[Floor].Quad(new(-0.5f, 0.001f, -0.5f), new(-0.5f, 0.001f, 0.5f), new(0.5f, 0.001f, 0.5f), new(0.5f, 0.001f, -0.5f), p => Shade(dirt, 0.8f + 0.3f * Fbm(p * 12, Seed)));
        const int tiles = 10; const float size = 0.94f / tiles;
        for (var i = 0; i < tiles; i++)
            for (var j = 0; j < tiles; j++)
            {
                if (rng.NextDouble() < 0.16) continue;
                var x = -0.47f + (i + 0.5f) * size; var z = -0.47f + (j + 0.5f) * size;
                var tilt = 0.03f * ((float)rng.NextDouble() - 0.5f);
                var c = Shade(Rgb(0.56f, 0.54f, 0.5f), 0.82f + 0.25f * (float)rng.NextDouble());
                Shapes.Box(model[Floor], Shapes.At(new Vector3(x, 0.004f, z), new Vector3(size - 0.008f, 0.008f, size - 0.008f), 0.05f * ((float)rng.NextDouble() - 0.5f), tilt, tilt), c);
            }
        // Walls: the back wall stands to full height at the corner and crumbles toward the right; the left wall falls to the front.
        float Jag(float u, int salt) => 0.06f * (Noise(u * 22, salt, 0, Seed) - 0.5f) + 0.05f * MathF.Floor(Noise(u * 9, salt, 2, Seed) * 3) / 3;
        Wall(model, rng, new(-0.47f, -0.45f), new(0.47f, -0.45f), 0.05f,
            u => u < 0.6f ? 0.98f - 0.4f * u * u + Jag(u, 1) : u < 0.7f ? 0.12f + Jag(u, 2) : 0.42f - 0.25f * (u - 0.7f) + Jag(u, 3),
            [(0.12f, 0.24f, 0.26f, 0.5f), (0.36f, 0.48f, 0.26f, 0.5f), (0.12f, 0.24f, 0.64f, 0.84f), (0.36f, 0.46f, 0.64f, 0.8f), (0.78f, 0.9f, 0.2f, 0.36f)]);
        Wall(model, rng, new(-0.45f, -0.42f), new(-0.45f, 0.47f), 0.05f,
            u => u < 0.3f ? 0.95f - 0.2f * u + Jag(u, 4) : 0.9f - 1.4f * (u - 0.3f) + Jag(u, 5),
            [(0.12f, 0.26f, 0.3f, 0.52f), (0.12f, 0.26f, 0.66f, 0.84f), (0.48f, 0.64f, 0, 0.42f)]);
        Wall(model, rng, new(0.47f, 0.45f), new(0.18f, 0.45f), 0.045f, u => 0.34f - 0.22f * u + Jag(u, 6), [(0.35f, 0.6f, 0.12f, 0.24f)]);
        Wall(model, rng, new(0.45f, 0.43f), new(0.45f, 0.2f), 0.045f, u => 0.3f - 0.18f * u + Jag(u, 7), []);
        var stone = Rgb(0.55f, 0.52f, 0.48f);
        Shapes.Box(model[Masonry], new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.39f, 1, -0.39f), p => Shade(stone, 0.8f + 0.25f * Fbm(p * 20, 9)));       // corner pilaster
        Shapes.Box(model[Masonry], new Vector3(0.14f, 0, -0.1f), new Vector3(0.21f, 0.5f, -0.03f), p => Shade(stone, 0.8f + 0.25f * Fbm(p * 20, 10)));      // a broken pillar
        Shapes.Box(model[Masonry], Shapes.At(new Vector3(0.175f, 0.53f, -0.065f), new Vector3(0.07f, 0.06f, 0.07f), 0.3f, 0.25f, 0.1f), stone);
        // The upper floor's stub: joists out of the back wall, a few boards on them, one hanging broken.
        var wood = Rgb(0.42f, 0.31f, 0.21f);
        for (var k = 0; k < 4; k++)
        {
            var x = -0.36f + 0.1f * k; var reach = 0.32f - 0.06f * k + 0.04f * (float)rng.NextDouble();
            Shapes.Box(model[Timber], new Vector3(x - 0.012f, 0.56f, -0.43f), new Vector3(x + 0.012f, 0.6f, -0.43f + reach), p => Shade(wood, 0.8f + 0.3f * Fbm(p * 30, k)));
        }
        for (var k = 0; k < 6; k++)
        {
            var z = -0.42f + 0.034f * k; var reach = 0.32f - 0.04f * k;
            Shapes.Box(model[Timber], new Vector3(-0.43f, 0.6f, z), new Vector3(-0.43f + reach, 0.612f, z + 0.03f), Shade(wood, 0.85f + 0.06f * (k % 3)));
        }
        Shapes.Box(model[Timber], Shapes.At(new Vector3(0.0f, 0.42f, -0.22f), new Vector3(0.025f, 0.035f, 0.32f), 0.15f, 0.75f, 0), wood);
        // Rubble at the wall feet and in the corner: tumbled blocks and broken stone.
        foreach (var (pile, count, spread) in new[] { (new Vector2(0.15f, -0.36f), 16, 0.12f), (new Vector2(-0.36f, 0.25f), 14, 0.1f), (new Vector2(0.32f, 0.32f), 10, 0.09f), (new Vector2(-0.1f, 0.1f), 5, 0.06f) })
            for (var k = 0; k < count; k++)
            {
                var a = (float)(rng.NextDouble() * 2 * MathF.PI); var r = spread * MathF.Sqrt((float)rng.NextDouble());
                var s = 0.018f + 0.03f * (float)rng.NextDouble();
                var y = s * 0.4f + (spread - r) * 0.35f;
                var p = new Vector3(pile.X + MathF.Cos(a) * r, y, pile.Y + MathF.Sin(a) * r);
                var c = Shade(Rgb(0.58f, 0.55f, 0.5f), 0.75f + 0.3f * (float)rng.NextDouble());
                if (k % 3 == 0) Shapes.Blob(model[Masonry], p, new Vector3(s * 0.7f, s * 0.5f, s * 0.6f), 0, 0.2f, Seed + k, (_, _) => c);
                else Shapes.Box(model[Masonry], Shapes.At(p, new Vector3(s, s * 0.6f, s * 0.7f), (float)rng.NextDouble() * 3, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f), c);
            }
        return model;
    }
}
