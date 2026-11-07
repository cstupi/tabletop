using System.Text.Json.Serialization;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Shared;

/// <summary>A folder of a player's Shared subtree: <c>AssetIds</c> are the shared (uploaded) assets filed in it. The Shared folder's
/// <c>ParentId</c> is null.</summary>
public sealed record SharedFolderDto(Guid Id, Guid? ParentId, string Name, IReadOnlyList<Guid> AssetIds);

/// <summary>A shared asset as it stands now. <c>ThumbnailKey</c> stays on the server; <c>Thumbnail</c> is its presigned GET.</summary>
public sealed record SharedAssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase,
    [property: JsonIgnore] string? ThumbnailKey = null, Uri? Thumbnail = null);

/// <summary><c>GET /v1/shared/{username}</c>: the owner's username as stored, display name, Shared subtree and the uploaded assets in it.</summary>
public sealed record SharedLibraryDto(string Username, string Name, IReadOnlyList<SharedFolderDto> Folders, IReadOnlyList<SharedAssetDto> Assets);

/// <summary>The body of <c>POST /v1/shared/{username}/copy</c>: exactly one of 1 to <c>Limits.Bulk</c> asset ids and a folder of the
/// Shared subtree (its tree is copied); <c>DestinationId</c> is the caller's folder the copies go in, or none.</summary>
public sealed record CopySharedRequest(IReadOnlyList<Guid>? AssetIds, Guid? FolderId, Guid? DestinationId);

/// <summary><c>Assets</c>: the caller's new asset ids, in source order; <c>Folders</c>: the new folders, the copied tree's root first;
/// <c>NotFound</c>: asset ids that are not shared.</summary>
public sealed record CopySharedResponse(IReadOnlyList<Guid> Assets, IReadOnlyList<FolderDto> Folders, IReadOnlyList<Guid> NotFound);

public enum SharedCopyRefusal { None, SharerMissing, Own, DestinationMissing, FolderMissing, NothingToCopy, TooMany, FolderCap, DailyCap, PendingConflict }

/// <summary>Copy's outcome: the new asset ids (source order) and folder ids (root first), the asset ids that are not shared; or a
/// refusal, nothing written. <c>RetryAfter</c>: when the daily cap frees a slot.</summary>
public sealed record SharedCopyResult(IReadOnlyList<Guid> Assets, IReadOnlyList<Guid> Folders, IReadOnlyList<Guid> NotFound,
    SharedCopyRefusal Refusal = SharedCopyRefusal.None, TimeSpan? RetryAfter = null)
{
    public static SharedCopyResult Refused(SharedCopyRefusal why, TimeSpan? retryAfter = null) => new([], [], [], why, retryAfter);
}

/// <summary>Copies a copier makes in a day, models in one copy, and folders an account holds.</summary>
public sealed record SharedCopyLimits(int PerDay, int PerCopy, int Folders)
{
    public static readonly SharedCopyLimits Default = new(PerDay: 50, PerCopy: Limits.Bulk, Folders: Limits.PerAccount);
}
