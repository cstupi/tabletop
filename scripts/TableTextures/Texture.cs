using System.Numerics;

namespace Tabletop.TableTextures;

/// <summary>One tileable texture: <see cref="At"/> gives the sRGB albedo and the height in [0, 1] at (u, v) in the unit tile.</summary>
public sealed record Texture(string Name, int Size, Func<float, float, (Vector3 Rgb, float Height)> At)
{
    /// <summary>RGBA bytes, row-major from v = 0: RGB the albedo, A the height.</summary>
    public byte[] Render()
    {
        var size = Size;
        var rgba = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (var x = 0; x < size; x++)
            {
                var (c, h) = At((x + 0.5f) / size, (y + 0.5f) / size);
                var i = (y * size + x) * 4;
                rgba[i] = Byte(c.X); rgba[i + 1] = Byte(c.Y); rgba[i + 2] = Byte(c.Z); rgba[i + 3] = Byte(h);
            }
        });
        return rgba;
    }

    public byte[] Png() => TableTextures.Png.Write(Size, Size, Render());

    static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);
}
