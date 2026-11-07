namespace Tabletop.Management.Content.Shared;

/// <summary>What one copy from a Shared subtree takes, worked out from the subtree's folders and filings before anything is written.
/// <c>Assets</c>: the source assets, ascending; <c>Folders</c>: the folders to copy, the named folder first and every folder after its
/// parent (Parent null for the copied root); <c>Filed</c>: the filings inside them; <c>NotFound</c>: named ids that are not shared.</summary>
public sealed record SharedCopyPlan(SharedCopyRefusal Refusal, IReadOnlyList<Guid> Assets, IReadOnlyList<(Guid Source, Guid? Parent)> Folders,
    IReadOnlyList<(Guid Asset, Guid Folder)> Filed, IReadOnlyList<Guid> NotFound)
{
    private static SharedCopyPlan Refused(SharedCopyRefusal why) => new(why, [], [], [], []);

    /// <param name="tree">The subtree's folders, the Shared folder's parent null.</param>
    /// <param name="filed">Each uploaded asset filed in the subtree, by folder.</param>
    public static SharedCopyPlan Make(IReadOnlyList<(Guid Id, Guid? ParentId)> tree, IReadOnlyList<(Guid Asset, Guid Folder)> filed,
        IReadOnlyList<Guid>? assetIds, Guid? folderId, SharedCopyLimits limits)
    {
        List<Guid> assets, notFound = [];
        var folders = new List<(Guid, Guid?)>();
        IReadOnlyList<(Guid Asset, Guid Folder)> inside = [];
        if (folderId is { } root)
        {
            if (!tree.Any(f => f.Id == root)) return Refused(SharedCopyRefusal.FolderMissing);
            var children = tree.Where(f => f.ParentId is not null).ToLookup(f => f.ParentId!.Value, f => f.Id);
            var queue = new Queue<(Guid, Guid?)>([(root, null)]);
            while (queue.TryDequeue(out var next))
            {
                folders.Add(next);
                foreach (var child in children[next.Item1].Order()) queue.Enqueue((child, next.Item1));
            }
            var ids = folders.Select(f => f.Item1).ToHashSet();
            inside = filed.Where(f => ids.Contains(f.Folder)).ToList();
            assets = inside.Select(f => f.Asset).Distinct().Order().ToList();
        }
        else
        {
            var shared = filed.Select(f => f.Asset).ToHashSet();
            var wanted = assetIds!.Distinct().ToList();
            assets = wanted.Where(shared.Contains).Order().ToList();
            notFound = wanted.Where(id => !shared.Contains(id)).ToList();
        }
        if (assets.Count == 0) return Refused(SharedCopyRefusal.NothingToCopy);
        if (assets.Count > limits.PerCopy) return Refused(SharedCopyRefusal.TooMany);
        return new SharedCopyPlan(SharedCopyRefusal.None, assets, folders, inside, notFound);
    }
}
