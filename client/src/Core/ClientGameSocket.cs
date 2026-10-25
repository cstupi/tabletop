using System.Net.WebSockets;
using System.Text;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed class ClientGameSocket : IGameSocket
{
    const int MaxFrameBytes = 4 * 1024 * 1024;
    readonly ClientWebSocket _socket = new() { Options = { DangerousDeflateOptions = SocketCompression.Offer() } };
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
        try { if (_socket.State == WebSocketState.Open) await _socket.SendAsync(bytes, WebSocketMessageType.Text, SocketCompression.FlagsFor(frame), _stop.Token); }
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
