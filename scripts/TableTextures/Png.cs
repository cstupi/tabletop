using System.IO.Compression;

namespace Tabletop.TableTextures;

/// <summary>8-bit RGBA (or RGB, when every pixel is opaque and the caller asks) PNG: written with a per-row filter (the one with the smallest absolute sum) and zlib at its smallest
/// setting; read back for the tests (non-interlaced 8-bit RGBA or RGB only).</summary>
public static class Png
{
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Write(int width, int height, byte[] rgba, bool opaque = false)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("rgba is not width × height × 4 bytes");
        var bpp = opaque ? 3 : 4;
        if (opaque) rgba = Enumerable.Range(0, width * height * 3).Select(i => rgba[i / 3 * 4 + i % 3]).ToArray();
        var stride = width * bpp;
        var raw = new byte[(stride + 1) * height];
        var candidate = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var best = long.MaxValue;
            for (byte f = 0; f <= 4; f++)
            {
                long sum = 0;
                for (var x = 0; x < stride; x++)
                {
                    var v = (byte)(rgba[y * stride + x] - Predict(f, rgba, y, x, stride, bpp));
                    candidate[x] = v; sum += v < 128 ? v : 256 - v;
                }
                if (sum >= best) continue;
                best = sum; raw[y * (stride + 1)] = f;
                Array.Copy(candidate, 0, raw, y * (stride + 1) + 1, stride);
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
        using var s = new MemoryStream();
        s.Write(Signature);
        var header = new byte[13];
        BigEndian(header, 0, (uint)width); BigEndian(header, 4, (uint)height);
        header[8] = 8; header[9] = (byte)(opaque ? 2 : 6);               // 8-bit RGB or RGBA
        Chunk(s, "IHDR", header); Chunk(s, "IDAT", compressed.ToArray()); Chunk(s, "IEND", []);
        return s.ToArray();
    }

    /// <summary>The image as RGBA bytes (RGB gains alpha 255).</summary>
    public static (int Width, int Height, byte[] Rgba) Read(byte[] png)
    {
        if (!png.AsSpan(0, 8).SequenceEqual(Signature)) throw new FormatException("not a PNG");
        int width = 0, height = 0, channels = 4;
        using var idat = new MemoryStream();
        for (var at = 8; at < png.Length;)
        {
            var length = (int)ReadBig(png, at); var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            if (type == "IHDR")
            {
                (width, height) = ((int)ReadBig(png, at + 8), (int)ReadBig(png, at + 12));
                if (png[at + 16] != 8 || png[at + 20] != 0 || png[at + 17] is not (6 or 2)) throw new FormatException("only 8-bit RGB(A) non-interlaced");
                channels = png[at + 17] == 6 ? 4 : 3;
            }
            else if (type == "IDAT") idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var rawStream = new MemoryStream(); z.CopyTo(rawStream);
        var raw = rawStream.ToArray();
        var stride = width * channels;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var f = raw[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
                pixels[y * stride + x] = (byte)(raw[y * (stride + 1) + 1 + x] + Predict(f, pixels, y, x, stride, channels));
        }
        if (channels == 4) return (width, height, pixels);
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++) { rgba[i * 4] = pixels[i * 3]; rgba[i * 4 + 1] = pixels[i * 3 + 1]; rgba[i * 4 + 2] = pixels[i * 3 + 2]; rgba[i * 4 + 3] = 255; }
        return (width, height, rgba);
    }

    static int Predict(byte filter, byte[] img, int y, int x, int stride, int bpp = 4)
    {
        int a = x >= bpp ? img[y * stride + x - bpp] : 0, b = y > 0 ? img[(y - 1) * stride + x] : 0, c = x >= bpp && y > 0 ? img[(y - 1) * stride + x - bpp] : 0;
        return filter switch
        {
            0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2,
            _ => Paeth(a, b, c),
        };
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8]; BigEndian(head, 0, (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
        s.Write(head);
        s.Write(data);
        var crc = Crc(head.AsSpan(4, 4), data);
        var tail = new byte[4]; BigEndian(tail, 0, crc); s.Write(tail);
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(ReadOnlySpan<byte> type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    static void BigEndian(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
    static uint ReadBig(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
}
