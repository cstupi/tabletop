using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

public enum AssetKind { Miniature, Terrain, Texture }

/// <summary>An asset of the caller's. <c>Shared</c>: every signed-in account can list and copy it (<c>/v1/shared-assets</c>).</summary>
public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase, bool Shared);
public sealed record CreateAssetRequest(string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetResponse(AssetDto Asset, Uri? UploadUrl);
public sealed record ConfirmResponse(AssetDto Asset, long? Triangles, string? Warning);
public sealed record PatchAssetRequest(string? Name, double? Scale, Orientation? Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);
/// <summary>The body of a bulk request: 1 to <c>Limits.Bulk</c> asset ids.</summary>
public sealed record BulkAssetsRequest(IReadOnlyList<Guid> AssetIds);
/// <summary>Per-item outcome: <c>NotFound</c> lists ids that are not the caller's assets.</summary>
public sealed record BulkAssetsResponse(IReadOnlyList<Guid> Done, IReadOnlyList<Guid> NotFound);

/// <summary>Another account's shared, uploaded asset: what the shared listing shows. <c>Folders</c> names the owner's folders holding it.</summary>
public sealed record SharedAssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase,
    string OwnerName, IReadOnlyList<string> Folders, DateTimeOffset SharedAt);
/// <summary>The body of <c>POST /v1/shared-assets/copy</c>: 1 to <c>Limits.Bulk</c> shared asset ids, and the caller's folder the copies are filed in.</summary>
public sealed record CopySharedRequest(IReadOnlyList<Guid> AssetIds, Guid? FolderId);
/// <summary>The caller's new assets, in request order, and the ids that are not another account's shared, uploaded asset.</summary>
public sealed record CopySharedResponse(IReadOnlyList<AssetDto> Copied, IReadOnlyList<Guid> NotFound);

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
