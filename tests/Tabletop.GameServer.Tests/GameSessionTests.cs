using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class GameSessionTests
{
    static readonly Guid P2 = Guid.CreateVersion7(), Watcher = Guid.CreateVersion7();
    static readonly GameInfo Info = Samples.Game with { Participants =
        [new Participant(Samples.P1, Guid.CreateVersion7(), "Clay", Samples.Team1, null), new Participant(P2, Guid.CreateVersion7(), "Opponent", Samples.Team2, null), new Participant(Watcher, Guid.CreateVersion7(), "Watcher", null, null)] };
    /// <summary>Two model ids out of Samples, so the spend map is concrete: one key per eligible spender, capped at its own tokens.</summary>
    static readonly Guid M1 = Samples.Model, M2 = Samples.Container;
    static readonly TokensSpent SpentByTeam1 = new(new Dictionary<Guid, int> { [M1] = 2 }), SpentByTeam2 = new(new Dictionary<Guid, int> { [M2] = 1 });
    static readonly Ask SimultaneousSpend = new(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", null);
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };
    static GameServerOptions CappedOptions(long events) => new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u", MaxEventsPerGame = events };

    // Rows are never touched on the playing path, so the session tests pass none; Tasks 10-12 test the row paths against Postgres.
    static SessionServices Services(IEventLog log, IRulesEngineFactory engines) => new(null!, log, engines, Options, NullLoggerFactory.Instance);

    static async Task<(GameSession Session, InMemoryEventLog Log, RecordingClient Clay, RecordingClient Opponent, RecordingClient Watcher)> PlayingAsync(IRulesEngineFactory? engines = null, GameStatus status = GameStatus.Playing)
    {
        var log = new InMemoryEventLog();
        var info = Info with { Status = status };
        var services = Services(log, engines ?? new InertEngineFactory());
        var session = await Hydrate(info, log, services);
        var (a, b, w) = (new RecordingClient(Samples.P1), new RecordingClient(P2), new RecordingClient(Watcher));
        await session.AttachAsync(a); await session.AttachAsync(b); await session.AttachAsync(w);
        return (session, log, a, b, w);
    }

    static async Task<GameSession> Hydrate(GameInfo info, IEventLog log, SessionServices services)
    {
        GameSession session;
        if (info.Status == GameStatus.Lobby) session = new GameSession(info.Id, info, GrimdarkFutureRules.Instance, null, null, null, services);
        else
        {
            var board = Samples.Board with { Armies = [.. Samples.Board.Armies, new ArmyInPlay(P2, Tabletop.Content.ArmySnapshot.Of(Tabletop.Content.Tests.ArmyTests.Sample(), Guid.CreateVersion7()))] };   // one instance, shared by the applier and the state; an army a side, so deployment waits for no one
            var applier = new EventApplier(board, info);
            applier.ApplyAll(await log.ReadAsync(info.Id, 0, CancellationToken.None));
            session = new GameSession(info.Id, info, GrimdarkFutureRules.Instance, null, new GameState(info, board, applier, GrimdarkFutureRules.Instance), services.Engines.Create(GrimdarkFutureRules.Instance, info), services);
        }
        await session.StartAsync();
        return session;
    }

    [Fact]
    public async Task A_command_is_logged_then_applied_then_broadcast_to_everyone()
    {
        var (session, log, clay, opponent, watcher) = await PlayingAsync();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        var logged = await log.ReadAsync(session.GameId, 0, CancellationToken.None);
        Assert.Equal([1L], logged.Select(e => e.Seq));
        Assert.Equal(Samples.P1, logged[0].Actor);
        Assert.Equal(4, session.Play!.Projection.Scores[Samples.Team1]);
        foreach (var c in new[] { clay, opponent, watcher }) Assert.Equal(1, c.Events.Single().Seq);
    }

    [Fact]
    public async Task A_refusal_goes_only_to_the_sender_and_logs_nothing()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync();
        var id = Guid.CreateVersion7();
        await session.ReceiveAsync(clay, new CommandMessage(id, new SetWounds(Guid.CreateVersion7(), 1)));
        Assert.Equal(id, Assert.IsType<Protocol.Refused>(clay.Received[0]).MessageId);
        Assert.Equal(0, Assert.IsType<AtRest>(clay.Received[1]).Seq);   // nothing appended: the sender alone, at the unchanged high-water mark
        Assert.Empty(opponent.Received);
        Assert.Empty(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Commands_are_refused_with_wrong_status_before_playing()
    {
        var log = new InMemoryEventLog();
        var session = await Hydrate(Info with { Status = GameStatus.Lobby }, log, Services(log, new InertEngineFactory()));
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 1)));
        Assert.Equal(RefusalReason.WrongStatus, Assert.IsType<Protocol.Refused>(clay.Received[0]).Reason);
        Assert.Equal(0, Assert.IsType<AtRest>(clay.Received[1]).Seq);   // no log at lobby, so the rest frame says 0
    }

    [Fact]
    public async Task A_command_that_appends_ends_at_rest_after_its_events_for_everyone()
    {
        var (session, _, clay, opponent, watcher) = await PlayingAsync();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        foreach (var c in new[] { clay, opponent, watcher })
        {
            Assert.Equal([typeof(EventsMessage), typeof(ModelFacts), typeof(AtRest)], c.Received.Select(m => m.GetType()));   // the events, the first facts, then rest, never the other way round
            Assert.Equal(1L, c.Received.OfType<AtRest>().Single().Seq);
        }
    }

    /// <summary>Security review M8: a batch the server's own projection cannot take is never stored, so the game is not bricked on rehydrate.</summary>
    [Fact]
    public async Task An_event_the_servers_own_projection_cannot_apply_rolls_the_command_back()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync(new DesyncingEngineFactory());
        var id = Guid.CreateVersion7();
        await session.ReceiveAsync(clay, new CommandMessage(id, new SetScore(Samples.Team1, 4)));
        Assert.False(session.Faulted);
        Assert.Empty(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
        Assert.Equal(id, clay.Received.OfType<Protocol.Refused>().Single().MessageId);
        Assert.Empty(opponent.Events);
        Assert.Equal(0, session.Play!.Projection.LastSeq);
        Assert.False(session.Play.Projection.Scores.TryGetValue(Samples.Team1, out var score) && score == 4);
    }

    [Fact]
    public async Task Starting_advances_the_engine_and_a_sealed_answer_is_withheld_per_viewer()
    {
        var (session, log, clay, opponent, watcher) = await PlayingAsync(new FixedAskEngineFactory(SimultaneousSpend));
        Assert.IsType<AskOpened>((await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Single().Payload);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new AnswerAsk(SimultaneousSpend.Id, SpentByTeam1)));
        Assert.NotNull(clay.Events.Select(e => e.Payload).OfType<AskAnswered>().Single().Answer);
        Assert.Null(opponent.Events.Select(e => e.Payload).OfType<AskAnswered>().Single().Answer);
        Assert.Null(watcher.Events.Select(e => e.Payload).OfType<AskAnswered>().Single().Answer);
        await session.ReceiveAsync(opponent, new CommandMessage(Guid.CreateVersion7(), new AnswerAsk(SimultaneousSpend.Id, SpentByTeam2)));
        Assert.Equal(2, watcher.Events.Select(e => e.Payload).OfType<AskClosed>().Single().Answers.Count);
    }

    [Fact]
    public async Task Presence_is_relayed_on_the_tick_pings_at_once_and_ends_with_the_socket()
    {
        var (session, _, clay, opponent, watcher) = await PlayingAsync();
        var value = new Presence(Guid.CreateVersion7(), new Cursor(new(1, 2, 3), null), null, [], null);
        await session.ReceiveAsync(clay, new PresenceMessage(value));
        await session.ReceiveAsync(clay, new PresenceMessage(value with { Cursor = new Cursor(new(4, 5, 6), null) }));
        Assert.Empty(opponent.Received.OfType<PresenceMessage>());
        await session.TickPresenceAsync();
        var relayed = Assert.Single(opponent.Received.OfType<PresenceMessage>()).Presence;
        Assert.Equal((Samples.P1, 4.0), (relayed.ParticipantId, relayed.Cursor!.Point.X));   // stamped with the sender, latest wins
        Assert.Empty(clay.Received.OfType<PresenceMessage>());
        await session.ReceiveAsync(clay, new PingMessage(new Ping(Guid.Empty, new(0, 0, 0), null)));
        Assert.Equal(Samples.P1, Assert.Single(watcher.Received.OfType<PingMessage>()).Ping.Sender);
        var late = new RecordingClient(Guid.CreateVersion7());
        await session.AttachAsync(late);
        Assert.Equal(Samples.P1, Assert.Single(late.Received.OfType<PresenceMessage>()).Presence.ParticipantId);
        await session.DetachAsync(clay);
        Assert.Equal(Samples.P1, Assert.Single(opponent.Received.OfType<PresenceEnded>()).ParticipantId);
    }

    [Fact]
    public async Task A_second_connection_for_the_same_participant_joins_the_first_and_both_hear_the_game()
    {
        var (session, _, clay, _, _) = await PlayingAsync();
        var again = new RecordingClient(Samples.P1);
        await session.AttachAsync(again);
        Assert.Null(clay.Closed);                                     // two windows of one account both play (AccountSockets caps them)
        Assert.Equal(4, session.Clients.Count);
        await session.ReceiveAsync(again, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.Equal(1, clay.Events.Single().Seq);
        Assert.Equal(1, again.Events.Single().Seq);
    }

    /// <summary>Security review M8: a throwing Advance rolls the whole command back; the session stays usable from the last stored state.</summary>
    [Fact]
    public async Task An_engine_that_throws_in_advance_refuses_the_command_and_stays_usable()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync(new ThrowingEngineFactory());
        var id = Guid.CreateVersion7();
        await session.ReceiveAsync(clay, new CommandMessage(id, new SetScore(Samples.Team1, 4)));
        Assert.False(session.Faulted);
        var refused = clay.Received.OfType<Protocol.Refused>().Single();
        Assert.Equal(id, refused.MessageId);
        Assert.DoesNotContain("broke", refused.Detail);
        Assert.Null(opponent.Closed);
        Assert.Equal(3, session.Clients.Count);
        Assert.Empty(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
        Assert.Equal(0, session.Play!.Projection.LastSeq);
        Assert.IsType<AtRest>(clay.Received.Last());
    }

    [Fact]
    public async Task Hydrating_a_session_whose_engine_throws_on_the_catch_up_stores_nothing_and_stays_usable()
    {
        var log = new InMemoryEventLog();
        var session = await Hydrate(Info with { Status = GameStatus.Playing }, log, Services(log, new ThrowingEngineFactory(always: true)));
        Assert.False(session.Faulted);
        Assert.Empty(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task A_full_log_refuses_further_commands()
    {
        var log = new InMemoryEventLog();
        var session = await Hydrate(Info with { Status = GameStatus.Playing }, log, new SessionServices(null!, log, new InertEngineFactory(), CappedOptions(2), NullLoggerFactory.Instance));
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        for (var i = 0; i < 3; i++) await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, i)));
        Assert.Equal(2, (await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Count);
        Assert.Equal(RefusalReason.NotPermitted, clay.Received.OfType<Protocol.Refused>().Single().Reason);
    }

    [Fact]
    public async Task An_ended_row_over_a_log_with_no_end_catches_up_on_hydration()
    {
        var log = new InMemoryEventLog();
        await log.AppendAsync(Info.Id, [new GameEvent(1, null, new PhaseStarted("played"), null)], CancellationToken.None);
        var session = await Hydrate(Info with { Status = GameStatus.Ended }, log, Services(log, new FixedAskEngineFactory(SimultaneousSpend)));
        Assert.False(session.Faulted);
        var events = await log.ReadAsync(session.GameId, 0, CancellationToken.None);
        Assert.IsType<AskOpened>(events[^1].Payload);   // ended is a flag; the log decides
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task An_ended_game_with_a_draft_hydrates_without_the_engine_appending_anything()
    {
        var log = new InMemoryEventLog();
        var session = await Hydrate(Info with { Status = GameStatus.Ended }, log, Services(log, new FixedAskEngineFactory(SimultaneousSpend)));
        Assert.False(session.Faulted);
        Assert.Empty(await log.ReadAsync(session.GameId, 0, CancellationToken.None));   // ended in setup: never played
    }

    [Fact]
    public async Task An_ended_row_over_an_ended_log_hydrates_without_the_engine_appending_anything()
    {
        var log = new InMemoryEventLog();
        await log.AppendAsync(Info.Id, [new GameEvent(1, null, new GameEnded("over", []), null)], CancellationToken.None);
        var session = await Hydrate(Info with { Status = GameStatus.Ended }, log, Services(log, new FixedAskEngineFactory(SimultaneousSpend)));
        Assert.False(session.Faulted);
        Assert.Single(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Losing_the_seq_race_faults_the_session_and_closes_every_socket()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync();
        await log.AppendAsync(session.GameId, [new GameEvent(1, null, new PhaseStarted("elsewhere"), null)], CancellationToken.None);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.True(session.Faulted);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, opponent.Closed!.Value.Status);
        Assert.Empty(session.Clients);
    }

    [Fact]
    public async Task A_faulted_session_drops_every_further_frame_and_refuses_new_attachments()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync();
        await log.AppendAsync(session.GameId, [new GameEvent(1, null, new PhaseStarted("elsewhere"), null)], CancellationToken.None);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.True(session.Faulted);
        var (clayBefore, opponentBefore) = (clay.Received.Count, opponent.Received.Count);

        await session.ReceiveAsync(opponent, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 9)));
        await session.ReceiveAsync(opponent, new PresenceMessage(new Presence(Guid.CreateVersion7(), null, null, [], null)));
        await session.TickPresenceAsync();

        Assert.Equal([1L], (await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Select(e => e.Seq));   // nothing further appended
        Assert.Equal(clayBefore, clay.Received.Count);       // nothing further reaches a client that was already closed
        Assert.Equal(opponentBefore, opponent.Received.Count);

        var late = new RecordingClient(Guid.CreateVersion7());
        await session.AttachAsync(late);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, late.Closed!.Value.Status);
        Assert.DoesNotContain(late, session.Clients);
    }

    /// <summary>One command, four batches (its own and three Advance batches): one append holds them all, and every viewer gets
    /// each batch as its own message, in order, only once the log holds it.</summary>
    [Fact]
    public async Task A_commands_batches_are_appended_once_and_no_client_receives_one_before_it_is_stored()
    {
        var stored = new InMemoryEventLog();
        var log = new CountingLog(stored);
        var session = await Hydrate(Info with { Status = GameStatus.Playing }, log, Services(log, new ChainEngineFactory(3)));
        bool Stored(long seq) => stored.ReadAsync(Info.Id, seq - 1, CancellationToken.None).Result.Any(e => e.Seq == seq);
        var clients = new[] { new DurabilityClient(Samples.P1, Stored), new DurabilityClient(P2, Stored), new DurabilityClient(Watcher, Stored) };
        foreach (var c in clients) await session.AttachAsync(c);

        await session.ReceiveAsync(clients[0], new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));

        Assert.Equal(1, log.Appends);
        Assert.Equal([1L, 2L, 3L, 4L], (await stored.ReadAsync(Info.Id, 0, CancellationToken.None)).Select(e => e.Seq));
        foreach (var c in clients)
        {
            Assert.Equal([[1L], [2L], [3L], [4L]], c.Received.OfType<EventsMessage>().Select(m => m.Events.Select(e => e.Seq).ToArray()));
            Assert.All(c.StoredWhenSent, Assert.True);
            Assert.Equal(4L, Assert.IsType<AtRest>(c.Received[^1]).Seq);
        }
    }

    /// <summary>The projection runs ahead of the log inside one command. A failed append faults the session before anything is sent,
    /// so no client saw the unstored events, and a fresh hydration rebuilds from the log and plays the command again from there.</summary>
    [Fact]
    public async Task A_failed_append_faults_the_session_sends_none_of_the_command_and_hydration_rebuilds_from_the_log()
    {
        var stored = new InMemoryEventLog();
        var log = new FailingLog(stored);
        var session = await Hydrate(Info with { Status = GameStatus.Playing }, log, Services(log, new ChainEngineFactory(3)));
        var (clay, opponent) = (new RecordingClient(Samples.P1), new RecordingClient(P2));
        await session.AttachAsync(clay); await session.AttachAsync(opponent);
        log.Fail = true;

        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));

        Assert.True(session.Faulted);
        foreach (var c in new[] { clay, opponent })
        {
            Assert.Equal(WebSocketCloseStatus.InternalServerError, c.Closed!.Value.Status);
            Assert.Empty(c.Received.OfType<EventsMessage>());
            Assert.Empty(c.Received.OfType<AtRest>());
        }
        Assert.Empty(await stored.ReadAsync(Info.Id, 0, CancellationToken.None));

        log.Fail = false;
        var again = await Hydrate(Info with { Status = GameStatus.Playing }, log, Services(log, new ChainEngineFactory(3)));
        Assert.Equal(0, again.Play!.Projection.LastSeq);
        var retry = new RecordingClient(Samples.P1);
        await again.AttachAsync(retry);
        await again.ReceiveAsync(retry, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.Equal([1L, 2L, 3L, 4L], (await stored.ReadAsync(Info.Id, 0, CancellationToken.None)).Select(e => e.Seq));
        Assert.Equal(4, again.Play.Projection.LastSeq);
    }

    /// <summary>Teardown on a drained session is a no-op. The handshake's finally detaches outside every catch, and two
    /// routine paths reach it after the mailbox is gone: the SIGTERM drain with a socket still unwinding, and the
    /// re-hydration that supersedes a faulted session. A throw here escapes the /ws endpoint and skips the send loop and
    /// the release behind it.</summary>
    [Fact]
    public async Task Detaching_from_a_drained_session_is_a_no_op()
    {
        var (session, _, clay, _, _) = await PlayingAsync();
        await session.DrainAsync("server shutting down");
        await session.DetachAsync(clay);                                        // the handshake's finally, after the drain
        await session.DrainAsync("released");                                   // and ReleaseIfEmptyAsync behind it
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, clay.Closed!.Value.Status);
    }

    /// <summary>Connecting is not teardown: a session released between get-or-hydrate and connect says so, which is how the
    /// handshake knows to hydrate again instead of closing the newcomer's socket 1011.</summary>
    [Fact]
    public async Task Connecting_to_a_drained_session_says_so()
    {
        var (session, _, _, _, _) = await PlayingAsync();
        await session.DrainAsync("released");
        await Assert.ThrowsAsync<SessionDrainedException>(() => session.ConnectAsync(new RecordingClient(Guid.CreateVersion7())));
    }
}

/// <summary>Emits one event naming a model nothing created, as soon as the log holds anything: the applier's desync, provoked from the server's own side.</summary>
public sealed class DesyncingEngine : IRulesEngine
{
    public Tabletop.GameServer.Dispatch.Legality Judge(GameState state, Command command, Actor actor) => Tabletop.GameServer.Dispatch.Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public Emit Advance(GameState state) => state.Applier.Log.Count == 1 ? new Emit([new WoundsSet(Guid.CreateVersion7(), 1)]) : Emit.None;
}

public sealed class DesyncingEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new DesyncingEngine();
}

/// <summary>Throws from Advance: on the first call when always, otherwise once the log holds an event.</summary>
public sealed class ThrowingEngine(bool always) : IRulesEngine
{
    public Tabletop.GameServer.Dispatch.Legality Judge(GameState state, Command command, Actor actor) => Tabletop.GameServer.Dispatch.Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public Emit Advance(GameState state) => always || state.Applier.Log.Count > 0 ? throw new InvalidOperationException("the engine broke") : Emit.None;
}

public sealed class ThrowingEngineFactory(bool always = false) : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new ThrowingEngine(always);
}

/// <summary>After the first event, emits one event per Advance until the log holds batches + 1: one command, several batches.</summary>
public sealed class ChainEngine(int batches) : IRulesEngine
{
    public Tabletop.GameServer.Dispatch.Legality Judge(GameState state, Command command, Actor actor) => Tabletop.GameServer.Dispatch.Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public Emit Advance(GameState state) => state.Applier.Log.Count is var n && n >= 1 && n <= batches ? new Emit([new PhaseStarted($"link {n}")]) : Emit.None;
}

public sealed class ChainEngineFactory(int batches) : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new ChainEngine(batches);
}

public sealed class CountingLog(IEventLog inner) : IEventLog
{
    public int Appends { get; private set; }
    public Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct) { Appends++; return inner.AppendAsync(gameId, events, ct); }
    public Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct) => inner.ReadAsync(gameId, afterSeq, ct);
    public Task<long> LastSeqAsync(Guid gameId, CancellationToken ct) => inner.LastSeqAsync(gameId, ct);
}

/// <summary>Throws from every append while Fail is set, storing nothing.</summary>
public sealed class FailingLog(IEventLog inner) : IEventLog
{
    public bool Fail { get; set; }
    public Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct) => Fail ? throw new InvalidOperationException("the database is gone") : inner.AppendAsync(gameId, events, ct);
    public Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct) => inner.ReadAsync(gameId, afterSeq, ct);
    public Task<long> LastSeqAsync(Guid gameId, CancellationToken ct) => inner.LastSeqAsync(gameId, ct);
}

/// <summary>Records, for every event it is sent, whether the log already held it at the moment of sending.</summary>
public sealed class DurabilityClient(Guid participantId, Func<long, bool> stored) : ISessionClient
{
    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public List<SocketMessage> Received { get; } = new();
    public List<bool> StoredWhenSent { get; } = new();
    public ValueTask SendAsync(SocketMessage message)
    {
        if (message is EventsMessage m) StoredWhenSent.AddRange(m.Events.Select(e => stored(e.Seq)));
        Received.Add(message);
        return ValueTask.CompletedTask;
    }
    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason) => ValueTask.CompletedTask;
}
