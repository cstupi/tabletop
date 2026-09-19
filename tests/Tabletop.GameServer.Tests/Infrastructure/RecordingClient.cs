using System.Net.WebSockets;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public sealed class RecordingClient(Guid participantId) : ISessionClient
{
    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public List<SocketMessage> Received { get; } = new();
    public (WebSocketCloseStatus Status, string Reason)? Closed { get; private set; }
    public ValueTask SendAsync(SocketMessage message) { Received.Add(message); return ValueTask.CompletedTask; }
    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason) { Closed = (status, reason); return ValueTask.CompletedTask; }
    public IEnumerable<GameEvent> Events => Received.OfType<EventsMessage>().SelectMany(m => m.Events);
}
