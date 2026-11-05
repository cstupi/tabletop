using System.Data.Common;
using Dapper;
using Npgsql;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Offers;

public sealed class OfferRepository(Database db) : IOfferRepository
{
    // Properties, not a constructor: Dapper reads text[] as System.Array, which no constructor parameter type matches.
    private sealed class PlayerRow { public Guid AccountId { get; init; } public string Name { get; init; } = ""; public string[] Games { get; init; } = []; public DateTime LastPlayed { get; init; } }

    private sealed record OfferRow(Guid Id, string State, Guid SharerId, string SharerName, Guid RecipientId, string RecipientName, string? Folder, DateTime CreatedAt, DateTime? DecidedAt);
    private sealed record AssetRow(Guid OfferId, Guid Id, string Name, string Kind, string Hash, long ByteSize, string ContentType, double Scale,
        double Pitch, double Yaw, double Roll, string? Shape, double? BaseA, double? BaseB);
    private sealed record Source(Guid Id, string Name, string Kind, string Hash, string Key, long ByteSize, string ContentType, double Scale,
        double Pitch, double Yaw, double Roll, string? Shape, double? BaseA, double? BaseB);

    private static DateTimeOffset Utc(DateTime t) => new(DateTime.SpecifyKind(t, DateTimeKind.Utc));

    /// <summary>Offer o is one the recipient @callerId can act on: open, from a sharer they have not blocked, one uploaded asset left.</summary>
    private const string Visible = @"o.recipient_id = @callerId and o.state = 'open'
        and not exists (select 1 from share_block k where k.recipient_id = o.recipient_id and k.sharer_id = o.sharer_id)
        and exists (select 1 from share_offer_asset oa join asset a on a.id = oa.asset_id join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
                    where oa.offer_id = o.id and b.uploaded)";

    /// <summary>The co-participant pairs: every other account holding a participant row in a game @callerId holds one in.</summary>
    private const string CoParticipants = @"select distinct p2.account_id, p2.game_id from participant p1
        join participant p2 on p2.game_id = p1.game_id and p2.account_id <> p1.account_id where p1.account_id = @callerId";

    public async Task<IReadOnlyList<PlayerDto>> Players(Guid callerId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<PlayerRow>($@"select x.account_id as AccountId, a.name as Name, max(g.created_at) as LastPlayed,
                (array_agg(g.name order by g.created_at desc, g.id desc))[1:3] as Games
            from ({CoParticipants}) x join account a on a.id = x.account_id join game g on g.id = x.game_id
            group by x.account_id, a.name order by lower(a.name), max(g.created_at) desc, x.account_id", new { callerId });
        return rows.Select(r => new PlayerDto(r.AccountId, r.Name, r.Games, Utc(r.LastPlayed))).ToList();
    }

    public async Task<OfferCreateResult> Create(Guid sharerId, IReadOnlyList<Guid> recipientIds, IReadOnlyList<Guid>? assetIds, Guid? folderId, OfferLimits limits)
    {
        var recipients = recipientIds.Distinct().ToArray();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        // The sharer's account row lock serializes their creates, so the daily count cannot be raced past.
        await c.ExecuteAsync("select 1 from account where id = @sharerId for update", new { sharerId }, tx);
        var eligible = (await c.QueryAsync<Guid>($"select distinct account_id from ({CoParticipants}) x where account_id = any(@recipients)",
            new { callerId = sharerId, recipients }, tx)).ToHashSet();
        if (recipients.Any(r => !eligible.Contains(r))) return new OfferCreateResult([], [], OfferRefusal.RecipientMissing);

        IReadOnlyList<Guid> offered, notFound = [];
        string? folderName = null;
        if (folderId is not null)
        {
            folderName = await c.ExecuteScalarAsync<string?>("select name from folder where id = @folderId and owner_id = @sharerId", new { folderId, sharerId }, tx);
            if (folderName is null) return new OfferCreateResult([], [], OfferRefusal.FolderMissing);
            offered = (await c.QueryAsync<Guid>(@"with recursive tree as (select id from folder where id = @folderId
                    union all select f.id from folder f join tree t on f.parent_id = t.id)
                select distinct a.id from asset_folder af join tree t on t.id = af.folder_id join asset a on a.id = af.asset_id
                    join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
                where a.owner_id = @sharerId and b.uploaded order by a.id", new { folderId, sharerId }, tx)).ToList();
        }
        else
        {
            var wanted = assetIds!.Distinct().ToArray();
            var found = (await c.QueryAsync<Guid>(@"select a.id from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
                where a.owner_id = @sharerId and a.id = any(@wanted) and b.uploaded", new { sharerId, wanted }, tx)).ToHashSet();
            offered = wanted.Where(found.Contains).ToList();
            notFound = wanted.Where(id => !found.Contains(id)).ToList();
        }
        if (offered.Count == 0) return new OfferCreateResult([], notFound, OfferRefusal.NothingToOffer);

        var (made, oldest, now) = await c.QuerySingleAsync<(int, DateTime?, DateTime)>(
            "select count(*)::int, min(created_at), now() from share_offer where sharer_id = @sharerId and created_at > now() - interval '1 day'", new { sharerId }, tx);
        if (made + recipients.Length > limits.PerDay)
            return new OfferCreateResult([], [], OfferRefusal.DailyCap, RetryAfter: (oldest ?? now).AddDays(1) - now);

        foreach (var r in recipients)
        {
            var open = await c.ExecuteScalarAsync<int>($"select count(*)::int from share_offer o where {Visible}", new { callerId = r }, tx);
            if (open >= limits.OpenPerRecipient)
                return new OfferCreateResult([], [], OfferRefusal.RecipientFull, await c.ExecuteScalarAsync<string>("select name from account where id = @r", new { r }, tx));
        }

        var ids = new List<Guid>();
        foreach (var r in recipients)
        {
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into share_offer (id, sharer_id, recipient_id, folder_name) values (@id, @sharerId, @r, @folderName)", new { id, sharerId, r, folderName }, tx);
            await c.ExecuteAsync("insert into share_offer_asset (offer_id, asset_id) select @id, unnest(@offered)", new { id, offered = offered.ToArray() }, tx);
            ids.Add(id);
        }
        await tx.CommitAsync();
        return new OfferCreateResult(ids, notFound);
    }

    private static async Task<IReadOnlyList<OfferDto>> Load(NpgsqlConnection c, IReadOnlyList<Guid> ids, DbTransaction? tx = null)
    {
        if (ids.Count == 0) return [];
        var offers = await c.QueryAsync<OfferRow>(@"select o.id as Id, o.state as State, o.sharer_id as SharerId, s.name as SharerName, o.recipient_id as RecipientId,
                r.name as RecipientName, o.folder_name as Folder, o.created_at as CreatedAt, o.decided_at as DecidedAt
            from share_offer o join account s on s.id = o.sharer_id join account r on r.id = o.recipient_id where o.id = any(@ids)", new { ids = ids.ToArray() }, tx);
        var assets = (await c.QueryAsync<AssetRow>(@"select oa.offer_id as OfferId, a.id as Id, a.name as Name, a.kind as Kind, a.blob_hash as Hash, b.byte_size as ByteSize,
                b.content_type as ContentType, a.scale::float8 as Scale, a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw, a.orientation_roll::float8 as Roll,
                a.modelled_base_shape as Shape, a.modelled_base_a::float8 as BaseA, a.modelled_base_b::float8 as BaseB
            from share_offer_asset oa join asset a on a.id = oa.asset_id join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where oa.offer_id = any(@ids) and b.uploaded order by a.id", new { ids = ids.ToArray() }, tx)).ToLookup(a => a.OfferId);
        var byId = offers.ToDictionary(o => o.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).Select(o => new OfferDto(o.Id, Enum.Parse<OfferState>(o.State, true), o.SharerId, o.SharerName, o.RecipientId,
            o.RecipientName, o.Folder, Utc(o.CreatedAt), o.DecidedAt is { } d ? Utc(d) : null,
            assets[o.Id].Select(a => new OfferAssetDto(a.Id, a.Name, AssetRules.Kind(a.Kind), a.Hash, a.ByteSize, a.ContentType, a.Scale, new Orientation(a.Pitch, a.Yaw, a.Roll),
                a.Shape is null ? null : new Base(AssetRules.Shape(a.Shape), a.BaseA!.Value, a.BaseB!.Value))).ToList())).ToList();
    }

    public async Task<IReadOnlyList<OfferDto>> Sent(Guid sharerId, Http.Keyset page)
    {
        await using var c = await db.Open();
        var ids = (await c.QueryAsync<Guid>("select id from share_offer where sharer_id = @sharerId and (@after::uuid is null or id < @after::uuid) order by id desc limit @limit",
            new { sharerId, after = page.After, limit = page.Limit })).ToList();
        return await Load(c, ids);
    }

    public async Task<IReadOnlyList<OfferDto>> Received(Guid recipientId, Http.Keyset page)
    {
        await using var c = await db.Open();
        var ids = (await c.QueryAsync<Guid>($"select o.id from share_offer o where {Visible} and (@after::uuid is null or o.id < @after::uuid) order by o.id desc limit @limit",
            new { callerId = recipientId, after = page.After, limit = page.Limit })).ToList();
        return await Load(c, ids);
    }

    public async Task<int> OpenCount(Guid recipientId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<int>($"select count(*)::int from share_offer o where {Visible}", new { callerId = recipientId });
    }

    public async Task<OfferDto?> Get(Guid callerId, Guid offerId)
    {
        await using var c = await db.Open();
        var seen = await c.ExecuteScalarAsync<bool>($"select exists (select 1 from share_offer o where o.id = @offerId and (o.sharer_id = @callerId or ({Visible})))", new { offerId, callerId });
        return seen ? (await Load(c, [offerId])).SingleOrDefault() : null;
    }

    public async Task<string?> BlobKey(Guid recipientId, Guid offerId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<string?>($@"select b.s3_key from share_offer o join share_offer_asset oa on oa.offer_id = o.id join asset a on a.id = oa.asset_id
                join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where o.id = @offerId and oa.asset_id = @assetId and b.uploaded and {Visible}", new { offerId, assetId, callerId = recipientId });
    }

    public async Task<AcceptResult> Accept(Guid recipientId, Guid offerId, Guid? folderId, AssetQuota quota)
    {
        var callerId = recipientId;
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        // The account row lock serializes the caller's creates and accepts, as Create's quota check does.
        await c.ExecuteAsync("select 1 from account where id = @callerId for update", new { callerId }, tx);
        // for update: a second accept, a decline or a withdraw waits, then finds the offer no longer open.
        if (!await c.ExecuteScalarAsync<bool>($"select exists (select 1 from share_offer o where o.id = @offerId and {Visible} for update of o)", new { offerId, callerId }, tx))
            return new AcceptResult([], OfferMissing: true);
        if (folderId is not null && !await c.ExecuteScalarAsync<bool>("select exists (select 1 from folder where id = @folderId and owner_id = @callerId)", new { folderId, callerId }, tx))
            return new AcceptResult([], FolderMissing: true);
        // for share: a concurrent delete of a source waits for this copy, then sees the copy's blob row naming the key.
        var found = (await c.QueryAsync<Source>(@"select a.id as Id, a.name as Name, a.kind as Kind, a.blob_hash as Hash, b.s3_key as Key,
                b.byte_size as ByteSize, b.content_type as ContentType, a.scale::float8 as Scale, a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw,
                a.orientation_roll::float8 as Roll, a.modelled_base_shape as Shape, a.modelled_base_a::float8 as BaseA, a.modelled_base_b::float8 as BaseB
            from share_offer_asset oa join asset a on a.id = oa.asset_id join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where oa.offer_id = @offerId and b.uploaded order by a.id for share of a, b", new { offerId }, tx)).ToList();

        var hashes = found.Select(s => s.Hash).Distinct().ToArray();
        // A pending row of the caller's for a source hash carries a declared, unverified size and content type: it is replaced by the
        // source's below, so the quota counts the source's size in its place, and its asset rows must be of a kind the bytes fit.
        var mine = (await c.QueryAsync<(string Hash, bool Uploaded)>("select hash, uploaded from blob where owner_id = @callerId and hash = any(@hashes)",
            new { callerId, hashes }, tx)).ToDictionary(r => r.Hash, r => r.Uploaded);
        var pending = mine.Where(m => !m.Value).Select(m => m.Key).ToArray();
        var pendingKinds = await c.QueryAsync<(string Hash, string Kind)>("select distinct blob_hash, kind from asset where owner_id = @callerId and blob_hash = any(@pending)",
            new { callerId, pending }, tx);
        var contentType = found.DistinctBy(s => s.Hash).ToDictionary(s => s.Hash, s => s.ContentType);
        if (pendingKinds.Any(p => !AssetRules.ContentTypes[AssetRules.Kind(p.Kind)].Contains(contentType[p.Hash]))) return new AcceptResult([], PendingConflict: true);

        var (assets, bytes) = await c.QuerySingleAsync<(int, long)>(@"select (select count(*) from asset where owner_id = @callerId)::int,
            (select coalesce(sum(byte_size), 0) from blob where owner_id = @callerId and not (not uploaded and hash = any(@pending)))::bigint", new { callerId, pending }, tx);
        if (assets + found.Count > quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
        if (bytes + found.Where(s => !mine.GetValueOrDefault(s.Hash)).DistinctBy(s => s.Hash).Sum(s => s.ByteSize) > quota.Bytes) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);

        foreach (var s in found.DistinctBy(s => s.Hash))
            await c.ExecuteAsync(@"insert into blob (owner_id, hash, s3_key, byte_size, content_type, uploaded) values (@callerId, @hash, @key, @size, @ct, true)
                on conflict (owner_id, hash) do update set s3_key = excluded.s3_key, byte_size = excluded.byte_size, content_type = excluded.content_type, uploaded = true
                where not blob.uploaded",
                new { callerId, hash = s.Hash, key = s.Key, size = s.ByteSize, ct = s.ContentType }, tx);
        var copied = new List<Guid>();
        foreach (var s in found)
        {
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(@"insert into asset (id, owner_id, blob_hash, name, kind, scale, orientation_pitch, orientation_yaw, orientation_roll, modelled_base_shape, modelled_base_a, modelled_base_b)
                values (@id, @callerId, @hash, @name, @kind, @scale, @p, @y, @r, @shape, @a, @b)",
                new { id, callerId, hash = s.Hash, name = s.Name, kind = s.Kind, scale = s.Scale, p = s.Pitch, y = s.Yaw, r = s.Roll, shape = s.Shape, a = s.BaseA, b = s.BaseB }, tx);
            var volumes = await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from asset_volume where asset_id = @source order by id", new { source = s.Id }, tx);
            foreach (var v in volumes.Select(v => v with { Id = Guid.CreateVersion7() }))
                await c.ExecuteAsync($"insert into asset_volume (asset_id, {VolumeRow.InsertColumns}) values (@AssetId, {VolumeRow.InsertValues})",
                    new { AssetId = id, v.Id, v.Kind, v.Footprint, v.DimA, v.DimB, v.OffsetX, v.OffsetY, v.Yaw, v.Bottom, v.Top, v.Impassable, v.BlocksLos, v.SeeInOut, v.Cover, v.Difficult, v.Dangerous, v.DangerousRushCharge, v.Steep, v.Notes }, tx);
            if (folderId is not null) await c.ExecuteAsync("insert into asset_folder (asset_id, folder_id) values (@id, @folderId)", new { id, folderId }, tx);
            copied.Add(id);
        }
        await c.ExecuteAsync("update share_offer set state = 'accepted', decided_at = now() where id = @offerId", new { offerId }, tx);
        await tx.CommitAsync();
        return new AcceptResult(copied);
    }

    public async Task<bool> Decline(Guid recipientId, Guid offerId, bool block)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var sharer = await c.ExecuteScalarAsync<Guid?>($"update share_offer o set state = 'declined', decided_at = now() where o.id = @offerId and {Visible} returning o.sharer_id",
            new { offerId, callerId = recipientId }, tx);
        if (sharer is null) return false;
        if (block)
        {
            await c.ExecuteAsync("insert into share_block (recipient_id, sharer_id) values (@recipientId, @sharer) on conflict do nothing", new { recipientId, sharer }, tx);
            await c.ExecuteAsync("update share_offer set state = 'declined', decided_at = now() where recipient_id = @recipientId and sharer_id = @sharer and state = 'open'",
                new { recipientId, sharer }, tx);
        }
        await tx.CommitAsync();
        return true;
    }

    public async Task<WithdrawResult> Withdraw(Guid sharerId, Guid offerId)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var state = await c.ExecuteScalarAsync<string?>("select state from share_offer where id = @offerId and sharer_id = @sharerId for update", new { offerId, sharerId }, tx);
        if (state is null) return WithdrawResult.NotFound;
        if (state != "open") return WithdrawResult.NotOpen;
        await c.ExecuteAsync("update share_offer set state = 'withdrawn', decided_at = now() where id = @offerId", new { offerId }, tx);
        await tx.CommitAsync();
        return WithdrawResult.Withdrawn;
    }

    public async Task<IReadOnlyList<BlockedDto>> Blocked(Guid recipientId)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<BlockedDto>("select k.sharer_id as AccountId, a.name as Name from share_block k join account a on a.id = k.sharer_id where k.recipient_id = @recipientId order by lower(a.name), k.sharer_id",
            new { recipientId })).ToList();
    }

    public async Task<bool> Unblock(Guid recipientId, Guid sharerId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from share_block where recipient_id = @recipientId and sharer_id = @sharerId", new { recipientId, sharerId }) > 0;
    }
}
