using System.Numerics;
using static Tabletop.TableTextures.Field;

namespace Tabletop.TableTextures;

/// <summary>The client's own table textures, packed in the build under <c>client/content/table/</c>: the wooden table under the
/// board, and the grain each terrain surface family multiplies its paint by (grey around 0.88, height in alpha).</summary>
public static class Details
{
    public static readonly IReadOnlyList<Texture> All =
    [
        new("wood", 1024, Wood),
        new("detail-stone", 512, Stone), new("detail-ground", 512, Ground), new("detail-foliage", 512, Foliage),
        new("detail-wood", 512, Bark), new("detail-metal", 512, Metal),
    ];

    static Vector3 Grey(float g) => new(g, g, g);

    /// <summary>Walnut planks across the tile, five to a repeat, each its own tone, with grain streaks and end joints.</summary>
    static (Vector3, float) Wood(float u, float v)
    {
        const int s = 900, planks = 5;
        var p = (int)MathF.Floor(v * planks); var fv = v * planks - p;
        var joint = Unit(p, 0, s);                                                       // where this plank's two boards meet
        var board = (u + joint) % 1 < 0.5f ? 0 : 1;
        var tone = Unit(p, board, s + 1);
        var streak = 0.5f + 0.5f * Gradient(u, v, 3, 160, s + 2 + board) * 0.8f + 0.25f * Gradient(u, v, 12, 320, s + 3);
        var figure = Fbm(u, v, 4, 3, s + 4 + p);
        var c = Mix(Rgb(0.10f, 0.06f, 0.035f), Rgb(0.24f, 0.14f, 0.08f), 0.25f * tone + 0.5f * streak + 0.25f * figure);
        var along = (u + joint) % 0.5f;
        var gap = Smooth(0.025f, 0.008f, MathF.Min(fv, 1 - fv)) + Smooth(0.003f, 0.001f, MathF.Min(along, 0.5f - along));
        c *= 1 - 0.7f * Math.Clamp(gap, 0, 1);
        return (c, 0.6f + 0.15f * streak - 0.5f * Math.Clamp(gap, 0, 1));
    }

    static (Vector3, float) Stone(float u, float v)
    {
        const int s = 1000;
        var body = Fbm(u, v, 6, 5, s);
        var (f1, _, id) = Cells(u, v, 48, s + 1);
        var pit = (id & 0xFF) < 70 ? Smooth(0.25f, 0.05f, f1) : 0;
        var speck = Grain(u, v, 512, s + 2);
        var g = 0.74f + 0.22f * body + 0.08f * (speck - 0.5f) - 0.25f * pit;
        return (Grey(g), 0.5f + 0.35f * (body - 0.5f) - 0.4f * pit + 0.05f * speck);
    }

    /// <summary>Flock: packed grains of sand and fibre, each a small dome of its own tone.</summary>
    static (Vector3, float) Ground(float u, float v)
    {
        const int s = 1100;
        var (f1, _, id) = Cells(u, v, 96, s);
        var tone = (id & 0xFF) / 255f;
        var dome = Smooth(0.6f, 0, f1);
        var g = 0.68f + 0.3f * tone * dome + 0.1f * Fbm(u, v, 8, 3, s + 1);
        return (Grey(g), 0.25f + 0.6f * dome * (0.6f + 0.4f * tone));
    }

    static (Vector3, float) Foliage(float u, float v)
    {
        const int s = 1200;
        var (f1, f2, id) = Cells(u, v, 40, s);
        var leaf = Smooth(0.0f, 0.55f, f1);
        var vein = Smooth(0.05f, 0.0f, f2 - f1);
        var g = 0.72f + 0.26f * (1 - leaf) * (0.5f + 0.5f * (id & 0xFF) / 255f) - 0.12f * vein;
        return (Grey(g), 0.65f - 0.5f * leaf - 0.2f * vein);
    }

    static (Vector3, float) Bark(float u, float v)
    {
        const int s = 1300;
        var grain = 0.5f + 0.5f * Gradient(u, v, 48, 4, s);
        var fine = 0.5f + 0.5f * Gradient(u, v, 160, 8, s + 1);
        var g = 0.7f + 0.22f * grain + 0.08f * fine;
        return (Grey(g), 0.3f + 0.5f * grain + 0.15f * fine);
    }

    static (Vector3, float) Metal(float u, float v)
    {
        const int s = 1400;
        var mottle = Fbm(u, v, 6, 4, s);
        var scratch = Smooth(0.85f, 0.95f, 0.5f + 0.5f * Gradient(u, v, 256, 6, s + 1));
        var pit = Grain(u, v, 256, s + 2) < 0.04f ? 1 : 0;
        var g = 0.8f + 0.14f * mottle + 0.08f * scratch - 0.2f * pit;
        return (Grey(g), 0.55f + 0.15f * mottle - 0.1f * scratch - 0.35f * pit);
    }
}
