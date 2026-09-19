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
            var board = Samples.Board;   // one instance, shared by the applier and the state (the sample is a property)
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
            Assert.Equal([typeof(EventsMessage), typeof(AtRest)], c.Received.Select(m => m.GetType()));   // the events, then rest, never the other way round
            Assert.Equal(1L, c.Received.OfType<AtRest>().Single().Seq);
        }
    }

    [Fact]
    public async Task An_event_the_servers_own_projection_cannot_apply_faults_the_session()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync(new DesyncingEngineFactory());
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.True(session.Faulted);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, opponent.Closed!.Value.Status);
        Assert.Empty(session.Clients);
        Assert.Equal([1L, 2L], (await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Select(e => e.Seq));   // the bad event is durable; the next hydrate faults too
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
    public async Task A_second_connection_for_the_same_participant_replaces_the_first()
    {
        var (session, _, clay, _, _) = await PlayingAsync();
        var again = new RecordingClient(Samples.P1);
        await session.AttachAsync(again);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, clay.Closed!.Value.Status);
        Assert.Equal(3, session.Clients.Count);
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
    public IReadOnlyList<EventPayload> Advance(GameState state) => state.Applier.Log.Count == 1 ? [new WoundsSet(Guid.CreateVersion7(), 1)] : [];
}

public sealed class DesyncingEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new DesyncingEngine();
}
