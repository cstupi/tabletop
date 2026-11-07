using System.Text.Json.Serialization;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Content.Offers;

public enum OfferState { Open, Accepted, Declined, Withdrawn }

/// <summary>An account the caller has been in a game with: <c>Games</c> names up to three of those games, newest first, and
/// <c>LastPlayed</c> is the newest one's creation, so two players of the same name can be told apart.</summary>
public sealed record PlayerDto(Guid AccountId, string Name, IReadOnlyList<string> Games, DateTimeOffset LastPlayed);

/// <summary>One offered asset as it stands now (an offer names assets, not copies of their fields).</summary>
/// <c>ThumbnailKey</c> stays on the server; <c>Thumbnail</c> is its presigned GET.
public sealed record OfferAssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase,
    [property: JsonIgnore] string? ThumbnailKey = null, Uri? Thumbnail = null);

/// <summary>An offer of a set of the sharer's assets to one recipient. <c>Folder</c> is the folder's name when a folder was offered.
/// <c>Assets</c> are the offered assets the sharer still has.</summary>
public sealed record OfferDto(Guid Id, OfferState State, Guid SharerId, string SharerName, Guid RecipientId, string RecipientName, string? Folder,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, IReadOnlyList<OfferAssetDto> Assets);

/// <summary>The body of <c>POST /v1/offers</c>: 1 to <see cref="OfferLimits.Recipients"/> recipients, and either 1 to <c>Limits.Bulk</c>
/// asset ids or one folder (its assets and its sub-folders' at this moment).</summary>
public sealed record CreateOffersRequest(IReadOnlyList<Guid> RecipientIds, IReadOnlyList<Guid>? AssetIds, Guid? FolderId);
/// <summary>One offer per recipient; <c>NotFound</c>: asset ids that are not the caller's uploaded assets.</summary>
public sealed record CreateOffersResponse(IReadOnlyList<OfferDto> Offers, IReadOnlyList<Guid> NotFound);

/// <summary>The body of <c>POST /v1/offers/{id}/accept</c>: the caller's folder the copies are filed in, or none.</summary>
public sealed record AcceptOfferRequest(Guid? FolderId);
/// <summary>The caller's new assets, one per offered asset the sharer still has.</summary>
public sealed record AcceptOfferResponse(IReadOnlyList<AssetDto> Copied);

/// <summary>The body of <c>POST /v1/offers/{id}/decline</c>. <c>Block</c>: also decline every open offer from the same sharer and
/// hide the sharer's offers from now on.</summary>
public sealed record DeclineOfferRequest(bool Block);

/// <summary>An account whose offers the caller has blocked.</summary>
public sealed record BlockedDto(Guid AccountId, string Name);

/// <summary>Open offers the caller can act on (the Library's badge).</summary>
public sealed record OfferCountDto(int Open);

/// <summary>How many offers a sharer makes in a day, how many open offers a recipient holds, and how many recipients one request names.</summary>
public sealed record OfferLimits(int PerDay, int OpenPerRecipient)
{
    public static readonly OfferLimits Default = new(PerDay: 50, OpenPerRecipient: 20);
    public const int Recipients = 20;
}
