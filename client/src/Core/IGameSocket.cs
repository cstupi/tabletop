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
