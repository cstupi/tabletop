using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

public sealed record AssetUsage(int Assets, long Bytes);

/// <summary>An owner's assets, their blob rows and terrain volumes. AssetRepository is the Postgres implementation.</summary>
public interface IAssetRepository
{
    /// <summary>Inserts the blob row unless the owner already has one for the hash, then the asset. Same bytes twice share one blob;
    /// a blob row not yet uploaded has its created_at restarted.</summary>
    Task<AssetDto> Create(Guid ownerId, CreateAssetRequest r);
    Task<AssetDto?> Get(Guid ownerId, Guid id);
    /// <summary>Ascending by id, after the cursor.</summary>
    Task<IReadOnlyList<AssetDto>> List(Guid ownerId, Http.Keyset page, AssetKind? kind);
    /// <summary>Null fields keep the stored value.</summary>
    Task<AssetDto?> Patch(Guid ownerId, Guid id, PatchAssetRequest p);
    Task MarkUploaded(Guid ownerId, string hash);
    /// <summary>The kind of each of these ids that is an asset of the owner with its bytes uploaded; other ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, AssetKind>> UploadedKinds(Guid ownerId, IReadOnlyList<Guid> ids);
    /// <summary>Deletes the asset; when it was the owner's last reference to the blob, deletes the blob row too and returns its S3 key.</summary>
    Task<(bool Found, string? OrphanedKey)> Delete(Guid ownerId, Guid id);
    Task<IReadOnlyList<TerrainVolume>> GetVolumes(Guid ownerId, Guid assetId);
    /// <summary>Replaces the asset's volumes with fresh ids. The caller has checked ownership.</summary>
    Task<IReadOnlyList<TerrainVolume>> PutVolumes(Guid assetId, IReadOnlyList<Volume> volumes);
    /// <summary>The owner's asset rows, and the byte sizes of their blob rows (uploaded or not), each blob once.</summary>
    Task<AssetUsage> Usage(Guid ownerId);
    /// <summary>Deletes every blob row not uploaded and created before the cutoff, with the asset rows on it, across owners;
    /// returns the deleted rows' S3 keys.</summary>
    Task<IReadOnlyList<string>> DeleteAbandoned(DateTimeOffset cutoff);
    /// <summary>account.starter_seeded_at is set.</summary>
    Task<bool> StarterSeeded(Guid ownerId);
    /// <summary>One transaction: the blob rows marked uploaded, an asset per mesh filed in the owner's root folder of this name
    /// (created when absent), and account.starter_seeded_at stamped.</summary>
    Task AddStarter(Guid ownerId, string folderName, IReadOnlyList<StarterMesh> meshes);
}
