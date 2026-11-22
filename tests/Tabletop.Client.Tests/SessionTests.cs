using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class SessionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-session-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly List<ClientSettings> _persisted = new();
    readonly Session _s;
    // Read the sample game once: it is an expression-bodied property that mints fresh Guids for the game id
    // and the account id on every access, so two reads are two different games.
    static readonly GameInfo Game = Samples.Game;
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Clay");
    // Three dot-separated segments, because SessionStore.Read refuses anything that is not JWT-shaped (Task 2).
    const string Jwt1 = "a.jwt-1.z";

    public SessionTests()
    {
        var store = new SessionStore(Path.Combine(_dir, "session.bin"));
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets, store,
            ClientSettings.Default with { RememberMe = true }, _persisted.Add, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    async Task LoggedIn() { _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt1, Clay)); await _s.LoginWithPasswordAsync("c@example.com", "pw"); }
    /// <summary>The owner, 2026-11-14: "the team of 2 was unable to select their unit and see its range". The second player on a team, whose
    /// teammate brought the army: the team is theirs, they are not watching, and the teammate's unit is one they get weapon ranges for.</summary>
    [Fact]
    public async Task The_second_player_on_a_team_plays_its_units()
    {
        await LoggedIn();
        var mate = Guid.CreateVersion7();
        var game = Game with { Status = GameStatus.Playing, Participants = [.. Game.Participants, new Participant(mate, Guid.CreateVersion7(), "Mate", Samples.Team1, null)] };
        await _s.ConnectAsync(game.Id);
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(mate, game));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage([new GameEvent(1, Samples.P1, new ScoreSet(Samples.Team1, 1), null)]));
        Assert.Equal([Samples.Team1], _s.MyTeamIds);
        Assert.Equal(((Guid?)Samples.Team1, false), (_s.MyTeamId, _s.IsWatching));
        Assert.Equal(Samples.Team1, new Names(_s.Game!, _s.Projection!).TeamOfUnit(Samples.Unit));
        Assert.Equal(Samples.Unit, RangeRings.UnitFor(_s.Projection!, _s.Game!, _s.MyTeamId, Samples.Unit));
    }

    async Task<FakeGameSocket> ConnectedAtPlaying()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, Game));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage([new GameEvent(1, Samples.P1, new ScoreSet(Samples.Team1, 1), null), new GameEvent(2, Samples.P1, new ScoreSet(Samples.Team1, 2), null)]));
        return sock;
    }

    [Fact]
    public async Task A_rules_options_message_replaces_the_catalog_zone_legs_and_terrain_words()
    {
        var sock = await ConnectedAtPlaying();
        var next = new RulesOptionCatalog([new RulesOption("a", "A", [new RulesOptionValue("off", "Off")], "off")], []);
        var legs = new Tabletop.Geometry.ZoneLegs(2.0 / 3, 0.75);
        var words = new Dictionary<string, string> { ["see_in_and_out"] = "Dense" };
        var texts = new Dictionary<string, string> { ["cover"] = "+1 to Defense" };
        sock.Deliver(new RulesOptionsMessage(next) { Zones = legs, TerrainWords = words, TerrainRules = texts, MarkerRange = 4 });
        Assert.Equal(4, _s.MarkerRange);
        Assert.Same(next, _s.Options);
        Assert.Equal(legs, _s.Zones);
        Assert.Same(words, _s.TerrainWords);
        Assert.Same(texts, _s.TerrainRules);
    }

    [Fact]
    public async Task Login_stores_the_jwt_when_remembered_and_resume_refreshes_it()
    {
        await LoggedIn();
        Assert.Equal((SessionPhase.LoggedIn, "Clay"), (_s.Phase, _s.Account!.Name));
        Assert.Equal(Jwt1, new SessionStore(Path.Combine(_dir, "session.bin")).Read());
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-2.z", Clay));
        Assert.True(await _s.TryResumeAsync());
        Assert.Equal("/v1/auth/session/refresh", _http.Requests[^1].RequestUri!.AbsolutePath);
        Assert.Contains(Jwt1, _http.Bodies[^1]);
        _http.Answer(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\"}", "application/problem+json");
        Assert.False(await _s.TryResumeAsync());
        Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
    }

    [Fact]
    public async Task A_426_anywhere_is_the_update_required_phase()
    {
        _http.Answer((HttpStatusCode)426, "{\"title\":\"Update required\"}", "application/problem+json");
        await Assert.ThrowsAsync<UpdateRequiredException>(() => _s.LoginWithPasswordAsync("c@example.com", "pw"));
        Assert.Equal(SessionPhase.UpdateRequired, _s.Phase);
    }

    [Fact]
    public async Task Connect_sends_the_connect_frame_first_and_builds_the_projection_from_board_and_log()
    {
        var sock = await ConnectedAtPlaying();
        var connect = Assert.IsType<Connect>(sock.Sent[0]);
        Assert.Equal((Jwt1, Game.Id, ProtocolVersion.Current), (connect.SessionJwt, connect.GameId, connect.ClientVersion));
        Assert.Equal(new Uri("ws://127.0.0.1:5200/ws"), sock.Url);
        Assert.Equal((SessionPhase.Connected, Samples.P1, Samples.Team1), (_s.Phase, _s.ParticipantId, _s.MyTeamId));
        Assert.Equal(2, _s.Projection!.Scores[Samples.Team1]);
        Assert.Equal(2, _s.Log.Count);
        Assert.Null(_s.Draft);
    }

    [Fact]
    public async Task At_setup_the_board_is_the_draft_and_a_board_message_replaces_it()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game with { Status = GameStatus.Setup }));
        _sockets.Last.Deliver(new BoardMessage(new Board(TableStateTests.Sample(), [])));
        Assert.Equal(72, _s.Draft!.Size.Width); Assert.Null(_s.Projection);
        _sockets.Last.Deliver(new BoardMessage(new Board(TableStateTests.Sample() with { Size = new TableSize(44, 30) }, [])));
        Assert.Equal(44, _s.Draft!.Size.Width);
    }

    [Fact]
    public async Task The_combat_log_is_shared_until_the_log_or_a_pending_undo_changes()
    {
        var sock = await ConnectedAtPlaying();
        var state = _s.CombatLog!;
        Assert.Same(state, _s.CombatLog);
        await _s.UndoToAsync(1);
        var pending = _s.CombatLog!;
        Assert.NotSame(state, pending);
        Assert.Equal((1L, 0L), (state.Undo!.ToSeq, pending.Undo!.ToSeq));   // the next press steps back one more
        sock.Deliver(new EventsMessage([new GameEvent(3, Samples.P1, new Undone(1), null)]));
        Assert.NotSame(pending, _s.CombatLog);
    }

    [Fact]
    public async Task Undo_is_predicted_locally_until_the_server_answers()
    {
        var sock = await ConnectedAtPlaying();
        await _s.UndoToAsync(1);
        var cmd = Assert.IsType<CommandMessage>(sock.Sent[^1]);
        Assert.Equal(1, Assert.IsType<Undo>(cmd.Command).ToSeq);
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);          // predicted
        Assert.Equal(2, _s.Applier!.Projection.Scores[Samples.Team1]);   // the truth so far
        sock.Deliver(new EventsMessage([new GameEvent(3, Samples.P1, new Undone(1), null)]));
        Assert.Null(_s.PendingUndoTo);
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);
        await _s.UndoToAsync(0);
        var id = ((CommandMessage)sock.Sent[^1]).CommandId;
        sock.Deliver(new Refused(id, RefusalReason.Unknown, "no"));
        Assert.Null(_s.PendingUndoTo);
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);           // back to the truth
    }

    /// <summary>The owner, 2026-11-22: "I undid the choice of Rush and now my game is stuck." A refusal of the undo that lands before the
    /// send's continuation still ends the prediction: the undo names its command before it sends.</summary>
    [Fact]
    public async Task An_undo_refused_before_its_send_returns_ends_the_prediction()
    {
        var sock = await ConnectedAtPlaying();
        sock.Answering = frame => { if (frame is CommandMessage { Command: Undo } c) sock.Deliver(new Refused(c.CommandId, RefusalReason.Unknown, "no")); };
        await _s.UndoToAsync(1);
        Assert.Null(_s.PendingUndoTo);
        Assert.Same(_s.Applier!.Projection, _s.Projection);
    }

    [Fact]
    public async Task SendCommandAsync_uses_the_caller_s_id_and_the_plain_overload_mints_one()
    {
        var sock = await ConnectedAtPlaying();
        var id = Guid.CreateVersion7();
        Assert.Equal(id, await _s.SendCommandAsync(new Undo(1), id));
        Assert.Equal(id, Assert.IsType<CommandMessage>(sock.Sent[^1]).CommandId);
        var minted = await _s.SendCommandAsync(new Undo(1));
        Assert.NotEqual(Guid.Empty, minted);
        Assert.NotEqual(id, minted);
        Assert.Equal(minted, Assert.IsType<CommandMessage>(sock.Sent[^1]).CommandId);
    }

    [Fact]
    public async Task Presence_pings_and_refusals_bypass_the_projection()
    {
        var sock = await ConnectedAtPlaying();
        var other = Game.Participants[0].Id;
        var changed = new List<Guid>(); _s.PresenceChanged += changed.Add;
        sock.Deliver(new PresenceMessage(new Presence(other, new Cursor(new Point3(1, 1, 0), null), null, [], null)));
        Assert.Single(_s.Others.All); Assert.Equal([other], changed);
        sock.Deliver(new PresenceEnded(other));
        Assert.Empty(_s.Others.All);
        Ping? ping = null; _s.Pinged += p => ping = p;
        sock.Deliver(new PingMessage(new Ping(other, new Point3(3, 3, 0), null)));
        Assert.Equal(3, ping!.Point.X);
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        Assert.IsType<PresenceMessage>(sock.Sent[^1]);
        Assert.Equal(2, _s.Log.Count);
    }

    [Fact]
    public async Task An_unexpected_close_reconnects_and_rebuilds_but_a_replaced_socket_does_not()
    {
        var sock = await ConnectedAtPlaying();
        var lost = new List<bool>(); _s.ConnectionLost += (_, will) => lost.Add(will);
        sock.Drop(1001, "server shutting down");
        await _sockets.ConnectedAsync(2);
        Assert.Equal([true], lost);
        Assert.Equal(2, _sockets.Created.Count);
        Assert.IsType<Connect>(_sockets.Last.Sent.Single());
        Assert.Null(_s.Projection); Assert.Empty(_s.Others.All);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([]));
        Assert.Equal(SessionPhase.Connected, _s.Phase);
        _sockets.Last.Drop(1000, "replaced by a newer connection");
        await Task.Delay(50);
        Assert.Equal([true, false], lost);
        Assert.Equal(2, _sockets.Created.Count);
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
    }

    // A reconnecting player rejoins the team their participant row names, and the
    // "no Game yet" of a reconnect is a different state from having no team.
    [Fact]
    public async Task A_reconnecting_player_rejoins_their_team_and_re_picks_nothing()
    {
        var sock = await ConnectedAtPlaying();
        Assert.Equal(Samples.Team1, _s.MyTeamId);
        sock.Drop(1001, "server shutting down");
        await _sockets.ConnectedAsync(2);
        Assert.Equal(SessionPhase.Reconnecting, _s.Phase);
        Assert.Null(_s.Game); Assert.Null(_s.MyTeamId);                 // nothing to read a team from yet, which is not watching
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));           // the server names the same team, out of the participant row
        Assert.Equal((SessionPhase.Connected, Samples.Team1), (_s.Phase, _s.MyTeamId));
        Assert.IsType<Connect>(Assert.Single(_sockets.Last.Sent));      // one connect frame, and no PickTeam edit
    }

    [Fact]
    public async Task A_rejected_connect_does_not_reconnect_and_upgrade_required_is_sticky()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Rejected(RejectReason.UpgradeRequired));
        _sockets.Last.Drop(1000, "upgrade_required");
        await Task.Delay(50);
        Assert.Equal(SessionPhase.UpdateRequired, _s.Phase);
        Assert.Single(_sockets.Created);
    }

    [Fact]
    public async Task Home_disconnects_and_keeps_the_seat_while_leave_calls_management()
    {
        var sock = await ConnectedAtPlaying();
        await _s.DisconnectAsync();
        Assert.False(sock.Connected);
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
        Assert.Null(_s.GameId);
        Assert.DoesNotContain(_http.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/leave"));
        await _s.ConnectAsync(Game.Id); _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _http.Answer(HttpStatusCode.NoContent);
        await _s.LeaveAsync();
        Assert.EndsWith($"/v1/games/{Game.Id}/leave", _http.Requests[^1].RequestUri!.AbsolutePath);
        Assert.Equal((SessionPhase.LoggedIn, (Guid?)null), (_s.Phase, _s.GameId));
    }

    // An event naming an id the projection does not hold: moving a token that was placed during setup is the
    // designed case (contracts design -> the applier's strictness). The applier throws ProjectionDesyncException.
    static GameEvent Desyncing(long seq) => new(seq, Samples.P1, new MarkerMoved(Guid.CreateVersion7(), new Point3(2, 2, 0), new Yaw(0)), null);
    static GameEvent Score(long seq, int score) => new(seq, Samples.P1, new ScoreSet(Samples.Team1, score), null);

    [Fact]
    public async Task A_desync_drops_the_projection_and_rebuilds_it_from_the_log()
    {
        var sock = await ConnectedAtPlaying();
        var lost = new List<bool>(); SocketClosed? closed = null;
        _s.ConnectionLost += (c, will) => { closed = c; lost.Add(will); };
        sock.Deliver(new PresenceMessage(new Presence(Game.Participants[0].Id, null, null, [], null)));
        await _s.UndoToAsync(1);
        sock.Deliver(new EventsMessage([Desyncing(3)]));
        Assert.Equal([true], lost);
        Assert.IsType<ProjectionDesyncException>(closed!.Error);     // how GameScreen tells this from a dropped socket
        Assert.Equal(2, _sockets.Created.Count);                     // the socket was closed and reopened
        Assert.Equal(SessionPhase.Reconnecting, _s.Phase);
        Assert.Null(_s.Projection); Assert.Null(_s.Applier); Assert.Null(_s.PendingUndoTo); Assert.Empty(_s.Others.All);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([Score(1, 1)]));
        Assert.Equal((SessionPhase.Connected, 1), (_s.Phase, _s.Projection!.Scores[Samples.Team1]));
    }

    [Fact]
    public async Task A_desync_that_makes_no_progress_stops_resyncing_and_goes_home_with_the_reason()
    {
        var sock = await ConnectedAtPlaying();
        var lost = new List<bool>(); _s.ConnectionLost += (_, will) => lost.Add(will);
        sock.Deliver(new EventsMessage([Desyncing(3)]));              // faults with LastSeq 2: the floor
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([Score(1, 1), Score(2, 2), Desyncing(3)]));   // the log replays it
        Assert.Equal([true, false], lost);
        Assert.Equal(2, _sockets.Created.Count);                      // no third socket
        Assert.Equal((SessionPhase.LoggedIn, (Guid?)null), (_s.Phase, _s.GameId));
        Assert.NotNull(_s.LastCloseReason);                           // the Home screen prints it
    }

    [Fact]
    public async Task An_at_rest_frame_is_ignored()
    {
        var sock = await ConnectedAtPlaying();
        var applied = 0; _s.EventsApplied += _ => applied++;
        sock.Deliver(new AtRest(2));
        Assert.Equal((0, 2, SessionPhase.Connected), (applied, _s.Log.Count, _s.Phase));
        Assert.Equal(2, _s.Projection!.Scores[Samples.Team1]);
    }

    /// <summary>Review Focus 3: the join replays the log in pages of 500, then ModelFacts; the end of setup sends an empty page.</summary>
    [Fact]
    public async Task Catching_up_runs_from_a_board_to_the_facts_or_an_empty_page()
    {
        var sock = await ConnectedAtPlaying();
        var seen = new List<bool>(); _s.FactsChanged += () => seen.Add(_s.CatchingUp); _s.EventsApplied += _ => seen.Add(_s.CatchingUp);
        Assert.True(_s.CatchingUp);
        sock.Deliver(new EventsMessage([new GameEvent(3, Samples.P1, new ScoreSet(Samples.Team1, 3), null)]));
        Assert.True(_s.CatchingUp);
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int>()));
        Assert.Equal([true, false], seen);
        Assert.False(_s.CatchingUp);

        sock.Deliver(new BoardMessage(Samples.Board));
        Assert.True(_s.CatchingUp);
        sock.Deliver(new EventsMessage([]));
        Assert.Equal([true, false, false], seen);
        Assert.False(_s.CatchingUp);
    }

    [Fact]
    public async Task Nothing_but_Connect_goes_out_before_Welcome_and_a_command_then_throws()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        var sock = _sockets.Last;
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        await _s.SendPingAsync(new Ping(Guid.Empty, new Point3(1, 1, 0), null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.SendCommandAsync(new Undo(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.SendEditAsync(new PickTeam(Samples.Team1)));
        Assert.IsType<Connect>(Assert.Single(sock.Sent));
        sock.Deliver(new Welcome(Samples.P1, Game));
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        await _s.SendCommandAsync(new Undo(1));
        Assert.Equal(3, sock.Sent.Count);
        Assert.IsType<PresenceMessage>(sock.Sent[1]);
        Assert.IsType<CommandMessage>(sock.Sent[2]);
    }

    [Fact]
    public async Task An_undo_whose_send_fails_leaves_no_prediction()
    {
        var sock = await ConnectedAtPlaying();
        sock.SendFails = new IOException("gone");
        await Assert.ThrowsAsync<IOException>(() => _s.UndoToAsync(1));
        Assert.Null(_s.PendingUndoTo);
        Assert.Same(_s.Applier!.Projection, _s.Projection);
    }

    [Fact]
    public async Task Logout_finishes_locally_when_the_remote_call_fails()
    {
        await LoggedIn();
        _http.Fail(new HttpRequestException("down"));
        await _s.LogoutAsync();
        Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
        Assert.Null(new SessionStore(Path.Combine(_dir, "session.bin")).Read());
    }

    [Fact]
    public void UpdateSettings_raises_SettingsChanged_after_persisting()
    {
        var seen = new List<string>();
        _s.SettingsChanged += () => seen.Add(_persisted[^1].Theme);
        _s.UpdateSettings(x => x with { Theme = "fantasy" });
        Assert.Equal(new[] { "fantasy" }, seen);
    }
}
