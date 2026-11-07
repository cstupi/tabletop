using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Shared;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests.Contracts;

/// <summary>Shared folders (spec 2026-11-07): browse is the Shared subtree only, a copy is the offers' copy, whole or nothing.</summary>
public abstract partial class RepositoryContract
{
    private static readonly SharedCopyLimits LooseCopies = new(PerDay: 1000, PerCopy: 200, Folders: 200);

    /// <summary>A sharer with a username and a Shared folder holding Orks (a) and Orks/Boyz (b, and a again); a private folder
    /// beside it holding p; an unfiled uploaded asset u; a pending asset filed in Shared.</summary>
    private sealed record Sharing(Guid Sharer, string Username, FolderDto Shared, FolderDto Orks, FolderDto Boyz, FolderDto Private,
        AssetDto A, AssetDto B, AssetDto P, AssetDto U, AssetDto Pending);

    private async Task<Sharing> SharerWithFolders()
    {
        var sharer = await NewAccount("Sharer");
        var username = "sharer" + Guid.NewGuid().ToString("N")[..10];
        Assert.Equal(UsernameResult.Set, await Accounts.SetUsername(sharer, username));
        var shared = await Folders.CreateShared(sharer);
        var orks = (await Folders.Create(sharer, new FolderRequest("Orks", shared.Id)))!;
        var boyz = (await Folders.Create(sharer, new FolderRequest("Boyz", orks.Id)))!;
        var priv = (await Folders.Create(sharer, new FolderRequest("Private", null)))!;
        var (a, b, p, u) = (await Uploaded(sharer, Mini(name: "Warboss")), await Uploaded(sharer, Mini(name: "Boy")), await Uploaded(sharer, Mini(name: "Secret")),
            await Uploaded(sharer, Mini(name: "Loose")));
        var pending = await Assets.Create(sharer, Mini(name: "Half"));
        await Folders.AddAsset(sharer, orks.Id, a.Id);
        await Folders.AddAsset(sharer, boyz.Id, b.Id);
        await Folders.AddAsset(sharer, boyz.Id, a.Id);
        await Folders.AddAsset(sharer, priv.Id, p.Id);
        await Folders.AddAsset(sharer, shared.Id, pending.Id);
        return new Sharing(sharer, username, shared, orks, boyz, priv, a, b, p, u, pending);
    }

    [Fact]
    public async Task Browse_finds_the_username_ignoring_case_and_shows_the_shared_subtree_only()
    {
        var s = await SharerWithFolders();
        var view = (await Shared.Browse(s.Username.ToUpperInvariant()))!;
        Assert.Equal((s.Username, "Sharer"), (view.Username, view.Name));
        Assert.Equal(new[] { s.Shared.Id, s.Orks.Id, s.Boyz.Id }.Order(), view.Folders.Select(f => f.Id).Order());
        Assert.Null(view.Folders.Single(f => f.Id == s.Shared.Id).ParentId);
        Assert.Empty(view.Folders.Single(f => f.Id == s.Shared.Id).AssetIds);                   // the pending upload is not shared
        Assert.Equal(new[] { s.A.Id, s.B.Id }.Order(), view.Folders.Single(f => f.Id == s.Boyz.Id).AssetIds.Order());
        Assert.Equal(new[] { s.A.Id, s.B.Id }.Order(), view.Assets.Select(a => a.Id).Order());
        var warboss = view.Assets.Single(a => a.Id == s.A.Id);
        Assert.Equal(("Warboss", s.A.Hash, Base.Circle(1)), (warboss.Name, warboss.Hash, warboss.ModelledBase));

        Assert.Equal(IBlobStore.Key(s.Sharer, s.A.Hash), await Shared.BlobKey(s.Username, s.A.Id));
        foreach (var hidden in new[] { s.P.Id, s.U.Id, s.Pending.Id, Guid.CreateVersion7() }) Assert.Null(await Shared.BlobKey(s.Username, hidden));

        Assert.Null(await Shared.Browse("nobody" + Guid.NewGuid().ToString("N")[..8]));
        var noFolder = await NewAccount();
        var name = "nofolder" + Guid.NewGuid().ToString("N")[..8];
        await Accounts.SetUsername(noFolder, name);
        Assert.Null(await Shared.Browse(name));
        Assert.Null(await Shared.BlobKey(name, s.A.Id));
    }

    [Fact]
    public async Task Copying_models_files_them_in_the_destination_naming_the_sharers_key_and_reports_the_rest()
    {
        var s = await SharerWithFolders();
        var me = await NewAccount("Copier");
        var into = (await Folders.Create(me, new FolderRequest("From friends", null)))!;
        var terrain = await Uploaded(s.Sharer, Mini(name: "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        await Assets.PutVolumes(terrain.Id, [new Volume(VolumeKind.Solid, Footprint.Rect(4, 2), new Point2(1, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, "wall")]);
        await Folders.AddAsset(s.Sharer, s.Shared.Id, terrain.Id);

        var r = await Shared.Copy(me, s.Username, [s.A.Id, terrain.Id, s.P.Id, s.U.Id], null, into.Id, Plenty, LooseCopies);
        Assert.Equal(SharedCopyRefusal.None, r.Refusal);
        Assert.Equal([s.P.Id, s.U.Id], r.NotFound);
        Assert.Empty(r.Folders);
        Assert.Equal(2, r.Assets.Count);
        var copy = (await Assets.Get(me, r.Assets[0]))!;
        Assert.Equal(("Warboss", s.A.Hash, true), (copy.Name, copy.Hash, copy.Uploaded));
        Assert.Equal(IBlobStore.Key(s.Sharer, s.A.Hash), await Assets.BlobKey(me, copy.Id));
        Assert.Single(await Assets.GetVolumes(me, r.Assets[1]));
        Assert.Equal(r.Assets.Order(), (await Folders.List(me)).Single().AssetIds.Order());

        Assert.Equal(SharedCopyRefusal.NothingToCopy, (await Shared.Copy(me, s.Username, [s.P.Id], null, null, Plenty, LooseCopies)).Refusal);
        Assert.Equal(SharedCopyRefusal.DestinationMissing, (await Shared.Copy(me, s.Username, [s.A.Id], null, s.Private.Id, Plenty, LooseCopies)).Refusal);
        Assert.Equal(SharedCopyRefusal.SharerMissing, (await Shared.Copy(me, "nobody" + Guid.NewGuid().ToString("N")[..8], [s.A.Id], null, null, Plenty, LooseCopies)).Refusal);
        Assert.Equal(SharedCopyRefusal.Own, (await Shared.Copy(s.Sharer, s.Username, [s.A.Id], null, null, Plenty, LooseCopies)).Refusal);
    }

    [Fact]
    public async Task Copying_a_folder_copies_its_tree_with_each_model_filed_where_it_was()
    {
        var s = await SharerWithFolders();
        var me = await NewAccount("Copier");
        var into = (await Folders.Create(me, new FolderRequest("Library", null)))!;

        var r = await Shared.Copy(me, s.Username, null, s.Orks.Id, into.Id, Plenty, LooseCopies);
        Assert.Equal(SharedCopyRefusal.None, r.Refusal);
        Assert.Equal(2, r.Assets.Count);                                                         // a is filed twice, copied once
        Assert.Equal(2, r.Folders.Count);
        var mine = (await Folders.List(me)).ToDictionary(f => f.Id);
        var orks = mine[r.Folders[0]];
        var boyz = mine.Values.Single(f => f.ParentId == orks.Id);
        Assert.Equal(("Orks", (Guid?)into.Id, false), (orks.Name, orks.ParentId, orks.Shared));
        Assert.Equal("Boyz", boyz.Name);
        var names = new Dictionary<Guid, string>();
        foreach (var id in r.Assets) names[id] = (await Assets.Get(me, id))!.Name;
        Assert.Equal(["Warboss"], orks.AssetIds.Select(id => names[id]));
        Assert.Equal(["Boy", "Warboss"], boyz.AssetIds.Select(id => names[id]).Order());

        // The whole Shared folder, to the top level; the pending upload is left out.
        var all = await Shared.Copy(me, s.Username, null, s.Shared.Id, null, Plenty, LooseCopies);
        Assert.Equal((2, 3), (all.Assets.Count, all.Folders.Count));
        Assert.Null((await Folders.List(me)).Single(f => f.Id == all.Folders[0]).ParentId);

        Assert.Equal(SharedCopyRefusal.FolderMissing, (await Shared.Copy(me, s.Username, null, s.Private.Id, null, Plenty, LooseCopies)).Refusal);
        var empty = (await Folders.Create(s.Sharer, new FolderRequest("Empty", s.Shared.Id)))!;
        Assert.Equal(SharedCopyRefusal.NothingToCopy, (await Shared.Copy(me, s.Username, null, empty.Id, null, Plenty, LooseCopies)).Refusal);
    }

    [Fact]
    public async Task A_copy_is_whole_or_nothing_under_the_quota_and_the_caps()
    {
        var s = await SharerWithFolders();
        var me = await NewAccount("Copier");
        await Uploaded(me, Mini(name: "Mine"));
        async Task Nothing()
        {
            Assert.Single(await Assets.List(me, All, null));
            Assert.Empty(await Folders.List(me));
        }

        await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Shared.Copy(me, s.Username, null, s.Orks.Id, null, new AssetQuota(2, 1L << 30), LooseCopies));
        await Nothing();
        var bytes = await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Shared.Copy(me, s.Username, null, s.Orks.Id, null, new AssetQuota(500, 250), LooseCopies));
        Assert.Equal(AssetQuotaExceeded.Limit.Bytes, bytes.Which);
        await Nothing();
        Assert.Equal(SharedCopyRefusal.TooMany, (await Shared.Copy(me, s.Username, null, s.Orks.Id, null, Plenty, LooseCopies with { PerCopy = 1 })).Refusal);
        Assert.Equal(SharedCopyRefusal.FolderCap, (await Shared.Copy(me, s.Username, null, s.Orks.Id, null, Plenty, LooseCopies with { Folders = 1 })).Refusal);
        await Nothing();

        var daily = LooseCopies with { PerDay = 2 };
        Assert.Equal(SharedCopyRefusal.None, (await Shared.Copy(me, s.Username, [s.A.Id], null, null, Plenty, daily)).Refusal);
        Assert.Equal(SharedCopyRefusal.None, (await Shared.Copy(me, s.Username, [s.B.Id], null, null, Plenty, daily)).Refusal);
        var over = await Shared.Copy(me, s.Username, [s.A.Id], null, null, Plenty, daily);
        Assert.Equal(SharedCopyRefusal.DailyCap, over.Refusal);
        Assert.InRange(over.RetryAfter!.Value, TimeSpan.FromHours(23), TimeSpan.FromHours(24));
        Assert.Equal(3, (await Assets.List(me, All, null)).Count);
    }

    [Fact]
    public async Task A_copy_outlives_the_sharers_delete_and_its_key_goes_with_the_last_row()
    {
        var s = await SharerWithFolders();
        var me = await NewAccount("Copier");
        var copy = (await Shared.Copy(me, s.Username, [s.B.Id], null, null, Plenty, LooseCopies)).Assets.Single();
        var key = IBlobStore.Key(s.Sharer, s.B.Hash);

        Assert.Equal((true, (string?)null), await Assets.Delete(s.Sharer, s.B.Id));
        Assert.Null(await Shared.BlobKey(s.Username, s.B.Id));
        Assert.Equal(key, await Assets.BlobKey(me, copy));
        Assert.True(await Folders.Delete(s.Sharer, s.Shared.Id));
        Assert.Null(await Shared.Browse(s.Username));
        Assert.True((await Assets.Get(me, copy))!.Uploaded);

        Assert.Equal((true, key), await Assets.Delete(me, copy));
        Assert.Equal([key], await Assets.Unreferenced([key]));
    }

    [Fact]
    public async Task A_pending_asset_of_a_kind_the_sources_bytes_do_not_fit_refuses_the_copy()
    {
        var s = await SharerWithFolders();
        var me = await NewAccount("Copier");
        await Assets.Create(me, Texture() with { Hash = s.A.Hash });
        Assert.Equal(SharedCopyRefusal.PendingConflict, (await Shared.Copy(me, s.Username, [s.A.Id], null, null, Plenty, LooseCopies)).Refusal);
        Assert.Single(await Assets.List(me, All, null));
    }
}
