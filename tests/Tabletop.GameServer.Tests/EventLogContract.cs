using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public abstract class EventLogContract
{
    protected abstract Task<(IEventLog Log, Guid GameId)> CreateAsync();
    static GameEvent Ev(long seq, EventPayload p) => new(seq, Samples.P1, p, null);

    [Fact]
    public async Task Appends_and_reads_back_in_order_with_payload_intact()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1)), Ev(2, new ScoreSet(Samples.Team1, 3))], CancellationToken.None);
        await log.AppendAsync(game, [new GameEvent(3, null, new Undone(1), "outside the legal set")], CancellationToken.None);
        var all = await log.ReadAsync(game, 0, CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], all.Select(e => e.Seq));
        Assert.Equal(WireJson.Serialize(Ev(2, new ScoreSet(Samples.Team1, 3))), WireJson.Serialize(all[1]));
        Assert.Equal("outside the legal set", all[2].Warning);
        Assert.Null(all[2].Actor);
        Assert.Equal([3L], (await log.ReadAsync(game, 2, CancellationToken.None)).Select(e => e.Seq));
        Assert.Equal(3, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_log_reads_empty_and_last_seq_is_zero()
    {
        var (log, game) = await CreateAsync();
        Assert.Empty(await log.ReadAsync(game, 0, CancellationToken.None));
        Assert.Equal(0, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task A_duplicate_seq_is_refused_and_nothing_of_the_batch_lands()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None);
        await Assert.ThrowsAsync<DuplicateSequenceException>(() => log.AppendAsync(game, [Ev(2, new TurnStarted(Samples.Team2)), Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None));
        Assert.Equal(1, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task Games_do_not_see_each_others_events()
    {
        var (log, game) = await CreateAsync();
        var (_, other) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None);
        Assert.Empty(await log.ReadAsync(other, 0, CancellationToken.None));
    }
}

public class InMemoryEventLogTests : EventLogContract
{
    readonly InMemoryEventLog _log = new();
    protected override Task<(IEventLog, Guid)> CreateAsync() => Task.FromResult<(IEventLog, Guid)>((_log, Guid.CreateVersion7()));
}
