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

    /// <summary>A session appends one command's batches (its own and every Advance batch after it) in one call: one transaction.</summary>
    [Fact]
    public async Task Several_batches_land_in_one_append_with_each_batchs_actor_and_warning()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None);
        await log.AppendAsync(game,
            [new GameEvent(2, Samples.P1, new ScoreSet(Samples.Team1, 3), "outside the legal set"), new GameEvent(3, Samples.P1, new ScoreSet(Samples.Team2, 1), null),
             new GameEvent(4, null, new PhaseStarted("deployment"), null), new GameEvent(5, null, new TurnStarted(Samples.Team2), "a second batch's warning")], CancellationToken.None);
        var all = await log.ReadAsync(game, 1, CancellationToken.None);
        Assert.Equal([2L, 3L, 4L, 5L], all.Select(e => e.Seq));
        Assert.Equal([Samples.P1, Samples.P1, null, null], all.Select(e => e.Actor));
        Assert.Equal(["outside the legal set", null, null, "a second batch's warning"], all.Select(e => e.Warning));
        Assert.Equal(5, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task An_append_that_throws_part_way_through_several_batches_stores_none_of_them()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None);
        await Assert.ThrowsAsync<DuplicateSequenceException>(() => log.AppendAsync(game,
            [Ev(2, new ScoreSet(Samples.Team1, 3)), Ev(3, new PhaseStarted("deployment")), Ev(4, new TurnStarted(Samples.Team2)), Ev(3, new ScoreSet(Samples.Team2, 1))], CancellationToken.None));
        Assert.Equal([1L], (await log.ReadAsync(game, 0, CancellationToken.None)).Select(e => e.Seq));
        await log.AppendAsync(game, [Ev(2, new ScoreSet(Samples.Team1, 3))], CancellationToken.None);   // the failed append left no seq behind
        Assert.Equal(2, await log.LastSeqAsync(game, CancellationToken.None));
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
