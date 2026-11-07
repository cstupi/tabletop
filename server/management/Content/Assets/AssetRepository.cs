using Dapper;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed class AssetRepository(Database db) : IAssetRepository
{
    private sealed record Row(Guid Id, Guid OwnerId, string Name, string Kind, string BlobHash, long ByteSize, string ContentType, bool Uploaded, double Scale,
        double OrientationPitch, double OrientationYaw, double OrientationRoll, string? ModelledBaseShape, double? ModelledBaseA, double? ModelledBaseB, string? ThumbnailKey)
    {
        public AssetDto ToDto() => new(Id, Name, AssetRules.Kind(Kind), BlobHash, ByteSize, ContentType, Uploaded, Scale, new Orientation(OrientationPitch, OrientationYaw, OrientationRoll),
            ModelledBaseShape is null ? null : new Base(AssetRules.Shape(ModelledBaseShape), ModelledBaseA!.Value, ModelledBaseB!.Value), ThumbnailKey);
    }

    private const string Select = @"select a.id as Id, a.owner_id as OwnerId, a.name as Name, a.kind as Kind, a.blob_hash as BlobHash, b.byte_size as ByteSize, b.content_type as ContentType,
        b.uploaded as Uploaded, a.scale::float8 as Scale, a.orientation_pitch::float8 as OrientationPitch, a.orientation_yaw::float8 as OrientationYaw, a.orientation_roll::float8 as OrientationRoll,
        a.modelled_base_shape as ModelledBaseShape, a.modelled_base_a::float8 as ModelledBaseA, a.modelled_base_b::float8 as ModelledBaseB, a.thumbnail_key as ThumbnailKey
        from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash";

    public async Task<AssetDto> Create(Guid ownerId, CreateAssetRequest r, AssetQuota? quota = null)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        if (quota is not null)
        {
            // The account row lock serializes an owner's creates, so the count and sum below cannot be raced past the quota.
            await c.ExecuteAsync("select 1 from account where id = @ownerId for update", new { ownerId }, tx);
            var (assets, bytes, known) = await c.QuerySingleAsync<(int, long, bool)>(@"select (select count(*) from asset where owner_id = @ownerId)::int,
                (select coalesce(sum(byte_size), 0) from blob where owner_id = @ownerId)::bigint,
                exists(select 1 from blob where owner_id = @ownerId and hash = @hash)", new { ownerId, hash = r.Hash }, tx);
            if (assets >= quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
            if (!known && bytes + r.ByteSize > quota.Bytes) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);
        }
        await c.ExecuteAsync("insert into blob (owner_id, hash, s3_key, byte_size, content_type) values (@ownerId, @hash, @key, @size, @ct) on conflict (owner_id, hash) do update set created_at = now() where not blob.uploaded",
            new { ownerId, hash = r.Hash, key = IBlobStore.Key(ownerId, r.Hash), size = r.ByteSize, ct = r.ContentType }, tx);
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(@"insert into asset (id, owner_id, blob_hash, name, kind, scale, orientation_pitch, orientation_yaw, orientation_roll, modelled_base_shape, modelled_base_a, modelled_base_b)
            values (@id, @ownerId, @hash, @name, @kind, @scale, @p, @y, @r, @shape, @a, @b)",
            new { id, ownerId, hash = r.Hash, name = r.Name, kind = AssetRules.Kind(r.Kind), scale = r.Scale, p = r.Orientation.Pitch, y = r.Orientation.Yaw, r = r.Orientation.Roll,
                  shape = r.ModelledBase is { } mb ? AssetRules.Shape(mb.Shape) : null, a = r.ModelledBase?.A, b = r.ModelledBase?.B }, tx);
        await tx.CommitAsync();
        return (await Get(ownerId, id))!;
    }

    public async Task<AssetDto?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return (await c.QuerySingleOrDefaultAsync<Row>(Select + " where a.id = @id and a.owner_id = @ownerId", new { id, ownerId }))?.ToDto();
    }

    public async Task<IReadOnlyList<AssetDto>> List(Guid ownerId, Http.Keyset page, AssetKind? kind)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<Row>(Select + " where a.owner_id = @ownerId and (@after::uuid is null or a.id > @after::uuid) and (@kind::text is null or a.kind = @kind::text) order by a.id limit @limit",
            new { ownerId, after = page.After, kind = kind is null ? null : AssetRules.Kind(kind.Value), limit = page.Limit });
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<AssetDto?> Patch(Guid ownerId, Guid id, PatchAssetRequest p)
    {
        await using var c = await db.Open();
        var n = await c.ExecuteAsync(@"update asset set name = coalesce(@name, name), scale = coalesce(@scale, scale),
            orientation_pitch = coalesce(@p, orientation_pitch), orientation_yaw = coalesce(@y, orientation_yaw), orientation_roll = coalesce(@r, orientation_roll),
            modelled_base_shape = coalesce(@shape, modelled_base_shape), modelled_base_a = coalesce(@a, modelled_base_a), modelled_base_b = coalesce(@b, modelled_base_b)
            where id = @id and owner_id = @ownerId",
            new { id, ownerId, name = p.Name, scale = p.Scale, p = p.Orientation?.Pitch, y = p.Orientation?.Yaw, r = p.Orientation?.Roll,
                  shape = p.ModelledBase is { } mb ? AssetRules.Shape(mb.Shape) : null, a = p.ModelledBase?.A, b = p.ModelledBase?.B });
        return n == 1 ? await Get(ownerId, id) : null;
    }

    public async Task<bool> MarkUploaded(Guid ownerId, string hash)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("update blob set uploaded = true where owner_id = @ownerId and hash = @hash", new { ownerId, hash }) == 1;
    }

    public async Task<(bool Found, string? Previous)> SetThumbnail(Guid ownerId, Guid id, string key)
    {
        await using var c = await db.Open();
        var row = await c.QuerySingleOrDefaultAsync<(Guid Id, string? Previous)>(@"update asset a set thumbnail_key = @key from asset old
            where a.id = @id and a.owner_id = @ownerId and old.id = a.id returning a.id, old.thumbnail_key", new { id, ownerId, key });
        return row.Id == Guid.Empty ? (false, null) : (true, row.Previous);
    }

    public async Task<IReadOnlyDictionary<Guid, AssetKind>> UploadedKinds(Guid ownerId, IReadOnlyList<Guid> ids)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(Guid Id, string Kind)>(@"select a.id, a.kind from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where a.owner_id = @ownerId and b.uploaded and a.id = any(@ids)", new { ownerId, ids = ids.ToArray() });
        return rows.ToDictionary(r => r.Id, r => AssetRules.Kind(r.Kind));
    }

    public async Task<(bool Found, string? OrphanedKey)> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var hash = await c.ExecuteScalarAsync<string?>("delete from asset where id = @id and owner_id = @ownerId returning blob_hash", new { id, ownerId }, tx);
        if (hash is null) return (false, null);
        var key = await c.ExecuteScalarAsync<string?>(@"delete from blob where owner_id = @ownerId and hash = @hash
            and not exists (select 1 from asset where owner_id = @ownerId and blob_hash = @hash) returning s3_key", new { ownerId, hash }, tx);
        // A separate statement, so it sees a copy's blob row committed while the delete above waited on the copy's lock.
        if (key is not null && await c.ExecuteScalarAsync<bool>("select exists (select 1 from blob where s3_key = @key)", new { key }, tx)) key = null;
        await tx.CommitAsync();
        return (true, key);
    }

    public async Task<string?> BlobKey(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<string?>("select b.s3_key from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash where a.id = @id and a.owner_id = @ownerId", new { id, ownerId });
    }

    public async Task<IReadOnlyList<string>> Unreferenced(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return [];
        await using var c = await db.Open();
        var named = (await c.QueryAsync<string>(@"select s3_key from blob where s3_key = any(@keys)
            union select thumbnail_key from asset where thumbnail_key = any(@keys)", new { keys = keys.ToArray() })).ToHashSet();
        return keys.Distinct().Where(k => !named.Contains(k)).ToList();
    }

    public async Task<IReadOnlyList<TerrainVolume>> GetVolumes(Guid ownerId, Guid assetId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from asset_volume where asset_id = @assetId and exists (select 1 from asset where id = @assetId and owner_id = @ownerId) order by id", new { assetId, ownerId });
        return rows.Select(r => new TerrainVolume(r.Id, r.ToVolume())).ToList();
    }

    public async Task<IReadOnlyList<TerrainVolume>> PutVolumes(Guid assetId, IReadOnlyList<Volume> volumes)
    {
        var rows = volumes.Select(v => VolumeRow.From(Guid.CreateVersion7(), v)).ToList();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("delete from asset_volume where asset_id = @assetId", new { assetId }, tx);
        foreach (var r in rows)
            await c.ExecuteAsync($"insert into asset_volume (asset_id, {VolumeRow.InsertColumns}) values (@AssetId, {VolumeRow.InsertValues})", new { AssetId = assetId, r.Id, r.Kind, r.Footprint, r.DimA, r.DimB, r.OffsetX, r.OffsetY, r.Yaw, r.Bottom, r.Top, r.Impassable, r.BlocksLos, r.SeeInOut, r.Cover, r.Difficult, r.Dangerous, r.DangerousRushCharge, r.Steep, r.Notes }, tx);
        await tx.CommitAsync();
        return rows.Select(r => new TerrainVolume(r.Id, r.ToVolume())).ToList();
    }
    public async Task<IReadOnlyList<string>> DeleteAbandoned(DateTimeOffset cutoff)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync(@"delete from asset a using blob b where b.owner_id = a.owner_id and b.hash = a.blob_hash
            and not b.uploaded and b.created_at < @cutoff", new { cutoff }, tx);
        var keys = (await c.QueryAsync<string>("delete from blob where not uploaded and created_at < @cutoff returning s3_key", new { cutoff }, tx)).Distinct().ToArray();
        var named = (await c.QueryAsync<string>("select distinct s3_key from blob where s3_key = any(@keys)", new { keys }, tx)).ToHashSet();
        await tx.CommitAsync();
        return keys.Where(k => !named.Contains(k)).ToList();
    }

    public async Task<bool> StarterSeeded(Guid ownerId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<bool>("select starter_seeded_at is not null from account where id = @ownerId", new { ownerId });
    }

    public async Task AddStarter(Guid ownerId, string folderName, IReadOnlyList<StarterMesh> meshes)
    {
        const string contentType = "model/gltf-binary";
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var folderId = await c.ExecuteScalarAsync<Guid?>("select id from folder where owner_id = @ownerId and parent_id is null and name = @name order by id limit 1", new { ownerId, name = folderName }, tx);
        if (folderId is null)
        {
            folderId = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into folder (id, owner_id, parent_id, name) values (@id, @ownerId, null, @name)", new { id = folderId, ownerId, name = folderName }, tx);
        }
        foreach (var m in meshes)
        {
            await c.ExecuteAsync(@"insert into blob (owner_id, hash, s3_key, byte_size, content_type, uploaded) values (@ownerId, @hash, @key, @size, @ct, true)
                on conflict (owner_id, hash) do update set uploaded = true", new { ownerId, hash = m.Hash, key = IBlobStore.Key(ownerId, m.Hash), size = (long)m.Glb.Length, ct = contentType }, tx);
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(@"insert into asset (id, owner_id, blob_hash, name, kind, scale, orientation_pitch, orientation_yaw, orientation_roll, modelled_base_shape, modelled_base_a, modelled_base_b)
                values (@id, @ownerId, @hash, @name, @kind, 1, 0, 0, 0, @shape, @a, @b)",
                new { id, ownerId, hash = m.Hash, name = m.Name, kind = AssetRules.Kind(AssetKind.Miniature), shape = AssetRules.Shape(m.ModelledBase.Shape), a = m.ModelledBase.A, b = m.ModelledBase.B }, tx);
            await c.ExecuteAsync("insert into asset_folder (asset_id, folder_id) values (@id, @folderId)", new { id, folderId }, tx);
        }
        await c.ExecuteAsync("update account set starter_seeded_at = now() where id = @ownerId", new { ownerId }, tx);
        await tx.CommitAsync();
    }
}
