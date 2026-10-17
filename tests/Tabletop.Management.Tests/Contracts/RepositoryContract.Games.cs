using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    [Fact]
    public async Task Game_create_needs_a_known_set_and_seats_the_creator()
    {
        var hash = await NewProperties("age-of-fantasy");
        var creator = await NewAccount("Clay");
        Assert.Null(await Games.Create(creator, GameRequest("sha256:contract-unknown")));
        var game = (await Games.Create(creator, GameRequest(hash, "Friday") with { SchedulerShape = SchedulerShape.AlternatingActivation, Enforcement = Enforcement.Strict, WaitsBetweenSteps = true }))!;
        Assert.Equal(("Friday", GameStatus.Lobby, creator, "age-of-fantasy", hash, SchedulerShape.AlternatingActivation, Enforcement.Strict, true),
            (game.Name, game.Status, game.CreatedBy, game.GameRules, game.PropertiesHash, game.SchedulerShape, game.Enforcement, game.WaitsBetweenSteps));
        Assert.Equal([(creator, "Clay", (Guid?)null, (Guid?)null)], game.Participants.Select(p => (p.AccountId, p.Name, p.TeamId, p.ArmyId)));
        Assert.Empty(game.Teams);
        Assert.Equal(game.RngSeed, (await Games.Get(game.Id))!.RngSeed);
        Assert.Empty((await Games.Get(game.Id))!.OptionalRules);                                           // the column default: the core game
        Assert.NotEqual(game.RngSeed, (await Games.Create(creator, GameRequest(hash)))!.RngSeed);
        Assert.True(await Games.IsCreator(game.Id, creator));
        Assert.True(await Games.IsParticipant(game.Id, creator));
    }

    [Fact]
    public async Task Game_list_pages_descending_by_id_before_the_cursor_with_participant_names_in_join_order()
    {
        var hash = await NewProperties();
        var (a, b) = (await NewAccount("A"), await NewAccount("B"));
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add((await Games.Create(a, GameRequest(hash, $"g{i}")))!.Id);
        ids.Sort();
        await Games.Join(ids[0], b);

        var newest = await Games.List(new Keyset(2, null));
        Assert.Equal([ids[2], ids[1]], newest.Select(g => g.Id));
        var older = await Games.List(new Keyset(2, ids[1]));
        Assert.Equal(ids[0], older[0].Id);
        var summary = (await Games.List(new Keyset(1, ids[1])))[0];
        Assert.Equal((2, GameStatus.Lobby, a), (summary.Participants, summary.Status, summary.CreatedBy));
        Assert.Equal(["A", "B"], summary.ParticipantNames);
    }

    [Fact]
    public async Task Game_join_is_one_row_at_any_status_and_the_last_leave_ends_the_game()
    {
        var hash = await NewProperties();
        var (a, b) = (await NewAccount("A"), await NewAccount("B"));
        var game = (await Games.Create(a, GameRequest(hash)))!.Id;
        Assert.Null(await Games.Join(Guid.CreateVersion7(), b));
        Assert.True(await Games.End(game));
        Assert.Equal(GameStatus.Ended, (await Games.Join(game, b))!.Status);
        Assert.Equal(2, (await Games.Join(game, b))!.Participants.Count);

        Assert.True(await Games.Leave(game, a));
        Assert.False(await Games.Leave(game, a));
        Assert.False(await Games.Leave(Guid.CreateVersion7(), a));
        Assert.True(await Games.IsCreator(game, a));
        Assert.False(await Games.IsParticipant(game, a));
        Assert.False(await Games.IsCreator(game, b));
        Assert.True(await Games.Leave(game, b));
        Assert.Empty((await Games.Get(game))!.Participants);
    }

    [Fact]
    public async Task Game_last_leave_ends_a_lobby_game_under_concurrent_leaves()
    {
        var hash = await NewProperties();
        var (a, b) = (await NewAccount(), await NewAccount());
        var game = (await Games.Create(a, GameRequest(hash)))!.Id;
        await Games.Join(game, b);
        await Task.WhenAll(Games.Leave(game, a), Games.Leave(game, b));
        Assert.Equal(GameStatus.Ended, (await Games.Get(game))!.Status);
        var other = (await Games.Create(a, GameRequest(hash)))!.Id;
        await Games.Join(other, b);
        await Games.Leave(other, b);
        Assert.Equal(GameStatus.Lobby, (await Games.Get(other))!.Status);
    }

    [Fact]
    public async Task Game_delete_takes_the_participants_and_the_log()
    {
        var hash = await NewProperties();
        var a = await NewAccount();
        var game = (await Games.Create(a, GameRequest(hash)))!.Id;
        await SeedGameEvent(game, 1);
        await SeedGameEvent(game, 2);
        Assert.False(await Games.End(Guid.CreateVersion7()));
        Assert.True(await Games.Delete(game));
        Assert.Null(await Games.Get(game));
        Assert.Equal(0, await GameEventCount(game));
        Assert.False(await Games.Delete(game));
        Assert.Empty(await Accounts.HostedGameIds(a));
        Assert.Empty(await Accounts.JoinedGameIds(a));
    }

    [Fact]
    public async Task Properties_list_the_most_recently_seeded_set_per_rules_and_name()
    {
        var name = "Contract listing " + Guid.NewGuid().ToString("N")[..6];
        var (older, newer, aof) = ("sha256:contract-old-" + name, "sha256:contract-new-" + name, "sha256:contract-aof-" + name);
        await SeedProperties(older, name, "grimdark-future", DateTimeOffset.UtcNow.AddDays(-1));
        await SeedProperties(newer, name, "grimdark-future", DateTimeOffset.UtcNow);
        await SeedProperties(aof, name, "age-of-fantasy", DateTimeOffset.UtcNow.AddDays(-2));
        var listed = (await Games.ListProperties()).Where(p => p.Name == name).ToList();
        Assert.Equal([new PropertiesSummary(aof, name, "age-of-fantasy"), new PropertiesSummary(newer, name, "grimdark-future")], listed);
        Assert.NotNull(await Games.Create(await NewAccount(), GameRequest(older)));
    }

    [Fact]
    public async Task Rule_texts_are_a_sets_entries_by_name_and_catalog_kinds_read_its_unit_and_model_entries()
    {
        var rules = "contract-rules-" + Guid.NewGuid().ToString("N")[..6];
        var hash = await NewProperties(rules);
        var bare = await NewProperties();
        await SeedCatalogEntry(hash, "Tough", "model", true, "Must take X wounds.", true);
        await SeedCatalogEntry(hash, "AP", "weapon", true, "-X to Defense.", true);
        await SeedCatalogEntry(hash, "Fearless", "unit", false, "+1 to morale.", true);
        await SeedCatalogEntry(hash, "Precise", "model", false, "+1 to hit.", false);
        Assert.Equal([
            new CatalogRuleText("AP", "weapon", true, "-X to Defense.", true),
            new CatalogRuleText("Fearless", "unit", false, "+1 to morale.", true),
            new CatalogRuleText("Precise", "model", false, "+1 to hit.", false),
            new CatalogRuleText("Tough", "model", true, "Must take X wounds.", true),
        ], await Games.RuleTexts(hash));
        Assert.Empty((await Games.RuleTexts(bare))!);
        Assert.Null(await Games.RuleTexts("sha256:contract-unknown"));
        var kinds = await Kinds.For(rules);
        Assert.Equal(new Dictionary<string, PropertyLevel> { ["Tough"] = PropertyLevel.Model, ["Precise"] = PropertyLevel.Model, ["Fearless"] = PropertyLevel.Unit }, kinds);
        Assert.Empty(await Kinds.For("contract-no-such-rules"));
    }

    [Fact]
    public async Task Manifest_is_every_uploaded_asset_in_play_whoever_owns_it()
    {
        var hash = await NewProperties();
        var (a, b) = (await NewAccount("A"), await NewAccount("B"));
        var game = (await Games.Create(a, GameRequest(hash)))!.Id;
        await Games.Join(game, b);
        async Task<AssetDto> Uploaded(Guid owner, Content.Assets.CreateAssetRequest r) { var x = await Assets.Create(owner, r); await Assets.MarkUploaded(owner, x.Hash); return (await Assets.Get(owner, x.Id))!; }
        var aMesh = await Uploaded(a, Mini(name: "a-mesh"));
        var bMesh = await Uploaded(b, Mini(name: "b-mesh"));
        var texture = await Uploaded(b, Texture());
        var pending = await Assets.Create(a, Mini(name: "pending"));
        await Assets.Patch(b, bMesh.Id, new PatchAssetRequest(null, 2.5, new Tabletop.Geometry.Orientation(90, 0, 0), null));

        var aArmy = SmallArmy();
        await Armies.Insert(a, aArmy);
        await Armies.Patch(a, aArmy.Id, new PatchArmyRequest(null, [new UnitPatch(aArmy.Units[0].Id, aMesh.Id)], [new SlotPatch(aArmy.Units[0].Models[1].Id, pending.Id, false, null, null)]));
        await PickArmy(game, a, aArmy.Id);
        var bArmy = SmallArmy();
        var frozen = ArmySnapshot.Of(bArmy with { Units = [bArmy.Units[1] with { Models = [bArmy.Units[1].Models[0] with { AssetId = bMesh.Id }] }] }, Guid.CreateVersion7());
        await FreezeArmy(game, b, frozen);
        await SeedGameTable(game, texture.Id);

        var rows = await Games.ManifestAssets(game);
        Assert.Equal(new HashSet<Guid> { aMesh.Id, bMesh.Id, texture.Id }, rows.Select(r => r.AssetId).ToHashSet());
        Assert.Equal(new ManifestRow(bMesh.Id, b, "miniature", bMesh.Hash, 2.5, 90, 0, 0), rows.Single(r => r.AssetId == bMesh.Id));
        Assert.Equal(new ManifestRow(texture.Id, b, "texture", texture.Hash, 1, 0, 0, 0), rows.Single(r => r.AssetId == texture.Id));
        Assert.Empty(await Games.ManifestAssets((await Games.Create(a, GameRequest(hash)))!.Id));
    }
}
