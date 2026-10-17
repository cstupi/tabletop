using Tabletop.Content;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class ZonesInPlayTests
{
    readonly InMemorySeeder db = new();
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static IEnumerable<(Guid, string, int)> Keys(IReadOnlyList<DeploymentZone> zones) => zones.Select(z => (z.Id, z.Shape, z.TeamOrdinal));

    // H2: the zones set in setup are the zones of the board in play, and a fresh process reads the same ones back.
    [Fact]
    public async Task Zones_set_in_setup_reach_the_board_in_play_and_a_fresh_process()
    {
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        IReadOnlyList<DeploymentZone> set;
        using (var first = GameServerFactory.WithoutScheduler(db))
        {
            await using var c = await TestClient.OpenAsync(first);
            await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
            await c.SendAsync(Edit(new SetZoneStyle(ZoneStyles.Spearhead, ZoneStyles.ShapesOf(ZoneStyles.Spearhead))));
            set = (await c.ExpectAsync<BoardMessage>()).Board.Table.Zones;
            Assert.Equal(2, set.Count);
            await c.SendAsync(Edit(new EndSetup()));
            Assert.Equal(Keys(set), Keys((await c.ExpectAsync<BoardMessage>()).Board.Table.Zones));
        }
        using var second = GameServerFactory.WithoutScheduler(db);
        await using var again = await TestClient.OpenAsync(second);
        await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(Keys(set), Keys((await again.ExpectAsync<BoardMessage>()).Board.Table.Zones));
    }

    // H1 and F1: a game whose setup never touched the zones still deploys into the core rules' zones (p.6: "fully within
    // 12" of their table edge", one long edge each), which is ZoneStyles' Frontline.
    [Fact]
    public async Task A_new_draft_starts_with_the_core_rules_deployment()
    {
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        using var f = GameServerFactory.WithoutScheduler(db);
        await using var c = await TestClient.OpenAsync(f);
        await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await c.SendAsync(Edit(new StartSetup()));
        var zones = (await c.ExpectAsync<BoardMessage>()).Board.Table.Zones;
        Assert.Equal(ZoneStyles.ShapesOf(ZoneStyles.Frontline).Select((shape, i) => (shape, i + 1)), zones.Select(z => (z.Shape, z.TeamOrdinal)));
        Assert.Equal(2, db.GameRows.DraftOf(seeded.GameId)!.Zones.Count);
    }
}
