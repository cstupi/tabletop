namespace Tabletop.Management.Content.Assets;

/// <summary>Gives an account's library the starter miniatures once, on the first login after account.starter_seeded_at is null (spec 2026-10-01-starter-meshes-design).</summary>
public sealed class StarterAssets(IAssetRepository assets, IBlobStore blobs)
{
    public const string FolderName = "Starter";
    private const string ContentType = "model/gltf-binary";

    public async Task SeedIfNeeded(Guid ownerId)
    {
        if (await assets.StarterSeeded(ownerId)) return;
        // The bytes first: a blob row with uploaded = true must never point at a missing object.
        foreach (var m in StarterMeshes.All) await blobs.Put(IBlobStore.Key(ownerId, m.Hash), m.Glb, ContentType);
        await assets.AddStarter(ownerId, FolderName, StarterMeshes.All);
    }
}
