using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    [Fact]
    public async Task Account_insert_is_a_no_op_on_an_existing_id_and_rename_reports_a_missing_one()
    {
        var id = await NewAccount("First");
        await Accounts.Insert(id, "Second");
        Assert.Equal(new AccountRow(id, "First"), await Accounts.Get(id));
        Assert.True(await Accounts.Rename(id, "Renamed"));
        Assert.Equal("Renamed", (await Accounts.Get(id))!.Name);
        Assert.False(await Accounts.Rename(Guid.CreateVersion7(), "Nobody"));
        Assert.Null(await Accounts.Get(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Account_game_ids_are_the_games_it_plays_in_and_the_games_it_created_and_left()
    {
        var hash = await NewProperties();
        var (me, other) = (await NewAccount("Me"), await NewAccount("Other"));
        var mine = (await Games.Create(me, GameRequest(hash)))!.Id;
        var left = (await Games.Create(me, GameRequest(hash)))!.Id;
        await Games.Join(left, other);
        Assert.True(await Games.Leave(left, me));
        var joined = (await Games.Create(other, GameRequest(hash)))!.Id;
        await Games.Join(joined, me);
        var untouched = (await Games.Create(other, GameRequest(hash)))!.Id;

        var ids = await Accounts.GameIds(me);
        Assert.Equal(new HashSet<Guid> { mine, left, joined }, ids.ToHashSet());
        Assert.DoesNotContain(untouched, ids);
    }

    [Fact]
    public async Task Account_delete_cascades_its_content_and_participant_rows_and_leaves_others_alone()
    {
        var hash = await NewProperties();
        var (me, other) = (await NewAccount("Me"), await NewAccount("Other"));
        var asset = await Assets.Create(me, Mini());
        var folder = (await Folders.Create(me, new FolderRequest("F", null)))!;
        await Folders.AddAsset(me, folder.Id, asset.Id);
        var army = SmallArmy();
        await Armies.Insert(me, army);
        var layout = await Layouts.Save(me, SmallLayout());
        var theirs = (await Games.Create(other, GameRequest(hash)))!.Id;
        await Games.Join(theirs, me);
        var keys = await Accounts.BlobKeys(me);
        var theirAsset = await Assets.Create(other, Mini());

        await Accounts.Delete(me);

        Assert.Equal([IBlobStore.Key(me, asset.Hash)], keys);
        Assert.Null(await Accounts.Get(me));
        Assert.Null(await Assets.Get(me, asset.Id));
        Assert.Empty(await Assets.List(me, Keyset.Parse(null, null), null));
        Assert.Empty(await Accounts.BlobKeys(me));
        Assert.Empty(await Folders.List(me));
        Assert.Null(await Armies.Get(me, army.Id));
        Assert.Null(await Layouts.Get(me, layout.Id));
        Assert.Equal([other], (await Games.Get(theirs))!.Participants.Select(p => p.AccountId));
        Assert.NotNull(await Accounts.Get(other));
        Assert.NotNull(await Assets.Get(other, theirAsset.Id));
    }

    [Fact]
    public async Task Account_delete_is_refused_while_a_game_names_it_as_creator()
    {
        var hash = await NewProperties();
        var me = await NewAccount("Me");
        var game = (await Games.Create(me, GameRequest(hash)))!.Id;
        await Assert.ThrowsAnyAsync<Exception>(() => Accounts.Delete(me));
        Assert.NotNull(await Accounts.Get(me));
        Assert.True(await Games.Delete(game));
        await Accounts.Delete(me);
        Assert.Null(await Accounts.Get(me));
    }
}
