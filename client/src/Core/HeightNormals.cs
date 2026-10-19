namespace Tabletop.Client.Core;

/// <summary>
/// A table print's normal map from its height (spec 2026-10-19-tabletop-look-design.md → Boards): the height is the alpha, or the
/// luminance when every pixel is opaque. Central differences that wrap at the edges (the prints tile), tangent space in the OpenGL
/// convention Godot reads (red +u, green toward the image's top), alpha 255.
/// </summary>
public static class HeightNormals
{
    /// <summary><paramref name="strength"/> is the slope of a full 0→255 rise across one pixel.</summary>
    public static byte[] FromHeights(ReadOnlySpan<byte> rgba, int width, int height, float strength)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("rgba is not width × height × 4 bytes");
        var h = Heights(rgba, width, height);
        var normals = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float H(int dx, int dy) => h[(y + dy + height) % height * width + (x + dx + width) % width];
                var dx = (H(1, 0) - H(-1, 0)) / 2 * strength;
                var dy = (H(0, 1) - H(0, -1)) / 2 * strength;
                var (nx, ny, nz) = (-dx, dy, 1f);
                var len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                var i = (y * width + x) * 4;
                normals[i] = Byte(nx / len); normals[i + 1] = Byte(ny / len); normals[i + 2] = Byte(nz / len); normals[i + 3] = 255;
            }
        return normals;
    }

    /// <summary>The average colour, each channel in [0, 1].</summary>
    public static (float R, float G, float B) Mean(ReadOnlySpan<byte> rgba)
    {
        long r = 0, g = 0, b = 0; var n = rgba.Length / 4;
        for (var i = 0; i < rgba.Length; i += 4) { r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; }
        return ((float)r / n / 255f, (float)g / n / 255f, (float)b / n / 255f);
    }

    static float[] Heights(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var opaque = true;
        for (var i = 3; i < rgba.Length && opaque; i += 4) opaque = rgba[i] == 255;
        var h = new float[width * height];
        for (var p = 0; p < h.Length; p++)
            h[p] = opaque ? (0.299f * rgba[p * 4] + 0.587f * rgba[p * 4 + 1] + 0.114f * rgba[p * 4 + 2]) / 255f : rgba[p * 4 + 3] / 255f;
        return h;
    }

    static byte Byte(float v) => (byte)Math.Clamp(MathF.Round((v * 0.5f + 0.5f) * 255), 0, 255);
}
