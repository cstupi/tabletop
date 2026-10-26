using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public sealed class InMemoryAssetRepository(InMemoryRows rows) : IAssetRepository
{
    private AssetDto Dto(Guid id, InMemoryRows.AssetRow a)
    {
        var b = rows.Blobs[(a.Owner, a.Hash)];
        return new AssetDto(id, a.Name, a.Kind, a.Hash, b.ByteSize, b.ContentType, b.Uploaded, a.Scale, a.Orientation, a.ModelledBase);
    }

    /// <summary>check ((kind = 'miniature') = (modelled_base_shape is not null))</summary>
    private static void Check(InMemoryRows.AssetRow a)
    {
        if ((a.Kind == AssetKind.Miniature) != (a.ModelledBase is not null)) throw new InvalidOperationException("check violation: a miniature carries a modelled base, nothing else does");
    }

    private void InsertAsset(Guid id, InMemoryRows.AssetRow a)
    {
        if (!rows.Blobs.ContainsKey((a.Owner, a.Hash))) throw new InvalidOperationException("foreign key violation: no blob");
        Check(a);
        rows.Assets.Add(id, a);
    }

    public Task<AssetDto> Create(Guid ownerId, CreateAssetRequest r, AssetQuota? quota = null)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(ownerId);
            if (quota is not null)
            {
                if (rows.Assets.Values.Count(a => a.Owner == ownerId) >= quota.Assets) throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Assets);
                if (!rows.Blobs.ContainsKey((ownerId, r.Hash)) && rows.Blobs.Where(b => b.Key.Owner == ownerId).Sum(b => b.Value.ByteSize) + r.ByteSize > quota.Bytes)
                    throw new AssetQuotaExceeded(AssetQuotaExceeded.Limit.Bytes);
            }
            if (!rows.Blobs.TryAdd((ownerId, r.Hash), new InMemoryRows.BlobRow { Key = IBlobStore.Key(ownerId, r.Hash), ByteSize = r.ByteSize, ContentType = r.ContentType })
                && rows.Blobs[(ownerId, r.Hash)] is { Uploaded: false } pending) pending.CreatedAt = DateTimeOffset.UtcNow;
            var id = rows.NextId();
            InsertAsset(id, new InMemoryRows.AssetRow { Owner = ownerId, Hash = r.Hash, Name = r.Name, Kind = r.Kind, Scale = r.Scale, Orientation = r.Orientation, ModelledBase = r.ModelledBase });
            return Task.FromResult(Dto(id, rows.Assets[id]));
        }
    }

    public Task<AssetDto?> Get(Guid ownerId, Guid id)
    {
        lock (rows.Gate) return Task.FromResult(rows.Assets.TryGetValue(id, out var a) && a.Owner == ownerId ? Dto(id, a) : null);
    }

    public Task<IReadOnlyList<AssetDto>> List(Guid ownerId, Keyset page, AssetKind? kind)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<AssetDto>>(rows.Assets
                .Where(a => a.Value.Owner == ownerId && (page.After is null || a.Key.CompareTo(page.After.Value) > 0) && (kind is null || a.Value.Kind == kind))
                .OrderBy(a => a.Key).Take(page.Limit).Select(a => Dto(a.Key, a.Value)).ToList());
    }

    public Task<AssetDto?> Patch(Guid ownerId, Guid id, PatchAssetRequest p)
    {
        lock (rows.Gate)
        {
            if (!rows.Assets.TryGetValue(id, out var a) || a.Owner != ownerId) return Task.FromResult<AssetDto?>(null);
            var next = new InMemoryRows.AssetRow
            {
                Owner = a.Owner, Hash = a.Hash, Name = p.Name ?? a.Name, Kind = a.Kind, Scale = p.Scale ?? a.Scale,
                Orientation = p.Orientation ?? a.Orientation, ModelledBase = p.ModelledBase ?? a.ModelledBase, Volumes = a.Volumes,
            };
            Check(next);
            rows.Assets[id] = next;
            return Task.FromResult<AssetDto?>(Dto(id, next));
        }
    }

    public Task<bool> MarkUploaded(Guid ownerId, string hash)
    {
        lock (rows.Gate)
        {
            if (!rows.Blobs.TryGetValue((ownerId, hash), out var b)) return Task.FromResult(false);
            b.Uploaded = true;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyDictionary<Guid, AssetKind>> UploadedKinds(Guid ownerId, IReadOnlyList<Guid> ids)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyDictionary<Guid, AssetKind>>(ids.Distinct()
                .Where(id => rows.Assets.TryGetValue(id, out var a) && a.Owner == ownerId && rows.Blobs[(a.Owner, a.Hash)].Uploaded)
                .ToDictionary(id => id, id => rows.Assets[id].Kind));
    }

    public Task<(bool Found, string? OrphanedKey)> Delete(Guid ownerId, Guid id)
    {
        lock (rows.Gate)
        {
            if (!rows.Assets.TryGetValue(id, out var a) || a.Owner != ownerId) return Task.FromResult<(bool, string?)>((false, null));
            rows.RemoveAsset(id);
            if (rows.Assets.Values.Any(x => x.Owner == ownerId && x.Hash == a.Hash)) return Task.FromResult<(bool, string?)>((true, null));
            var key = rows.Blobs[(ownerId, a.Hash)].Key;
            rows.Blobs.Remove((ownerId, a.Hash));
            return Task.FromResult<(bool, string?)>((true, rows.Blobs.Values.Any(b => b.Key == key) ? null : key));
        }
    }

    public Task<string?> BlobKey(Guid ownerId, Guid id)
    {
        lock (rows.Gate) return Task.FromResult(rows.Assets.TryGetValue(id, out var a) && a.Owner == ownerId ? rows.Blobs[(a.Owner, a.Hash)].Key : null);
    }

    public Task<IReadOnlyList<string>> Unreferenced(IReadOnlyList<string> keys)
    {
        lock (rows.Gate) return Task.FromResult<IReadOnlyList<string>>(keys.Distinct().Where(k => !rows.Blobs.Values.Any(b => b.Key == k)).ToList());
    }

    public Task<IReadOnlyList<TerrainVolume>> GetVolumes(Guid ownerId, Guid assetId)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<TerrainVolume>>(rows.Assets.TryGetValue(assetId, out var a) && a.Owner == ownerId ? a.Volumes.OrderBy(v => v.Id).ToList() : []);
    }

    public Task<IReadOnlyList<TerrainVolume>> PutVolumes(Guid assetId, IReadOnlyList<Volume> volumes)
    {
        lock (rows.Gate)
        {
            if (!rows.Assets.TryGetValue(assetId, out var a)) throw new InvalidOperationException($"foreign key violation: no asset {assetId}");
            a.Volumes = volumes.Select(v => new TerrainVolume(rows.NextId(), v)).ToList();
            return Task.FromResult<IReadOnlyList<TerrainVolume>>(a.Volumes.ToList());
        }
    }

    public Task<IReadOnlyList<string>> DeleteAbandoned(DateTimeOffset cutoff)
    {
        lock (rows.Gate)
        {
            var gone = rows.Blobs.Where(b => !b.Value.Uploaded && b.Value.CreatedAt < cutoff).ToList();
            foreach (var (key, blob) in gone)
            {
                foreach (var id in rows.Assets.Where(a => a.Value.Owner == key.Owner && a.Value.Hash == key.Hash).Select(a => a.Key).ToList()) rows.RemoveAsset(id);
                rows.Blobs.Remove(key);
            }
            return Task.FromResult<IReadOnlyList<string>>(gone.Select(b => b.Value.Key).Distinct().Where(k => !rows.Blobs.Values.Any(b => b.Key == k)).ToList());
        }
    }

    public Task<bool> StarterSeeded(Guid ownerId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Accounts.TryGetValue(ownerId, out var a) && a.StarterSeeded);
    }

    public Task AddStarter(Guid ownerId, string folderName, IReadOnlyList<StarterMesh> meshes)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(ownerId);
            var folderId = rows.Folders.Where(f => f.Value.Owner == ownerId && f.Value.ParentId is null && f.Value.Name == folderName).OrderBy(f => f.Key).Select(f => (Guid?)f.Key).FirstOrDefault();
            if (folderId is null)
            {
                folderId = rows.NextId();
                rows.Folders.Add(folderId.Value, new InMemoryRows.FolderRow { Owner = ownerId, Name = folderName });
            }
            foreach (var m in meshes)
            {
                if (rows.Blobs.TryGetValue((ownerId, m.Hash), out var existing)) existing.Uploaded = true;
                else rows.Blobs.Add((ownerId, m.Hash), new InMemoryRows.BlobRow { Key = IBlobStore.Key(ownerId, m.Hash), ByteSize = m.Glb.Length, ContentType = "model/gltf-binary", Uploaded = true });
                var id = rows.NextId();
                InsertAsset(id, new InMemoryRows.AssetRow { Owner = ownerId, Hash = m.Hash, Name = m.Name, Kind = AssetKind.Miniature, Scale = 1, Orientation = new Orientation(0, 0, 0), ModelledBase = m.ModelledBase });
                rows.Filed.Add((id, folderId.Value));
            }
            rows.Accounts[ownerId].StarterSeeded = true;
        }
        return Task.CompletedTask;
    }
}
