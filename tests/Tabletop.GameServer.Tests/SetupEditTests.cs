using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class SetupEditTests(PostgresFixture pg)
{
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);

    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Watcher)> InSetupAsync()
    {
        var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2, spectators: 1);
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
        await Wait.UntilAsync(async () => await pg.CountAsync("select count(*) from management.terrain_placement where id = $1", hill.Id) == 1,
            "the added terrain never reached the draft row");
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.terrain_volume where terrain_placement_id = $1", hill.Id));

        await creator.SendAsync(Edit(new SetZoneStyle("long-edges", ["long-edges/1", "long-edges/2"])));
        Assert.Equal(2, (await watcher.ExpectAsync<BoardMessage>()).Board.Table.Zones.Count);
        await creator.ExpectAsync<BoardMessage>();
        await watcher.SendAsync(Edit(new SwapZones(1, 2)));
        Assert.Equal("long-edges/2", (await creator.ExpectAsync<BoardMessage>()).Board.Table.Zones.Single(z => z.TeamOrdinal == 1).Shape);

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
        await Wait.UntilAsync(async () => await pg.CountAsync("select count(*) from management.table_state where game_id = $1 and width = 44", g.GameId) == 1,
            "the replaced draft never reached the table_state row");
        Assert.Equal(layout.Markers.Count, await pg.CountAsync("select count(*) from management.marker m join management.table_state t on t.id = m.table_state_id where t.game_id = $1", g.GameId));
    }

    /// <summary>A layout's children carry the primary keys of the layout's own rows — terrain_placement, terrain_volume, marker
    /// and deployment_zone ids are global, and layouts share table_state with drafts — so replace_draft has to mint fresh ones
    /// or the draft write hits a unique violation and faults the session.</summary>
    [Fact]
    public async Task Loading_a_saved_layout_row_writes_fresh_child_ids_and_leaves_the_layout_alone()
    {
        var (f, g, creator, watcher) = await InSetupAsync();
        var layout = await pg.SeedLayoutAsync(await pg.SeedAccountAsync("Layout owner"));

        await creator.SendAsync(Edit(new ReplaceDraft(layout)));
        var table = (await watcher.ExpectAsync<BoardMessage>()).Board.Table;
        await creator.ExpectAsync<BoardMessage>();                                                               // the sender's own copy of the same broadcast
        Assert.NotEqual(layout.Terrain[0].Id, table.Terrain[0].Id);
        Assert.NotEqual(layout.Terrain[0].Volumes[0].Id, table.Terrain[0].Volumes[0].Id);
        Assert.NotEqual(layout.Markers[0].Id, table.Markers[0].Id);
        Assert.Empty(table.Zones.Select(z => z.Id).Intersect(layout.Zones.Select(z => z.Id)));

        const string terrainOfGame = "select count(*) from management.terrain_placement p join management.table_state t on t.id = p.table_state_id where t.game_id = $1";
        await Wait.UntilAsync(async () => await pg.CountAsync(terrainOfGame, g.GameId) == layout.Terrain.Count, "the loaded layout never reached the draft rows");
        Assert.Equal(layout.Markers.Count, await pg.CountAsync("select count(*) from management.marker m join management.table_state t on t.id = m.table_state_id where t.game_id = $1", g.GameId));
        Assert.Equal(layout.Zones.Count, await pg.CountAsync("select count(*) from management.deployment_zone z join management.table_state t on t.id = z.table_state_id where t.game_id = $1", g.GameId));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.terrain_volume where terrain_placement_id = $1", layout.Terrain[0].Id));   // the layout's own rows still stand

        await creator.SendAsync(Edit(new SetTableSize(new TableSize(50, 40))));                                   // and the session did not fault
        Assert.Equal(50.0, (await creator.ExpectAsync<BoardMessage>()).Board.Table.Size.Width);
    }

    [Fact]
    public async Task Setup_edits_are_refused_outside_setup()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.SendAsync(Edit(new SetTableSize(new TableSize(1, 1))));
        Assert.Equal(RefusalReason.WrongStatus, (await client.ExpectAsync<Refused>()).Reason);
    }
}
