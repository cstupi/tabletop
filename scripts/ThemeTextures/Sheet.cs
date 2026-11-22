using System.Numerics;
using Tabletop.TableTextures;

namespace Tabletop.ThemeTextures;

/// <summary>A point of a material: sRGB albedo, coverage, how much it shines (0 matte wood, 1 polished metal), its shininess
/// exponent, and light it gives off (a glow, a star) added after shading.</summary>
public readonly record struct Surface(Vector3 Albedo, float Alpha = 1, float Spec = 0, float Shine = 24, Vector3 Emit = default)
{
    public static readonly Surface Clear = new(Vector3.Zero, 0);
}

/// <summary>How a sheet is drawn: a nine-patch (corners fixed, edges and centre repeated), a tile (repeated whole), or a picture
/// stretched over the screen.</summary>
public enum Use { NinePatch, Tile, Stretch }

/// <summary>One theme image, designed in 1× pixels: <see cref="Height"/> (in pixels, lit from the top left) and <see cref="At"/>
/// (the material) are functions of a point in the sheet. A nine-patch's centre and edges repeat with period
/// <see cref="Width"/> − 2·<see cref="Margin"/> (and the same down), so every function of the centre is periodic there;
/// <see cref="Inset"/> is the drawn frame, outside the padding: text sits only inside it.</summary>
public sealed record Sheet(string Theme, string Name, int Width, int Height, Use Use, Func<float, float, float> Relief, Func<float, float, Surface> At,
    int Margin = 0, int Inset = 0, bool Opaque = false)
{
    static readonly Vector3 Light = Vector3.Normalize(new(-0.55f, -0.65f, 0.75f));
    static readonly Vector3 Half = Vector3.Normalize(Light + Vector3.UnitZ);

    /// <summary>The file name at a scale: <c>panel.png</c>, <c>panel@2x.png</c>.</summary>
    public string File(int scale) => scale == 1 ? $"{Name}.png" : $"{Name}@{scale}x.png";

    /// <summary>The colour at a point: the albedo lit by the height's slope (a flat point keeps its albedo exactly), plus the shine
    /// and the glow; straight alpha.</summary>
    public Vector4 Shade(float x, float y)
    {
        var s = At(x, y);
        if (s.Alpha <= 0) return Vector4.Zero;
        const float e = 0.4f;
        var dx = (Relief(x + e, y) - Relief(x - e, y)) / (2 * e);
        var dy = (Relief(x, y + e) - Relief(x, y - e)) / (2 * e);
        var n = Vector3.Normalize(new(-dx, -dy, 1));
        const float ambient = 0.45f;
        var lit = (ambient + MathF.Max(0, Vector3.Dot(n, Light))) / (ambient + Light.Z);
        var spec = s.Spec * (MathF.Pow(MathF.Max(0, Vector3.Dot(n, Half)), s.Shine) - MathF.Pow(Half.Z, s.Shine));
        var c = s.Albedo * lit + new Vector3(spec) + s.Emit;
        return new(Vector3.Clamp(c, Vector3.Zero, Vector3.One), Math.Clamp(s.Alpha, 0, 1));
    }

    /// <summary>RGBA bytes at a scale, each pixel the mean of 3×3 samples (premultiplied, so edges stay clean).</summary>
    public byte[] Render(int scale)
    {
        int w = Width * scale, h = Height * scale;
        var rgba = new byte[w * h * 4];
        Parallel.For(0, h, py =>
        {
            for (var px = 0; px < w; px++)
            {
                var sum = Vector4.Zero;
                for (var j = 0; j < 3; j++)
                    for (var i = 0; i < 3; i++)
                    {
                        var c = Shade((px + (i + 0.5f) / 3) / scale, (py + (j + 0.5f) / 3) / scale);
                        sum += new Vector4(new Vector3(c.X, c.Y, c.Z) * c.W, c.W);
                    }
                sum /= 9;
                var a = sum.W; var k = (py * w + px) * 4;
                var rgb = a > 0 ? new Vector3(sum.X, sum.Y, sum.Z) / a : Vector3.Zero;
                rgba[k] = Byte(rgb.X); rgba[k + 1] = Byte(rgb.Y); rgba[k + 2] = Byte(rgb.Z); rgba[k + 3] = Opaque ? (byte)255 : Byte(a);
            }
        });
        return rgba;
    }

    public byte[] Png(int scale) => TableTextures.Png.Write(Width * scale, Height * scale, Render(scale), Opaque);

    static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);
}
