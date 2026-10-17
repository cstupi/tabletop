using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

/// <summary>An owner's assets, their blob rows and terrain volumes. AssetRepository is the Postgres implementation.</summary>
public interface IAssetRepository
{
    /// <summary>Inserts the blob row unless the owner already has one for the hash, then the asset. Same bytes twice share one blob.</summary>
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
    /// <summary>account.starter_seeded_at is set.</summary>
    Task<bool> StarterSeeded(Guid ownerId);
    /// <summary>One transaction: the blob rows marked uploaded, an asset per mesh filed in the owner's root folder of this name
    /// (created when absent), and account.starter_seeded_at stamped.</summary>
    Task AddStarter(Guid ownerId, string folderName, IReadOnlyList<StarterMesh> meshes);
}
