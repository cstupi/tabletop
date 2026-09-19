using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>One socket. Frames are read by the handshake's loop and posted to the session; sends go through a queue so the mailbox never waits on a slow peer.</summary>
public sealed class SocketConnection(WebSocket socket, Guid participantId, ILogger log) : ISessionClient
{
    private const int MaxFrameBytes = 4 * 1024 * 1024;
    private readonly Channel<SocketMessage> _outbound = Channel.CreateUnbounded<SocketMessage>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<(WebSocketCloseStatus, string)?> _close = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;

    public ValueTask SendAsync(SocketMessage message) { _outbound.Writer.TryWrite(message); return ValueTask.CompletedTask; }

    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason)
    {
        _close.TrySetResult((status, reason));
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes queued frames until closed, then sends the close frame. Runs for the life of the socket.</summary>
    public async Task RunSendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync(ct))
                await socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, ct);
            var (status, reason) = (await _close.Task) ?? (WebSocketCloseStatus.NormalClosure, "bye");
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(status, reason, CancellationToken.None);
        }
        // The transport reports an abrupt disconnect as IOException, not WebSocketException, when the peer tears down the
        // connection without a close handshake (a crashed client): the same "gone, not a bug" ending as a clean close.
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException) { log.LogDebug(ex, "send loop ended for {Connection}", ConnectionId); }
    }

    public static async Task<SocketMessage?> ReadFrameAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        using var frame = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new ProtocolViolationException("binary frames are not part of the protocol");
            frame.Write(buffer.Array!, 0, result.Count);
            if (frame.Length > MaxFrameBytes) throw new ProtocolViolationException("frame too large");
            if (result.EndOfMessage) break;
        }
        try { return WireJson.Deserialize(Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length)); }
        catch (System.Text.Json.JsonException ex) { throw new ProtocolViolationException($"unreadable frame: {ex.Message}"); }
    }
}
