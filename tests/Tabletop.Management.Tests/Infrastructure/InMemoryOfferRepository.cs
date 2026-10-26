using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Offers;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public sealed class InMemoryOfferRepository(InMemoryRows rows) : IOfferRepository
{
    private bool Uploaded(Guid assetId) => rows.Assets.TryGetValue(assetId, out var a) && rows.Blobs[(a.Owner, a.Hash)].Uploaded;

    private bool Visible(Guid callerId, InMemoryRows.OfferRow o) =>
        o.Recipient == callerId && o.State == "open" && !rows.Blocks.Contains((o.Recipient, o.Sharer)) && o.AssetIds.Any(Uploaded);

    private IEnumerable<(Guid Account, Guid Game)> CoParticipants(Guid callerId) =>
        rows.Participants.Where(p => p.AccountId == callerId).Select(p => p.GameId).Distinct()
            .SelectMany(g => rows.Participants.Where(p => p.GameId == g && p.AccountId != callerId).Select(p => (p.AccountId, g))).Distinct();

    private OfferDto Dto(Guid id, InMemoryRows.OfferRow o) => new(id, Enum.Parse<OfferState>(o.State, true), o.Sharer, rows.Accounts[o.Sharer].Name, o.Recipient,
        rows.Accounts[o.Recipient].Name, o.Folder, o.CreatedAt, o.DecidedAt,
        o.AssetIds.Where(Uploaded).Order().Select(aid =>
        {
            var a = rows.Assets[aid]; var b = rows.Blobs[(a.Owner, a.Hash)];
            return new OfferAssetDto(aid, a.Name, a.Kind, a.Hash, b.ByteSize, b.ContentType, a.Scale, a.Orientation, a.ModelledBase);
        }).ToList());

    public Task<IReadOnlyList<PlayerDto>> Players(Guid callerId)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<PlayerDto>>(CoParticipants(callerId).GroupBy(x => x.Account).Select(grp =>
            {
                var games = grp.Select(x => (Id: x.Game, Row: rows.Games[x.Game])).OrderByDescending(x => x.Row.CreatedAt).ThenByDescending(x => x.Id).ToList();
                return new PlayerDto(grp.Key, rows.Accounts[grp.Key].Name, games.Take(3).Select(x => x.Row.Name).ToList(), games[0].Row.CreatedAt);
            }).OrderBy(p => p.Name.ToLowerInvariant(), StringComparer.Ordinal).ThenByDescending(p => p.LastPlayed).ThenBy(p => p.AccountId).ToList());
    }

    private IEnumerable<Guid> Tree(Guid folderId) => new[] { folderId }.Concat(rows.Folders.Where(f => f.Value.ParentId == folderId).SelectMany(f => Tree(f.Key)));

    public Task<OfferCreateResult> Create(Guid sharerId, IReadOnlyList<Guid> recipientIds, IReadOnlyList<Guid>? assetIds, Guid? folderId, OfferLimits limits)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(sharerId);
            var recipients = recipientIds.Distinct().ToList();
            var eligible = CoParticipants(sharerId).Select(x => x.Account).ToHashSet();
            if (recipients.Any(r => !eligible.Contains(r))) return Task.FromResult(new OfferCreateResult([], [], OfferRefusal.RecipientMissing));
            bool Offerable(Guid id) => rows.Assets.TryGetValue(id, out var a) && a.Owner == sharerId && Uploaded(id);
            List<Guid> offered; List<Guid> notFound = [];
            string? folderName = null;
            if (folderId is { } f)
            {
                if (!(rows.Folders.TryGetValue(f, out var folder) && folder.Owner == sharerId)) return Task.FromResult(new OfferCreateResult([], [], OfferRefusal.FolderMissing));
                folderName = folder.Name;
                var tree = Tree(f).ToHashSet();
                offered = rows.Filed.Where(x => tree.Contains(x.FolderId)).Select(x => x.AssetId).Distinct().Where(Offerable).Order().ToList();
            }
            else
            {
                var wanted = assetIds!.Distinct().ToList();
                offered = wanted.Where(Offerable).ToList();
                notFound = wanted.Where(id => !Offerable(id)).ToList();
            }
            if (offered.Count == 0) return Task.FromResult(new OfferCreateResult([], notFound, OfferRefusal.NothingToOffer));
            var now = DateTimeOffset.UtcNow;
            var today = rows.Offers.Values.Where(o => o.Sharer == sharerId && o.CreatedAt > now.AddDays(-1)).ToList();
            if (today.Count + recipients.Count > limits.PerDay)
                return Task.FromResult(new OfferCreateResult([], [], OfferRefusal.DailyCap, RetryAfter: (today.Count == 0 ? now : today.Min(o => o.CreatedAt)).AddDays(1) - now));
            foreach (var r in recipients)
                if (rows.Offers.Values.Count(o => Visible(r, o)) >= limits.OpenPerRecipient)
                    return Task.FromResult(new OfferCreateResult([], [], OfferRefusal.RecipientFull, rows.Accounts[r].Name));
            var ids = new List<Guid>();
            foreach (var r in recipients)
            {
                var id = rows.NextId();
                rows.Offers.Add(id, new InMemoryRows.OfferRow { Sharer = sharerId, Recipient = r, Folder = folderName, CreatedAt = now, AssetIds = offered.ToList() });
                ids.Add(id);
            }
            return Task.FromResult(new OfferCreateResult(ids, notFound));
        }
    }

    private IReadOnlyList<OfferDto> Page(Func<InMemoryRows.OfferRow, bool> which, Keyset page) =>
        rows.Offers.Where(e => which(e.Value) && (page.After is null || e.Key.CompareTo(page.After.Value) < 0))
            .OrderByDescending(e => e.Key).Take(page.Limit).Select(e => Dto(e.Key, e.Value)).ToList();

    public Task<IReadOnlyList<OfferDto>> Sent(Guid sharerId, Keyset page)
    {
        lock (rows.Gate) return Task.FromResult(Page(o => o.Sharer == sharerId, page));
    }

    public Task<IReadOnlyList<OfferDto>> Received(Guid recipientId, Keyset page)
    {
        lock (rows.Gate) return Task.FromResult(Page(o => Visible(recipientId, o), page));
    }

    public Task<int> OpenCount(Guid recipientId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Offers.Values.Count(o => Visible(recipientId, o)));
    }

    public Task<OfferDto?> Get(Guid callerId, Guid offerId)
    {
        lock (rows.Gate)
            return Task.FromResult(rows.Offers.TryGetValue(offerId, out var o) && (o.Sharer == callerId || Visible(callerId, o)) ? Dto(offerId, o) : null);
    }

    public Task<string?> BlobKey(Guid recipientId, Guid offerId, Guid assetId)
    {
        lock (rows.Gate)
        {
            if (!(rows.Offers.TryGetValue(offerId, out var o) && Visible(recipientId, o) && o.AssetIds.Contains(assetId) && Uploaded(assetId))) return Task.FromResult<string?>(null);
            var a = rows.Assets[assetId];
            return Task.FromResult<string?>(rows.Blobs[(a.Owner, a.Hash)].Key);
        }
    }

    public Task<AcceptResult> Accept(Guid recipientId, Guid offerId, Guid? folderId, AssetQuota quota)
    {
        var callerId = recipientId;
        lock (rows.Gate)
        {
            rows.RequireAccount(callerId);
            if (!(rows.Offers.TryGetValue(offerId, out var offer) && Visible(callerId, offer))) return Task.FromResult(new AcceptResult([], OfferMissing: true));
            if (folderId is { } f && !(rows.Folders.TryGetValue(f, out var folder) && folder.Owner == callerId)) return Task.FromResult(new AcceptResult([], FolderMissing: true));
            var sources = offer.AssetIds.Where(Uploaded).Order().Select(id => rows.Assets[id]).ToList();
            var hashes = sources.Select(s => s.Hash).ToHashSet();
            bool Mine(string hash) => rows.Blobs.TryGetValue((callerId, hash), out var b) && b.Uploaded;
            bool PendingOnSource((Guid Owner, string Hash) k, InMemoryRows.BlobRow b) => k.Owner == callerId && !b.Uploaded && hashes.Contains(k.Hash);
            if (rows.Assets.Values.Any(a => a.Owner == callerId && hashes.Contains(a.Hash) && !Mine(a.Hash)
                    && !AssetRules.ContentTypes[a.Kind].Contains(sources.First(s => s.Hash == a.Hash) is var s ? rows.Blobs[(s.Owner, s.Hash)].ContentType : "")))
                return Task.FromResult(new AcceptResult([], PendingConflict: true));
            if (rows.Assets.Values.Count(a => a.Owner == callerId) + sources.Count > quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
            var added = sources.Where(s => !Mine(s.Hash)).DistinctBy(s => s.Hash).Sum(s => rows.Blobs[(s.Owner, s.Hash)].ByteSize);
            if (rows.Blobs.Where(b => b.Key.Owner == callerId && !PendingOnSource(b.Key, b.Value)).Sum(b => b.Value.ByteSize) + added > quota.Bytes)
                throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);

            foreach (var s in sources.DistinctBy(s => s.Hash))
            {
                var source = rows.Blobs[(s.Owner, s.Hash)];
                if (!rows.Blobs.TryGetValue((callerId, s.Hash), out var mine))
                    rows.Blobs.Add((callerId, s.Hash), new InMemoryRows.BlobRow { Key = source.Key, ByteSize = source.ByteSize, ContentType = source.ContentType, Uploaded = true });
                else if (!mine.Uploaded) { mine.Key = source.Key; mine.ByteSize = source.ByteSize; mine.ContentType = source.ContentType; mine.Uploaded = true; }
            }
            var copied = new List<Guid>();
            foreach (var s in sources)
            {
                var id = rows.NextId();
                rows.Assets.Add(id, new InMemoryRows.AssetRow
                {
                    Owner = callerId, Hash = s.Hash, Name = s.Name, Kind = s.Kind, Scale = s.Scale, Orientation = s.Orientation, ModelledBase = s.ModelledBase,
                    Volumes = s.Volumes.Select(v => new Tabletop.Content.TerrainVolume(rows.NextId(), v.Volume)).ToList(),
                });
                if (folderId is { } into) rows.Filed.Add((id, into));
                copied.Add(id);
            }
            offer.State = "accepted"; offer.DecidedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new AcceptResult(copied));
        }
    }

    public Task<bool> Decline(Guid recipientId, Guid offerId, bool block)
    {
        lock (rows.Gate)
        {
            if (!(rows.Offers.TryGetValue(offerId, out var offer) && Visible(recipientId, offer))) return Task.FromResult(false);
            var now = DateTimeOffset.UtcNow;
            offer.State = "declined"; offer.DecidedAt = now;
            if (block)
            {
                rows.Blocks.Add((recipientId, offer.Sharer));
                foreach (var o in rows.Offers.Values.Where(o => o.Recipient == recipientId && o.Sharer == offer.Sharer && o.State == "open")) { o.State = "declined"; o.DecidedAt = now; }
            }
            return Task.FromResult(true);
        }
    }

    public Task<WithdrawResult> Withdraw(Guid sharerId, Guid offerId)
    {
        lock (rows.Gate)
        {
            if (!(rows.Offers.TryGetValue(offerId, out var o) && o.Sharer == sharerId)) return Task.FromResult(WithdrawResult.NotFound);
            if (o.State != "open") return Task.FromResult(WithdrawResult.NotOpen);
            o.State = "withdrawn"; o.DecidedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(WithdrawResult.Withdrawn);
        }
    }

    public Task<IReadOnlyList<BlockedDto>> Blocked(Guid recipientId)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<BlockedDto>>(rows.Blocks.Where(b => b.Recipient == recipientId).Select(b => new BlockedDto(b.Sharer, rows.Accounts[b.Sharer].Name))
                .OrderBy(b => b.Name.ToLowerInvariant(), StringComparer.Ordinal).ThenBy(b => b.AccountId).ToList());
    }

    public Task<bool> Unblock(Guid recipientId, Guid sharerId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Blocks.Remove((recipientId, sharerId)));
    }
}
