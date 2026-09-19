using System.Net.WebSockets;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>One connected socket as the session sees it. SocketConnection (Task 8) is the real one; tests record.</summary>
public interface ISessionClient
{
    Guid ConnectionId { get; }
    Guid ParticipantId { get; }
    ValueTask SendAsync(SocketMessage message);
    ValueTask CloseAsync(WebSocketCloseStatus status, string reason);
}
