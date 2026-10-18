using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Ends the game once the log holds a score: a stand-in for a strict breach, so the session's row path is tested alone.</summary>
public sealed class EndsOnScoreEngine : IRulesEngine
{
    public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public Emit Advance(GameState state) =>
        state.Projection.Ended is null && state.Applier.Log.Any(e => e.Payload is ScoreSet) ? new Emit([new GameEnded("over", [])]) : Emit.None;
}

public sealed class EndsOnScoreEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new EndsOnScoreEngine();
}

/// <summary>The session's row path at the end of a game. EndGameAsync's own guard is GameRowsContract's.</summary>
public class GameEndTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };
    readonly InMemorySeeder db = new();
    IGameRows _rows => db.Rows;

    async Task<(GameSession Session, InMemoryEventLog Log, RecordingClient Clay, SeededGame Seeded)> Hydrated(
        IRulesEngineFactory engines, GameStatus status = GameStatus.Playing, params EventPayload[] history)
    {
        var seeded = await db.SeedGameAsync(status);
        await db.SeedTableStateAsync(seeded.GameId);
        db.SeedArmiesInPlay(seeded);
        var log = new InMemoryEventLog();
        if (history.Length > 0)
            await log.AppendAsync(seeded.GameId, history.Select((p, i) => new GameEvent(i + 1, null, p, null)).ToList(), CancellationToken.None);
        var services = new SessionServices(_rows, log, engines, Options, NullLoggerFactory.Instance);
        var session = await Hydrator.HydrateAsync(seeded.GameId, services, CancellationToken.None);
        await session.StartAsync();
        var clay = new RecordingClient(seeded.Participants[0].ParticipantId);
        await session.AttachAsync(clay);
        return (session, log, clay, seeded);
    }

    async Task<GameStatus> StatusOf(Guid gameId) => (await _rows.ReadGameAsync(gameId, CancellationToken.None))!.Status;

    [Fact]
    public async Task The_end_marks_the_row_and_everyone_hears_it_before_rest()
    {
        var (session, log, clay, seeded) = await Hydrated(new EndsOnScoreEngineFactory());
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 1)));
        Assert.Equal(GameStatus.Ended, await StatusOf(seeded.GameId));
        Assert.Equal(GameStatus.Ended, session.Info.Status);
        Assert.IsType<GameEnded>((await log.ReadAsync(seeded.GameId, 0, CancellationToken.None))[^1].Payload);
        var info = clay.Received.FindIndex(m => m is GameInfoMessage g && g.Game.Status == GameStatus.Ended);
        var rest = clay.Received.FindLastIndex(m => m is AtRest);
        Assert.True(info >= 0 && info < rest, $"GameInfoMessage at {info}, at_rest at {rest}");
    }

    [Fact]
    public async Task A_crash_between_the_append_and_the_row_is_reconciled_on_hydration()
    {
        var (_, log, _, seeded) = await Hydrated(new EndsOnScoreEngineFactory(), GameStatus.Playing, new ScoreSet(Guid.CreateVersion7(), 1), new GameEnded("over", []));
        Assert.Equal(GameStatus.Ended, await StatusOf(seeded.GameId));
        Assert.Equal(2, (await log.ReadAsync(seeded.GameId, 0, CancellationToken.None)).Count);   // reconciled, not advanced
    }

    [Fact]
    public async Task An_undone_end_still_advances_after_a_restart()
    {
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", [Guid.CreateVersion7()], new Confirmation(), false, false, null, null);
        var (_, log, _, seeded) = await Hydrated(new FixedAskEngineFactory(ask), GameStatus.Ended,
            new ScoreSet(Guid.CreateVersion7(), 1), new GameEnded("over", []), new Undone(1));
        var events = await log.ReadAsync(seeded.GameId, 0, CancellationToken.None);
        Assert.IsType<AskOpened>(events[^1].Payload);                                          // the engine caught up although the row says ended
        Assert.Equal(GameStatus.Ended, await StatusOf(seeded.GameId));                          // and the row stays ended (manager ruling)
    }
}
