using System.Net.WebSockets;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>An ISessionClient that keeps its frames instead of writing them to a socket.</summary>
public sealed class RecorderClient(Guid participantId) : ISessionClient
{
    private readonly List<SocketMessage> _frames = [];
    private readonly object _gate = new();

    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public bool Closed { get; private set; }
    public WebSocketCloseStatus? CloseStatus { get; private set; }
    public string? CloseReason { get; private set; }

    public IReadOnlyList<SocketMessage> Frames { get { lock (_gate) return _frames.ToList(); } }

    public IReadOnlyList<SocketMessage> TakeFrames()
    {
        lock (_gate) { var taken = _frames.ToList(); _frames.Clear(); return taken; }
    }

    public ValueTask SendAsync(SocketMessage message) { lock (_gate) _frames.Add(message); return ValueTask.CompletedTask; }
    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason) { Closed = true; CloseStatus = status; CloseReason = reason; return ValueTask.CompletedTask; }
}
