using System.Data.Common;
using Dapper;
using Npgsql;

namespace Tabletop.Management.Content.Assets;

/// <summary>Copying another account's uploaded assets into the caller's library: an offer's Accept and a copy from a Shared folder
/// (shared-models spec → Copying). The caller holds the transaction and the caller's account-row lock.</summary>
public static class AssetCopies
{
    /// <summary><c>Copies</c>: (source asset id, the caller's new asset id), ascending by source id; empty with <c>PendingConflict</c>,
    /// when a not-yet-uploaded asset of the caller's on a source hash is of a kind those bytes do not fit (nothing written).</summary>
    public sealed record Outcome(IReadOnlyList<(Guid Source, Guid Copy)> Copies, bool PendingConflict = false);

    private sealed record Source(Guid Id, string Name, string Kind, string Hash, string Key, long ByteSize, string ContentType, double Scale,
        double Pitch, double Yaw, double Roll, string? Shape, double? BaseA, double? BaseB);

    /// <summary>Per source asset that is uploaded: an asset row of the caller's with the source's name, kind, scale, orientation, modelled
    /// base and volumes, and a blob row naming the source's S3 key unless the caller has the hash uploaded (a pending row takes the
    /// source's key, size and content type). The quota is checked over all the copies at once; a refusal throws AssetQuotaExceeded.
    /// The sources are read for share: a concurrent delete of one waits, then sees the copy's blob row naming the key.</summary>
    public static async Task<Outcome> Copy(NpgsqlConnection c, DbTransaction tx, Guid callerId, IReadOnlyCollection<Guid> sourceIds, AssetQuota quota)
    {
        var found = (await c.QueryAsync<Source>(@"select a.id as Id, a.name as Name, a.kind as Kind, a.blob_hash as Hash, b.s3_key as Key,
                b.byte_size as ByteSize, b.content_type as ContentType, a.scale::float8 as Scale, a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw,
                a.orientation_roll::float8 as Roll, a.modelled_base_shape as Shape, a.modelled_base_a::float8 as BaseA, a.modelled_base_b::float8 as BaseB
            from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where a.id = any(@ids) and b.uploaded order by a.id for share of a, b", new { ids = sourceIds.ToArray() }, tx)).ToList();

        var hashes = found.Select(s => s.Hash).Distinct().ToArray();
        // A pending row of the caller's for a source hash carries a declared, unverified size and content type: it is replaced by the
        // source's below, so the quota counts the source's size in its place, and its asset rows must be of a kind the bytes fit.
        var mine = (await c.QueryAsync<(string Hash, bool Uploaded)>("select hash, uploaded from blob where owner_id = @callerId and hash = any(@hashes)",
            new { callerId, hashes }, tx)).ToDictionary(r => r.Hash, r => r.Uploaded);
        var pending = mine.Where(m => !m.Value).Select(m => m.Key).ToArray();
        var pendingKinds = await c.QueryAsync<(string Hash, string Kind)>("select distinct blob_hash, kind from asset where owner_id = @callerId and blob_hash = any(@pending)",
            new { callerId, pending }, tx);
        var contentType = found.DistinctBy(s => s.Hash).ToDictionary(s => s.Hash, s => s.ContentType);
        if (pendingKinds.Any(p => !AssetRules.ContentTypes[AssetRules.Kind(p.Kind)].Contains(contentType[p.Hash]))) return new Outcome([], PendingConflict: true);

        var (assets, bytes) = await c.QuerySingleAsync<(int, long)>(@"select (select count(*) from asset where owner_id = @callerId)::int,
            (select coalesce(sum(byte_size), 0) from blob where owner_id = @callerId and not (not uploaded and hash = any(@pending)))::bigint", new { callerId, pending }, tx);
        if (assets + found.Count > quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
        if (bytes + found.Where(s => !mine.GetValueOrDefault(s.Hash)).DistinctBy(s => s.Hash).Sum(s => s.ByteSize) > quota.Bytes) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);

        foreach (var s in found.DistinctBy(s => s.Hash))
            await c.ExecuteAsync(@"insert into blob (owner_id, hash, s3_key, byte_size, content_type, uploaded) values (@callerId, @hash, @key, @size, @ct, true)
                on conflict (owner_id, hash) do update set s3_key = excluded.s3_key, byte_size = excluded.byte_size, content_type = excluded.content_type, uploaded = true
                where not blob.uploaded",
                new { callerId, hash = s.Hash, key = s.Key, size = s.ByteSize, ct = s.ContentType }, tx);
        var copies = new List<(Guid, Guid)>();
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
            copies.Add((s.Id, id));
        }
        return new Outcome(copies);
    }
}
