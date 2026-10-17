namespace Tabletop.Management.Content.Assets;

/// <summary>An owner's folder tree and which assets each holds. FolderRepository is the Postgres implementation.</summary>
public interface IFolderRepository
{
    /// <summary>Every folder of the owner, ascending by id, each with its asset ids.</summary>
    Task<IReadOnlyList<FolderDto>> List(Guid ownerId);
    /// <summary>Null when the parent is not the owner's.</summary>
    Task<FolderDto?> Create(Guid ownerId, FolderRequest r);
    /// <summary>Found is false for another owner's folder; Valid is false for a parent that is not the owner's, is the folder itself, or sits below it.</summary>
    Task<(bool Found, bool Valid)> Patch(Guid ownerId, Guid id, FolderRequest r);
    /// <summary>Deletes the folder and every folder below it. Assets stay.</summary>
    Task<bool> Delete(Guid ownerId, Guid id);
    /// <summary>Idempotent. False unless both the folder and the asset are the owner's.</summary>
    Task<bool> AddAsset(Guid ownerId, Guid folderId, Guid assetId);
    Task<bool> RemoveAsset(Guid ownerId, Guid folderId, Guid assetId);
}
