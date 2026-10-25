namespace Tabletop.Management.Content.Assets;

/// <summary>A copy's outcome: the caller's new asset ids in request order, the ids that are not another account's shared, uploaded
/// asset, or FolderMissing when the folder named is not the caller's (nothing written).</summary>
public sealed record CopyResult(IReadOnlyList<Guid> Copied, IReadOnlyList<Guid> NotFound, bool FolderMissing = false);

/// <summary>Other accounts' shared assets (shared-models spec). SharedAssetRepository is the Postgres implementation.</summary>
public interface ISharedAssetRepository
{
    /// <summary>Shared, uploaded assets of every account but the caller, ascending by id after the cursor. <c>q</c> matches the asset
    /// name, the owner's name or one of the owner's folder names holding it, case-insensitive.</summary>
    Task<IReadOnlyList<SharedAssetDto>> List(Guid callerId, Http.Keyset page, AssetKind? kind, string? q);
    /// <summary>The S3 key of a shared, uploaded asset of another account; null otherwise.</summary>
    Task<string?> BlobKey(Guid callerId, Guid id);
    /// <summary>One transaction: per source, an asset row of the caller's with the source's name, kind, scale, orientation, modelled base and
    /// volumes, unshared, filed in folderId; a blob row naming the source's S3 key unless the caller has the hash uploaded. The quota is
    /// checked as Create checks it, over all the copies at once, and a refusal throws AssetQuotaExceeded.</summary>
    Task<CopyResult> Copy(Guid callerId, IReadOnlyList<Guid> ids, Guid? folderId, AssetQuota quota);
}
