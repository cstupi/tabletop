using System.Numerics;

namespace Tabletop.TableTextures;

/// <summary>Tileable noise over the unit square: every function of (u, v) here repeats with period 1 on both axes, so a texture
/// drawn from them wraps without a seam. Periods are whole lattice cells per tile.</summary>
public static class Field
{
    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    public static float Unit(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / (float)0x1000000;

    static int Wrap(int i, int p) => ((i % p) + p) % p;

    /// <summary>Gradient noise in about [−1, 1], <paramref name="period"/> cells per tile.</summary>
    public static float Gradient(float u, float v, int period, int seed) => Gradient(u, v, period, period, seed);

    /// <summary>Gradient noise with its own period on each axis: streaks along the axis with fewer cells.</summary>
    public static float Gradient(float u, float v, int periodU, int periodV, int seed)
    {
        float x = u * periodU, y = v * periodV;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float Dot(int dx, int dy)
        {
            var a = Unit(Wrap(ix + dx, periodU), Wrap(iy + dy, periodV), seed) * MathF.Tau;
            return MathF.Cos(a) * (fx - dx) + MathF.Sin(a) * (fy - dy);
        }
        float sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10), sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
        var top = Lerp(Dot(0, 0), Dot(1, 0), sx); var bottom = Lerp(Dot(0, 1), Dot(1, 1), sx);
        return Lerp(top, bottom, sy) * 1.41f;
    }

    /// <summary>Fractal sum of <see cref="Gradient"/>, mapped to [0, 1]; each octave doubles the period.</summary>
    public static float Fbm(float u, float v, int period, int octaves, int seed, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0;
        for (var o = 0; o < octaves; o++)
        {
            sum += Gradient(u, v, period << o, seed + o * 101) * amp; norm += amp; amp *= gain;
        }
        return Math.Clamp(0.5f + 0.5f * sum / norm * 1.6f, 0, 1);
    }

    /// <summary>Cellular noise on a <paramref name="period"/>² grid of jittered points: the nearest and second-nearest distances
    /// (in cells) and the nearest point's cell id.</summary>
    public static (float F1, float F2, uint Id) Cells(float u, float v, int period, int seed, float jitter = 0.9f)
    {
        float x = u * period, y = v * period;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float f1 = 9, f2 = 9; uint id = 0;
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                int cx = ix + dx, cy = iy + dy, wx = Wrap(cx, period), wy = Wrap(cy, period);
                var px = cx + 0.5f + (Unit(wx, wy, seed) - 0.5f) * jitter; var py = cy + 0.5f + (Unit(wx, wy, seed + 7) - 0.5f) * jitter;
                var d = MathF.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 13); }
                else if (d < f2) f2 = d;
            }
        return (f1, f2, id);
    }

    /// <summary>White noise per pixel cell of a <paramref name="period"/>² grid, in [0, 1].</summary>
    public static float Grain(float u, float v, int period, int seed) => Unit(Wrap((int)MathF.Floor(u * period), period), Wrap((int)MathF.Floor(v * period), period), seed);

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Smooth(float e0, float e1, float x) { var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); }
    public static Vector3 Mix(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, Math.Clamp(t, 0, 1));
    public static Vector3 Rgb(float r, float g, float b) => new(r, g, b);
}
