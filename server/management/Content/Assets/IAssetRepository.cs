using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

/// <summary>An owner's most asset rows, and most bytes over their blob rows (uploaded or not), each blob once.</summary>
public sealed record AssetQuota(int Assets, long Bytes);

/// <summary>A create the quota refuses; nothing was written.</summary>
public sealed class AssetQuotaExceeded(AssetQuotaExceeded.Limit which) : Exception($"asset quota: {which}")
{
    public enum Limit { Assets, Bytes }
    public Limit Which { get; } = which;
}

/// <summary>An owner's assets, their blob rows and terrain volumes. AssetRepository is the Postgres implementation.</summary>
public interface IAssetRepository
{
    /// <summary>Inserts the blob row unless the owner already has one for the hash, then the asset. Same bytes twice share one blob;
    /// a blob row not yet uploaded has its created_at restarted. With a quota, the owner's count and bytes are read and checked in
    /// the same transaction as the insert, serialized per owner, and a refusal throws AssetQuotaExceeded.</summary>
    Task<AssetDto> Create(Guid ownerId, CreateAssetRequest r, AssetQuota? quota = null);
    Task<AssetDto?> Get(Guid ownerId, Guid id);
    /// <summary>Ascending by id, after the cursor.</summary>
    Task<IReadOnlyList<AssetDto>> List(Guid ownerId, Http.Keyset page, AssetKind? kind);
    /// <summary>Null fields keep the stored value.</summary>
    Task<AssetDto?> Patch(Guid ownerId, Guid id, PatchAssetRequest p);
    /// <summary>False when the owner has no blob row for the hash (the sweep deleted it).</summary>
    Task<bool> MarkUploaded(Guid ownerId, string hash);
    /// <summary>The kind of each of these ids that is an asset of the owner with its bytes uploaded; other ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, AssetKind>> UploadedKinds(Guid ownerId, IReadOnlyList<Guid> ids);
    /// <summary>Deletes the asset; when it was the owner's last reference to the blob, deletes the blob row too, and when no blob row
    /// of any owner still names its S3 key (a copy names the sharer's), returns that key.</summary>
    Task<(bool Found, string? OrphanedKey)> Delete(Guid ownerId, Guid id);
    /// <summary>Sets or clears shared_at on each of these ids that is an asset of the owner (sharing an already shared asset keeps its
    /// time); returns those ids.</summary>
    Task<IReadOnlyList<Guid>> SetShared(Guid ownerId, IReadOnlyList<Guid> ids, bool shared);
    /// <summary>The S3 key the asset's blob row names; null when the owner has no such asset.</summary>
    Task<string?> BlobKey(Guid ownerId, Guid id);
    /// <summary>The keys no blob row of any owner names: what may be deleted from the store.</summary>
    Task<IReadOnlyList<string>> Unreferenced(IReadOnlyList<string> keys);
    Task<IReadOnlyList<TerrainVolume>> GetVolumes(Guid ownerId, Guid assetId);
    /// <summary>Replaces the asset's volumes with fresh ids. The caller has checked ownership.</summary>
    Task<IReadOnlyList<TerrainVolume>> PutVolumes(Guid assetId, IReadOnlyList<Volume> volumes);
    /// <summary>Deletes every blob row not uploaded and created before the cutoff, with the asset rows on it, across owners;
    /// returns the deleted rows' S3 keys that no remaining blob row names.</summary>
    Task<IReadOnlyList<string>> DeleteAbandoned(DateTimeOffset cutoff);
    /// <summary>account.starter_seeded_at is set.</summary>
    Task<bool> StarterSeeded(Guid ownerId);
    /// <summary>One transaction: the blob rows marked uploaded, an asset per mesh filed in the owner's root folder of this name
    /// (created when absent), and account.starter_seeded_at stamped.</summary>
    Task AddStarter(Guid ownerId, string folderName, IReadOnlyList<StarterMesh> meshes);
}
