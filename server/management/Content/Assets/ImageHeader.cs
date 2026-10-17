using System.Buffers.Binary;

namespace Tabletop.Management.Content.Assets;

public sealed record ImageInfo(string ContentType, int Width, int Height);

/// <summary>An image's format and dimensions from its header: PNG IHDR, JPEG SOFn, WebP VP8/VP8L/VP8X. Nothing is decoded.</summary>
public static class ImageHeader
{
    private const int Prefix = 64 * 1024;

    /// <summary>Null when the bytes at offset..offset+length are none of the three formats or the header is cut short.</summary>
    public static async Task<ImageInfo?> Read(RangeReader read, long offset, long length)
    {
        if (length < 12) return null;
        var head = await read(offset, offset + Math.Min(length, Prefix) - 1);
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xD8) return await Jpeg(read, offset, length, head);
        return Fixed(head);
    }

    /// <summary>PNG and WebP: the dimensions sit at fixed offsets in the first 30 bytes.</summary>
    private static ImageInfo? Fixed(byte[] head)
    {
        if (head.Length >= 24 && head.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && head.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return Sized("image/png", BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(16)), BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(20)));
        if (head.Length >= 25 && head.AsSpan(0, 4).SequenceEqual("RIFF"u8) && head.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            var fourcc = head.AsSpan(12, 4);
            if (head.Length >= 30 && fourcc.SequenceEqual("VP8 "u8) && head[23] == 0x9D && head[24] == 0x01 && head[25] == 0x2A)
                return Sized("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(26)) & 0x3FFFu, BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(28)) & 0x3FFFu);
            if (fourcc.SequenceEqual("VP8L"u8) && head[20] == 0x2F)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(21));
                return Sized("image/webp", (bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
            }
            if (head.Length >= 30 && fourcc.SequenceEqual("VP8X"u8))
                return Sized("image/webp", (head[24] | (uint)head[25] << 8 | (uint)head[26] << 16) + 1, (head[27] | (uint)head[28] << 8 | (uint)head[29] << 16) + 1);
            return null;
        }
        return null;
    }

    /// <summary>The bytes are a PNG, JPEG or WebP of the declared type, at most ImageSide on each side.</summary>
    public static async Task<ContentCheck> CheckTexture(string declaredType, long length, RangeReader read)
    {
        var info = await Read(read, 0, length);
        if (info is null) return ContentCheck.Reject("The file is not a PNG, JPEG or WebP image.", "no recognised image header");
        if (info.ContentType != declaredType) return ContentCheck.Reject($"The file is {info.ContentType}, not the declared {declaredType}.", $"magic {info.ContentType}, declared {declaredType}");
        return Within(info) ?? ContentCheck.Pass();
    }

    /// <summary>A rejection when either side is zero or above ImageSide.</summary>
    public static ContentCheck? Within(ImageInfo info) =>
        info.Width is > 0 and <= UploadLimits.ImageSide && info.Height is > 0 and <= UploadLimits.ImageSide ? null
        : ContentCheck.Reject($"Images are at most {UploadLimits.ImageSide} × {UploadLimits.ImageSide} pixels.", $"{info.Width} × {info.Height}");

    private static ImageInfo Sized(string type, uint width, uint height) => new(type, (int)Math.Min(width, int.MaxValue), (int)Math.Min(height, int.MaxValue));

    /// <summary>Walks the marker segments to the first frame header; a segment past the first read is fetched where it starts.</summary>
    private static async Task<ImageInfo?> Jpeg(RangeReader read, long offset, long length, byte[] head)
    {
        async Task<byte[]?> At(long pos, int n)
        {
            if (pos < 0 || pos + n > length) return null;
            if (pos + n <= head.Length) return head[(int)pos..(int)(pos + n)];
            var b = await read(offset + pos, offset + pos + n - 1);
            return b.Length == n ? b : null;
        }
        long at = 2;
        for (var steps = 0; steps < 4096; steps++)
        {
            if (await At(at, 2) is not { } m || m[0] != 0xFF) return null;
            var marker = m[1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is >= 0xD0 and <= 0xD7 or 0x01) { at += 2; continue; }
            if (marker is 0xD9 or 0xDA) return null;   // end of image, or scan data, before any frame header
            if (await At(at + 2, 2) is not { } l) return null;
            var segment = l[0] << 8 | l[1];
            if (segment < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (await At(at + 4, 5) is not { } sof) return null;
                return Sized("image/jpeg", (uint)(sof[3] << 8 | sof[4]), (uint)(sof[1] << 8 | sof[2]));
            }
            at += 2 + segment;
        }
        return null;
    }
}
