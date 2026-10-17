using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>How much a connection may hold for a peer that is not reading: messages and serialized bytes.</summary>
public sealed record SocketLimits(int OutboundMessages, long OutboundBytes);

/// <summary>A frame as read: the message, and whether every string in it is free of control and bidi-override characters (InboundText).</summary>
public sealed record InboundFrame(SocketMessage Message, bool CleanText);

/// <summary>A frame the peer sent that the protocol does not allow (binary, too large, unreadable). Closes with Status.</summary>
public sealed class ProtocolBreachException(WebSocketCloseStatus status, string reason) : ProtocolViolationException(reason)
{
    public WebSocketCloseStatus Status { get; } = status;
}

/// <summary>
/// One socket. Frames are read by the handshake's loop and posted to the session; sends go through a bounded queue so the mailbox never
/// waits on a slow peer. Messages are serialized on the way in, so the queue is bounded by bytes as well as count; a peer that falls
/// further behind is closed 1013. Presence is state, not history: each participant's latest value replaces the queued one.
/// </summary>
public sealed class SocketConnection(WebSocket socket, Guid participantId, Guid accountId, ILogger log, SocketLimits limits) : ISessionClient
{
    public const int MaxCloseReasonBytes = 123;
    public const WebSocketCloseStatus TryAgainLater = (WebSocketCloseStatus)1013;
    static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);

    abstract record Outbound;
    sealed record Frame(byte[] Bytes) : Outbound;
    sealed record PresenceSlot(Guid Participant) : Outbound;

    private readonly Channel<Outbound> _outbound = Channel.CreateUnbounded<Outbound>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<Guid, byte[]> _presence = new();
    private readonly object _gate = new();
    private int _queuedMessages;
    private long _queuedBytes;
    private readonly CancellationTokenSource _closing = new();

    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public Guid AccountId { get; } = accountId;
    /// <summary>The close this connection was asked for, first one wins; null while open.</summary>
    public (WebSocketCloseStatus Status, string Reason)? CloseRequested { get; private set; }
    /// <summary>Cancelled once the close frame is out (plus a short grace for the peer's reply) or the send loop has died: the read loop stops on it.</summary>
    public CancellationToken Closing => _closing.Token;

    public ValueTask SendAsync(SocketMessage message)
    {
        if (CloseRequested is not null) return ValueTask.CompletedTask;
        var bytes = Encoding.UTF8.GetBytes(WireJson.Serialize(message));
        lock (_gate)
        {
            if (message is PresenceMessage p)
            {
                var who = p.Presence.ParticipantId;
                if (_presence.TryGetValue(who, out var queued)) { _queuedBytes += bytes.Length - queued.Length; _presence[who] = bytes; return ValueTask.CompletedTask; }
                _presence[who] = bytes;
                return Enqueue(new PresenceSlot(who), bytes.Length);
            }
            return Enqueue(new Frame(bytes), bytes.Length);
        }
    }

    private ValueTask Enqueue(Outbound item, int size)
    {
        if (_queuedMessages + 1 > limits.OutboundMessages || _queuedBytes + size > limits.OutboundBytes)
        {
            log.LogWarning("Connection {Connection}: the peer is {Messages} messages / {Bytes} bytes behind; closing", ConnectionId, _queuedMessages, _queuedBytes);
            return CloseAsync(TryAgainLater, "too far behind");
        }
        _queuedMessages++; _queuedBytes += size;
        _outbound.Writer.TryWrite(item);
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason)
    {
        lock (_gate)
        {
            if (CloseRequested is not null) return ValueTask.CompletedTask;
            CloseRequested = (status, Truncate(reason));
        }
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    /// <summary>A close frame's reason is at most 123 UTF-8 bytes (RFC 6455 §5.5); a longer one throws in the transport.</summary>
    public static string Truncate(string reason)
    {
        if (Encoding.UTF8.GetByteCount(reason) <= MaxCloseReasonBytes) return reason;
        var cut = reason.Length;
        while (cut > 0 && Encoding.UTF8.GetByteCount(reason.AsSpan(0, cut)) > MaxCloseReasonBytes) cut--;
        if (cut > 0 && char.IsHighSurrogate(reason[cut - 1])) cut--;
        return reason[..cut];
    }

    /// <summary>Writes queued frames until closed, then sends the close frame. Runs for the life of the socket and never throws.</summary>
    public async Task RunSendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in _outbound.Reader.ReadAllAsync(ct))
            {
                byte[]? bytes;
                lock (_gate)
                {
                    bytes = item switch { Frame f => f.Bytes, PresenceSlot s => _presence.Remove(s.Participant, out var b) ? b : null, _ => null };
                    _queuedMessages--;
                    if (bytes is not null) _queuedBytes -= bytes.Length;
                }
                if (bytes is not null) await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            var (status, reason) = CloseRequested ?? (WebSocketCloseStatus.NormalClosure, "bye");
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(status, reason, CancellationToken.None);
            _closing.CancelAfter(CloseGrace);
        }
        // The transport reports an abrupt disconnect as IOException, not WebSocketException, when the peer tears down the
        // connection without a close handshake (a crashed client): the same "gone, not a bug" ending as a clean close.
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
        { log.LogDebug(ex, "send loop ended for {Connection}", ConnectionId); _closing.Cancel(); }
        catch (Exception ex)
        { log.LogError(ex, "send loop failed for {Connection}", ConnectionId); _closing.Cancel(); }
    }

    /// <summary>Reads one text frame of at most cap bytes, or null at the peer's close. capOf names the cap of the kind the frame parses to
    /// (a layout loaded whole may be larger than a command); a frame over its kind's cap closes 1009 as one over cap does.</summary>
    public static async Task<InboundFrame?> ReadFrameAsync(WebSocket socket, int cap, CancellationToken ct, Func<SocketMessage, int>? capOf = null)
    {
        var buffer = new byte[Math.Min(cap, 16 * 1024)];
        using var frame = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new ProtocolBreachException(WebSocketCloseStatus.InvalidMessageType, "binary frames are not part of the protocol");
            frame.Write(buffer, 0, result.Count);
            if (frame.Length > cap) throw new ProtocolBreachException(WebSocketCloseStatus.MessageTooBig, "frame too large");
            if (result.EndOfMessage) break;
        }
        SocketMessage message;
        try { message = WireJson.Deserialize(Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length)); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new ProtocolBreachException(WebSocketCloseStatus.InvalidPayloadData, "unreadable frame"); }
        if (capOf is not null && frame.Length > capOf(message)) throw new ProtocolBreachException(WebSocketCloseStatus.MessageTooBig, "frame too large");
        return new InboundFrame(message, InboundText.IsClean(frame.GetBuffer().AsSpan(0, (int)frame.Length)));
    }
}
