using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    [Fact]
    public async Task Asset_same_bytes_twice_share_one_blob_and_the_last_delete_returns_its_key()
    {
        var owner = await NewAccount();
        var first = await Assets.Create(owner, Mini(name: "A"));
        Assert.False(first.Uploaded);
        await Assets.MarkUploaded(owner, first.Hash);
        var second = await Assets.Create(owner, Mini(first.Hash, "B"));
        Assert.True(second.Uploaded);
        Assert.Equal([IBlobStore.Key(owner, first.Hash)], await Accounts.BlobKeys(owner));

        Assert.Equal((true, (string?)null), await Assets.Delete(owner, first.Id));
        Assert.Equal((true, IBlobStore.Key(owner, first.Hash)), await Assets.Delete(owner, second.Id));
        Assert.Empty(await Accounts.BlobKeys(owner));
        Assert.Equal((false, (string?)null), await Assets.Delete(owner, second.Id));
    }

    [Fact]
    public async Task Uploaded_kinds_names_only_the_owners_uploaded_assets()
    {
        var (owner, stranger) = (await NewAccount(), await NewAccount());
        var uploaded = await Assets.Create(owner, Mini(name: "up"));
        await Assets.MarkUploaded(owner, uploaded.Hash);
        var pending = await Assets.Create(owner, Texture());
        var theirs = await Assets.Create(stranger, Mini(name: "theirs"));
        await Assets.MarkUploaded(stranger, theirs.Hash);
        var kinds = await Assets.UploadedKinds(owner, [uploaded.Id, pending.Id, theirs.Id, Guid.CreateVersion7()]);
        Assert.Equal(new Dictionary<Guid, AssetKind> { [uploaded.Id] = AssetKind.Miniature }, kinds);
    }

    [Fact]
    public async Task Asset_blobs_are_per_owner()
    {
        var (a, b) = (await NewAccount(), await NewAccount());
        var mine = await Assets.Create(a, Mini());
        await Assets.MarkUploaded(a, mine.Hash);
        var theirs = await Assets.Create(b, Mini(mine.Hash));
        Assert.False(theirs.Uploaded);
        Assert.Equal((true, IBlobStore.Key(a, mine.Hash)), await Assets.Delete(a, mine.Id));
        Assert.NotNull(await Assets.Get(b, theirs.Id));
    }

    [Fact]
    public async Task Asset_list_pages_ascending_by_id_after_the_cursor_and_filters_by_kind()
    {
        var owner = await NewAccount();
        var stranger = await NewAccount();
        await Assets.Create(stranger, Mini());
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add((await Assets.Create(owner, Mini(name: $"m{i}"))).Id);
        var texture = (await Assets.Create(owner, Texture())).Id;
        ids.Sort();

        var page1 = await Assets.List(owner, new Keyset(2, null), AssetKind.Miniature);
        Assert.Equal(ids.Take(2), page1.Select(a => a.Id));
        var page2 = await Assets.List(owner, new Keyset(2, page1[^1].Id), AssetKind.Miniature);
        Assert.Equal(ids.Skip(2), page2.Select(a => a.Id));
        Assert.Equal([texture], (await Assets.List(owner, new Keyset(10, null), AssetKind.Texture)).Select(a => a.Id));
        Assert.Equal(4, (await Assets.List(owner, new Keyset(10, null), null)).Count);
    }

    [Fact]
    public async Task Asset_patch_keeps_what_it_does_not_name_and_is_owner_only()
    {
        var owner = await NewAccount();
        var created = await Assets.Create(owner, Mini(name: "Old"));
        Assert.Null(await Assets.Patch(await NewAccount(), created.Id, new PatchAssetRequest("x", null, null, null)));
        var renamed = await Assets.Patch(owner, created.Id, new PatchAssetRequest("New", null, null, null));
        Assert.Equal(created with { Name = "New" }, renamed);
        var moved = await Assets.Patch(owner, created.Id, new PatchAssetRequest(null, 2.5, new Orientation(90, 0, 0), Base.Oval(2, 1)));
        Assert.Equal(created with { Name = "New", Scale = 2.5, Orientation = new Orientation(90, 0, 0), ModelledBase = Base.Oval(2, 1) }, moved);
        Assert.Null(await Assets.Get(await NewAccount(), created.Id));
    }

    [Fact]
    public async Task Asset_volumes_are_replaced_with_fresh_ids_and_read_only_by_the_owner()
    {
        var owner = await NewAccount();
        var terrain = await Assets.Create(owner, Mini() with { Kind = AssetKind.Terrain, ModelledBase = null });
        var wall = new Volume(VolumeKind.Solid, Footprint.Rect(6, 4), new Point2(1, 2), new Yaw(30), 0, 3, TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, "wall");
        var area = new Volume(VolumeKind.Area, Footprint.Circle(5), new Point2(0, 0), new Yaw(0), 0, 0.5, TerrainDeclarations.Difficult, null);
        var first = await Assets.PutVolumes(terrain.Id, [wall, area]);
        Assert.Equal([wall, area], first.Select(v => v.Volume));
        Assert.Equal(first.OrderBy(v => v.Id), await Assets.GetVolumes(owner, terrain.Id));
        var second = await Assets.PutVolumes(terrain.Id, [area]);
        Assert.DoesNotContain(second[0].Id, first.Select(v => v.Id));
        Assert.Equal(second, await Assets.GetVolumes(owner, terrain.Id));
        Assert.Empty(await Assets.GetVolumes(await NewAccount(), terrain.Id));
    }

    [Fact]
    public async Task Starter_rows_land_uploaded_in_a_root_folder_once_stamped()
    {
        var owner = await NewAccount();
        Assert.False(await Assets.StarterSeeded(owner));
        await Assets.AddStarter(owner, StarterAssets.FolderName, StarterMeshes.All);
        Assert.True(await Assets.StarterSeeded(owner));

        var assets = await Assets.List(owner, new Keyset(100, null), null);
        Assert.Equal(StarterMeshes.All.Select(m => (m.Name, m.Hash, (long)m.Glb.Length, m.ModelledBase)), assets.Select(a => (a.Name, a.Hash, a.ByteSize, a.ModelledBase!.Value)));
        Assert.All(assets, a => Assert.Equal((AssetKind.Miniature, true, 1.0, "model/gltf-binary"), (a.Kind, a.Uploaded, a.Scale, a.ContentType)));
        var folder = Assert.Single(await Folders.List(owner));
        Assert.Equal((StarterAssets.FolderName, (Guid?)null), (folder.Name, folder.ParentId));
        Assert.Equal(assets.Select(a => a.Id).Order(), folder.AssetIds.Order());
    }

    [Fact]
    public async Task Starter_rows_reuse_an_existing_root_folder_and_mark_an_existing_blob_uploaded()
    {
        var owner = await NewAccount();
        var folder = (await Folders.Create(owner, new FolderRequest(StarterAssets.FolderName, null)))!;
        var pending = await Assets.Create(owner, Mini(StarterMeshes.All[0].Hash, "pending", StarterMeshes.All[0].Glb.Length));
        await Assets.AddStarter(owner, StarterAssets.FolderName, StarterMeshes.All);
        Assert.Equal([folder.Id], (await Folders.List(owner)).Select(f => f.Id));
        Assert.True((await Assets.Get(owner, pending.Id))!.Uploaded);
    }
}
