using System.Numerics;
using static Tabletop.TableTextures.Field;

namespace Tabletop.TableTextures;

/// <summary>The gaming-mat prints the catalog lists under <c>textures</c>: 1024² per 12" repeat (spec 2026-10-19-tabletop-look-design.md → Boards).</summary>
public static class Boards
{
    public const int Size = 1024;

    public static readonly IReadOnlyList<Texture> All =
    [
        new("grass", Size, Grass), new("desert", Size, Desert), new("urban", Size, Urban), new("snow", Size, Snow),
        new("ash", Size, Ash), new("wasteland", Size, Wasteland), new("alien", Size, Alien),
    ];

    /// <summary>A scatter of small stones: per cell of a <paramref name="period"/>² grid, one stone in <paramref name="share"/> of them,
    /// its mask (1 inside), its tone and its dome height.</summary>
    static (float Mask, float Tone, float Dome) Stones(float u, float v, int period, int seed, float share, float radius = 0.3f)
    {
        var (f1, _, id) = Cells(u, v, period, seed);
        f1 *= 0.75f + 0.5f * Fbm(u, v, period * 4, 2, seed + 3);                       // a ragged outline, not a disc
        var r = radius * (0.6f + 0.6f * (id >> 8 & 0xFF) / 255f);
        if ((id & 0xFF) / 255f > share || f1 > r) return (0, 0, 0);
        var edge = Smooth(r, r * 0.75f, f1);
        return (edge, (id >> 16 & 0xFF) / 255f, MathF.Sqrt(Math.Max(0, 1 - f1 * f1 / (r * r))));
    }

    /// <summary>Hair-thin dark lines between the cells of a <paramref name="period"/>² grid, 1 on a line.</summary>
    static float Cracks(float u, float v, int period, int seed, float width)
    {
        var (f1, f2, _) = Cells(u, v, period, seed);
        return Smooth(width, width * 0.3f, f2 - f1);
    }

    static (Vector3, float) Grass(float u, float v)
    {
        const int s = 100;
        var macro = Fbm(u, v, 2, 4, s);
        var ragged = Fbm(u, v, 16, 3, s + 1) - 0.5f;
        var grain = Fbm(u, v, 256, 2, s + 4) * 0.7f + Grain(u, v, 1024, s + 11) * 0.3f; var speck = Grain(u, v, 1024, s + 8);
        var dirtness = Smooth(0.6f, 0.68f, macro + 0.22f * ragged + 0.08f * (grain - 0.5f));
        var lush = Fbm(u, v, 4, 4, s + 2); var dry = Smooth(0.5f, 0.78f, Fbm(u, v, 3, 3, s + 3));
        var green = Mix(Rgb(0.17f, 0.25f, 0.09f), Rgb(0.30f, 0.37f, 0.14f), lush);
        green = Mix(green, Rgb(0.50f, 0.46f, 0.25f), dry * 0.55f);
        var blade = Fbm(u, v, 96, 2, s + 5);
        var tuft = Smooth(0.62f, 0.75f, Fbm(u, v, 24, 2, s + 9));
        green = Mix(green, Rgb(0.55f, 0.52f, 0.28f), tuft * 0.35f);
        var tip = Mix(green * 1.3f, Rgb(0.55f, 0.56f, 0.30f), 0.2f);               // static grass: dark roots, pale tips
        var grassCol = Mix(green * 0.6f, tip, Smooth(0.2f, 0.85f, grain)) * (0.85f + 0.3f * blade);
        var grassH = 0.55f + 0.22f * grain * blade + 0.12f * tuft;
        var earth = Mix(Rgb(0.31f, 0.23f, 0.15f), Rgb(0.44f, 0.35f, 0.24f), Fbm(u, v, 12, 3, s + 6));
        var dirtCol = earth * (0.8f + 0.35f * speck);
        var dirtH = 0.28f + 0.12f * Fbm(u, v, 48, 2, s + 7) + 0.06f * speck;
        var (stone, tone, dome) = Stones(u, v, 64, s + 10, 0.02f + 0.4f * dirtness, 0.24f);
        var c = Mix(grassCol, dirtCol, dirtness); var h = Lerp(grassH, dirtH, dirtness);
        c = Mix(c, Rgb(0.40f, 0.38f, 0.35f) * (0.65f + 0.5f * tone) * (0.75f + 0.25f * dome), stone); h = Lerp(h, 0.45f + 0.45f * dome, stone);
        return (c, h);
    }

    static (Vector3, float) Desert(float u, float v)
    {
        const int s = 200;
        var warp = Fbm(u, v, 2, 3, s) - 0.5f;
        var phase = ((v * 22 + u * 4 + warp * 3.5f) % 1 + 1) % 1;
        var ripple = MathF.Pow(phase < 0.75f ? phase / 0.75f : (1 - phase) / 0.25f, 1.5f);   // a long windward slope, a short lee
        var tone = Fbm(u, v, 3, 4, s + 1);
        var sand = Mix(Rgb(0.80f, 0.64f, 0.42f), Rgb(0.68f, 0.50f, 0.31f), tone);
        var speck = Grain(u, v, 1024, s + 2);
        sand *= 0.88f + 0.12f * ripple + 0.12f * (speck - 0.5f);
        if (speck < 0.05f) sand *= 0.55f;
        var pan = Smooth(0.66f, 0.72f, Fbm(u, v, 2, 4, s + 3));
        var panCol = Mix(Rgb(0.66f, 0.50f, 0.34f), Rgb(0.58f, 0.43f, 0.29f), Fbm(u, v, 20, 2, s + 4)) * (1 - 0.5f * Cracks(u, v, 14, s + 5, 0.05f));
        var (stone, stoneTone, dome) = Stones(u, v, 48, s + 6, 0.18f);
        var c = Mix(sand, panCol, pan); var h = Lerp(0.35f + 0.3f * ripple * (1 - pan), 0.5f - 0.2f * Cracks(u, v, 14, s + 5, 0.05f), pan);
        c = Mix(c, Rgb(0.52f, 0.40f, 0.30f) * (0.7f + 0.5f * stoneTone), stone); h = Lerp(h, 0.5f + 0.45f * dome, stone);
        return (c, h);
    }

    static (Vector3, float) Urban(float u, float v)
    {
        const int s = 300;
        const int rows = 4;
        var row = (int)MathF.Floor(v * rows);
        var su = u * rows + (row % 2 == 1 ? 0.5f : 0);
        var col = (int)MathF.Floor(su);
        float fu = su - col, fv = v * rows - row;
        var id = Hash(((col % rows) + rows) % rows, row, s);
        var seam = MathF.Min(MathF.Min(fu, 1 - fu), MathF.Min(fv, 1 - fv));
        var kind = (id & 0xFF) / 255f;                                                   // < 0.06 missing, < 0.12 deck plate, else rockcrete
        var tone = (id >> 8 & 0xFF) / 255f;
        var speck = Grain(u, v, 1024, s + 1);
        var stain = Smooth(0.6f, 0.8f, Fbm(u, v, 3, 4, s + 2));
        var grime = Fbm(u, v, 32, 3, s + 3);
        Vector3 c; float h;
        if (kind < 0.06f)
        {
            var rubble = Cells(u, v, 160, s + 4);
            c = Mix(Rgb(0.30f, 0.27f, 0.23f), Rgb(0.52f, 0.50f, 0.46f), (rubble.Id & 0xFF) / 255f) * (0.8f + 0.3f * speck);
            h = 0.15f + 0.3f * Smooth(0.5f, 0, rubble.F1);
        }
        else if (kind < 0.12f)
        {
            var tread = MathF.Abs(MathF.Sin((fu + fv) * 60 * MathF.PI)) * MathF.Abs(MathF.Sin((fu - fv) * 60 * MathF.PI));
            var rivet = MathF.Min(MathF.Min(Vector2.Distance(new(fu, fv), new(0.06f, 0.06f)), Vector2.Distance(new(fu, fv), new(0.94f, 0.06f))),
                MathF.Min(Vector2.Distance(new(fu, fv), new(0.06f, 0.94f)), Vector2.Distance(new(fu, fv), new(0.94f, 0.94f))));
            var rim = Smooth(0.12f, 0.0f, MathF.Min(MathF.Min(fu, 1 - fu), MathF.Min(fv, 1 - fv)));
            var rust = Smooth(0.6f, 0.8f, Fbm(u, v, 12, 3, s + 5) + 0.3f * rim);
            c = Mix(Rgb(0.30f, 0.31f, 0.32f) * (0.9f + 0.2f * tone), Rgb(0.36f, 0.21f, 0.13f), rust * 0.7f) * (0.85f + 0.15f * tread);
            h = 0.55f + 0.08f * tread + (rivet < 0.025f ? 0.15f : 0);
        }
        else
        {
            var concrete = Mix(Rgb(0.33f, 0.32f, 0.30f), Rgb(0.47f, 0.45f, 0.42f), 0.5f * tone + 0.5f * Fbm(u, v, 24, 3, s + 6));
            var pits = speck < 0.08f ? 0.7f : 1;
            var crack = Cracks(u, v, 7, s + 7 + (int)(id % 5), 0.035f) * (tone < 0.5f ? 1 : 0);
            c = concrete * pits * (0.88f + 0.24f * grime) * (1 - 0.6f * crack);
            h = 0.6f + 0.04f * grime - 0.25f * crack + (tone - 0.5f) * 0.06f;
        }
        var seamLine = Smooth(0.016f, 0.006f, seam);
        c *= 1 - 0.65f * seamLine; h = Lerp(h, 0.1f, seamLine);
        c *= 1 - 0.22f * stain;
        var scorch = Cells(u, v, 3, s + 8);
        var burn = MathF.Pow(Smooth(0.4f, 0.0f, scorch.F1), 2) * ((scorch.Id & 3) == 0 ? 1 : 0) * (0.6f + 0.4f * Fbm(u, v, 24, 2, s + 9));
        c = Mix(c, Rgb(0.07f, 0.06f, 0.05f), burn * 0.7f);
        return (c, h);
    }

    static (Vector3, float) Snow(float u, float v)
    {
        const int s = 400;
        var drift = Fbm(u, v, 3, 5, s);
        var bare = Smooth(0.42f, 0.34f, 0.6f * Fbm(u, v, 3, 4, s + 1) + 0.4f * Fbm(u, v, 5, 3, s + 10) + 0.18f * (Fbm(u, v, 16, 2, s + 2) - 0.5f));
        var speck = Grain(u, v, 1024, s + 3);
        var wind = 0.5f + 0.5f * Gradient(u, v, 6, 40, s + 7);                          // wind-cut ridges across the snow
        var snow = Mix(Rgb(0.52f, 0.57f, 0.67f), Rgb(0.74f, 0.77f, 0.82f), Smooth(0.25f, 0.8f, drift * 0.75f + wind * 0.25f)) * (0.94f + 0.08f * speck);
        var earth = Mix(Rgb(0.22f, 0.20f, 0.18f), Rgb(0.34f, 0.31f, 0.26f), Fbm(u, v, 20, 3, s + 4)) * (0.8f + 0.4f * speck);
        var straw = Smooth(0.7f, 0.85f, Fbm(u, v, 80, 2, s + 5));
        earth = Mix(earth, Rgb(0.48f, 0.43f, 0.30f), straw * 0.7f);
        var (stone, tone, dome) = Stones(u, v, 24, s + 6, 0.06f + 0.4f * bare, 0.36f);
        var c = Mix(snow, earth, bare); var h = Lerp(0.5f + 0.3f * drift + 0.15f * wind, 0.25f + 0.1f * straw, bare);
        var rock = Rgb(0.36f, 0.35f, 0.35f) * (0.7f + 0.4f * tone);
        var capped = Mix(rock, snow, Smooth(0.5f, 0.9f, dome) * (1 - bare) * 0.8f);
        c = Mix(c, capped, stone * (0.4f + 0.6f * bare)); h = Lerp(h, 0.55f + 0.4f * dome, stone * (0.4f + 0.6f * bare));
        return (c, h);
    }

    static (Vector3, float) Ash(float u, float v)
    {
        const int s = 500;
        var drift = Fbm(u, v, 3, 5, s);
        var speck = Grain(u, v, 1024, s + 1);
        var ash = Mix(Rgb(0.19f, 0.18f, 0.18f), Rgb(0.27f, 0.26f, 0.25f), Smooth(0.3f, 0.85f, drift));
        ash *= 0.85f + 0.3f * speck;
        if (speck > 0.985f) ash = Rgb(0.42f, 0.20f, 0.12f);                                 // rusty cinder
        var plates = Smooth(0.56f, 0.5f, drift);                                           // cooled crust where the ash is thin
        var (f1, f2, id) = Cells(u, v, 8, s + 2);
        var crack = Smooth(0.07f, 0.02f, f2 - f1);
        var crust = Mix(Rgb(0.13f, 0.12f, 0.12f), Rgb(0.24f, 0.22f, 0.21f), (id & 0xFF) / 255f) * (0.85f + 0.3f * Fbm(u, v, 40, 2, s + 3));
        crust = Mix(crust, Rgb(0.05f, 0.04f, 0.04f), crack);
        var c = Mix(ash, crust, plates); var h = Lerp(0.4f + 0.35f * drift + 0.05f * speck, 0.55f - 0.4f * crack - 0.1f * Smooth(0.3f, 0.5f, f1), plates);
        return (c, h);
    }

    static (Vector3, float) Wasteland(float u, float v)
    {
        const int s = 600;
        var macro = Fbm(u, v, 2, 4, s);
        var speck = Grain(u, v, 1024, s + 1);
        var dust = Mix(Rgb(0.42f, 0.26f, 0.17f), Rgb(0.58f, 0.40f, 0.27f), Fbm(u, v, 6, 4, s + 2)) * (0.86f + 0.26f * speck);
        var cracked = Smooth(0.55f, 0.62f, macro);
        var (f1, f2, id) = Cells(u, v, 10, s + 3);
        var crack = Smooth(0.08f, 0.025f, f2 - f1);
        var plate = Mix(Rgb(0.50f, 0.33f, 0.22f), Rgb(0.60f, 0.43f, 0.30f), (id & 0xFF) / 255f) * (0.9f + 0.2f * Fbm(u, v, 36, 2, s + 4));
        plate = Mix(plate, Rgb(0.20f, 0.12f, 0.08f), crack);
        var scrub = Smooth(0.68f, 0.78f, Fbm(u, v, 18, 3, s + 5)) * (1 - cracked);
        dust = Mix(dust, Rgb(0.33f, 0.30f, 0.18f) * (0.7f + 0.5f * speck), scrub * 0.8f);
        var (stone, tone, dome) = Stones(u, v, 44, s + 6, 0.3f);
        var c = Mix(dust, plate, cracked); var h = Lerp(0.35f + 0.1f * speck + 0.15f * scrub, 0.6f - 0.45f * crack - 0.12f * f1, cracked);
        c = Mix(c, Rgb(0.45f, 0.36f, 0.30f) * (0.7f + 0.5f * tone), stone); h = Lerp(h, 0.5f + 0.45f * dome, stone);
        return (c, h);
    }

    static (Vector3, float) Alien(float u, float v)
    {
        const int s = 700;
        var speck = Grain(u, v, 1024, s + 1);
        var soil = Mix(Rgb(0.27f, 0.22f, 0.31f), Rgb(0.44f, 0.37f, 0.47f), Fbm(u, v, 5, 4, s + 2)) * (0.85f + 0.3f * speck);
        if (speck > 0.975f) soil = Rgb(0.82f, 0.86f, 0.90f);
        var crust = Smooth(0.64f, 0.7f, 0.6f * Fbm(u, v, 3, 4, s + 3) + 0.4f * Fbm(u, v, 5, 3, s + 8) + 0.1f * (Fbm(u, v, 20, 2, s + 4) - 0.5f));
        var (f1, f2, id) = Cells(u, v, 22, s + 5);
        var facet = Smooth(0.0f, 0.5f, f1);
        var crystal = Mix(Rgb(0.16f, 0.32f, 0.34f), Rgb(0.40f, 0.62f, 0.60f), (id & 0xFF) / 255f * 0.6f + 0.4f * (1 - facet));
        crystal = Mix(crystal, Rgb(0.08f, 0.16f, 0.17f), Smooth(0.06f, 0.015f, f2 - f1));
        var lichen = Smooth(0.72f, 0.8f, Fbm(u, v, 14, 3, s + 6)) * (1 - crust);
        soil = Mix(soil, Rgb(0.55f, 0.40f, 0.36f), lichen * 0.5f);
        var c = Mix(soil, crystal, crust); var h = Lerp(0.35f + 0.15f * Fbm(u, v, 40, 2, s + 7) + 0.1f * lichen, 0.55f + 0.35f * (1 - facet) - 0.3f * Smooth(0.06f, 0.015f, f2 - f1), crust);
        return (c, h);
    }
}
