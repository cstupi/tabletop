using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Offers;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    private static readonly AssetQuota Plenty = new(500, 1L << 30);
    private static readonly OfferLimits Loose = new(PerDay: 1000, OpenPerRecipient: 1000);
    private static readonly Keyset All = new(200, null);

    private async Task<AssetDto> Uploaded(Guid owner, CreateAssetRequest r)
    {
        var a = await Assets.Create(owner, r);
        await Assets.MarkUploaded(owner, a.Hash);
        return (await Assets.Get(owner, a.Id))!;
    }

    /// <summary>Puts the accounts in one new game together.</summary>
    private async Task<Guid> PlayTogether(string name, Guid creator, params Guid[] others)
    {
        var game = (await Games.Create(creator, GameRequest(await NewProperties(), name)))!;
        foreach (var o in others) await Games.Join(game.Id, o);
        return game.Id;
    }

    /// <summary>A sharer and a recipient who have played, and a third account who has played neither.</summary>
    private async Task<(Guid Sharer, Guid Recipient, Guid Stranger)> Trio()
    {
        var (sharer, recipient, stranger) = (await NewAccount("Sharer"), await NewAccount("Recipient"), await NewAccount("Stranger"));
        await PlayTogether("Friday", sharer, recipient);
        return (sharer, recipient, stranger);
    }

    private async Task<Guid> Offer(Guid sharer, Guid recipient, params Guid[] assets) =>
        (await Offers.Create(sharer, [recipient], assets, null, Loose)).OfferIds.Single();

    [Fact]
    public async Task Players_are_the_accounts_the_caller_has_been_in_a_game_with_and_the_games_tell_them_apart()
    {
        var me = await NewAccount("Me");
        var (twin1, twin2, stranger) = (await NewAccount("Twin"), await NewAccount("Twin"), await NewAccount("Stranger"));
        await PlayTogether("Old", me, twin1);
        await PlayTogether("Newer", twin1, me);
        await PlayTogether("Other", twin2, me);
        await PlayTogether("Theirs", twin2, stranger);

        var players = await Offers.Players(me);
        Assert.Equal([twin2, twin1], players.Select(p => p.AccountId));                    // same name: the latest game first
        Assert.Equal(["Newer", "Old"], players.Single(p => p.AccountId == twin1).Games);
        Assert.Equal(["Other"], players.Single(p => p.AccountId == twin2).Games);
        Assert.DoesNotContain(players, p => p.AccountId == stranger || p.AccountId == me);
        Assert.Empty(await Offers.Players(await NewAccount()));
    }

    [Fact]
    public async Task An_offer_names_the_sharers_uploaded_assets_and_only_a_player_they_have_been_in_a_game_with()
    {
        var (sharer, recipient, stranger) = await Trio();
        var mesh = await Uploaded(sharer, Mini(name: "Walker"));
        var pending = await Assets.Create(sharer, Mini(name: "Pending"));
        var theirs = await Uploaded(recipient, Mini(name: "Not the sharer's"));

        Assert.Equal(OfferRefusal.RecipientMissing, (await Offers.Create(sharer, [stranger], [mesh.Id], null, Loose)).Refusal);
        Assert.Equal(OfferRefusal.RecipientMissing, (await Offers.Create(sharer, [recipient, sharer], [mesh.Id], null, Loose)).Refusal);
        Assert.Equal(OfferRefusal.NothingToOffer, (await Offers.Create(sharer, [recipient], [pending.Id, theirs.Id], null, Loose)).Refusal);
        var made = await Offers.Create(sharer, [recipient], [mesh.Id, pending.Id, theirs.Id], null, Loose);
        Assert.Equal([pending.Id, theirs.Id], made.NotFound);

        var offer = (await Offers.Get(sharer, made.OfferIds.Single()))!;
        Assert.Equal((OfferState.Open, "Sharer", "Recipient", (string?)null), (offer.State, offer.SharerName, offer.RecipientName, offer.Folder));
        var item = Assert.Single(offer.Assets);
        Assert.Equal((mesh.Id, "Walker", mesh.Hash, Base.Circle(1)), (item.Id, item.Name, item.Hash, item.ModelledBase));
        Assert.Empty(await Offers.Sent(recipient, All));
        Assert.Equal([offer.Id], (await Offers.Sent(sharer, All)).Select(o => o.Id));
    }

    [Fact]
    public async Task A_folder_offer_is_its_assets_and_its_sub_folders_at_that_moment()
    {
        var (sharer, recipient, _) = await Trio();
        var top = (await Folders.Create(sharer, new FolderRequest("Orks", null)))!;
        var sub = (await Folders.Create(sharer, new FolderRequest("Boyz", top.Id)))!;
        var (a, b, later) = (await Uploaded(sharer, Mini(name: "a")), await Uploaded(sharer, Mini(name: "b")), await Uploaded(sharer, Mini(name: "later")));
        await Folders.AddAsset(sharer, top.Id, a.Id);
        await Folders.AddAsset(sharer, sub.Id, b.Id);
        await Folders.AddAsset(sharer, sub.Id, a.Id);

        var made = await Offers.Create(sharer, [recipient], null, top.Id, Loose);
        await Folders.AddAsset(sharer, top.Id, later.Id);
        var offer = (await Offers.Get(recipient, made.OfferIds.Single()))!;
        Assert.Equal("Orks", offer.Folder);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), offer.Assets.Select(x => x.Id).Order());

        Assert.Equal(OfferRefusal.FolderMissing, (await Offers.Create(recipient, [sharer], null, top.Id, Loose)).Refusal);
        var empty = (await Folders.Create(sharer, new FolderRequest("Empty", null)))!;
        Assert.Equal(OfferRefusal.NothingToOffer, (await Offers.Create(sharer, [recipient], null, empty.Id, Loose)).Refusal);
    }

    [Fact]
    public async Task Only_the_recipient_sees_previews_and_downloads_an_open_offer_and_the_sharer_sees_its_state()
    {
        var (sharer, recipient, stranger) = await Trio();
        var mesh = await Uploaded(sharer, Mini());
        var id = await Offer(sharer, recipient, mesh.Id);

        Assert.Equal([id], (await Offers.Received(recipient, All)).Select(o => o.Id));
        Assert.Equal(1, await Offers.OpenCount(recipient));
        Assert.NotNull(await Offers.Get(recipient, id));
        Assert.Equal(IBlobStore.Key(sharer, mesh.Hash), await Offers.BlobKey(recipient, id, mesh.Id));

        Assert.Null(await Offers.Get(stranger, id));
        Assert.Null(await Offers.BlobKey(stranger, id, mesh.Id));
        Assert.Null(await Offers.BlobKey(sharer, id, mesh.Id));
        Assert.Empty(await Offers.Received(stranger, All));
        Assert.Empty(await Offers.Received(sharer, All));
        var other = await Uploaded(sharer, Mini());
        Assert.Null(await Offers.BlobKey(recipient, id, other.Id));               // not in this offer
        Assert.True((await Offers.Accept(stranger, id, null, Plenty)).OfferMissing);
        Assert.False(await Offers.Decline(stranger, id, false));
        Assert.Equal(WithdrawResult.NotFound, await Offers.Withdraw(stranger, id));
        Assert.Equal(WithdrawResult.NotFound, await Offers.Withdraw(recipient, id));
    }

    [Fact]
    public async Task Accept_copies_into_the_recipients_library_naming_the_sharers_key_and_closes_the_offer()
    {
        var (sharer, recipient, _) = await Trio();
        var source = await Uploaded(sharer, Mini(name: "Walker") with { Scale = 2, Orientation = new Orientation(90, 180, 0) });
        var terrain = await Uploaded(sharer, Mini(name: "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        await Assets.PutVolumes(terrain.Id, [new Volume(VolumeKind.Solid, Footprint.Rect(4, 2), new Point2(1, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, "wall")]);
        var id = await Offer(sharer, recipient, source.Id, terrain.Id);
        var into = (await Folders.Create(recipient, new FolderRequest("From others", null)))!;

        Assert.True((await Offers.Accept(recipient, id, (await Folders.Create(sharer, new FolderRequest("Theirs", null)))!.Id, Plenty)).FolderMissing);
        var r = await Offers.Accept(recipient, id, into.Id, Plenty);
        Assert.Equal(2, r.Copied.Count);
        var copies = await Task.WhenAll(r.Copied.Select(c => Assets.Get(recipient, c)));
        var copy = copies.Single(c => c!.Name == "Walker")!;
        Assert.Equal((AssetKind.Miniature, source.Hash, true, 2.0, new Orientation(90, 180, 0)), (copy.Kind, copy.Hash, copy.Uploaded, copy.Scale, copy.Orientation));
        Assert.Equal(IBlobStore.Key(sharer, source.Hash), await Assets.BlobKey(recipient, copy.Id));
        Assert.Equal([1], (await Assets.GetVolumes(recipient, copies.Single(c => c!.Name == "Ruin")!.Id)).Select(v => v.Volume.Footprint.B / 2).ToList());
        Assert.Equal(r.Copied.Order(), (await Folders.List(recipient)).Single().AssetIds.Order());

        var after = (await Offers.Get(sharer, id))!;
        Assert.Equal(OfferState.Accepted, after.State);
        Assert.NotNull(after.DecidedAt);
        Assert.Null(await Offers.Get(recipient, id));
        Assert.Null(await Offers.BlobKey(recipient, id, source.Id));
        Assert.Equal(0, await Offers.OpenCount(recipient));
        Assert.True((await Offers.Accept(recipient, id, null, Plenty)).OfferMissing);
        Assert.Equal(WithdrawResult.NotOpen, await Offers.Withdraw(sharer, id));
    }

    [Fact]
    public async Task Decline_writes_nothing_and_block_declines_the_rest_and_hides_new_offers_until_unblocked()
    {
        var (sharer, recipient, _) = await Trio();
        var mesh = await Uploaded(sharer, Mini());
        var (first, second, third) = (await Offer(sharer, recipient, mesh.Id), await Offer(sharer, recipient, mesh.Id), await Offer(sharer, recipient, mesh.Id));

        Assert.True(await Offers.Decline(recipient, first, false));
        Assert.Equal(OfferState.Declined, (await Offers.Get(sharer, first))!.State);
        Assert.Empty(await Assets.List(recipient, All, null));
        Assert.False(await Offers.Decline(recipient, first, false));
        Assert.Equal(2, await Offers.OpenCount(recipient));

        Assert.True(await Offers.Decline(recipient, second, true));
        Assert.Equal(OfferState.Declined, (await Offers.Get(sharer, third))!.State);
        Assert.Equal([new BlockedDto(sharer, "Sharer")], await Offers.Blocked(recipient));
        var hidden = await Offer(sharer, recipient, mesh.Id);
        Assert.Equal(OfferState.Open, (await Offers.Get(sharer, hidden))!.State);
        Assert.Null(await Offers.Get(recipient, hidden));
        Assert.Equal(0, await Offers.OpenCount(recipient));
        Assert.True((await Offers.Accept(recipient, hidden, null, Plenty)).OfferMissing);

        Assert.False(await Offers.Unblock(sharer, recipient));
        Assert.True(await Offers.Unblock(recipient, sharer));
        Assert.Empty(await Offers.Blocked(recipient));
        Assert.Equal([hidden], (await Offers.Received(recipient, All)).Select(o => o.Id));
    }

    [Fact]
    public async Task Withdraw_closes_an_open_offer_to_the_recipient()
    {
        var (sharer, recipient, _) = await Trio();
        var mesh = await Uploaded(sharer, Mini());
        var id = await Offer(sharer, recipient, mesh.Id);
        Assert.Equal(WithdrawResult.Withdrawn, await Offers.Withdraw(sharer, id));
        Assert.Equal(OfferState.Withdrawn, (await Offers.Get(sharer, id))!.State);
        Assert.Null(await Offers.Get(recipient, id));
        Assert.Null(await Offers.BlobKey(recipient, id, mesh.Id));
        Assert.True((await Offers.Accept(recipient, id, null, Plenty)).OfferMissing);
        Assert.Equal(WithdrawResult.NotOpen, await Offers.Withdraw(sharer, id));
    }

    [Fact]
    public async Task The_daily_cap_counts_the_sharers_offers_and_the_open_cap_the_recipients_visible_ones()
    {
        var (sharer, recipient, _) = await Trio();
        var other = await NewAccount("Other");
        await PlayTogether("Saturday", other, recipient, sharer);
        var mesh = await Uploaded(sharer, Mini());

        var daily = new OfferLimits(PerDay: 3, OpenPerRecipient: 1000);
        Assert.Equal(2, (await Offers.Create(sharer, [recipient, other], [mesh.Id], null, daily)).OfferIds.Count);
        var over = await Offers.Create(sharer, [recipient, other], [mesh.Id], null, daily);
        Assert.Equal(OfferRefusal.DailyCap, over.Refusal);
        Assert.InRange(over.RetryAfter!.Value, TimeSpan.FromHours(23), TimeSpan.FromHours(24));
        Assert.Single((await Offers.Create(sharer, [recipient], [mesh.Id], null, daily)).OfferIds);

        var otherMesh = await Uploaded(other, Mini());
        var open = new OfferLimits(PerDay: 1000, OpenPerRecipient: 3);   // the recipient holds 2 open offers from the sharer
        Assert.Single((await Offers.Create(other, [recipient], [otherMesh.Id], null, open)).OfferIds);
        var full = await Offers.Create(other, [recipient], [otherMesh.Id], null, open);
        Assert.Equal((OfferRefusal.RecipientFull, "Recipient"), (full.Refusal, full.FullRecipient));
        // Blocking the sharer hides their offers, which frees the recipient's slots.
        Assert.True(await Offers.Decline(recipient, (await Offers.Received(recipient, All)).First(o => o.SharerId == sharer).Id, true));
        Assert.Single((await Offers.Create(other, [recipient], [otherMesh.Id], null, open)).OfferIds);
    }

    [Fact]
    public async Task An_asset_the_sharer_deletes_leaves_the_offer_and_an_empty_offer_is_not_the_recipients()
    {
        var (sharer, recipient, _) = await Trio();
        var (kept, gone) = (await Uploaded(sharer, Mini(name: "kept")), await Uploaded(sharer, Mini(name: "gone")));
        var id = await Offer(sharer, recipient, kept.Id, gone.Id);
        await Assets.Delete(sharer, gone.Id);
        Assert.Equal([kept.Id], (await Offers.Get(recipient, id))!.Assets.Select(a => a.Id));
        await Assets.Delete(sharer, kept.Id);
        Assert.Null(await Offers.Get(recipient, id));
        Assert.Equal(0, await Offers.OpenCount(recipient));
        Assert.Empty((await Offers.Get(sharer, id))!.Assets);
    }

    [Fact]
    public async Task Accept_keeps_the_recipients_own_uploaded_blob_and_counts_the_quota()
    {
        var (sharer, me, _) = await Trio();
        var source = await Uploaded(sharer, Mini(size: 300));
        var own = await Uploaded(me, Mini(source.Hash, "mine", 300));
        var r = await Offers.Accept(me, await Offer(sharer, me, source.Id), null, Plenty);
        Assert.Equal(IBlobStore.Key(me, own.Hash), await Assets.BlobKey(me, r.Copied.Single()));

        var other = await Uploaded(sharer, Mini(size: 300));
        var id = await Offer(sharer, me, other.Id);
        Assert.Equal(AssetQuotaExceeded.Limit.Assets, (await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Offers.Accept(me, id, null, new AssetQuota(2, 1L << 30)))).Which);
        Assert.Equal(AssetQuotaExceeded.Limit.Bytes, (await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Offers.Accept(me, id, null, new AssetQuota(500, 500)))).Which);
        Assert.Equal(2, (await Assets.List(me, All, null)).Count);
        Assert.Equal(OfferState.Open, (await Offers.Get(sharer, id))!.State);           // a refusal leaves the offer open
    }

    [Fact]
    public async Task A_pending_row_on_the_hash_takes_the_sources_size_and_type_and_is_charged_for_it()
    {
        var (sharer, me, _) = await Trio();
        var source = await Uploaded(sharer, Mini(size: 1000));
        var pending = await Assets.Create(me, Mini(source.Hash, "declared small", 1));
        var id = await Offer(sharer, me, source.Id);

        Assert.Equal(AssetQuotaExceeded.Limit.Bytes, (await Assert.ThrowsAsync<AssetQuotaExceeded>(() => Offers.Accept(me, id, null, new AssetQuota(500, 999)))).Which);
        var copy = (await Offers.Accept(me, id, null, new AssetQuota(500, 1000))).Copied.Single();
        var mine = (await Assets.Get(me, pending.Id))!;
        Assert.Equal((true, 1000L, "model/gltf-binary"), (mine.Uploaded, mine.ByteSize, mine.ContentType));
        Assert.Equal(IBlobStore.Key(sharer, source.Hash), await Assets.BlobKey(me, copy));
    }

    [Fact]
    public async Task A_pending_asset_of_a_kind_the_sources_bytes_do_not_fit_refuses_the_accept()
    {
        var (sharer, me, _) = await Trio();
        var source = await Uploaded(sharer, Mini());
        var pending = await Assets.Create(me, Texture() with { Hash = source.Hash });
        var id = await Offer(sharer, me, source.Id);

        Assert.True((await Offers.Accept(me, id, null, Plenty)).PendingConflict);
        var still = (await Assets.Get(me, pending.Id))!;
        Assert.Equal((false, "image/png"), (still.Uploaded, still.ContentType));
        Assert.Single(await Assets.List(me, All, null));
        Assert.Equal(OfferState.Open, (await Offers.Get(sharer, id))!.State);
    }

    [Fact]
    public async Task A_key_a_copy_names_survives_the_sharers_delete_and_goes_with_the_last_row()
    {
        var (sharer, me, _) = await Trio();
        var source = await Uploaded(sharer, Mini());
        var copy = (await Offers.Accept(me, await Offer(sharer, me, source.Id), null, Plenty)).Copied.Single();
        var key = IBlobStore.Key(sharer, source.Hash);

        Assert.Equal((true, (string?)null), await Assets.Delete(sharer, source.Id));
        Assert.Empty(await Assets.Unreferenced([key, "other/key"]).ContinueWith(t => t.Result.Where(k => k == key)));
        Assert.Equal(key, await Assets.BlobKey(me, copy));

        // The sharer uploads the same bytes again and abandons it: the sweep leaves the key the copy names.
        var again = await Assets.Create(sharer, Mini(source.Hash));
        await BackdateBlob(sharer, again.Hash, TimeSpan.FromHours(2));
        Assert.DoesNotContain(key, await Assets.DeleteAbandoned(DateTimeOffset.UtcNow.AddHours(-1)));

        Assert.Equal((true, key), await Assets.Delete(me, copy));
        Assert.Equal([key], await Assets.Unreferenced([key]));
    }

    [Fact]
    public async Task A_copy_survives_the_sharers_account_delete_and_the_offers_go_with_it()
    {
        var (sharer, me, _) = await Trio();
        var (source, more) = (await Uploaded(sharer, Mini()), await Uploaded(sharer, Mini()));
        var copy = (await Offers.Accept(me, await Offer(sharer, me, source.Id), null, Plenty)).Copied.Single();
        var open = await Offer(sharer, me, more.Id);
        await Offers.Decline(me, open, true);
        foreach (var g in await Games.List(new Keyset(200, null), sharer, joinedOnly: true)) await Games.Delete(g.Id);
        var keys = await Accounts.BlobKeys(sharer);
        await Accounts.Delete(sharer);
        Assert.Empty(await Assets.Unreferenced(keys).ContinueWith(t => t.Result.Where(k => k == IBlobStore.Key(sharer, source.Hash))));
        Assert.True((await Assets.Get(me, copy))!.Uploaded);
        Assert.Null(await Offers.Get(me, open));
        Assert.Empty(await Offers.Blocked(me));
        Assert.Equal(0, await Offers.OpenCount(me));
    }
}
