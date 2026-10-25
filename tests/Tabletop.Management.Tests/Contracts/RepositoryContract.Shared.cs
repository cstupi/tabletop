using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    private static readonly AssetQuota Plenty = new(500, 1L << 30);

    private async Task<AssetDto> Uploaded(Guid owner, CreateAssetRequest r)
    {
        var a = await Assets.Create(owner, r);
        await Assets.MarkUploaded(owner, a.Hash);
        return (await Assets.Get(owner, a.Id))!;
    }

    private async Task<IReadOnlyList<SharedAssetDto>> SharedBy(Guid caller, Guid owner, string? q = null, AssetKind? kind = null)
    {
        var name = (await Accounts.Get(owner))!.Name;
        return (await Shared.List(caller, new Keyset(200, null), kind, q)).Where(s => s.OwnerName == name).ToList();
    }

    [Fact]
    public async Task Shared_lists_another_accounts_shared_uploaded_assets_with_owner_and_folders()
    {
        var owner = await NewAccount("Sharer " + Guid.NewGuid().ToString("N")[..8]);
        var me = await NewAccount();
        var orks = await Uploaded(owner, Mini(name: "Ork boy"));
        var hidden = await Uploaded(owner, Mini(name: "Not shared"));
        var pending = await Assets.Create(owner, Mini(name: "Pending"));
        var folder = (await Folders.Create(owner, new FolderRequest("Greenskins", null)))!;
        await Folders.AddAsset(owner, folder.Id, orks.Id);
        Assert.Equal([orks.Id, pending.Id], await Assets.SetShared(owner, [orks.Id, pending.Id, Guid.CreateVersion7()], true));
        Assert.True((await Assets.Get(owner, orks.Id))!.Shared);
        Assert.False((await Assets.Get(owner, hidden.Id))!.Shared);

        var listed = Assert.Single(await SharedBy(me, owner));
        Assert.Equal((orks.Id, "Ork boy", orks.Hash), (listed.Id, listed.Name, listed.Hash));
        Assert.Equal(["Greenskins"], listed.Folders);
        Assert.Equal(Base.Circle(1), listed.ModelledBase);
        Assert.Empty(await SharedBy(owner, owner));
        Assert.Single(await SharedBy(me, owner, "greenSKIN"));
        Assert.Single(await SharedBy(me, owner, "ork B"));
        Assert.Single(await SharedBy(me, owner, "sharer"));
        Assert.Empty(await SharedBy(me, owner, "%"));
        Assert.Empty(await SharedBy(me, owner, kind: AssetKind.Texture));
        Assert.Equal(IBlobStore.Key(owner, orks.Hash), await Shared.BlobKey(me, orks.Id));
        Assert.Null(await Shared.BlobKey(owner, orks.Id));
        Assert.Null(await Shared.BlobKey(me, hidden.Id));

        Assert.Equal([orks.Id], await Assets.SetShared(owner, [orks.Id], false));
        Assert.Empty(await SharedBy(me, owner));
        Assert.Null(await Shared.BlobKey(me, orks.Id));
    }

    [Fact]
    public async Task Copy_makes_the_callers_own_asset_naming_the_sharers_key()
    {
        var (owner, me) = (await NewAccount(), await NewAccount());
        var source = await Uploaded(owner, Mini(name: "Walker") with { Scale = 2, Orientation = new Orientation(90, 180, 0) });
        var terrain = await Uploaded(owner, Mini(name: "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        await Assets.PutVolumes(terrain.Id, [new Volume(VolumeKind.Solid, Footprint.Rect(4, 2), new Point2(1, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, "wall")]);
        await Assets.SetShared(owner, [source.Id, terrain.Id], true);
        var mine = (await Folders.Create(me, new FolderRequest("From others", null)))!;
        var stray = Guid.CreateVersion7();

        var r = await Shared.Copy(me, [source.Id, stray, terrain.Id, source.Id], mine.Id, Plenty);
        Assert.Equal([stray], r.NotFound);
        Assert.Equal(2, r.Copied.Count);
        var copy = (await Assets.Get(me, r.Copied[0]))!;
        Assert.Equal(("Walker", AssetKind.Miniature, source.Hash, true, false, 2.0, new Orientation(90, 180, 0)),
            (copy.Name, copy.Kind, copy.Hash, copy.Uploaded, copy.Shared, copy.Scale, copy.Orientation));
        Assert.Equal(IBlobStore.Key(owner, source.Hash), await Assets.BlobKey(me, copy.Id));
        Assert.Equal([1], (await Assets.GetVolumes(me, r.Copied[1])).Select(v => v.Volume.Footprint.B / 2).ToList());
        Assert.Equal(r.Copied.Order(), (await Folders.List(me)).Single().AssetIds.Order());
        Assert.Equal(new Dictionary<Guid, AssetKind> { [copy.Id] = AssetKind.Miniature }, await Assets.UploadedKinds(me, [copy.Id]));

        Assert.True((await Shared.Copy(me, [source.Id], (await Folders.Create(owner, new FolderRequest("Theirs", null)))!.Id, Plenty)).FolderMissing);
        Assert.Equal([source.Id], (await Shared.Copy(owner, [source.Id], null, Plenty)).NotFound);
    }

    [Fact]
    public async Task Copy_keeps_the_callers_own_uploaded_blob_and_counts_the_quota()
    {
        var (owner, me) = (await NewAccount(), await NewAccount());
        var source = await Uploaded(owner, Mini(size: 300));
        await Assets.SetShared(owner, [source.Id], true);
        var own = await Uploaded(me, Mini(source.Hash, "mine", 300));
        var r = await Shared.Copy(me, [source.Id], null, Plenty);
        Assert.Equal(IBlobStore.Key(me, own.Hash), await Assets.BlobKey(me, r.Copied.Single()));

        var other = await Uploaded(owner, Mini(size: 300));
        await Assets.SetShared(owner, [other.Id], true);
        Assert.Equal(AssetQuotaExceeded.Limit.Assets, (await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Shared.Copy(me, [other.Id], null, new AssetQuota(2, 1L << 30)))).Which);
        Assert.Equal(AssetQuotaExceeded.Limit.Bytes, (await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Shared.Copy(me, [other.Id], null, new AssetQuota(500, 500)))).Which);
        Assert.Equal(2, (await Assets.List(me, new Keyset(200, null), null)).Count);
    }

    [Fact]
    public async Task A_key_a_copy_names_survives_the_sharers_delete_and_goes_with_the_last_row()
    {
        var (owner, me) = (await NewAccount(), await NewAccount());
        var source = await Uploaded(owner, Mini());
        await Assets.SetShared(owner, [source.Id], true);
        var copy = (await Shared.Copy(me, [source.Id], null, Plenty)).Copied.Single();
        var key = IBlobStore.Key(owner, source.Hash);

        Assert.Equal((true, (string?)null), await Assets.Delete(owner, source.Id));
        Assert.Empty(await Assets.Unreferenced([key, "other/key"]).ContinueWith(t => t.Result.Where(k => k == key)));
        Assert.Equal(key, await Assets.BlobKey(me, copy));

        // The sharer uploads the same bytes again and abandons it: the sweep leaves the key the copy names.
        var again = await Assets.Create(owner, Mini(source.Hash));
        await BackdateBlob(owner, again.Hash, TimeSpan.FromHours(2));
        Assert.DoesNotContain(key, await Assets.DeleteAbandoned(DateTimeOffset.UtcNow.AddHours(-1)));

        Assert.Equal((true, key), await Assets.Delete(me, copy));
        Assert.Equal([key], await Assets.Unreferenced([key]));
    }

    [Fact]
    public async Task A_copy_survives_the_sharers_account_delete()
    {
        var (owner, me) = (await NewAccount(), await NewAccount());
        var source = await Uploaded(owner, Mini());
        await Assets.SetShared(owner, [source.Id], true);
        var copy = (await Shared.Copy(me, [source.Id], null, Plenty)).Copied.Single();
        var keys = await Accounts.BlobKeys(owner);
        await Accounts.Delete(owner);
        Assert.Empty(await Assets.Unreferenced(keys));
        Assert.True((await Assets.Get(me, copy))!.Uploaded);
    }
}
