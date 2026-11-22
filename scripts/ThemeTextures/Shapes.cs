using System.Numerics;
using static Tabletop.TableTextures.Field;

namespace Tabletop.ThemeTextures;

/// <summary>Distances and profiles in 1× pixels, shared by the themes.</summary>
public static class Shapes
{
    /// <summary>Distance inside a w × h rectangle with corners rounded to r (negative outside).</summary>
    public static float RoundRect(float x, float y, float w, float h, float r)
    {
        float qx = MathF.Abs(x - w / 2) - (w / 2 - r), qy = MathF.Abs(y - h / 2) - (h / 2 - r);
        var outside = new Vector2(MathF.Max(qx, 0), MathF.Max(qy, 0)).Length() + MathF.Min(MathF.Max(qx, qy), 0);
        return r - outside;
    }

    /// <summary>Distance inside a w × h rectangle with its corners cut at 45°, c along each edge (negative outside).</summary>
    public static float Chamfer(float x, float y, float w, float h, float c)
    {
        float lx = MathF.Min(x, w - x), ly = MathF.Min(y, h - y);
        return MathF.Min(MathF.Min(lx, ly), (lx + ly - c) * 0.70710678f);
    }

    /// <summary>Distance from a point to the segment a–b.</summary>
    public static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; var t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return (p - (a + ab * t)).Length();
    }

    /// <summary>A dome's height at distance d from its centre, radius r.</summary>
    public static float Dome(float d, float r, float height) => d >= r ? 0 : height * MathF.Sqrt(1 - d * d / (r * r));

    /// <summary>A rounded step: 0 at distance 0 from an edge, full height once inside by width.</summary>
    public static float Bevel(float inside, float width) => Smooth(0, width, inside);

    /// <summary>x modulo p, always in [0, p).</summary>
    public static float Mod(float x, float p) => x - p * MathF.Floor(x / p);

    public static float Luminance(Vector3 c)
    {
        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Lin(c.X) + 0.7152f * Lin(c.Y) + 0.0722f * Lin(c.Z);
    }

    /// <summary>Scratches: short, nearly straight hairlines scattered over cells of a strip, a along it (period p, cells of length
    /// <paramref name="cell"/>), b across it in [0, width]. 1 on a scratch, 0 off.</summary>
    public static float Scratches(float a, float b, float p, float cell, float width, int seed, float chance = 0.45f)
    {
        var n = (int)MathF.Round(p / cell); var best = 0f;
        var i0 = (int)MathF.Floor(a / cell);
        for (var di = -1; di <= 1; di++)
        {
            var i = i0 + di; var w = ((i % n) + n) % n;
            if (Unit(w, 0, seed) > chance) continue;
            var x0 = i * cell + Unit(w, 1, seed) * cell * 0.6f; var len = cell * (0.4f + 0.6f * Unit(w, 2, seed));
            var y0 = Unit(w, 3, seed) * width; var y1 = Math.Clamp(y0 + (Unit(w, 4, seed) - 0.5f) * width * 0.8f, 0, width);
            var d = Segment(new(a, b), new(x0, y0), new(x0 + len, y1));
            best = MathF.Max(best, Smooth(0.6f, 0.1f, d) * (0.5f + 0.5f * Unit(w, 5, seed)));
        }
        return best;
    }
}
