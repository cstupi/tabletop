using Tabletop.Content.Tests;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class EndSetupTests(PostgresFixture pg)
{
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static readonly Ask Deploy = new(Guid.CreateVersion7(), AskQuestions.Deploy, "Deploy", [], new PieceSelection([], 1, 1), false, false, null, null);

    [Fact]
    public async Task Ending_setup_freezes_armies_drops_off_slab_terrain_flips_status_opens_the_log_and_advances()
    {
        using var factory = new GameServerFactory { ConfigureTestServices = s => s.AddSingleton<IRulesEngineFactory>(new FixedAskEngineFactory(Deploy)) };
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var army = await pg.SeedArmyAsync(seeded.CreatorAccountId);
        await using var creator = await TestClient.OpenAsync(factory); await using var other = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId); await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await creator.SendAsync(Edit(new PickArmy(army.Id))); await creator.ExpectAsync<GameInfoMessage>(); await other.ExpectAsync<GameInfoMessage>();

        await other.SendAsync(Edit(new EndSetup()));
        Assert.Equal(RefusalReason.WrongStatus, (await other.ExpectAsync<Refused>()).Reason);       // still lobby
        await creator.SendAsync(Edit(new StartSetup())); await creator.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>();
        var on = TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(10, 10, 0) };
        var off = TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(-6, 10, 0) };
        await creator.SendAsync(Edit(new AddTerrain(on))); await creator.SendAsync(Edit(new AddTerrain(off)));
        await creator.ExpectAsync<BoardMessage>(); await creator.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>();

        await other.SendAsync(Edit(new EndSetup()));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        await creator.SendAsync(Edit(new EndSetup()));
        Assert.Equal(GameStatus.Playing, (await other.ExpectAsync<GameInfoMessage>()).Game.Status);
        var board = (await other.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal([on.Id], board.Table.Terrain.Select(t => t.Id));
        var inPlay = Assert.Single(board.Armies);
        Assert.Equal((seeded.Participants[0].ParticipantId, army.Name, army.Units.Count), (inPlay.ParticipantId, inPlay.Snapshot.Name, inPlay.Snapshot.Units.Count));
        Assert.Empty((await other.ExpectAsync<EventsMessage>()).Events);
        Assert.IsType<AskOpened>((await other.ExpectAsync<EventsMessage>()).Events.Single().Payload);   // the engine advanced after the flip
        Assert.Equal(1L, (await other.ExpectAsync<AtRest>()).Seq);                                     // and the transition ends at rest, on the opened log

        Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1 and status = 'playing'", seeded.GameId));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.participant where game_id = $1 and army_snapshot is not null", seeded.GameId));
        Assert.Equal(0, await pg.CountAsync("select count(*) from management.terrain_placement where id = $1", off.Id));

        await creator.SendAsync(Edit(new SetTableSize(new TableSize(1, 1))));
        Assert.Equal(RefusalReason.WrongStatus, (await creator.ExpectAsync<Refused>()).Reason);         // no way back
        await creator.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 1)));
        Assert.Equal(2, (await creator.ExpectAsync<EventsMessage>()).Events.Single().Seq);
    }

    [Fact]
    public async Task A_fresh_process_hydrates_the_played_game_from_rows_and_log()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var army = await pg.SeedArmyAsync(seeded.CreatorAccountId);
        using (var first = new GameServerFactory())
        {
            await using var c = await TestClient.OpenAsync(first);
            await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await c.SendAsync(Edit(new PickArmy(army.Id))); await c.ExpectAsync<GameInfoMessage>();
            await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
            await c.SendAsync(Edit(new EndSetup())); await c.ExpectAsync<BoardMessage>(); await c.ExpectAsync<EventsMessage>();
            await c.SendAsync(new CommandMessage(Guid.CreateVersion7(), new MoveModels([new ModelRelocation(army.Units[0].Models[0].Id, new OnShelf(), new OnTable(new Placement(new Point3(12, 12, 0), new Yaw(0))))])));
            await c.ExpectAsync<EventsMessage>();
        }
        using var second = new GameServerFactory();
        await using var again = await TestClient.OpenAsync(second);
        var welcome = await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(GameStatus.Playing, welcome.Game.Status);
        Assert.Single((await again.ExpectAsync<BoardMessage>()).Board.Armies);
        var moved = Assert.IsType<ModelsMoved>((await again.ExpectAsync<EventsMessage>()).Events.Single().Payload);
        Assert.IsType<OnTable>(moved.Moves.Single().To);
    }
}
