using System.Net.WebSockets;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class ClientGameSocketTests
{
    /// <summary>A frame for a socket that is not open fails its send: an answer dropped without a word leaves the decision box waiting on a
    /// reply that never comes (owner, 2026-11-22: "now my game is stuck"); a failed send shows on the card and draws the Ask again.</summary>
    [Fact]
    public async Task A_send_on_a_socket_that_is_not_open_fails()
    {
        var socket = new ClientGameSocket();
        await Assert.ThrowsAsync<WebSocketException>(() => socket.SendAsync(new CommandMessage(Guid.CreateVersion7(), new Undo(1))));
    }
}
