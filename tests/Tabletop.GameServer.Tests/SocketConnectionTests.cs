using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>The outbound queue is bounded by count and bytes; presence coalesces per participant; close reasons fit a close frame.</summary>
public class SocketConnectionTests
{
    static SocketConnection Connection(FakeWebSocket socket, int messages = 8, long bytes = 1 << 20) =>
        new(socket, Guid.NewGuid(), Guid.NewGuid(), NullLogger.Instance, new SocketLimits(messages, bytes));

    static PresenceMessage Presence(Guid who, double x) => new(new Presence(who, new Cursor(new(x, 0, 0), null), null, [], null));

    [Fact]
    public async Task A_peer_that_stops_reading_is_closed_1013_once_the_queue_is_full()
    {
        var socket = new FakeWebSocket();
        var c = Connection(socket, messages: 8);
        var sending = c.RunSendLoopAsync(CancellationToken.None);
        for (var i = 0; i < 20; i++) await c.SendAsync(new AtRest(i));
        Assert.Equal((WebSocketCloseStatus)1013, c.CloseRequested?.Status);   // try again later
        socket.Gate.SetResult();
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((WebSocketCloseStatus)1013, socket.ClosedWith?.Status);
    }

    [Fact]
    public async Task The_byte_bound_closes_too()
    {
        var socket = new FakeWebSocket();
        var c = Connection(socket, messages: 1000, bytes: 2000);
        _ = c.RunSendLoopAsync(CancellationToken.None);
        for (var i = 0; i < 100; i++) await c.SendAsync(new AtRest(i));
        Assert.Equal((WebSocketCloseStatus)1013, c.CloseRequested?.Status);
    }

    [Fact]
    public async Task Presence_coalesces_per_participant_and_the_latest_wins()
    {
        var socket = new FakeWebSocket();
        var c = Connection(socket, messages: 8);
        var sending = c.RunSendLoopAsync(CancellationToken.None);
        var who = Guid.NewGuid();
        for (var i = 0; i < 500; i++) await c.SendAsync(Presence(who, i));
        Assert.Null(c.CloseRequested);
        socket.Gate.SetResult();
        await Wait.UntilAsync(() => socket.Sent.Count >= 1, "nothing sent");
        await c.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        var xs = socket.Sent.Select(WireJson.Deserialize).OfType<PresenceMessage>().Select(p => p.Presence.Cursor!.Point.X).ToList();
        Assert.Equal(499, xs.Last());
        Assert.True(xs.Count <= 2, $"sent {xs.Count} presence frames");
    }

    [Fact]
    public async Task A_close_reason_is_cut_to_123_utf8_bytes()
    {
        var socket = new FakeWebSocket(open: true);
        var c = Connection(socket);
        var sending = c.RunSendLoopAsync(CancellationToken.None);
        await c.CloseAsync(WebSocketCloseStatus.PolicyViolation, new string('é', 300));
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.ClosedWith?.Status);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(socket.ClosedWith!.Value.Reason!) <= 123);
    }

    [Fact]
    public async Task Any_send_failure_ends_the_loop_quietly()
    {
        var socket = new FakeWebSocket(open: true) { ThrowOnSend = new InvalidOperationException("boom") };
        var c = Connection(socket);
        var sending = c.RunSendLoopAsync(CancellationToken.None);
        await c.SendAsync(new AtRest(1));
        await sending.WaitAsync(TimeSpan.FromSeconds(5));   // does not throw
        Assert.True(c.Closing.IsCancellationRequested);
    }

    [Fact]
    public async Task Closing_cancels_the_read_token_after_the_close_frame()
    {
        var socket = new FakeWebSocket(open: true);
        var c = Connection(socket);
        var sending = c.RunSendLoopAsync(CancellationToken.None);
        Assert.False(c.Closing.IsCancellationRequested);
        await c.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced by a newer connection");
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        await Wait.UntilAsync(() => c.Closing.IsCancellationRequested, "the read token never cancelled");
    }
}
