using Tabletop.GameServer.Log;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
[Trait("Category", "E2E")]
public class PostgresEventLogTests(PostgresFixture pg) : EventLogContract
{
    protected override async Task<(IEventLog, Guid)> CreateAsync() => (new PostgresEventLog(pg.Db), (await pg.SeedGameAsync(GameStatus.Playing)).GameId);

    [Fact]
    public async Task Kind_column_is_the_registered_discriminator()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);
        Assert.Equal(1, await pg.CountAsync("select count(*) from game.game_event where game_id = $1 and kind = 'phase_started' and payload->'payload'->>'kind' = 'phase_started'", game));
    }
}
