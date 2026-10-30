using System.Net.WebSockets;
using System.Threading.Channels;
using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Work was posted to a session whose mailbox has already been completed by DrainAsync. Teardown swallows it —
/// a drained session has nothing left to tear down — and the handshake answers it on connect by hydrating the game again.
/// It is an InvalidOperationException so the presence tick, which already tolerates that, keeps its meaning.</summary>
public sealed class SessionDrainedException() : InvalidOperationException("session is drained");

/// <summary>
/// One game in memory. Every mutation runs on the mailbox loop, so there is no lock: connections post frames, the loop handles
/// them one at a time. A command's events are appended in one call before any of them is broadcast; a row is written after the broadcast (setup's rows, and the end of the game).
/// </summary>
public sealed partial class GameSession
{
    private readonly Channel<Func<Task>> _mailbox = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<ISessionClient> _clients = new();
    /// <summary>Admin watchers: they receive what a teamless participant receives and are never in _clients.</summary>
    private readonly List<ISessionClient> _watchers = new();
    private readonly PresenceTable _presence = new();
    private readonly SessionServices _services;
    private readonly ILogger _log;
    private Task? _loop;
    private bool _drained;

    public GameSession(Guid gameId, GameInfo info, IGameRules rules, TableState? draft, GameState? play, IRulesEngine? engine, SessionServices services)
    {
        GameId = gameId; Info = info; Rules = rules; Draft = draft; Play = play; Engine = engine; _services = services;
        _log = services.Logging.CreateLogger<GameSession>();
        Dispatcher = engine is null ? null : new CommandDispatcher(engine);
    }

    public Guid GameId { get; }
    public GameInfo Info { get; internal set; }
    public IGameRules Rules { get; private set; }
    public TableState? Draft { get; internal set; }
    public GameState? Play { get; internal set; }
    public IRulesEngine? Engine { get; internal set; }
    public CommandDispatcher? Dispatcher { get; internal set; }
    public IReadOnlyList<ISessionClient> Clients => _clients;
    public IReadOnlyList<ISessionClient> Watchers => _watchers;
    public bool Faulted { get; private set; }

    /// <summary>
    /// Starts the loop, then decides from the log, not the row (enforcement spec → Hydration): an ended log over a playing row is a crash
    /// between the append and the row write, so the row is written and nothing advances; an ended log over an ended row is left; a log
    /// with no live end catches up, whatever the row says — ended is a flag, and an undone strict end is still listed as ended — except a non-playing row over an empty log, a game ended in setup and never played.
    /// </summary>
    public Task StartAsync()
    {
        _loop ??= Task.Run(async () => { await foreach (var work in _mailbox.Reader.ReadAllAsync()) await work(); });
        return RunAsync(async () =>
        {
            if (Play is null) return 0;
            if (Play.Projection.Ended is not null) { if (Info.Status == GameStatus.Playing) await MarkEndedAsync(); return 0; }
            if (Info.Status != GameStatus.Playing && Play.Projection.LastSeq == 0) return 0;   // ended during setup: never played, nothing to advance
            await RunToRestAsync([], null, null);
            return 0;
        });
    }

    /// <summary>
    /// The one row this server writes from playing on (enforcement spec → The row and the broadcast). A failed write is logged and left:
    /// the log already holds the end, and the next hydration writes the row (StartAsync).
    /// </summary>
    private async Task MarkEndedAsync()
    {
        try { await _services.Rows.EndGameAsync(GameId, CancellationToken.None); }
        catch (Exception ex) { _log.LogError(ex, "Game {GameId}: the log ended the game but the row write failed; the next hydration retries.", GameId); return; }
        await ReloadInfoAsync();
    }

    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_mailbox.Writer.TryWrite(async () => { try { tcs.SetResult(await work()); } catch (Exception ex) { tcs.SetException(ex); } }))
            tcs.SetException(new SessionDrainedException());
        return tcs.Task;
    }
    private Task RunAsync(Func<Task> work) => RunAsync(async () => { await work(); return 0; });

    public Guid? TeamOf(Guid participantId) => Info.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;

    /// <summary>Every participant row of the connection's account: one per team it plays (self-play), or its one teamless row. The connection
    /// carries the account's oldest row, which the lobby edits never delete while the account keeps another (DropSeatAsync).</summary>
    public IReadOnlyList<Participant> SeatsOf(ISessionClient client) =>
        Info.Participants.FirstOrDefault(p => p.Id == client.ParticipantId) is { } me ? Info.Participants.Where(p => p.AccountId == me.AccountId).ToList() : [];
    public IReadOnlySet<Guid> TeamsOf(ISessionClient client) => SeatsOf(client).Where(p => p.TeamId is not null).Select(p => p.TeamId!.Value).ToHashSet();

    /// <summary>
    /// Who a command acts as: one of the account's seats. An answer acts for the team it names, else for the first of the account's teams the Ask
    /// addresses that has not answered; a hand move for the team whose model it moves first; anything else for the account's first team.
    /// No seat the command can use leaves the actor's team null, which the dispatcher refuses (a watcher; an Ask for no team of mine).
    /// </summary>
    public Actor ActorFor(ISessionClient client, Command command)
    {
        var seats = SeatsOf(client).Where(p => p.TeamId is not null).ToList();
        Participant? seat = null;
        switch (command)
        {
            case AnswerAsk { TeamId: { } named }:
                seat = seats.FirstOrDefault(p => p.TeamId == named);
                if (seat is null) return new Actor(client.ParticipantId, null);
                break;
            case AnswerAsk a when Play?.Projection.OpenAsks.TryGetValue(a.AskId, out var open) is true:
                var addressed = open.Ask.Teams.Select(t => seats.FirstOrDefault(p => p.TeamId == t)).OfType<Participant>().ToList();
                seat = addressed.FirstOrDefault(p => !open.Answers.ContainsKey(p.TeamId!.Value)) ?? addressed.FirstOrDefault();
                break;
            case MoveModels m when Play is { } play:
                var team = m.Moves.Select(x => play.Projection.Models.TryGetValue(x.ModelId, out var model) ? TeamOf(play.Projection.Units[model.UnitId].ParticipantId) : null)
                    .FirstOrDefault(t => t is not null);
                seat = seats.FirstOrDefault(p => p.TeamId == team);
                break;
        }
        seat ??= seats.FirstOrDefault();
        return seat is null ? new Actor(client.ParticipantId, null) : new Actor(seat.Id, seat.TeamId);
    }

    public Task AttachAsync(ISessionClient client) => RunAsync(() => AttachCoreAsync(client));

    /// <summary>An account may hold several sockets on one game (two windows playing both sides); AccountSockets caps them, closing the oldest.
    /// The newcomer gets everyone else's current presence.</summary>
    private async Task AttachCoreAsync(ISessionClient client)
    {
        if (Faulted) { await client.CloseAsync(WebSocketCloseStatus.InternalServerError, "this game session has faulted"); return; }
        _clients.Add(client);
        foreach (var value in _presence.All.Where(v => v.ParticipantId != client.ParticipantId)) await client.SendAsync(new PresenceMessage(value));
    }

    /// <summary>The handshake detaches in a finally, outside every catch, and reaches a drained session by two routine paths:
    /// the SIGTERM drain completes the mailbox while read loops are still unwinding, and a re-hydration drains the faulted
    /// session its old loops still hold. A drained session has already closed and dropped every client, so this is a no-op
    /// rather than a throw into the /ws endpoint.</summary>
    public Task DetachAsync(ISessionClient client) => WhenNotDrained(RunAsync(async () =>
    {
        if (_watchers.Remove(client)) return;
        if (!_clients.Remove(client)) return;
        if (_clients.All(c => c.ParticipantId != client.ParticipantId)) { _presence.Remove(client.ParticipantId); await BroadcastAsync(new PresenceEnded(client.ParticipantId)); }
        // The client's Leave deletes the row through management, then closes the socket: the lobby the others draw drops the player now.
        if (Info.Status is GameStatus.Lobby or GameStatus.Setup && !Faulted) await RefreshInfoAsync();
    }));

    /// <summary>Teardown that raced the drain it was tearing down: the work is already done by definition.</summary>
    private static async Task WhenNotDrained(Task teardown)
    {
        try { await teardown; } catch (SessionDrainedException) { }
    }

    public async Task BroadcastAsync(SocketMessage message, ISessionClient? except = null)
    {
        foreach (var c in _clients.Concat(_watchers).ToList()) if (c != except) await c.SendAsync(message);
    }

    /// <summary>One command's batches, applied to the projection and not yet in the log: each with the sealed asks open before it.</summary>
    private readonly List<(IReadOnlyList<GameEvent> Batch, IReadOnlySet<Guid> SealedBefore)> _staged = new();

    /// <summary>
    /// The log path for one command (or the catch-up at start, or the end of setup): the command's own events, if any, then the engine run
    /// until it rests, every batch applied as it comes so the next Advance reads it. Then one append of every batch, one transaction, and only
    /// then each batch broadcast as its own message, in order, with each viewer's sealed answers withheld. Nothing is sent before it is stored.
    /// A failed append faults the session: the projection is ahead of the log, and only a fresh hydration from the log is trusted again.
    /// An Advance that throws, or an event the projection cannot take, rolls the whole run back (security review M8): nothing staged is stored
    /// or sent, the projection is rebuilt from the log as stored, and false is returned so the command is refused. A stored event the server
    /// cannot replay would fault every later hydration of the game; a refused command leaves it playable from its last good state.
    /// </summary>
    internal async Task<bool> RunToRestAsync(IReadOnlyList<EventPayload> payloads, Guid? actor, string? warning)
    {
        if (Play is null || Faulted) return true;
        var endedBefore = Play.Projection.Ended is not null;
        var stored = Play.Applier.Log.Count;
        var fault = Stage(payloads, actor, warning) ?? Advance();
        var staged = _staged.ToList();
        _staged.Clear();
        if (fault is not null)
        {
            _log.LogError("Game {GameId}: {Fault}; the run of {Batches} batches is rolled back to seq {Seq}", GameId, fault, staged.Count, stored == 0 ? 0 : Play.Applier.Log[stored - 1].Seq);
            var applier = new EventApplier(Play.Board, Play.Info);
            applier.ApplyAll(Play.Applier.Log.Take(stored).ToList());
            Play = new GameState(Play.Info, Play.Board, applier, Play.Rules);
            return false;
        }
        if (staged.Count > 0)
        {
            var events = staged.SelectMany(s => s.Batch).ToList();
            try { await _services.Log.AppendAsync(GameId, events, CancellationToken.None); }
            catch (DuplicateSequenceException ex) { await FaultAsync($"another writer holds this game (seq {ex.Seq})"); return true; }
            catch (Exception ex) { await FaultAsync($"the append at seq {events[0].Seq} failed: {ex.Message}"); return true; }
        }
        foreach (var (batch, sealedBefore) in staged)
        {
            foreach (var c in _clients.ToList()) await c.SendAsync(new EventsMessage(EventFilter.ForViewer(batch, TeamsOf(c), sealedBefore)));
            foreach (var w in _watchers.ToList()) await w.SendAsync(new EventsMessage(EventFilter.ForViewer(batch, (Guid?)null, sealedBefore)));
        }
        if (staged.Count == 0) return true;
        await BroadcastFactsIfChangedAsync();
        if (!endedBefore && Play.Projection.Ended is not null && Info.Status == GameStatus.Playing) await MarkEndedAsync();
        return true;
    }

    /// <summary>Seq assigned here, the batch applied and staged. Returns the fault reason when the server's own event does not apply to its own
    /// projection (or the projection throws on it); RunToRestAsync then rolls the run back.</summary>
    private string? Stage(IReadOnlyList<EventPayload> payloads, Guid? actor, string? warning)
    {
        if (payloads.Count == 0 || Play is null) return null;
        var sealedBefore = EventFilter.SealedOpenAsks(Play.Projection);
        var next = Play.Projection.LastSeq;
        var batch = payloads.Select((p, i) => new GameEvent(next + i + 1, actor, p, i == 0 ? warning : null)).ToList();
        _staged.Add((batch, sealedBefore));
        try { Play.Applier.ApplyAll(batch); }
        catch (Exception ex) { return $"this server's own event does not apply to its own projection: {ex}"; }
        return null;
    }

    /// <summary>The rest frame for the log as it stands: nothing further is coming right now, and the log ends at Seq (0 before it opens).</summary>
    internal AtRest Rest() => new(Play?.Projection.LastSeq ?? 0);

    /// <summary>Runs the engine until it has nothing to say, staging each batch. Every iteration reads the projection afresh. Returns a fault reason or null.</summary>
    private string? Advance()
    {
        if (Play is null || Engine is null) return null;
        if (WaitingForPlayers.Holds(Info, Play.Board, Play.Projection.LastSeq)) return null;   // deployment waits until every team has a player and an army
        for (var i = 0; i < 10_000; i++)
        {
            Emit emit;
            try { emit = Engine.Advance(Play); }
            catch (Exception ex) { return $"the engine threw in Advance: {ex}"; }
            if (emit.Events.Count == 0) return null;
            if (Stage(emit.Events, null, emit.Warning) is { } fault) return fault;
        }
        _log.LogError("Game {GameId}: the engine advanced 10000 times without resting; stopping.", GameId);
        return null;
    }

    private async Task FaultAsync(string reason)
    {
        Faulted = true;
        _log.LogError("Game {GameId} faulted: {Reason}", GameId, reason);
        // The reason can carry exception text; the peer gets a fixed phrase and the log has the rest (security review L3).
        foreach (var c in _clients.Concat(_watchers).ToList()) await c.CloseAsync(WebSocketCloseStatus.InternalServerError, "this game session has faulted");
        _clients.Clear(); _watchers.Clear();
    }

    /// <summary>Finishes queued work, closes every socket with 1001, and stops the loop. Everything durable was already written when it was queued.
    /// Idempotent: a faulted session already has no clients to close, and the registry may drain it once itself (superseding it in
    /// GetOrHydrateAsync) and again through ReleaseIfEmptyAsync on the same instance; the second call is a no-op rather than a
    /// write to an already-completed mailbox.</summary>
    public async Task DrainAsync(string reason)
    {
        if (_drained) return;
        _drained = true;
        await WhenNotDrained(RunAsync(async () => { foreach (var c in _clients.Concat(_watchers).ToList()) await c.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, reason); _clients.Clear(); _watchers.Clear(); }));
        _mailbox.Writer.TryComplete();
        if (_loop is not null) await _loop;
    }
}
