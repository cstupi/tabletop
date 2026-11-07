using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public sealed class InMemoryFolderRepository(InMemoryRows rows) : IFolderRepository
{
    private bool Owns(Guid ownerId, Guid folderId) => rows.Folders.TryGetValue(folderId, out var f) && f.Owner == ownerId;

    private bool IsBelow(Guid ancestor, Guid candidate)
    {
        for (var at = rows.Folders.GetValueOrDefault(candidate)?.ParentId; at is { } p; at = rows.Folders.GetValueOrDefault(p)?.ParentId)
            if (p == ancestor) return true;
        return false;
    }

    public Task<IReadOnlyList<FolderDto>> List(Guid ownerId)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<FolderDto>>(rows.Folders.Where(f => f.Value.Owner == ownerId).OrderBy(f => f.Key)
                .Select(f => new FolderDto(f.Key, f.Value.ParentId, f.Value.Name, rows.Filed.Where(x => x.FolderId == f.Key).Select(x => x.AssetId).ToList(), f.Value.Shared)).ToList());
    }

    public Task<int> Count(Guid ownerId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Folders.Values.Count(f => f.Owner == ownerId));
    }

    public Task<FolderDto?> Create(Guid ownerId, FolderRequest r)
    {
        lock (rows.Gate)
        {
            if (r.ParentId is { } p && !Owns(ownerId, p)) return Task.FromResult<FolderDto?>(null);
            rows.RequireAccount(ownerId);
            var id = rows.NextId();
            rows.Folders.Add(id, new InMemoryRows.FolderRow { Owner = ownerId, ParentId = r.ParentId, Name = r.Name });
            return Task.FromResult<FolderDto?>(new FolderDto(id, r.ParentId, r.Name, []));
        }
    }

    public Task<FolderDto> CreateShared(Guid ownerId)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(ownerId);
            if (!rows.Folders.Any(f => f.Value.Owner == ownerId && f.Value.Shared))
                rows.Folders.Add(rows.NextId(), new InMemoryRows.FolderRow { Owner = ownerId, Name = "Shared", Shared = true });
            var (id, row) = rows.Folders.Single(f => f.Value.Owner == ownerId && f.Value.Shared);
            return Task.FromResult(new FolderDto(id, null, row.Name, rows.Filed.Where(x => x.FolderId == id).Select(x => x.AssetId).ToList(), true));
        }
    }

    public Task<(bool Found, bool Valid)> Patch(Guid ownerId, Guid id, FolderRequest r)
    {
        lock (rows.Gate)
        {
            if (!Owns(ownerId, id)) return Task.FromResult((false, false));
            if (r.ParentId is { } p && (p == id || rows.Folders[id].Shared || !Owns(ownerId, p) || IsBelow(ancestor: id, candidate: p))) return Task.FromResult((true, false));
            var f = rows.Folders[id];
            f.Name = r.Name;
            f.ParentId = r.ParentId;
            return Task.FromResult((true, true));
        }
    }

    public Task<bool> Delete(Guid ownerId, Guid id)
    {
        lock (rows.Gate)
        {
            if (!Owns(ownerId, id)) return Task.FromResult(false);
            rows.RemoveFolder(id);
            return Task.FromResult(true);
        }
    }

    public Task<bool> AddAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        lock (rows.Gate)
        {
            var assetOwned = rows.Assets.TryGetValue(assetId, out var a) && a.Owner == ownerId;
            if (!Owns(ownerId, folderId) || !assetOwned) return Task.FromResult(false);
            if (!rows.Filed.Contains((assetId, folderId))) rows.Filed.Add((assetId, folderId));
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        lock (rows.Gate) return Task.FromResult(Owns(ownerId, folderId) && rows.Filed.Remove((assetId, folderId)));
    }
}
