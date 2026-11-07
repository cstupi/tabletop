using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests.Contracts;

/// <summary>Asset thumbnails (UX redesign, web): the owner sets an asset's thumbnail key; a copy names its source's, as its blob row names
/// the source's file; a key stays referenced while any asset row names it.</summary>
public abstract partial class RepositoryContract
{
    [Fact]
    public async Task A_thumbnail_key_is_the_owners_to_set_follows_a_copy_and_goes_with_the_last_row_naming_it()
    {
        var s = await SharerWithFolders();
        var key = IBlobStore.ThumbnailKey(s.B.Id);
        Assert.Null((await Assets.Get(s.Sharer, s.B.Id))!.ThumbnailKey);
        Assert.False(await Assets.SetThumbnail(await NewAccount("Stranger"), s.B.Id, key));
        Assert.True(await Assets.SetThumbnail(s.Sharer, s.B.Id, key));
        Assert.Equal(key, (await Assets.Get(s.Sharer, s.B.Id))!.ThumbnailKey);
        Assert.Equal(key, (await Assets.List(s.Sharer, All, null)).Single(a => a.Id == s.B.Id).ThumbnailKey);
        Assert.Equal(key, (await Shared.Browse(s.Username))!.Assets.Single(a => a.Id == s.B.Id).ThumbnailKey);
        Assert.Contains(key, await Accounts.BlobKeys(s.Sharer));
        Assert.Empty(await Assets.Unreferenced([key]));

        var me = await NewAccount("Copier");
        var copy = (await Shared.Copy(me, s.Username, [s.B.Id], null, null, Plenty, LooseCopies)).Assets.Single();
        Assert.Equal(key, (await Assets.Get(me, copy))!.ThumbnailKey);
        Assert.Equal((true, (string?)null), await Assets.Delete(s.Sharer, s.B.Id));
        Assert.Empty(await Assets.Unreferenced([key]));
        Assert.Equal((true, IBlobStore.Key(s.Sharer, s.B.Hash)), await Assets.Delete(me, copy));
        Assert.Equal([key], await Assets.Unreferenced([key]));
    }

    [Fact]
    public async Task A_patch_keeps_the_thumbnail_key()
    {
        var owner = await NewAccount();
        var a = await Uploaded(owner, Mini(name: "Kept"));
        Assert.True(await Assets.SetThumbnail(owner, a.Id, IBlobStore.ThumbnailKey(a.Id)));
        Assert.Equal(IBlobStore.ThumbnailKey(a.Id), (await Assets.Patch(owner, a.Id, new PatchAssetRequest("Renamed", null, null, null)))!.ThumbnailKey);
    }
}
