using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content.Tests;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>The socket outlives the JWT it connected with (Reauth), and a reconnect neither drops nor throws a command sent during it.</summary>
public class SessionReconnectTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-reconnect-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;
    static readonly GameInfo Game = Samples.Game;
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Clay");
    const string Jwt1 = "a.jwt-1.z", Jwt2 = "a.jwt-2.z";

    public SessionReconnectTests()
    {
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets, new SessionStore(Path.Combine(_dir, "session.bin")),
            ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    async Task LoggedIn() { _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt1, Clay)); await _s.LoginWithPasswordAsync("c@example.com", "pw"); }

    [Fact]
    public async Task A_refresh_while_welcomed_hands_the_new_jwt_to_the_open_socket()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        var sock = _sockets.Last;
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        Assert.True(await _s.RefreshAsync());
        Assert.DoesNotContain(sock.Sent, f => f is Reauth);                 // not welcomed yet: the Connect frame is all that goes out
        sock.Deliver(new Welcome(Samples.P1, Game));
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-3.z", Clay));
        Assert.True(await _s.RefreshAsync());
        Assert.Equal(new Reauth("a.jwt-3.z"), sock.Sent.OfType<Reauth>().Single());
    }

    [Fact]
    public async Task A_refresh_outside_a_game_sends_nothing()
    {
        await LoggedIn();
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        Assert.True(await _s.RefreshAsync());
        Assert.Empty(_sockets.Created);
    }

    [Fact]
    public async Task A_command_sent_while_reconnecting_goes_out_after_the_next_welcome()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Drop(1008, "session expired");
        var second = await _sockets.ConnectedAsync(2);
        Assert.Equal(SessionPhase.Reconnecting, _s.Phase);
        Assert.True(_s.Queueing);
        var id = Guid.CreateVersion7();
        var sent = _s.SendCommandAsync(new Undo(1), id);
        Assert.False(sent.IsCompleted);
        Assert.IsType<Connect>(Assert.Single(second.Sent));
        second.Deliver(new Welcome(Samples.P1, Game));
        Assert.Equal(id, await sent);
        Assert.Equal(id, Assert.IsType<CommandMessage>(second.Sent[^1]).CommandId);
    }

    [Fact]
    public async Task A_queued_command_fails_with_the_reason_when_the_reconnect_goes_home()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Drop(1001, "gone");
        await _sockets.ConnectedAsync(2);
        var sent = _s.SendEditAsync(new CreateTeam());
        _sockets.Last.Deliver(new Rejected(RejectReason.NotAParticipant));
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => sent);
        Assert.Contains("disconnected", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outside_a_game_a_command_still_throws_not_connected()
    {
        await LoggedIn();
        Assert.False(_s.Queueing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.SendCommandAsync(new Undo(1)));
    }
}
