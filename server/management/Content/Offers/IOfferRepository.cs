using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Content.Offers;

public enum OfferRefusal { None, RecipientMissing, FolderMissing, NothingToOffer, DailyCap, RecipientFull }

/// <summary>Create's outcome: the new offer ids (one per recipient, in request order) and the asset ids that are not the sharer's uploaded
/// assets; or a refusal, nothing written. <c>FullRecipient</c> names the recipient at their open-offer cap; <c>RetryAfter</c> is when the
/// daily cap frees a slot.</summary>
public sealed record OfferCreateResult(IReadOnlyList<Guid> OfferIds, IReadOnlyList<Guid> NotFound, OfferRefusal Refusal = OfferRefusal.None,
    string? FullRecipient = null, TimeSpan? RetryAfter = null);

/// <summary>Accept's outcome: the caller's new asset ids; or OfferMissing (not an open offer the caller can see), FolderMissing (the folder is
/// not the caller's), PendingConflict (a not-yet-uploaded asset of the caller's on an offered hash is of a kind those bytes do not fit).
/// Nothing is written on a refusal.</summary>
public sealed record AcceptResult(IReadOnlyList<Guid> Copied, bool OfferMissing = false, bool FolderMissing = false, bool PendingConflict = false);

public enum WithdrawResult { NotFound, NotOpen, Withdrawn }

/// <summary>Offers of one account's assets to another (shared-models spec). OfferRepository is the Postgres implementation.
/// A recipient sees an offer while it is open, its sharer is not blocked by them and one offered asset remains; the sharer sees it always.</summary>
public interface IOfferRepository
{
    /// <summary>The accounts holding a participant row in a game the caller holds one in, by name.</summary>
    Task<IReadOnlyList<PlayerDto>> Players(Guid callerId);
    /// <summary>One transaction: one open offer per recipient of the sharer's uploaded assets named (or the folder's and its sub-folders'
    /// at this moment). Every recipient must be one of <see cref="Players"/>; the sharer's offers in the last 24 h plus these stay within
    /// <c>limits.PerDay</c>; each recipient holds fewer than <c>limits.OpenPerRecipient</c> visible open offers.</summary>
    Task<OfferCreateResult> Create(Guid sharerId, IReadOnlyList<Guid> recipientIds, IReadOnlyList<Guid>? assetIds, Guid? folderId, OfferLimits limits);
    /// <summary>The caller's offers in every state, newest first, before the cursor.</summary>
    Task<IReadOnlyList<OfferDto>> Sent(Guid sharerId, Http.Keyset page);
    /// <summary>The offers the caller can see as recipient, newest first, before the cursor.</summary>
    Task<IReadOnlyList<OfferDto>> Received(Guid recipientId, Http.Keyset page);
    Task<int> OpenCount(Guid recipientId);
    /// <summary>The offer to its sharer, or to its recipient while they can see it; null otherwise.</summary>
    Task<OfferDto?> Get(Guid callerId, Guid offerId);
    /// <summary>The S3 key of an uploaded asset of an offer the caller can see as recipient; null otherwise.</summary>
    Task<string?> BlobKey(Guid recipientId, Guid offerId, Guid assetId);
    /// <summary>One transaction: per offered asset, an asset row of the caller's with the source's name, kind, scale, orientation, modelled
    /// base and volumes, filed in folderId, and a blob row naming the source's S3 key unless the caller has the hash uploaded (a pending row
    /// takes the source's key, size and content type). The quota is checked over all the copies at once, as Create checks it; a refusal
    /// throws AssetQuotaExceeded. The offer becomes accepted.</summary>
    Task<AcceptResult> Accept(Guid recipientId, Guid offerId, Guid? folderId, AssetQuota quota);
    /// <summary>Declines an offer the caller can see; with block, also every open offer from its sharer to the caller, and blocks the sharer.
    /// False when the caller cannot see it.</summary>
    Task<bool> Decline(Guid recipientId, Guid offerId, bool block);
    Task<WithdrawResult> Withdraw(Guid sharerId, Guid offerId);
    Task<IReadOnlyList<BlockedDto>> Blocked(Guid recipientId);
    /// <summary>False when the caller had not blocked the account.</summary>
    Task<bool> Unblock(Guid recipientId, Guid sharerId);
}
