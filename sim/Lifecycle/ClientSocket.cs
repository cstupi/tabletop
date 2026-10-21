using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Lifecycle;

/// <summary>An edit or command the server refused, with its reason and detail.</summary>
public sealed class RefusedException(Refused refused, string what)
    : Exception($"{what} was refused: {refused.Reason} {refused.Detail}")
{
    public Refused Refused { get; } = refused;
}

/// <summary>
/// One account's socket as the client holds it outside play: the connect, then lobby and setup edits, the wait for players, and the replay
/// watcher (`/ws?watch=1`). It keeps what the client keeps from the frames: the latest GameInfo, Board and rest, and every event in order.
/// Play itself goes through SocketDriver. A lobby edit is answered by a broadcast GameInfo and a setup edit by a broadcast Board, neither
/// naming the edit, so each wait is on a condition of the state, or on the Refused that names the edit.
/// </summary>
public sealed class ClientSocket : IAsyncDisposable
{
    readonly WebSocket _socket;
    readonly Channel<SocketMessage> _inbox = Channel.CreateUnbounded<SocketMessage>();
    readonly CancellationTokenSource _stopping = new();
    readonly Task _reader;
    Exception? _fault;
    readonly List<GameEvent> _events = [];

    ClientSocket(WebSocket socket, string name)
    {
        _socket = socket;
        Name = name;
        _reader = Task.Run(() => SocketFrames.PumpAsync(_socket, _inbox.Writer, ex => _fault ??= ex, _stopping.Token), CancellationToken.None);
    }

    /// <summary>How long one wait may take before the socket counts as hung.</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public string Name { get; }
    public Welcome Welcome { get; private set; } = null!;
    public GameInfo Game { get; private set; } = null!;
    public Board? Board { get; private set; }
    /// <summary>The latest AtRest's seq: the log's high-water mark as far as this socket knows; -1 before the first.</summary>
    public long Rest { get; private set; } = -1;
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Forgets the latest rest, so a wait for Rest &gt;= 0 is a wait for the next AtRest (EndSetup's, a late army's).</summary>
    public void ExpectRest() => Rest = -1;

    /// <summary>Connects and reads to the connect's own AtRest (Welcome, the board from setup on, the log once playing). A Rejected throws.</summary>
    public static async Task<ClientSocket> OpenAsync(IWebSocketConnector connector, string name, string jwt, Guid gameId, CancellationToken ct)
    {
        // The frame is built before the socket opens: the server's handshake timeout runs from the open.
        var connect = Encoding.UTF8.GetBytes(WireJson.Serialize(new Connect(jwt, gameId, ProtocolVersion.Current)));
        var client = new ClientSocket(await connector.ConnectAsync(ct), name);
        try
        {
            await client._socket.SendAsync(connect, WebSocketMessageType.Text, true, ct);
            var first = await client.ReadAsync("the welcome", ct);
            if (first is Rejected rejected) throw new InvalidOperationException($"{name}'s connect was rejected: {rejected.Reason}");
            if (first is not Welcome welcome) throw new InvalidOperationException($"{name} expected a welcome, got {first.GetType().Name}");
            client.Welcome = welcome;
            client.Game = welcome.Game;
            await client.UntilAsync(() => client.Rest >= 0, "the connect's rest", ct);
            return client;
        }
        catch { await client.DisposeAsync(); throw; }
    }

    /// <summary>A lobby edit, waited on until the broadcast GameInfo shows it landed. A refusal throws RefusedException.</summary>
    public async Task<GameInfo> EditAsync(LobbyEdit edit, Func<GameInfo, bool> landed, CancellationToken ct)
    {
        var id = await SendEditAsync(edit);
        await UntilAsync(() => landed(Game), $"{Name}'s {edit.GetType().Name} to land", ct, id);
        return Game;
    }

    /// <summary>A setup edit, waited on until the broadcast Board shows it landed.</summary>
    public async Task<Board> EditAsync(SetupEdit edit, Func<Board, bool> landed, CancellationToken ct)
    {
        var id = await SendEditAsync(edit);
        await UntilAsync(() => Board is { } b && landed(b), $"{Name}'s {edit.GetType().Name} to land", ct, id);
        return Board!;
    }

    /// <summary>An edit expected to be refused: the Refused that names it.</summary>
    public async Task<Refused> RefusedEditAsync(Edit edit, CancellationToken ct)
    {
        var id = await SendEditAsync(edit);
        try { await UntilAsync(() => false, $"the refusal of {Name}'s {edit.GetType().Name}", ct, id); }
        catch (RefusedException refused) { return refused.Refused; }
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>A command expected to be refused (a watcher's): the Refused that names it.</summary>
    public async Task<Refused> RefusedCommandAsync(Command command, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await SendAsync(new CommandMessage(id, command));
        try { await UntilAsync(() => false, $"the refusal of {Name}'s {command.GetType().Name}", ct, id); }
        catch (RefusedException refused) { return refused.Refused; }
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>Reads frames, keeping state, until the condition holds; a Refused naming `refusedId` throws RefusedException.</summary>
    public async Task UntilAsync(Func<bool> condition, string what, CancellationToken ct, Guid? refusedId = null)
    {
        while (!condition())
            if (Take(await ReadAsync(what, ct)) is { } refused && refused.MessageId == refusedId)
                throw new RefusedException(refused, $"{Name}'s {what}");
    }

    async Task<Guid> SendEditAsync(Edit edit)
    {
        var id = Guid.CreateVersion7();
        await SendAsync(new EditMessage(id, edit));
        return id;
    }

    Task SendAsync(SocketMessage message) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    Refused? Take(SocketMessage frame)
    {
        switch (frame)
        {
            case GameInfoMessage g: Game = g.Game; break;
            case BoardMessage b: Board = b.Board; break;
            case EventsMessage e: _events.AddRange(e.Events); break;
            case AtRest r: Rest = r.Seq; break;
            case Refused r: return r;
        }
        return null;
    }

    async Task<SocketMessage> ReadAsync(string what, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(Timeout);
        try { return await _inbox.Reader.ReadAsync(window.Token); }
        catch (Exception ex) when (ex is ChannelClosedException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new TimeoutException($"{Name} waited {Timeout.TotalSeconds:0}s for {what}"
                + (_fault is null ? "" : $" (the socket failed: {_fault.Message})"), _fault);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (_socket.State == WebSocketState.Open) await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "lifecycle finished", window.Token);
        }
        catch (Exception) { }
        await Task.WhenAny(_reader, Task.Delay(TimeSpan.FromSeconds(2)));
        await _stopping.CancelAsync();
        _socket.Dispose();
        _stopping.Dispose();
    }
}
