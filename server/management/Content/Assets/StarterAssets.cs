using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

/// <summary>Gives an account's library the starter miniatures once, on the first login after account.starter_seeded_at is null (spec 2026-10-01-starter-meshes-design).</summary>
public sealed class StarterAssets(Database db, IBlobStore blobs)
{
    public const string FolderName = "Starter";
    private const string ContentType = "model/gltf-binary";

    public async Task SeedIfNeeded(Guid ownerId)
    {
        await using var c = await db.Open();
        if (await c.ExecuteScalarAsync<bool>("select starter_seeded_at is not null from account where id = @ownerId", new { ownerId })) return;

        // The bytes first: a blob row with uploaded = true must never point at a missing object.
        foreach (var m in StarterMeshes.All) await blobs.Put(IBlobStore.Key(ownerId, m.Hash), m.Glb, ContentType);

        await using var tx = await c.BeginTransactionAsync();
        var folderId = await c.ExecuteScalarAsync<Guid?>("select id from folder where owner_id = @ownerId and parent_id is null and name = @name order by id limit 1", new { ownerId, name = FolderName }, tx);
        if (folderId is null)
        {
            folderId = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into folder (id, owner_id, parent_id, name) values (@id, @ownerId, null, @name)", new { id = folderId, ownerId, name = FolderName }, tx);
        }
        foreach (var m in StarterMeshes.All)
        {
            await c.ExecuteAsync(@"insert into blob (owner_id, hash, s3_key, byte_size, content_type, uploaded) values (@ownerId, @hash, @key, @size, @ct, true)
                on conflict (owner_id, hash) do update set uploaded = true", new { ownerId, hash = m.Hash, key = IBlobStore.Key(ownerId, m.Hash), size = (long)m.Glb.Length, ct = ContentType }, tx);
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
