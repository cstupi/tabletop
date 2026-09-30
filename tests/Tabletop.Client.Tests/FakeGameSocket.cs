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
