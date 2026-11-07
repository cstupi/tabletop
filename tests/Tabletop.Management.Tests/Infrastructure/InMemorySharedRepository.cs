using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Shared;

namespace Tabletop.Management.Tests;

public sealed class InMemorySharedRepository(InMemoryRows rows) : ISharedRepository
{
    /// <summary>shared_copy: the copier and when.</summary>
    private readonly List<(Guid Copier, DateTimeOffset At)> _copies = [];

    private (Guid Owner, InMemoryRows.AccountRow Account, Guid Root)? Find(string username)
    {
        var a = rows.Accounts.FirstOrDefault(x => string.Equals(x.Value.Username, username, StringComparison.OrdinalIgnoreCase));
        if (a.Value is null) return null;
        var root = rows.Folders.FirstOrDefault(f => f.Value.Owner == a.Key && f.Value.Shared);
        return root.Value is null ? null : (a.Key, a.Value, root.Key);
    }

    private (List<(Guid Id, Guid? ParentId)> Tree, List<(Guid Asset, Guid Folder)> Filed) Load(Guid owner, Guid root)
    {
        var tree = new List<(Guid, Guid?)>();
        void Walk(Guid id, Guid? parent) { tree.Add((id, parent)); foreach (var c in rows.Folders.Where(f => f.Value.ParentId == id).Select(f => f.Key).Order()) Walk(c, id); }
        Walk(root, null);
        var ids = tree.Select(t => t.Item1).ToHashSet();
        var filed = rows.Filed.Where(f => ids.Contains(f.FolderId) && rows.Assets.TryGetValue(f.AssetId, out var a) && a.Owner == owner && rows.Blobs[(a.Owner, a.Hash)].Uploaded)
            .Select(f => (f.AssetId, f.FolderId)).ToList();
        return (tree, filed);
    }

    public Task<SharedLibraryDto?> Browse(string username)
    {
        lock (rows.Gate)
        {
            if (Find(username) is not var (owner, account, root)) return Task.FromResult<SharedLibraryDto?>(null);
            var (tree, filed) = Load(owner, root);
            var byFolder = filed.ToLookup(f => f.Folder, f => f.Asset);
            var folders = tree.OrderBy(t => t.Id).Select(t => new SharedFolderDto(t.Id, t.ParentId, rows.Folders[t.Id].Name, byFolder[t.Id].Order().ToList())).ToList();
            var assets = filed.Select(f => f.Asset).Distinct().Order().Select(id =>
            {
                var a = rows.Assets[id]; var b = rows.Blobs[(a.Owner, a.Hash)];
                return new SharedAssetDto(id, a.Name, a.Kind, a.Hash, b.ByteSize, b.ContentType, a.Scale, a.Orientation, a.ModelledBase);
            }).ToList();
            return Task.FromResult<SharedLibraryDto?>(new SharedLibraryDto(account.Username!, account.Name, folders, assets));
        }
    }

    public Task<string?> BlobKey(string username, Guid assetId)
    {
        lock (rows.Gate)
        {
            if (Find(username) is not var (owner, _, root) || !Load(owner, root).Filed.Any(f => f.Asset == assetId)) return Task.FromResult<string?>(null);
            var a = rows.Assets[assetId];
            return Task.FromResult<string?>(rows.Blobs[(a.Owner, a.Hash)].Key);
        }
    }

    public Task<SharedCopyResult> Copy(Guid copierId, string username, IReadOnlyList<Guid>? assetIds, Guid? folderId, Guid? destinationId, AssetQuota quota, SharedCopyLimits limits)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(copierId);
            if (Find(username) is not var (owner, _, root)) return Task.FromResult(SharedCopyResult.Refused(SharedCopyRefusal.SharerMissing));
            if (owner == copierId) return Task.FromResult(SharedCopyResult.Refused(SharedCopyRefusal.Own));
            if (destinationId is { } dest && !(rows.Folders.TryGetValue(dest, out var d) && d.Owner == copierId))
                return Task.FromResult(SharedCopyResult.Refused(SharedCopyRefusal.DestinationMissing));
            var (tree, filed) = Load(owner, root);
            var plan = SharedCopyPlan.Make(tree, filed, assetIds, folderId, limits);
            if (plan.Refusal != SharedCopyRefusal.None) return Task.FromResult(SharedCopyResult.Refused(plan.Refusal));
            if (rows.Folders.Values.Count(f => f.Owner == copierId) + plan.Folders.Count > limits.Folders)
                return Task.FromResult(SharedCopyResult.Refused(SharedCopyRefusal.FolderCap));
            var now = DateTimeOffset.UtcNow;
            var today = _copies.Where(x => x.Copier == copierId && x.At > now.AddDays(-1)).ToList();
            if (today.Count >= limits.PerDay)
                return Task.FromResult(SharedCopyResult.Refused(SharedCopyRefusal.DailyCap, today.Min(x => x.At).AddDays(1) - now));

            var copies = rows.CopyAssets(copierId, plan.Assets, quota);
            if (copies is null) return Task.FromResult(SharedCopyResult.Refused(SharedCopyRefusal.PendingConflict));
            var copyOf = copies.ToDictionary(x => x.Source, x => x.Copy);
            var folderCopy = new Dictionary<Guid, Guid>();
            foreach (var (source, parent) in plan.Folders)
            {
                var id = rows.NextId();
                rows.Folders.Add(id, new InMemoryRows.FolderRow { Owner = copierId, ParentId = parent is { } p ? folderCopy[p] : destinationId, Name = rows.Folders[source].Name });
                folderCopy[source] = id;
            }
            var filings = plan.Folders.Count > 0
                ? plan.Filed.Select(f => (copyOf[f.Asset], folderCopy[f.Folder])).Distinct()
                : destinationId is { } into ? copyOf.Values.Select(a => (a, into)) : [];
            foreach (var f in filings) rows.Filed.Add(f);
            _copies.Add((copierId, now));
            return Task.FromResult(new SharedCopyResult(copies.Select(x => x.Copy).ToList(), plan.Folders.Select(f => folderCopy[f.Source]).ToList(), plan.NotFound));
        }
    }
}
