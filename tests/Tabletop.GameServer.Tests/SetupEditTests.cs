using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class SetupEditTests
{
    readonly InMemorySeeder db = new();
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);

    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Watcher)> InSetupAsync()
    {
        var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2, spectators: 1);
        var creator = await TestClient.OpenAsync(factory); var watcher = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await watcher.ConnectAsync(seeded.Participants[2].AccountId, seeded.GameId);
        await creator.SendAsync(Edit(new StartSetup()));
        await creator.ExpectAsync<BoardMessage>(); await watcher.ExpectAsync<BoardMessage>();
        return (factory, seeded, creator, watcher);
    }

    [Fact]
    public async Task Anyone_edits_everyone_sees_the_whole_draft_and_the_row_follows()
    {
        var (f, g, creator, watcher) = await InSetupAsync();
        var hill = TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(10, 10, 0) };
        await watcher.SendAsync(Edit(new AddTerrain(hill)));                       // a participant with no team edits freely
        var board = (await creator.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal(hill.Id, board.Table.Terrain.Single().Id);
        Assert.Equal(hill.Id, (await watcher.ExpectAsync<BoardMessage>()).Board.Table.Terrain.Single().Id);
        await Wait.UntilAsync(() => db.GameRows.DraftOf(g.GameId)!.Terrain.Any(t => t.Id == hill.Id),
            "the added terrain never reached the draft row");
        Assert.Single(db.GameRows.DraftOf(g.GameId)!.Terrain.Single(t => t.Id == hill.Id).Volumes);

        await creator.SendAsync(Edit(new SetZoneStyle("spearhead", ["spearhead/1", "spearhead/2"])));
        Assert.Equal(2, (await watcher.ExpectAsync<BoardMessage>()).Board.Table.Zones.Count);
        await creator.ExpectAsync<BoardMessage>();
        await watcher.SendAsync(Edit(new SwapZones(1, 2)));
        Assert.Equal("spearhead/2", (await creator.ExpectAsync<BoardMessage>()).Board.Table.Zones.Single(z => z.TeamOrdinal == 1).Shape);

        await watcher.SendAsync(Edit(new MoveTerrain(Guid.CreateVersion7(), new Point3(0, 0, 0), new Yaw(0))));
        Assert.Equal(RefusalReason.Unknown, (await watcher.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Loading_a_layout_replaces_the_draft_size_included()
    {
        var (f, g, creator, watcher) = await InSetupAsync();
        var layout = TableStateTests.Sample() with { Size = new TableSize(44, 30) };
        await creator.SendAsync(Edit(new ReplaceDraft(layout)));
        var table = (await watcher.ExpectAsync<BoardMessage>()).Board.Table;
        Assert.Equal((44.0, layout.Terrain.Count, layout.Markers.Count, layout.Zones.Count), (table.Size.Width, table.Terrain.Count, table.Markers.Count, table.Zones.Count));
        Assert.NotEqual(layout.Id, table.Id);
        await Wait.UntilAsync(() => db.GameRows.DraftOf(g.GameId)!.Size.Width == 44,
            "the replaced draft never reached the table_state row");
        Assert.Equal(layout.Markers.Count, db.GameRows.DraftOf(g.GameId)!.Markers.Count);
    }

    /// <summary>A layout's children carry the primary keys of the layout's own rows — terrain_placement, terrain_volume, marker
    /// and deployment_zone ids are global, and layouts share table_state with drafts — so replace_draft has to mint fresh ones
    /// or the draft write hits a unique violation and faults the session.</summary>
    [Fact]
    public async Task Loading_a_saved_layout_row_writes_fresh_child_ids_and_leaves_the_layout_alone()
    {
        var (f, g, creator, watcher) = await InSetupAsync();
        var layout = await db.SeedLayoutAsync(await db.SeedAccountAsync("Layout owner"));

        await creator.SendAsync(Edit(new ReplaceDraft(layout)));
        var table = (await watcher.ExpectAsync<BoardMessage>()).Board.Table;
        await creator.ExpectAsync<BoardMessage>();                                                               // the sender's own copy of the same broadcast
        Assert.NotEqual(layout.Terrain[0].Id, table.Terrain[0].Id);
        Assert.NotEqual(layout.Terrain[0].Volumes[0].Id, table.Terrain[0].Volumes[0].Id);
        Assert.NotEqual(layout.Markers[0].Id, table.Markers[0].Id);
        Assert.Empty(table.Zones.Select(z => z.Id).Intersect(layout.Zones.Select(z => z.Id)));

        await Wait.UntilAsync(() => db.GameRows.DraftOf(g.GameId)!.Terrain.Count == layout.Terrain.Count, "the loaded layout never reached the draft rows");
        Assert.Equal(layout.Markers.Count, db.GameRows.DraftOf(g.GameId)!.Markers.Count);
        Assert.Equal(layout.Zones.Count, db.GameRows.DraftOf(g.GameId)!.Zones.Count);
        Assert.Single(db.GameRows.Table(layout.Id)!.Table.Terrain.Single(t => t.Id == layout.Terrain[0].Id).Volumes);   // the layout's own rows still stand

        await creator.SendAsync(Edit(new SetTableSize(new TableSize(50, 40))));                                   // and the session did not fault
        Assert.Equal(50.0, (await creator.ExpectAsync<BoardMessage>()).Board.Table.Size.Width);
    }

    [Fact]
    public async Task Setup_edits_are_refused_outside_setup()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.SendAsync(Edit(new SetTableSize(new TableSize(1, 1))));
        Assert.Equal(RefusalReason.WrongStatus, (await client.ExpectAsync<Refused>()).Reason);
    }
}
