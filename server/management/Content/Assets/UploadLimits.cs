namespace Tabletop.Management.Content.Assets;

/// <summary>What one upload, and one account's library, may hold.</summary>
public static class UploadLimits
{
    /// <summary>A miniature or terrain GLB.</summary>
    public const long MeshBytes = 64L << 20;
    public const long TextureBytes = 16L << 20;
    public const int JsonChunkBytes = 1 << 20;
    /// <summary>Summed over every primitive's POSITION accessor.</summary>
    public const long Vertices = 500_000;
    public const long Triangles = 1_000_000;
    /// <summary>Above this, confirm succeeds with a warning.</summary>
    public const long WarnTriangles = 250_000;
    /// <summary>Elements in any one accessor: the index count of a mesh at the triangle limit.</summary>
    public const long AccessorElements = 3 * Triangles;
    /// <summary>Count × element size summed over every accessor: what a loader allocates, bufferView or not.</summary>
    public const long AccessorBytes = 128L << 20;
    public const int Images = 8;
    /// <summary>Width and height, for a texture and for an image inside a GLB.</summary>
    public const int ImageSide = 4096;
    public const long AccountBytes = 1L << 30;
    public const int AccountAssets = 500;
    public const int Volumes = 16;

    /// <summary>extensionsRequired a GLB may name: extensions the Godot client loads with no decoder. The web converter emits none;
    /// these cover a GLB uploaded as it is. Compression extensions (EXT_meshopt_compression, KHR_draco_mesh_compression) are out:
    /// they need a decoder and a second, fallback buffer.</summary>
    public static readonly IReadOnlySet<string> RequiredExtensions = new HashSet<string>(StringComparer.Ordinal)
    {
        "KHR_mesh_quantization", "KHR_texture_transform", "KHR_materials_unlit", "KHR_materials_emissive_strength", "EXT_texture_webp",
    };

    public static long Bytes(AssetKind kind) => kind == AssetKind.Texture ? TextureBytes : MeshBytes;
}

/// <summary>Reads bytes first..lastInclusive of one object.</summary>
public delegate Task<byte[]> RangeReader(long first, long lastInclusive);

/// <summary>The verdict on an upload's bytes. Rejection is the fixed text the caller may see; Detail is for the log only.</summary>
public sealed record ContentCheck(string? Rejection, string? Detail, long? Triangles)
{
    public static ContentCheck Pass(long? triangles = null) => new(null, null, triangles);
    public static ContentCheck Reject(string rejection, string detail) => new(rejection, detail, null);
}
