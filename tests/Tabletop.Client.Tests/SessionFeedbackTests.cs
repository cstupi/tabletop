using System.Net;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>What the session tells the player: why a game closed, whether they watch, the account's name.</summary>
public class SessionFeedbackTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-session-feedback").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;
    static readonly GameInfo Game = Samples.Game;
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Player-1a2b");

    public SessionFeedbackTests()
    {
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets,
            new SessionStore(Path.Combine(_dir, "session.bin")), ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    async Task LoggedIn() { _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-1.z", Clay)); await _s.LoginWithPasswordAsync("c@example.com", "pw"); }

    async Task<FakeGameSocket> Joined(GameInfo game)
    {
        await LoggedIn();
        await _s.ConnectAsync(game.Id);
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, game));
        return sock;
    }

    static GameInfo Teamless(GameStatus status) => Game with { Status = status, Participants = Game.Participants.Select(p => p.Id == Samples.P1 ? p with { TeamId = null } : p).ToList() };

    [Fact]
    public async Task A_teamless_participant_watches_from_play_on_and_sends_no_presence_or_ping()
    {
        var sock = await Joined(Teamless(GameStatus.Playing));
        Assert.True(_s.IsWatching);
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        await _s.SendPingAsync(new Ping(Guid.Empty, new Point3(1, 2, 0), null));
        Assert.IsType<Connect>(Assert.Single(sock.Sent));
    }

    [Fact]
    public async Task While_deployment_waits_for_players_a_teamless_participant_may_still_take_the_empty_team()
    {
        var sock = await Joined(Teamless(GameStatus.Playing));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage([]));                                                   // the end of setup: the log is open and empty
        Assert.Equal(["Waiting for a player on Team 1", "Waiting for a player on Team 2"], _s.WaitingTeams.Select(w => w.Text));
        Assert.False(_s.IsWatching);
        sock.Deliver(new EventsMessage([new GameEvent(1, null, new ScoreSet(Samples.Team1, 1), null)]));
        Assert.Empty(_s.WaitingTeams);                                                         // a log that has begun waits for no one
        Assert.True(_s.IsWatching);
    }

    [Fact]
    public async Task One_account_on_both_teams_holds_both_and_is_no_watcher()
    {
        var game = Game with { Participants = [.. Game.Participants, new Participant(Guid.CreateVersion7(), Game.Participants[0].AccountId, "Clay", Samples.Team2, null)] };
        await Joined(game);
        Assert.Equal([Samples.Team1, Samples.Team2], _s.MyTeamIds);
        Assert.False(_s.IsWatching);
    }

    [Fact]
    public async Task Before_play_a_teamless_participant_is_choosing_a_team_not_watching()
    {
        var sock = await Joined(Teamless(GameStatus.Lobby));
        Assert.False(_s.IsWatching);
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        Assert.IsType<PresenceMessage>(sock.Sent[^1]);
    }

    [Fact]
    public async Task An_admin_watcher_watches_in_every_status_and_a_seated_player_never()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Guid.Empty, Game with { Status = GameStatus.Lobby }));
        Assert.True(_s.IsWatching);
        await _s.ConnectAsync(Game.Id);
        Assert.False(_s.IsWatching);                                    // mid-connect has no game: not watching
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        Assert.False(_s.IsWatching);
    }

    [Fact]
    public async Task A_rejection_leaves_home_a_line_saying_what_to_do()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Rejected(RejectReason.NotAParticipant));
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
        Assert.Equal(Feedback.Rejected(RejectReason.NotAParticipant), _s.LastCloseReason);
    }

    [Fact]
    public async Task A_replaced_socket_leaves_home_a_line_naming_the_other_window()
    {
        var sock = await Joined(Game);
        sock.Drop(1000, "replaced by a newer connection");
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
        Assert.Equal("This game was opened in another window, which took over.", _s.LastCloseReason);
    }

    [Fact]
    public async Task Each_reconnect_attempt_is_counted_until_a_welcome()
    {
        var sock = await Joined(Game);
        Assert.Equal(0, _s.ReconnectAttempt);
        sock.Drop(1013, "too far behind");
        await _sockets.ConnectedAsync(2);
        Assert.Equal((SessionPhase.Reconnecting, 1), (_s.Phase, _s.ReconnectAttempt));
        _sockets.Last.Drop(null, "Connection refused");
        await _sockets.ConnectedAsync(3);
        Assert.Equal(2, _s.ReconnectAttempt);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        Assert.Equal((SessionPhase.Connected, 0), (_s.Phase, _s.ReconnectAttempt));
    }

    [Fact]
    public async Task A_1008_refreshes_the_sign_in_before_it_reconnects_with_the_new_token()
    {
        var sock = await Joined(Game);
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-2.z", Clay));
        sock.Drop(1008, "session expired");
        await _sockets.ConnectedAsync(2);
        Assert.Equal("/v1/auth/session/refresh", _http.Requests[^1].RequestUri!.AbsolutePath);
        Assert.Equal("a.jwt-2.z", Assert.IsType<Connect>(_sockets.Last.Sent.Single()).SessionJwt);
    }

    [Fact]
    public async Task Rename_patches_the_account_and_takes_the_server_s_row()
    {
        await LoggedIn();
        var changed = 0; _s.AccountChanged += () => changed++;
        _http.Answer(HttpStatusCode.OK, new AccountRow(Clay.Id, "Clay"));
        await _s.RenameAsync("  Clay ");
        var request = _http.Requests[^1];
        Assert.Equal((HttpMethod.Patch, "/v1/account"), (request.Method, request.RequestUri!.AbsolutePath));
        Assert.Contains("\"name\":\"Clay\"", _http.Bodies[^1]);
        Assert.Equal(("Clay", 1), (_s.Account!.Name, changed));
    }

    [Fact]
    public async Task A_refused_rename_keeps_the_old_name_and_throws_the_server_s_detail()
    {
        await LoggedIn();
        _http.Answer(HttpStatusCode.UnprocessableEntity, "{\"title\":\"Unprocessable\",\"detail\":\"name is at most 64 characters.\"}", "application/problem+json");
        var e = await Assert.ThrowsAsync<ManagementException>(() => _s.RenameAsync("x"));
        Assert.Equal("name is at most 64 characters.", Feedback.Error(e, ""));
        Assert.Equal("Player-1a2b", _s.Account!.Name);
    }
}
