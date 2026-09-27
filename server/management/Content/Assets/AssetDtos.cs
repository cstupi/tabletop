using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

public enum AssetKind { Miniature, Terrain, Texture }

public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetRequest(string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetResponse(AssetDto Asset, Uri? UploadUrl);
public sealed record ConfirmResponse(AssetDto Asset, long? Triangles, string? Warning);
public sealed record PatchAssetRequest(string? Name, double? Scale, Orientation? Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);

public static class AssetRules
{
    public static readonly IReadOnlyDictionary<AssetKind, string[]> ContentTypes = new Dictionary<AssetKind, string[]>
    {
        [AssetKind.Miniature] = ["model/gltf-binary"], [AssetKind.Terrain] = ["model/gltf-binary"], [AssetKind.Texture] = ["image/png", "image/jpeg", "image/webp"],
    };
    public static bool IsSha256(string hash) => hash.Length == 64 && hash.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Kind(AssetKind k) => k.ToString().ToLowerInvariant();
    public static AssetKind Kind(string k) => Enum.Parse<AssetKind>(k, ignoreCase: true);
    public static string Shape(BaseShape s) => s.ToString().ToLowerInvariant();
    public static BaseShape Shape(string s) => Enum.Parse<BaseShape>(s, ignoreCase: true);
}
