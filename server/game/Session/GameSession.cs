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
/// them one at a time. The log is appended before anything is broadcast; a row is written after the broadcast (setup's rows, and the end of the game).
/// </summary>
public sealed partial class GameSession
{
    private readonly Channel<Func<Task>> _mailbox = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<ISessionClient> _clients = new();
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
    public IGameRules Rules { get; }
    public TableState? Draft { get; internal set; }
    public GameState? Play { get; internal set; }
    public IRulesEngine? Engine { get; internal set; }
    public CommandDispatcher? Dispatcher { get; internal set; }
    public IReadOnlyList<ISessionClient> Clients => _clients;
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
            await AdvanceAsync();
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
    public Actor ActorOf(ISessionClient client) => new(client.ParticipantId, TeamOf(client.ParticipantId));

    public Task AttachAsync(ISessionClient client) => RunAsync(() => AttachCoreAsync(client));

    /// <summary>One connection per participant: a newer socket replaces the older one. The newcomer gets everyone else's current presence.</summary>
    private async Task AttachCoreAsync(ISessionClient client)
    {
        if (Faulted) { await client.CloseAsync(WebSocketCloseStatus.InternalServerError, "this game session has faulted"); return; }
        var replaced = _clients.FirstOrDefault(c => c.ParticipantId == client.ParticipantId);
        if (replaced is not null) { _clients.Remove(replaced); await replaced.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced by a newer connection"); }
        _clients.Add(client);
        foreach (var value in _presence.All.Where(v => v.ParticipantId != client.ParticipantId)) await client.SendAsync(new PresenceMessage(value));
    }

    /// <summary>The handshake detaches in a finally, outside every catch, and reaches a drained session by two routine paths:
    /// the SIGTERM drain completes the mailbox while read loops are still unwinding, and a re-hydration drains the faulted
    /// session its old loops still hold. A drained session has already closed and dropped every client, so this is a no-op
    /// rather than a throw into the /ws endpoint.</summary>
    public Task DetachAsync(ISessionClient client) => WhenNotDrained(RunAsync(async () =>
    {
        if (!_clients.Remove(client)) return;
        if (_clients.All(c => c.ParticipantId != client.ParticipantId)) { _presence.Remove(client.ParticipantId); await BroadcastAsync(new PresenceEnded(client.ParticipantId)); }
    }));

    /// <summary>Teardown that raced the drain it was tearing down: the work is already done by definition.</summary>
    private static async Task WhenNotDrained(Task teardown)
    {
        try { await teardown; } catch (SessionDrainedException) { }
    }

    public async Task BroadcastAsync(SocketMessage message, ISessionClient? except = null)
    {
        foreach (var c in _clients.ToList()) if (c != except) await c.SendAsync(message);
    }

    /// <summary>The log path: seq assigned here, appended first, applied, then broadcast with each viewer's sealed answers withheld.</summary>
    internal async Task AppendAsync(IReadOnlyList<EventPayload> payloads, Guid? actor, string? warning)
    {
        if (payloads.Count == 0 || Play is null) return;
        var sealedBefore = EventFilter.SealedOpenAsks(Play.Projection);
        var endedBefore = Play.Projection.Ended is not null;
        var next = Play.Projection.LastSeq;
        var batch = payloads.Select((p, i) => new GameEvent(next + i + 1, actor, p, i == 0 ? warning : null)).ToList();
        try { await _services.Log.AppendAsync(GameId, batch, CancellationToken.None); }
        catch (DuplicateSequenceException ex) { await FaultAsync($"another writer holds this game (seq {ex.Seq})"); return; }
        catch (Exception ex) { await FaultAsync($"the append at seq {batch[0].Seq} failed: {ex.Message}"); return; }
        // The batch is durable and the projection cannot take it: the server's own event names an id nothing created.
        // Faulting is the whole response. A resync would replay the same event from the same log and fault again.
        try { Play.Applier.ApplyAll(batch); }
        catch (ProjectionDesyncException ex) { await FaultAsync($"this server's own event does not apply to its own projection: {ex.Message}"); return; }
        foreach (var c in _clients.ToList()) await c.SendAsync(new EventsMessage(EventFilter.ForViewer(batch, TeamOf(c.ParticipantId), sealedBefore)));
        if (!endedBefore && Play.Projection.Ended is not null && Info.Status == GameStatus.Playing) await MarkEndedAsync();
    }

    /// <summary>The rest frame for the log as it stands: nothing further is coming right now, and the log ends at Seq (0 before it opens).</summary>
    internal AtRest Rest() => new(Play?.Projection.LastSeq ?? 0);

    /// <summary>Runs the engine until it has nothing to say. Every iteration reads the projection afresh.</summary>
    internal async Task AdvanceAsync()
    {
        if (Play is null || Engine is null || Faulted) return;
        for (var i = 0; i < 10_000; i++)
        {
            Emit emit;
            try { emit = Engine.Advance(Play); }
            catch (Exception ex) { await FaultAsync($"the engine threw in Advance: {ex.Message}"); return; }
            if (emit.Events.Count == 0) return;
            await AppendAsync(emit.Events, null, emit.Warning);
            if (Faulted) return;
        }
        _log.LogError("Game {GameId}: the engine advanced 10000 times without resting; stopping.", GameId);
    }

    private async Task FaultAsync(string reason)
    {
        Faulted = true;
        _log.LogError("Game {GameId} faulted: {Reason}", GameId, reason);
        foreach (var c in _clients.ToList()) await c.CloseAsync(WebSocketCloseStatus.InternalServerError, reason);
        _clients.Clear();
    }

    /// <summary>Finishes queued work, closes every socket with 1001, and stops the loop. Everything durable was already written when it was queued.
    /// Idempotent: a faulted session already has no clients to close, and the registry may drain it once itself (superseding it in
    /// GetOrHydrateAsync) and again through ReleaseIfEmptyAsync on the same instance; the second call is a no-op rather than a
    /// write to an already-completed mailbox.</summary>
    public async Task DrainAsync(string reason)
    {
        if (_drained) return;
        _drained = true;
        await WhenNotDrained(RunAsync(async () => { foreach (var c in _clients.ToList()) await c.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, reason); _clients.Clear(); }));
        _mailbox.Writer.TryComplete();
        if (_loop is not null) await _loop;
    }
}
