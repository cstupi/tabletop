using System.Net.WebSockets;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class EndpointTests
{
    [Fact]
    public async Task Ws_endpoint_accepts_a_websocket()
    {
        using var factory = new GameServerFactory();
        var client = factory.Server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task Plain_get_on_ws_is_400()
    {
        using var factory = new GameServerFactory();
        var response = await factory.CreateClient().GetAsync("/ws");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
