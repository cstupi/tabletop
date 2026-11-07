using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    [Fact]
    public async Task Folder_parents_must_be_the_owners_and_never_make_a_cycle()
    {
        var (owner, stranger) = (await NewAccount(), await NewAccount());
        var top = (await Folders.Create(owner, new FolderRequest("Top", null)))!;
        var mid = (await Folders.Create(owner, new FolderRequest("Mid", top.Id)))!;
        var low = (await Folders.Create(owner, new FolderRequest("Low", mid.Id)))!;
        Assert.Null(await Folders.Create(stranger, new FolderRequest("Under yours", top.Id)));

        Assert.Equal((true, false), await Folders.Patch(owner, top.Id, new FolderRequest("Top", low.Id)));
        Assert.Equal((true, false), await Folders.Patch(owner, top.Id, new FolderRequest("Top", top.Id)));
        Assert.Equal((false, false), await Folders.Patch(stranger, top.Id, new FolderRequest("Mine", null)));
        Assert.Equal((true, true), await Folders.Patch(owner, low.Id, new FolderRequest("Low", null)));
        Assert.Equal((true, true), await Folders.Patch(owner, top.Id, new FolderRequest("Renamed", low.Id)));
        var tree = (await Folders.List(owner)).ToDictionary(f => f.Id);
        Assert.Equal(("Renamed", (Guid?)low.Id), (tree[top.Id].Name, tree[top.Id].ParentId));
        Assert.Equal(new[] { top.Id, mid.Id, low.Id }.Order(), tree.Keys);
    }

    [Fact]
    public async Task The_shared_folder_is_made_once_stays_at_the_top_and_other_folders_move_into_it()
    {
        var owner = await NewAccount();
        var shared = await Folders.CreateShared(owner);
        Assert.Equal(("Shared", (Guid?)null, true), (shared.Name, shared.ParentId, shared.Shared));
        Assert.Equal(shared.Id, (await Folders.CreateShared(owner)).Id);
        var other = (await Folders.Create(owner, new FolderRequest("Orks", null)))!;
        Assert.False(other.Shared);
        Assert.Equal((true, false), await Folders.Patch(owner, shared.Id, new FolderRequest("Shared", other.Id)));
        Assert.Equal((true, true), await Folders.Patch(owner, shared.Id, new FolderRequest("For friends", null)));
        Assert.Equal((true, true), await Folders.Patch(owner, other.Id, new FolderRequest("Orks", shared.Id)));
        var list = (await Folders.List(owner)).ToDictionary(f => f.Id);
        Assert.Equal(("For friends", true), (list[shared.Id].Name, list[shared.Id].Shared));
        Assert.Equal(((Guid?)shared.Id, false), (list[other.Id].ParentId, list[other.Id].Shared));
        Assert.True(await Folders.Delete(owner, shared.Id));
        Assert.NotEqual(shared.Id, (await Folders.CreateShared(owner)).Id);
    }

    [Fact]
    public async Task Folder_delete_takes_every_folder_below_it_and_leaves_the_assets()
    {
        var owner = await NewAccount();
        var top = (await Folders.Create(owner, new FolderRequest("Top", null)))!;
        var mid = (await Folders.Create(owner, new FolderRequest("Mid", top.Id)))!;
        await Folders.Create(owner, new FolderRequest("Low", mid.Id));
        var other = (await Folders.Create(owner, new FolderRequest("Other", null)))!;
        var asset = await Assets.Create(owner, Mini());
        Assert.True(await Folders.AddAsset(owner, mid.Id, asset.Id));
        Assert.True(await Folders.AddAsset(owner, other.Id, asset.Id));

        Assert.False(await Folders.Delete(await NewAccount(), top.Id));
        Assert.True(await Folders.Delete(owner, top.Id));
        var left = Assert.Single(await Folders.List(owner));
        Assert.Equal((other.Id, "Other"), (left.Id, left.Name));
        Assert.Equal([asset.Id], left.AssetIds);
        Assert.NotNull(await Assets.Get(owner, asset.Id));
        Assert.False(await Folders.Delete(owner, top.Id));
    }

    [Fact]
    public async Task Folder_filing_is_idempotent_owner_only_and_dropped_with_the_asset()
    {
        var (owner, stranger) = (await NewAccount(), await NewAccount());
        var folder = (await Folders.Create(owner, new FolderRequest("F", null)))!;
        var asset = await Assets.Create(owner, Mini());
        var theirs = await Assets.Create(stranger, Mini());
        Assert.True(await Folders.AddAsset(owner, folder.Id, asset.Id));
        Assert.True(await Folders.AddAsset(owner, folder.Id, asset.Id));
        Assert.False(await Folders.AddAsset(owner, folder.Id, theirs.Id));
        Assert.False(await Folders.AddAsset(stranger, folder.Id, theirs.Id));
        Assert.Equal([asset.Id], (await Folders.List(owner)).Single().AssetIds);

        Assert.False(await Folders.RemoveAsset(stranger, folder.Id, asset.Id));
        Assert.True(await Folders.RemoveAsset(owner, folder.Id, asset.Id));
        Assert.False(await Folders.RemoveAsset(owner, folder.Id, asset.Id));

        await Folders.AddAsset(owner, folder.Id, asset.Id);
        await Assets.Delete(owner, asset.Id);
        Assert.Empty((await Folders.List(owner)).Single().AssetIds);
    }

    [Fact]
    public async Task Folder_count_is_per_owner()
    {
        var (owner, stranger) = (await NewAccount(), await NewAccount());
        var top = (await Folders.Create(owner, new FolderRequest("Top", null)))!;
        await Folders.Create(owner, new FolderRequest("Below", top.Id));
        Assert.Equal(2, await Folders.Count(owner));
        Assert.Equal(0, await Folders.Count(stranger));
    }
}
