# Task 4: The socket seam, the real socket, and the presence table

**Files:**
- Create: `client/src/Core/IGameSocket.cs`, `client/src/Core/ClientGameSocket.cs`, `client/src/Core/PresenceTable.cs`
- Test: `tests/Tabletop.Client.Tests/FakeGameSocket.cs`, `tests/Tabletop.Client.Tests/PresenceTableTests.cs`

**Interfaces:**
- Consumes: `SocketMessage`, `WireJson`, `Presence` (Protocol).
- Produces: `sealed record SocketClosed(int? Code, string? Reason, Exception? Error)`; `interface IGameSocket : IAsyncDisposable { event Action<SocketMessage> FrameReceived; event Action<SocketClosed> Closed; Task ConnectAsync(Uri url, CancellationToken ct); Task SendAsync(SocketMessage frame); Task CloseAsync(); }`; `interface IGameSocketFactory { IGameSocket Create(); }`; `sealed class ClientGameSocket : IGameSocket` over `ClientWebSocket` (text frames, one `SocketMessage` per frame, a read loop that raises `FrameReceived` on its own thread and `Closed` once; `CloseAsync` half-closes with `CloseOutputAsync` under the send gate and cancels the loop and raises `Closed` in a `finally`); `sealed class ClientGameSocketFactory : IGameSocketFactory`; `sealed class PresenceTable` — `void Set(Presence)`, `void Remove(Guid participantId)`, `Presence? Of(Guid)`, `IReadOnlyCollection<Presence> All`, `void Clear()`. Test infrastructure `FakeGameSocket` (`Sent`, `Deliver(SocketMessage)`, `Drop(int? code, string? reason)`, `Connected`, `Url`, `SendFails`: an exception every send but `Connect` returns) and `FakeSocketFactory` (`Created` list).
- Both events fire off the Godot main thread; `Session` (Task 5) marshals them through `IDispatcher`. The game server's protocol (game server plan → Interfaces needed → Client shell plan): one text frame per message, `Connect` first, close codes `1000` (normal, or "replaced by a newer connection"), `1001` (server shutting down), `1002` (protocol error).

Spec: `architecture.md` → Transport (WebSockets; Godot's high-level multiplayer is not used); presence → Nothing here is written down (a presence value exists while a socket is open; the table is cleared on close), Connectedness is not a field (who is connected is which values exist).

- [ ] **Step 1: Write `IGameSocket.cs` and `PresenceTable.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed record SocketClosed(int? Code, string? Reason, Exception? Error);

/// <summary>One game socket. Events are raised on the socket's own thread; the Session posts them to the main thread.</summary>
public interface IGameSocket : IAsyncDisposable
{
    event Action<SocketMessage> FrameReceived;
    event Action<SocketClosed> Closed;
    Task ConnectAsync(Uri url, CancellationToken ct);
    Task SendAsync(SocketMessage frame);
    Task CloseAsync();
}

public interface IGameSocketFactory { IGameSocket Create(); }
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Other participants' latest presence. Existence is connectedness; cleared when our own socket closes.</summary>
public sealed class PresenceTable
{
    readonly Dictionary<Guid, Presence> _values = new();
    public void Set(Presence value) => _values[value.ParticipantId] = value;
    public void Remove(Guid participantId) => _values.Remove(participantId);
    public Presence? Of(Guid participantId) => _values.GetValueOrDefault(participantId);
    public IReadOnlyCollection<Presence> All => _values.Values;
    public void Clear() => _values.Clear();
}
```

- [ ] **Step 2: Write `ClientGameSocket.cs`**

```csharp
using System.Net.WebSockets;
using System.Text;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed class ClientGameSocket : IGameSocket
{
    const int MaxFrameBytes = 4 * 1024 * 1024;
    readonly ClientWebSocket _socket = new();
    readonly SemaphoreSlim _sendGate = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    int _closedRaised;

    public event Action<SocketMessage>? FrameReceived;
    public event Action<SocketClosed>? Closed;

    public async Task ConnectAsync(Uri url, CancellationToken ct)
    {
        await _socket.ConnectAsync(url, ct);
        _ = Task.Run(ReadLoopAsync);
    }

    public async Task SendAsync(SocketMessage frame)
    {
        var bytes = Encoding.UTF8.GetBytes(WireJson.Serialize(frame));
        await _sendGate.WaitAsync();
        try { if (_socket.State == WebSocketState.Open) await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token); }
        finally { _sendGate.Release(); }
    }

    public async Task CloseAsync()
    {
        try
        {
            await _sendGate.WaitAsync();
            try { if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
            catch (Exception ex) when (ex is WebSocketException or InvalidOperationException or ObjectDisposedException) { }
            finally { _sendGate.Release(); }
        }
        finally
        {
            _stop.Cancel();
            RaiseClosed(new SocketClosed(1000, "bye", null));
        }
    }

    async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var frame = new MemoryStream();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                frame.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { RaiseClosed(new SocketClosed((int?)_socket.CloseStatus, _socket.CloseStatusDescription, null)); return; }
                    frame.Write(buffer, 0, result.Count);
                    if (frame.Length > MaxFrameBytes) throw new WebSocketException("frame too large");
                } while (!result.EndOfMessage);
                FrameReceived?.Invoke(WireJson.Deserialize(Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length)));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is WebSocketException or System.Text.Json.JsonException or IOException or ObjectDisposedException or InvalidOperationException) { RaiseClosed(new SocketClosed(null, ex.Message, ex)); }
    }

    void RaiseClosed(SocketClosed closed) { if (Interlocked.Exchange(ref _closedRaised, 1) == 0) Closed?.Invoke(closed); }

    public async ValueTask DisposeAsync() { _stop.Cancel(); await Task.Yield(); _socket.Dispose(); }
}

public sealed class ClientGameSocketFactory : IGameSocketFactory { public IGameSocket Create() => new ClientGameSocket(); }
```

`CloseAsync` sends its close frame with `CloseOutputAsync`. `ClientWebSocket.CloseAsync` would wait to receive the answering close frame while the read loop is already receiving, and a second concurrent receive throws. The half-close leaves the answering frame to the read loop. It holds the send gate so it cannot interleave with a send. The widened catch covers a socket already closed or disposed. Cancelling the loop and raising `Closed` sit in a `finally`, so a failed close still ends the loop and tells the session once.

- [ ] **Step 3: Write `FakeGameSocket.cs` and the presence table test**

```csharp
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public sealed class FakeGameSocket : IGameSocket
{
    public readonly List<SocketMessage> Sent = new();
    public bool Connected { get; private set; }
    public Uri? Url { get; private set; }
    public event Action<SocketMessage>? FrameReceived;
    public event Action<SocketClosed>? Closed;
    public Task ConnectAsync(Uri url, CancellationToken ct) { Url = url; Connected = true; return Task.CompletedTask; }
    public Exception? SendFails { get; set; }
    public Task SendAsync(SocketMessage frame)
    {
        if (SendFails is { } error && frame is not Connect) return Task.FromException(error);
        Sent.Add(frame); return Task.CompletedTask;
    }
    public Task CloseAsync() { Connected = false; Closed?.Invoke(new SocketClosed(1000, "bye", null)); return Task.CompletedTask; }
    public void Deliver(SocketMessage frame) => FrameReceived?.Invoke(frame);
    public void Drop(int? code, string? reason) { Connected = false; Closed?.Invoke(new SocketClosed(code, reason, null)); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeSocketFactory : IGameSocketFactory
{
    public readonly List<FakeGameSocket> Created = new();
    public FakeGameSocket Last => Created[^1];
    public IGameSocket Create() { var s = new FakeGameSocket(); Created.Add(s); return s; }
}
```

```csharp
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class PresenceTableTests
{
    [Fact]
    public void Latest_wins_and_removal_ends_existence()
    {
        var t = new PresenceTable();
        var p = Guid.CreateVersion7();
        t.Set(new Presence(p, new Cursor(new Point3(1, 1, 0), null), null, [], null));
        t.Set(new Presence(p, new Cursor(new Point3(2, 2, 0), null), null, [], null));
        Assert.Equal(2, t.Of(p)!.Cursor!.Point.X);
        Assert.Single(t.All);
        t.Remove(p);
        Assert.Null(t.Of(p)); Assert.Empty(t.All);
    }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Client.Tests`; expect 12 passed. Commit: `Add the game socket seam, the ClientWebSocket adapter and the presence table`.**
