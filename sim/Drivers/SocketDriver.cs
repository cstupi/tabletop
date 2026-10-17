using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres
/// — the whole path architecture.md calls the only path (spec → Two drivers behind one interface).
/// </summary>
/// <remarks>
/// Every wait is on a log sequence number, never on a guess. An AtRest frame has no correlation id, but its
/// Seq is the log's high-water mark, and the server sends one after every command (broadcast when the batch
/// appended, to the sender alone when it did not) and one after every connect. So:
/// <list type="bullet">
/// <item>_knownHead is the high-water mark this driver has been told of. A submit's own AtRest is the first one
/// above it, or the one after its Refused; an AtRest at or below it is stale and is never accepted.</item>
/// <item>_restExpected is the mark the recorder must reach before a step may return. The recorder's AtRest
/// frames are recorded as they are read, wherever they are read (a drain included), so none is lost.</item>
/// </list>
/// An accepted command always appends at least one event (an override one, an answer two or more, an Undo one),
/// which is what makes "above the head" the test for "mine".
/// </remarks>
public sealed class SocketDriver : IGameDriver
{
    private sealed class Peer(Guid participantId, WebSocket socket)
    {
        public Guid ParticipantId { get; } = participantId;
        public WebSocket Socket { get; } = socket;
        /// <summary>One send at a time: a Reauth from the renewal loop and a command may meet on one socket.</summary>
        public SemaphoreSlim Sending { get; } = new(1, 1);
        public Task Renewal { get; set; } = Task.CompletedTask;
        public Channel<SocketMessage> Inbox { get; } = Channel.CreateUnbounded<SocketMessage>();
        public Task Reader { get; set; } = Task.CompletedTask;
        /// <summary>The first thing that broke the read loop (transport, framing, JSON), kept so a wait can report its cause.</summary>
        public Exception? Fault { get; set; }
    }

    private readonly CompiledScenario _scenario;
    private readonly IWebSocketConnector _connector;
    private readonly SocketCredentials _credentials;
    private readonly EventApplier _applier;
    private readonly List<Peer> _players = [];
    private readonly List<Peer> _all = [];                  // every socket ever opened, for disposal
    private bool _disposed;
    private readonly List<GameEvent> _incoming = [];        // read off the recorder's socket, not yet applied
    private readonly List<GameEvent> _handout = [];         // applied, not yet handed to a caller
    private readonly CancellationTokenSource _stopping = new();
    private Peer _recorder = null!;
    private long _recorderRest = -1;                        // the highest AtRest.Seq read off the recorder's socket
    private long _restExpected;                             // the recorder may not step until it has read this
    private long _knownHead;                                // the log's high-water mark as far as any socket has told us

    private readonly TimeSpan _renewBefore;

    private SocketDriver(CompiledScenario scenario, IWebSocketConnector connector, SocketCredentials credentials, TimeSpan renewBefore)
    {
        _scenario = scenario;
        _connector = connector;
        _credentials = credentials;
        _renewBefore = renewBefore;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    /// <summary>How long one wait for the server may take before it counts as hung. Not a quiescence guess.</summary>
    public TimeSpan RestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The server closes a socket 1008 at its session's expiry unless a Reauth moved it on, so with `credentials.Refresh` each socket
    /// sends one `renewBefore` (default a minute) ahead of its token's `exp`.
    /// </summary>
    public static async Task<SocketDriver> StartAsync(CompiledScenario scenario, IWebSocketConnector connector,
        SocketCredentials credentials, CancellationToken ct, TimeSpan? renewBefore = null)
    {
        var driver = new SocketDriver(scenario, connector, credentials, renewBefore ?? TimeSpan.FromMinutes(1));
        try { await driver.StartAsync(ct); }
        catch { await driver.DisposeAsync(); throw; }   // a rejected connect leaves no socket, reader or pinned session
        return driver;
    }

    public string Name => "socket";
    public Guid GameId => _scenario.Info.Id;
    public GameInfo Info => _scenario.Info;
    public Board Board => _scenario.Board;
    public IReadOnlyList<Guid> TeamIds => _scenario.TeamIds;
    public Guid ParticipantFor(Guid teamId) => _scenario.ParticipantFor(teamId);
    public DriverMode Mode { get; set; } = DriverMode.Run;

    public async Task StartAsync(CancellationToken ct)
    {
        // The recorder connects first, so its stream carries the whole log from the start. Connect appends
        // nothing, so its AtRest goes to the connecting client alone: waiting for each socket's own says that
        // socket has the whole log, where draining whatever had arrived raced it.
        var recorderIndex = _scenario.Info.Participants.Count - 1;
        _recorder = await OpenAsync(_scenario.RecorderParticipantId, recorderIndex, ct);
        await AwaitConnectRestAsync(ct);
        for (var i = 0; i < _scenario.ParticipantIds.Count; i++)
        {
            var player = await OpenAsync(_scenario.ParticipantIds[i], i, ct);
            _players.Add(player);
            _knownHead = Math.Max(_knownHead, await ConnectRestAsync(player, ct));
        }
        Absorb();
    }

    private async Task<Peer> OpenAsync(Guid participantId, int credentialIndex, CancellationToken ct)
    {
        // The frame is built before the socket opens: the server's handshake timeout runs from the open, and a cold
        // serializer's first use can take seconds on a loaded machine.
        var connect = Frame(new Connect(_credentials.For(credentialIndex), GameId, ProtocolVersion.Current));
        var socket = await _connector.ConnectAsync(ct);
        var peer = new Peer(participantId, socket);
        _all.Add(peer);
        peer.Reader = Task.Run(() => ReadLoopAsync(peer), CancellationToken.None);
        await peer.Socket.SendAsync(connect, WebSocketMessageType.Text, true, CancellationToken.None);
        var welcome = await ReadAsync(peer, ct);
        if (welcome is Rejected rejected) throw new InvalidOperationException($"the server rejected the connection: {rejected.Reason}");
        if (welcome is not Welcome w) throw new InvalidOperationException($"expected a welcome, got {welcome.GetType().Name}");
        if (w.ParticipantId != participantId)
            throw new InvalidOperationException($"the JWT at index {credentialIndex} named participant {w.ParticipantId:N}, not {participantId:N}");
        if (_credentials.Refresh is { } refresh)
            peer.Renewal = Task.Run(() => RenewLoopAsync(peer, credentialIndex, _credentials.For(credentialIndex), refresh), CancellationToken.None);
        return peer;
    }

    /// <summary>Reads the recorder to its connect-time AtRest, keeping the log it delivers on the way.</summary>
    private async Task AwaitConnectRestAsync(CancellationToken ct)
    {
        _recorderRest = -1;
        while (_recorderRest < 0) Take(await ReadAsync(_recorder, ct));
        _restExpected = _recorderRest;
        _knownHead = Math.Max(_knownHead, _recorderRest);
    }

    /// <summary>A player's own connect AtRest; its events are the recorder's concern and are dropped.</summary>
    private async Task<long> ConnectRestAsync(Peer player, CancellationToken ct)
    {
        while (true)
            if (await ReadAsync(player, ct) is AtRest rest) return rest.Seq;
    }

    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var peer = _players.FirstOrDefault(p => p.ParticipantId == participantId)
            ?? throw new ArgumentException($"no socket for participant {participantId}", nameof(participantId));
        var commandId = Guid.CreateVersion7();
        var head = _knownHead;
        await SendAsync(peer, new CommandMessage(commandId, command));

        // Only this command's own rest frame ends the wait. This socket may already hold AtRest frames from
        // the connect and from other participants' batches; every one of those is at or below the head.
        // Refusals come before that command's AtRest, so after a Refused the next AtRest is the one.
        Refused? refusal = null;
        string? warning = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RestTimeout);
        long restSeq;
        while (true)
        {
            var frame = await ReadAsync(peer, deadline.Token, ct);
            if (frame is Refused r && r.MessageId == commandId) refusal = r;
            else if (frame is EventsMessage batch) warning ??= batch.Events.FirstOrDefault(e => e.Seq > head && e.Actor == participantId)?.Warning;
            else if (frame is AtRest rest && (refusal is not null || rest.Seq > head)) { restSeq = rest.Seq; break; }
        }

        // An accepted command appended, so the recorder has a batch and the same rest frame coming.
        _knownHead = Math.Max(_knownHead, restSeq);
        if (refusal is null) _restExpected = Math.Max(_restExpected, restSeq);
        Absorb();
        return refusal is null ? new SubmitResult.Accepted(warning) : new SubmitResult.Rejected(refusal.Reason, refusal.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)
    {
        Absorb();
        return Task.FromResult(Handout());
    }

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        // Wait on the sequence the last accepted command rested at, not on a frame: a rest frame already read
        // by an earlier drain still counts, and a refused or empty command leaves nothing to wait for.
        Absorb();
        if (_recorderRest < _restExpected)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(RestTimeout);
            while (_recorderRest < _restExpected) Take(await ReadAsync(_recorder, deadline.Token, ct));
            Absorb();
        }
        // One connection delivers in order, so every EventsMessage of a batch preceded its AtRest. A mismatch
        // is a lost, reordered or duplicated frame, and it is a fault rather than a step.
        if (_applier.Projection.LastSeq != _recorderRest)
            throw new DriverFaultException(
                $"the server rested at seq {_recorderRest} but the recorder's stream reached {_applier.Projection.LastSeq}");
        var events = Handout();
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    /// <summary>Reconnect: the same participant, a second socket. The first is closed by the server with 1000.</summary>
    public async Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken ct)
    {
        Absorb();
        var old = _recorder;
        _incoming.Clear();
        _recorder = await OpenAsync(old.ParticipantId, _scenario.Info.Participants.Count - 1, ct);
        await AwaitConnectRestAsync(ct);
        // Its log arrives again from seq 1; what is already applied is skipped.
        Absorb();
        await Task.WhenAny(old.Reader, Task.Delay(TimeSpan.FromSeconds(5), ct));
        return old.Socket.CloseStatus;
    }

    /// <summary>
    /// Everything already on the recorder's socket goes to the projection, and every other socket's backlog
    /// is dropped. AtRest frames are recorded, not discarded, so a drain cannot cost a step its rest.
    /// </summary>
    private void Absorb()
    {
        while (_recorder.Inbox.Reader.TryRead(out var frame)) Take(frame);
        foreach (var player in _players) while (player.Inbox.Reader.TryRead(out _)) { }
        if (_recorder.Fault is { } fault) throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq, fault);
        if (_incoming.Count == 0) return;
        var ordered = _incoming.OrderBy(e => e.Seq).ToList();
        _incoming.Clear();
        foreach (var e in ordered)
        {
            if (e.Seq <= _applier.Projection.LastSeq) continue;
            _applier.Apply(e);
            _handout.Add(e);
        }
    }

    /// <summary>A recorder frame: events are kept for Absorb, a rest frame raises the mark; anything else is of no use here.</summary>
    private void Take(SocketMessage frame)
    {
        switch (frame)
        {
            case EventsMessage message: _incoming.AddRange(message.Events); break;
            case AtRest rest: _recorderRest = Math.Max(_recorderRest, rest.Seq); break;
        }
    }

    private IReadOnlyList<GameEvent> Handout()
    {
        var taken = _handout.ToList();
        _handout.Clear();
        return taken;
    }

    private Task<SocketMessage> ReadAsync(Peer peer, CancellationToken ct) => ReadAsync(peer, ct, ct);

    /// <summary>One frame, bounded by RestTimeout (or by `deadline` when the caller holds one). A dead connection is a timeout too.</summary>
    private async Task<SocketMessage> ReadAsync(Peer peer, CancellationToken deadline, CancellationToken caller)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        if (deadline == caller) window.CancelAfter(RestTimeout);
        try { return await peer.Inbox.Reader.ReadAsync(window.Token); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq, peer.Fault);
        }
        catch (ChannelClosedException)
        {
            // A reader fault is the cause; a plain close carries none.
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq, peer.Fault);
        }
    }

    private static byte[] Frame(SocketMessage message) => Encoding.UTF8.GetBytes(WireJson.Serialize(message));

    private static async Task SendAsync(Peer peer, SocketMessage message)
    {
        var frame = Frame(message);
        await peer.Sending.WaitAsync();
        try { await peer.Socket.SendAsync(frame, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { peer.Sending.Release(); }
    }

    /// <summary>Sends a fresh session `_renewBefore` ahead of each expiry, until the driver stops or the socket goes. A token that would
    /// renew again at once (a lifetime under the margin) ends the loop rather than spin.</summary>
    private async Task RenewLoopAsync(Peer peer, int credentialIndex, string jwt, Func<int, CancellationToken, Task<MintedSession>> refresh)
    {
        try
        {
            for (var renewed = false; MintedSession.ExpiryOf(jwt) is { } expires; renewed = true)
            {
                var wait = expires - DateTimeOffset.UtcNow - _renewBefore;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, _stopping.Token);
                else if (renewed) return;
                if (peer.Socket.State != WebSocketState.Open) return;
                jwt = (await refresh(credentialIndex, _stopping.Token)).Jwt;
                await SendAsync(peer, new Reauth(Guid.CreateVersion7(), jwt));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or WebSocketException or IOException or HttpRequestException) { }
    }

    private async Task ReadLoopAsync(Peer peer)
    {
        var buffer = new byte[4 * 1024 * 1024];
        try
        {
            while (peer.Socket.State == WebSocketState.Open)
            {
                var total = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (total == buffer.Length) throw new InvalidOperationException($"a frame larger than {buffer.Length} bytes");
                    result = await peer.Socket.ReceiveAsync(new Memory<byte>(buffer, total, buffer.Length - total), _stopping.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // A server fault closes with InternalServerError and the reason; keep both for the wait that fails.
                        peer.Fault ??= new IOException($"the server closed the socket: {peer.Socket.CloseStatus} {peer.Socket.CloseStatusDescription}");
                        peer.Inbox.Writer.TryComplete();
                        return;
                    }
                    total += result.Count;
                } while (!result.EndOfMessage);
                peer.Inbox.Writer.TryWrite(WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total)));
            }
        }
        catch (OperationCanceledException) { peer.Inbox.Writer.TryComplete(); }
        catch (Exception ex)
        {
            peer.Fault = ex;
            peer.Inbox.Writer.TryComplete(ex);
        }
        finally { peer.Inbox.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var peers = _all.ToList();
        // Close while the readers still run, so the server's close reply is read; its registry then drops the game.
        foreach (var peer in peers)
        {
            try
            {
                using var window = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "sim finished", window.Token);
            }
            catch (Exception) { }
        }
        await Task.WhenAny(Task.WhenAll(peers.Select(p => p.Reader)), Task.Delay(TimeSpan.FromSeconds(2)));
        await _stopping.CancelAsync();
        await Task.WhenAll(peers.Select(p => p.Renewal));
        foreach (var peer in peers) peer.Socket.Dispose();
        _stopping.Dispose();
    }
}
