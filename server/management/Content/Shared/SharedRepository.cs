using Dapper;
using Npgsql;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Shared;

public sealed class SharedRepository(Database db) : ISharedRepository
{
    private sealed record Owner(Guid Id, string Username, string Name, Guid Root);
    private sealed record FolderRow(Guid Id, Guid? ParentId, string Name);
    private sealed record AssetRow(Guid Id, string Name, string Kind, string Hash, long ByteSize, string ContentType, double Scale,
        double Pitch, double Yaw, double Roll, string? Shape, double? BaseA, double? BaseB);

    /// <summary>The account holding the username (ignoring case) and its Shared folder.</summary>
    private static Task<Owner?> Find(NpgsqlConnection c, string username, System.Data.Common.DbTransaction? tx = null) =>
        c.QuerySingleOrDefaultAsync<Owner>(@"select a.id as Id, a.username as Username, a.name as Name, f.id as Root from account a
            join folder f on f.owner_id = a.id and f.shared where lower(a.username) = lower(@username)", new { username }, tx);

    /// <summary>The Shared subtree from @root.</summary>
    private const string Tree = @"with recursive tree as (select id, parent_id, name from folder where id = @root
        union all select f.id, f.parent_id, f.name from folder f join tree t on f.parent_id = t.id)";

    /// <summary>(asset, folder) for each uploaded asset of @owner filed in the subtree.</summary>
    private const string Filings = Tree + @" select af.asset_id as Item1, af.folder_id as Item2 from asset_folder af join tree t on t.id = af.folder_id
        join asset a on a.id = af.asset_id join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash where a.owner_id = @owner and b.uploaded";

    private static async Task<(List<FolderRow> Folders, List<(Guid Asset, Guid Folder)> Filed)> Load(NpgsqlConnection c, Owner o, System.Data.Common.DbTransaction? tx = null)
    {
        var folders = (await c.QueryAsync<FolderRow>(Tree + " select id as Id, parent_id as ParentId, name as Name from tree order by id", new { root = o.Root }, tx)).ToList();
        var filed = (await c.QueryAsync<(Guid, Guid)>(Filings, new { root = o.Root, owner = o.Id }, tx)).ToList();
        return (folders, filed);
    }

    public async Task<SharedLibraryDto?> Browse(string username)
    {
        await using var c = await db.Open();
        if (await Find(c, username) is not { } o) return null;
        var (folders, filed) = await Load(c, o);
        var ids = filed.Select(f => f.Asset).Distinct().ToArray();
        var assets = await c.QueryAsync<AssetRow>(@"select a.id as Id, a.name as Name, a.kind as Kind, a.blob_hash as Hash, b.byte_size as ByteSize, b.content_type as ContentType,
                a.scale::float8 as Scale, a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw, a.orientation_roll::float8 as Roll,
                a.modelled_base_shape as Shape, a.modelled_base_a::float8 as BaseA, a.modelled_base_b::float8 as BaseB
            from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash where a.id = any(@ids) order by a.id", new { ids });
        var byFolder = filed.ToLookup(f => f.Folder, f => f.Asset);
        return new SharedLibraryDto(o.Username, o.Name,
            folders.Select(f => new SharedFolderDto(f.Id, f.Id == o.Root ? null : f.ParentId, f.Name, byFolder[f.Id].Order().ToList())).ToList(),
            assets.Select(a => new SharedAssetDto(a.Id, a.Name, AssetRules.Kind(a.Kind), a.Hash, a.ByteSize, a.ContentType, a.Scale, new Orientation(a.Pitch, a.Yaw, a.Roll),
                a.Shape is null ? null : new Base(AssetRules.Shape(a.Shape), a.BaseA!.Value, a.BaseB!.Value))).ToList());
    }

    public async Task<string?> BlobKey(string username, Guid assetId)
    {
        await using var c = await db.Open();
        if (await Find(c, username) is not { } o) return null;
        return await c.ExecuteScalarAsync<string?>(Tree + @" select b.s3_key from asset_folder af join tree t on t.id = af.folder_id
                join asset a on a.id = af.asset_id join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where a.id = @assetId and a.owner_id = @owner and b.uploaded limit 1", new { root = o.Root, owner = o.Id, assetId });
    }

    public async Task<SharedCopyResult> Copy(Guid copierId, string username, IReadOnlyList<Guid>? assetIds, Guid? folderId, Guid? destinationId, AssetQuota quota, SharedCopyLimits limits)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        // The account row lock serializes the copier's creates, accepts and copies: the quota, the folder cap and the daily cap.
        await c.ExecuteAsync("select 1 from account where id = @copierId for update", new { copierId }, tx);
        if (await Find(c, username, tx) is not { } o) return SharedCopyResult.Refused(SharedCopyRefusal.SharerMissing);
        if (o.Id == copierId) return SharedCopyResult.Refused(SharedCopyRefusal.Own);
        if (destinationId is not null && !await c.ExecuteScalarAsync<bool>("select exists (select 1 from folder where id = @destinationId and owner_id = @copierId)",
                new { destinationId, copierId }, tx))
            return SharedCopyResult.Refused(SharedCopyRefusal.DestinationMissing);

        var (folders, filed) = await Load(c, o, tx);
        var plan = SharedCopyPlan.Make(folders.Select(f => (f.Id, f.Id == o.Root ? null : f.ParentId)).ToList(), filed, assetIds, folderId, limits);
        if (plan.Refusal != SharedCopyRefusal.None) return SharedCopyResult.Refused(plan.Refusal);

        var held = await c.ExecuteScalarAsync<int>("select count(*)::int from folder where owner_id = @copierId", new { copierId }, tx);
        if (held + plan.Folders.Count > limits.Folders) return SharedCopyResult.Refused(SharedCopyRefusal.FolderCap);
        var (made, oldest, now) = await c.QuerySingleAsync<(int, DateTime?, DateTime)>(
            "select count(*)::int, min(created_at), now() from shared_copy where copier_id = @copierId and created_at > now() - interval '1 day'", new { copierId }, tx);
        if (made >= limits.PerDay) return SharedCopyResult.Refused(SharedCopyRefusal.DailyCap, (oldest ?? now).AddDays(1) - now);

        var outcome = await AssetCopies.Copy(c, tx, copierId, plan.Assets, quota);
        if (outcome.PendingConflict) return SharedCopyResult.Refused(SharedCopyRefusal.PendingConflict);
        var copyOf = outcome.Copies.ToDictionary(x => x.Source, x => x.Copy);

        var names = folders.ToDictionary(f => f.Id, f => f.Name);
        var folderCopy = new Dictionary<Guid, Guid>();
        foreach (var (source, parent) in plan.Folders)
        {
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into folder (id, owner_id, parent_id, name) values (@id, @copierId, @parent, @name)",
                new { id, copierId, parent = parent is { } p ? folderCopy[p] : destinationId, name = names[source] }, tx);
            folderCopy[source] = id;
        }
        var filings = plan.Folders.Count > 0
            ? plan.Filed.Where(f => copyOf.ContainsKey(f.Asset)).Select(f => (Asset: copyOf[f.Asset], Folder: folderCopy[f.Folder])).Distinct()
            : destinationId is { } d ? copyOf.Values.Select(a => (Asset: a, Folder: d)) : [];
        foreach (var (asset, folder) in filings)
            await c.ExecuteAsync("insert into asset_folder (asset_id, folder_id) values (@asset, @folder)", new { asset, folder }, tx);
        await c.ExecuteAsync("insert into shared_copy (id, copier_id) values (@id, @copierId)", new { id = Guid.CreateVersion7(), copierId }, tx);
        await tx.CommitAsync();
        return new SharedCopyResult(outcome.Copies.Select(x => x.Copy).ToList(), plan.Folders.Select(f => folderCopy[f.Source]).ToList(), plan.NotFound);
    }
}
