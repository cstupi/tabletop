using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public sealed class InMemorySharedAssetRepository(InMemoryRows rows) : ISharedAssetRepository
{
    private bool Visible(Guid callerId, InMemoryRows.AssetRow a) => a.SharedAt is not null && a.Owner != callerId && rows.Blobs[(a.Owner, a.Hash)].Uploaded;

    private IEnumerable<string> FolderNames(Guid assetId) =>
        rows.Filed.Where(f => f.AssetId == assetId).Select(f => rows.Folders[f.FolderId].Name).Order(StringComparer.Ordinal);

    public Task<IReadOnlyList<SharedAssetDto>> List(Guid callerId, Keyset page, AssetKind? kind, string? q)
    {
        bool Match(string s) => s.Contains(q!, StringComparison.OrdinalIgnoreCase);
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<SharedAssetDto>>(rows.Assets
                .Where(e => Visible(callerId, e.Value) && (page.After is null || e.Key.CompareTo(page.After.Value) > 0) && (kind is null || e.Value.Kind == kind)
                    && (string.IsNullOrEmpty(q) || Match(e.Value.Name) || Match(rows.Accounts[e.Value.Owner].Name) || FolderNames(e.Key).Any(Match)))
                .OrderBy(e => e.Key).Take(page.Limit)
                .Select(e =>
                {
                    var (a, b) = (e.Value, rows.Blobs[(e.Value.Owner, e.Value.Hash)]);
                    return new SharedAssetDto(e.Key, a.Name, a.Kind, a.Hash, b.ByteSize, b.ContentType, a.Scale, a.Orientation, a.ModelledBase,
                        rows.Accounts[a.Owner].Name, FolderNames(e.Key).ToList(), a.SharedAt!.Value);
                }).ToList());
    }

    public Task<string?> BlobKey(Guid callerId, Guid id)
    {
        lock (rows.Gate) return Task.FromResult(rows.Assets.TryGetValue(id, out var a) && Visible(callerId, a) ? rows.Blobs[(a.Owner, a.Hash)].Key : null);
    }

    public Task<CopyResult> Copy(Guid callerId, IReadOnlyList<Guid> ids, Guid? folderId, AssetQuota quota)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(callerId);
            if (folderId is { } f && !(rows.Folders.TryGetValue(f, out var folder) && folder.Owner == callerId)) return Task.FromResult(new CopyResult([], [], FolderMissing: true));
            var wanted = ids.Distinct().ToList();
            var found = wanted.Where(id => rows.Assets.TryGetValue(id, out var a) && Visible(callerId, a)).ToList();
            var notFound = wanted.Except(found).ToList();
            if (found.Count == 0) return Task.FromResult(new CopyResult([], notFound));
            var sources = found.Select(id => rows.Assets[id]).ToList();
            if (rows.Assets.Values.Count(a => a.Owner == callerId) + found.Count > quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
            var added = sources.Where(s => !rows.Blobs.ContainsKey((callerId, s.Hash))).DistinctBy(s => s.Hash).Sum(s => rows.Blobs[(s.Owner, s.Hash)].ByteSize);
            if (rows.Blobs.Where(b => b.Key.Owner == callerId).Sum(b => b.Value.ByteSize) + added > quota.Bytes) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);

            foreach (var s in sources.DistinctBy(s => s.Hash))
            {
                var source = rows.Blobs[(s.Owner, s.Hash)];
                if (!rows.Blobs.TryGetValue((callerId, s.Hash), out var mine))
                    rows.Blobs.Add((callerId, s.Hash), new InMemoryRows.BlobRow { Key = source.Key, ByteSize = source.ByteSize, ContentType = source.ContentType, Uploaded = true });
                else if (!mine.Uploaded) { mine.Key = source.Key; mine.Uploaded = true; }
            }
            var copied = new List<Guid>();
            foreach (var s in sources)
            {
                var id = rows.NextId();
                rows.Assets.Add(id, new InMemoryRows.AssetRow
                {
                    Owner = callerId, Hash = s.Hash, Name = s.Name, Kind = s.Kind, Scale = s.Scale, Orientation = s.Orientation, ModelledBase = s.ModelledBase,
                    Volumes = s.Volumes.Select(v => new TerrainVolume(rows.NextId(), v.Volume)).ToList(),
                });
                if (folderId is { } into) rows.Filed.Add((id, into));
                copied.Add(id);
            }
            return Task.FromResult(new CopyResult(copied, notFound));
        }
    }
}
