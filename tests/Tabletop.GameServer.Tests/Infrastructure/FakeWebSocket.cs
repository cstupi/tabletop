using System.Net.WebSockets;
using System.Text;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A server-side socket for SocketConnection's unit tests: sends wait on Gate (a stalled peer until it opens), and every sent
/// frame and the close are recorded. Receive returns Inbound's frames, then a close.</summary>
public sealed class FakeWebSocket : WebSocket
{
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Sent { get; } = new();
    public (WebSocketCloseStatus Status, string? Reason)? ClosedWith { get; private set; }
    public Exception? ThrowOnSend { get; set; }
    public Queue<string> Inbound { get; } = new();
    WebSocketState _state = WebSocketState.Open;

    public FakeWebSocket(bool open = false) { if (open) Gate.SetResult(); }

    public override WebSocketCloseStatus? CloseStatus => ClosedWith?.Status;
    public override string? CloseStatusDescription => ClosedWith?.Reason;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;
    public override void Abort() => _state = WebSocketState.Aborted;
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        if (statusDescription is not null && Encoding.UTF8.GetByteCount(statusDescription) > 123) throw new ArgumentException("close reason over 123 bytes");
        ClosedWith = (closeStatus, statusDescription);
        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }
    public override void Dispose() { }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (Inbound.TryDequeue(out var text))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            bytes.CopyTo(buffer.Array!, buffer.Offset);
            return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
        }
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
    }

    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        await Gate.Task.WaitAsync(cancellationToken);
        if (ThrowOnSend is { } ex) throw ex;
        lock (Sent) Sent.Add(Encoding.UTF8.GetString(buffer));
    }
}
