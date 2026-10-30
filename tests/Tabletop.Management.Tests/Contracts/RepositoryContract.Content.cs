using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests.Contracts;

public abstract partial class RepositoryContract
{
    [Fact]
    public async Task Army_reads_back_the_tree_it_was_given_owner_only()
    {
        var owner = await NewAccount();
        var army = SmallArmy();
        await Armies.Insert(owner, army);
        Assert.Equal(ContentJson.Serialize(army), ContentJson.Serialize(await Armies.Get(owner, army.Id)));
        Assert.Null(await Armies.Get(await NewAccount(), army.Id));
        Assert.Equal([new ArmySummary(army.Id, "Army", "grimdark-future", "test", 3, 4)], await Armies.List(owner, new Keyset(10, null)));
    }

    [Fact]
    public async Task An_army_keeps_its_edition_and_the_summary_shows_it()
    {
        var owner = await NewAccount();
        var army = SmallArmy() with { Edition = ArmyEditions.V36 };
        await Armies.Insert(owner, army);
        Assert.Equal("3.6", (await Armies.Get(owner, army.Id))!.Edition);
        Assert.Equal("3.6", (await Armies.List(owner, new Keyset(10, null))).Single().Edition);
    }

    [Fact]
    public async Task Army_count_is_the_owners_armies()
    {
        var owner = await NewAccount();
        Assert.Equal(0, await Armies.Count(owner));
        var army = SmallArmy();
        await Armies.Insert(owner, army);
        await Armies.Insert(owner, SmallArmy("Second"));
        await Armies.Insert(await NewAccount(), SmallArmy());
        Assert.Equal(2, await Armies.Count(owner));
        await Armies.Delete(owner, army.Id);
        Assert.Equal(1, await Armies.Count(owner));
    }

    [Fact]
    public async Task Army_list_pages_ascending_by_id_after_the_cursor()
    {
        var owner = await NewAccount();
        await Armies.Insert(await NewAccount(), SmallArmy());
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) { var a = SmallArmy($"a{i}"); await Armies.Insert(owner, a); ids.Add(a.Id); }
        ids.Sort();
        var page1 = await Armies.List(owner, new Keyset(2, null));
        Assert.Equal(ids.Take(2), page1.Select(a => a.Id));
        Assert.Equal(ids.Skip(2), (await Armies.List(owner, new Keyset(2, page1[^1].Id))).Select(a => a.Id));
    }

    [Fact]
    public async Task Army_patch_sets_a_unit_default_then_a_slot_override_and_ignores_slots_of_other_armies()
    {
        var owner = await NewAccount();
        var army = SmallArmy();
        var foreign = SmallArmy("Foreign");
        await Armies.Insert(owner, army);
        await Armies.Insert(owner, foreign);
        var squad = army.Units[0];
        var (mesh, special) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var patched = (await Armies.Patch(owner, army.Id, new PatchArmyRequest("Renamed", [new UnitPatch(squad.Id, mesh)],
            [new SlotPatch(squad.Models[0].Id, special, false, Base.Oval(2, 1), 2), new SlotPatch(foreign.Units[0].Models[0].Id, special, false, null, null)])))!;
        Assert.Equal("Renamed", patched.Name);
        Assert.Equal((special, Base.Oval(2, 1), 2.0), (patched.Units[0].Models[0].AssetId, patched.Units[0].Models[0].Base, patched.Units[0].Models[0].Height));
        Assert.Equal(mesh, patched.Units[0].Models[1].AssetId);
        Assert.Null(patched.Units[1].Models[0].AssetId);
        Assert.Null((await Armies.Get(owner, foreign.Id))!.Units[0].Models[0].AssetId);

        var kept = (await Armies.Patch(owner, army.Id, new PatchArmyRequest(null, null, [new SlotPatch(squad.Models[1].Id, null, false, null, null)])))!;
        Assert.Equal(("Renamed", (Guid?)mesh), (kept.Name, kept.Units[0].Models[1].AssetId));
        var cleared = (await Armies.Patch(owner, army.Id, new PatchArmyRequest(null, null, [new SlotPatch(squad.Models[0].Id, null, true, null, null)])))!;
        Assert.Null(cleared.Units[0].Models[0].AssetId);
        Assert.Equal(Base.Oval(2, 1), cleared.Units[0].Models[0].Base);
        var unbound = (await Armies.Patch(owner, army.Id, new PatchArmyRequest(null, [new UnitPatch(squad.Id, null)], null)))!;
        Assert.All(unbound.Units[0].Models, m => Assert.Null(m.AssetId));

        Assert.Null(await Armies.Patch(await NewAccount(), army.Id, new PatchArmyRequest("Theirs", null, null)));
    }

    [Fact]
    public async Task Army_delete_is_owner_only_and_clears_a_lobby_pick()
    {
        var hash = await NewProperties();
        var owner = await NewAccount();
        var army = SmallArmy();
        await Armies.Insert(owner, army);
        var game = (await Games.Create(owner, GameRequest(hash)))!.Id;
        await PickArmy(game, owner, army.Id);
        Assert.Equal(army.Id, (await Games.Get(game))!.Participants.Single().ArmyId);

        Assert.False(await Armies.Delete(await NewAccount(), army.Id));
        Assert.True(await Armies.Delete(owner, army.Id));
        Assert.False(await Armies.Delete(owner, army.Id));
        Assert.Null((await Games.Get(game))!.Participants.Single().ArmyId);
    }

    [Fact]
    public async Task Layout_save_copies_with_fresh_ids_and_reads_zones_by_team()
    {
        var owner = await NewAccount();
        var input = SmallLayout();
        var saved = await Layouts.Save(owner, input);
        Assert.NotEqual(input.Id, saved.Id);
        Assert.NotEqual(input.Terrain[0].Id, saved.Terrain[0].Id);
        Assert.NotEqual(input.Terrain[0].Volumes[0].Id, saved.Terrain[0].Volumes[0].Id);
        Assert.NotEqual(input.Markers[0].Id, saved.Markers[0].Id);
        Assert.Equal(input.Terrain[0].Volumes[0].Volume, saved.Terrain[0].Volumes[0].Volume);
        Assert.Equal([1, 2], saved.Zones.Select(z => z.TeamOrdinal));
        Assert.Equal(ContentJson.Serialize(saved), ContentJson.Serialize(await Layouts.Get(owner, saved.Id)));
        Assert.Equal([new LayoutSummary(saved.Id, "Ruins", new TableSize(72, 48), 1, 1)], await Layouts.List(owner));
        Assert.Null(await Layouts.Get(await NewAccount(), saved.Id));
    }

    [Fact]
    public async Task Layout_patch_renames_or_replaces_and_is_owner_only()
    {
        var owner = await NewAccount();
        var saved = await Layouts.Save(owner, SmallLayout());
        Assert.Null(await Layouts.Patch(await NewAccount(), saved.Id, new PatchLayoutRequest("Theirs", null)));
        var renamed = (await Layouts.Patch(owner, saved.Id, new PatchLayoutRequest("Renamed", null)))!;
        Assert.Equal(ContentJson.Serialize(saved with { Name = "Renamed" }), ContentJson.Serialize(renamed));
        var replaced = (await Layouts.Patch(owner, saved.Id, new PatchLayoutRequest(null, SmallLayout("Ignored") with { Size = new TableSize(44, 30), Texture = VisualRef.OfBuiltin("sand"), Terrain = [] })))!;
        Assert.Equal(("Renamed", new TableSize(44, 30), VisualRef.OfBuiltin("sand"), 0, 1), (replaced.Name, replaced.Size, replaced.Texture, replaced.Terrain.Count, replaced.Markers.Count));
        Assert.NotEqual(renamed.Markers[0].Id, replaced.Markers[0].Id);
        Assert.False(await Layouts.Delete(await NewAccount(), saved.Id));
        Assert.True(await Layouts.Delete(owner, saved.Id));
        Assert.Empty(await Layouts.List(owner));
    }
}
