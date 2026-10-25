using Dapper;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed class SharedAssetRepository(Database db) : ISharedAssetRepository
{
    // Properties, not a constructor: Dapper reads text[] as System.Array, which no constructor parameter type matches.
    private sealed class Row
    {
        public Guid Id { get; init; } public string Name { get; init; } = ""; public string Kind { get; init; } = ""; public string Hash { get; init; } = "";
        public long ByteSize { get; init; } public string ContentType { get; init; } = ""; public double Scale { get; init; }
        public double Pitch { get; init; } public double Yaw { get; init; } public double Roll { get; init; }
        public string? Shape { get; init; } public double? BaseA { get; init; } public double? BaseB { get; init; }
        public string OwnerName { get; init; } = ""; public string[] Folders { get; init; } = []; public DateTime SharedAt { get; init; }

        public SharedAssetDto ToDto() => new(Id, Name, AssetRules.Kind(Kind), Hash, ByteSize, ContentType, Scale, new Orientation(Pitch, Yaw, Roll),
            Shape is null ? null : new Base(AssetRules.Shape(Shape), BaseA!.Value, BaseB!.Value), OwnerName, Folders, new DateTimeOffset(DateTime.SpecifyKind(SharedAt, DateTimeKind.Utc)));
    }

    private sealed record Source(Guid Id, Guid OwnerId, string Name, string Kind, string Hash, string Key, long ByteSize, string ContentType, double Scale,
        double Pitch, double Yaw, double Roll, string? Shape, double? BaseA, double? BaseB);

    /// <summary>A shared asset of another account whose bytes arrived.</summary>
    private const string Visible = "a.shared_at is not null and b.uploaded and a.owner_id <> @callerId";

    /// <summary>q as an ILIKE substring pattern, its wildcards escaped.</summary>
    public static string Pattern(string q) => "%" + q.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

    public async Task<IReadOnlyList<SharedAssetDto>> List(Guid callerId, Http.Keyset page, AssetKind? kind, string? q)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<Row>($@"select a.id as Id, a.name as Name, a.kind as Kind, a.blob_hash as Hash, b.byte_size as ByteSize, b.content_type as ContentType,
                a.scale::float8 as Scale, a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw, a.orientation_roll::float8 as Roll,
                a.modelled_base_shape as Shape, a.modelled_base_a::float8 as BaseA, a.modelled_base_b::float8 as BaseB, o.name as OwnerName,
                array(select f.name from asset_folder af join folder f on f.id = af.folder_id where af.asset_id = a.id order by f.name) as Folders,
                a.shared_at as SharedAt
            from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash join account o on o.id = a.owner_id
            where {Visible} and (@after::uuid is null or a.id > @after::uuid) and (@kind::text is null or a.kind = @kind::text)
              and (@pattern::text is null or a.name ilike @pattern or o.name ilike @pattern
                   or exists (select 1 from asset_folder af join folder f on f.id = af.folder_id where af.asset_id = a.id and f.name ilike @pattern))
            order by a.id limit @limit",
            new { callerId, after = page.After, kind = kind is null ? null : AssetRules.Kind(kind.Value), pattern = string.IsNullOrEmpty(q) ? null : Pattern(q), limit = page.Limit });
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<string?> BlobKey(Guid callerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<string?>($"select b.s3_key from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash where a.id = @id and {Visible}", new { id, callerId });
    }

    public async Task<CopyResult> Copy(Guid callerId, IReadOnlyList<Guid> ids, Guid? folderId, AssetQuota quota)
    {
        var wanted = ids.Distinct().ToList();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        // The account row lock serializes the caller's creates and copies, as Create's quota check does.
        await c.ExecuteAsync("select 1 from account where id = @callerId for update", new { callerId }, tx);
        if (folderId is not null && !await c.ExecuteScalarAsync<bool>("select exists (select 1 from folder where id = @folderId and owner_id = @callerId)", new { folderId, callerId }, tx))
            return new CopyResult([], [], FolderMissing: true);
        // for share: a concurrent delete of a source waits for this copy, then sees the copy's blob row naming the key.
        var sources = (await c.QueryAsync<Source>($@"select a.id as Id, a.owner_id as OwnerId, a.name as Name, a.kind as Kind, a.blob_hash as Hash, b.s3_key as Key,
                b.byte_size as ByteSize, b.content_type as ContentType, a.scale::float8 as Scale, a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw,
                a.orientation_roll::float8 as Roll, a.modelled_base_shape as Shape, a.modelled_base_a::float8 as BaseA, a.modelled_base_b::float8 as BaseB
            from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where a.id = any(@ids) and {Visible} for share of a, b", new { ids = wanted.ToArray(), callerId }, tx)).ToDictionary(s => s.Id);
        var found = wanted.Where(sources.ContainsKey).Select(id => sources[id]).ToList();
        var notFound = wanted.Where(id => !sources.ContainsKey(id)).ToList();
        if (found.Count == 0) return new CopyResult([], notFound);

        var (assets, bytes) = await c.QuerySingleAsync<(int, long)>(@"select (select count(*) from asset where owner_id = @callerId)::int,
            (select coalesce(sum(byte_size), 0) from blob where owner_id = @callerId)::bigint", new { callerId }, tx);
        var known = (await c.QueryAsync<string>("select hash from blob where owner_id = @callerId and hash = any(@hashes)",
            new { callerId, hashes = found.Select(s => s.Hash).Distinct().ToArray() }, tx)).ToHashSet();
        if (assets + found.Count > quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
        if (bytes + found.Where(s => !known.Contains(s.Hash)).DistinctBy(s => s.Hash).Sum(s => s.ByteSize) > quota.Bytes) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);

        foreach (var s in found.DistinctBy(s => s.Hash))
            await c.ExecuteAsync(@"insert into blob (owner_id, hash, s3_key, byte_size, content_type, uploaded) values (@callerId, @hash, @key, @size, @ct, true)
                on conflict (owner_id, hash) do update set s3_key = excluded.s3_key, uploaded = true where not blob.uploaded",
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
                    new { AssetId = id, v.Id, v.Kind, v.Footprint, v.DimA, v.DimB, v.OffsetX, v.OffsetY, v.Yaw, v.Bottom, v.Top, v.Impassable, v.BlocksLos, v.SeeInOut, v.Cover, v.Difficult, v.Dangerous, v.DangerousRushCharge, v.Notes }, tx);
            if (folderId is not null) await c.ExecuteAsync("insert into asset_folder (asset_id, folder_id) values (@id, @folderId)", new { id, folderId }, tx);
            copied.Add(id);
        }
        await tx.CommitAsync();
        return new CopyResult(copied, notFound);
    }
}
