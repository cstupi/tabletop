using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, IReadOnlyList<Guid> AssetIds);
public sealed record FolderRequest(string Name, Guid? ParentId);

public sealed class FolderRepository(Database db)
{
    private sealed record Row(Guid Id, Guid? ParentId, string Name);

    public async Task<IReadOnlyList<FolderDto>> List(Guid ownerId)
    {
        await using var c = await db.Open();
        var folders = (await c.QueryAsync<Row>("select id as Id, parent_id as ParentId, name as Name from folder where owner_id = @ownerId order by id", new { ownerId })).ToList();
        var members = (await c.QueryAsync<(Guid FolderId, Guid AssetId)>("select af.folder_id, af.asset_id from asset_folder af join folder f on f.id = af.folder_id where f.owner_id = @ownerId", new { ownerId }))
            .ToLookup(m => m.FolderId, m => m.AssetId);
        return folders.Select(f => new FolderDto(f.Id, f.ParentId, f.Name, members[f.Id].ToList())).ToList();
    }

    /// <summary>Null when the parent is not the owner's, or would make a cycle.</summary>
    public async Task<FolderDto?> Create(Guid ownerId, FolderRequest r)
    {
        await using var c = await db.Open();
        if (r.ParentId is { } p && !await Owns(c, ownerId, p)) return null;
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into folder (id, owner_id, parent_id, name) values (@id, @ownerId, @parent, @name)", new { id, ownerId, parent = r.ParentId, name = r.Name });
        return new FolderDto(id, r.ParentId, r.Name, []);
    }

    public async Task<(bool Found, bool Valid)> Patch(Guid ownerId, Guid id, FolderRequest r)
    {
        await using var c = await db.Open();
        if (!await Owns(c, ownerId, id)) return (false, false);
        if (r.ParentId is { } p && (p == id || !await Owns(c, ownerId, p) || await IsDescendant(c, ancestor: id, candidate: p))) return (true, false);
        await c.ExecuteAsync("update folder set name = @name, parent_id = @parent where id = @id", new { id, name = r.Name, parent = r.ParentId });
        return (true, true);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from folder where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }

    public async Task<bool> AddAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync(@"insert into asset_folder (asset_id, folder_id) select a.id, f.id from asset a, folder f
            where a.id = @assetId and a.owner_id = @ownerId and f.id = @folderId and f.owner_id = @ownerId on conflict do nothing", new { ownerId, folderId, assetId }) >= 0
            && await Owns(c, ownerId, folderId) && await c.ExecuteScalarAsync<bool>("select exists(select 1 from asset where id = @assetId and owner_id = @ownerId)", new { assetId, ownerId });
    }

    public async Task<bool> RemoveAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from asset_folder af using folder f where f.id = af.folder_id and f.owner_id = @ownerId and af.folder_id = @folderId and af.asset_id = @assetId", new { ownerId, folderId, assetId }) == 1;
    }

    private static Task<bool> Owns(Npgsql.NpgsqlConnection c, Guid ownerId, Guid folderId) =>
        c.ExecuteScalarAsync<bool>("select exists(select 1 from folder where id = @folderId and owner_id = @ownerId)", new { folderId, ownerId });

    private static Task<bool> IsDescendant(Npgsql.NpgsqlConnection c, Guid ancestor, Guid candidate) =>
        c.ExecuteScalarAsync<bool>(@"with recursive tree as (select id from folder where parent_id = @ancestor union all select f.id from folder f join tree t on f.parent_id = t.id)
            select exists(select 1 from tree where id = @candidate)", new { ancestor, candidate });
}
