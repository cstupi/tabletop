using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Content.Shared;

/// <summary>Players' Shared folders, found by username (shared-folders spec). SharedRepository is the Postgres implementation.
/// What is shared is the Shared folder's subtree at the moment of the read and the uploaded assets filed in it; nothing else.</summary>
public interface ISharedRepository
{
    /// <summary>The Shared subtree of the account whose username matches ignoring case; null when there is none or it has no Shared folder.</summary>
    Task<SharedLibraryDto?> Browse(string username);
    /// <summary>The S3 key of an asset shared under the username right now; null otherwise.</summary>
    Task<string?> BlobKey(string username, Guid assetId);
    /// <summary>One transaction under the copier's account-row lock: the shared assets named (or the folder's tree: a new folder per
    /// folder, each copy filed in the copies of the folders its source was filed in) copied as <see cref="AssetCopies.Copy"/> into
    /// destinationId (or no folder; a copied tree at the top level). Refused whole: an unknown username or no Shared folder, the copier's
    /// own, a destination not the copier's, a folder outside the subtree, nothing shared to copy, more than <c>limits.PerCopy</c> assets,
    /// the copier's folders past <c>limits.Folders</c>, <c>limits.PerDay</c> copies in 24 h, a pending conflict; a quota refusal throws
    /// AssetQuotaExceeded. A copy records one shared_copy row.</summary>
    Task<SharedCopyResult> Copy(Guid copierId, string username, IReadOnlyList<Guid>? assetIds, Guid? folderId, Guid? destinationId, AssetQuota quota, SharedCopyLimits limits);
}
