# Task 5a: The session object: interfaces and tests

**Files:**
- Create: `client/src/Core/IDispatcher.cs`, `client/src/Core/Session.cs`
- Test: `tests/Tabletop.Client.Tests/SessionTests.cs`, `tests/Tabletop.Client.Tests/SessionRefreshTests.cs`
- Modify: `tests/Tabletop.Client.Tests/FakeManagement.cs` (Task 3): `Fail(Exception)` and `Gate`

**Interfaces:**
- Consumes: Tasks 2–4; `EventApplier`, `Projection`, `GameInfo`, `Board`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `Refused`, `RefusalReason`, `Rejected`, `RejectReason`, `Connect`, `CommandMessage`, `EditMessage`, `Command`, `Edit`, `Undo`, `Undone`, `AtRest`, `ProjectionDesyncException`, `ProtocolVersion` (Protocol); `TableState` (Content).
- Produces: `interface IDispatcher { void Post(Action action); }`, `sealed class ImmediateDispatcher : IDispatcher`; `enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }`; `sealed class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings, Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null, TimeProvider? time = null)` with:
  - state: `SessionPhase Phase`, `AccountRow? Account`, `ClientSettings Settings`, `ManagementClient Management`, `Guid? GameId`, `Guid? ParticipantId`, `GameInfo? Game`, `TableState? Draft`, `Projection? Projection` (the undo prediction while one is pending), `IReadOnlyList<GameEvent> Log`, `EventApplier? Applier`, `PresenceTable Others`, `Guid? MyTeamId`, `bool IsCreator`, `long? PendingUndoTo`, `string? LastCloseReason`;
  - events (a frame's or a close's on the dispatcher; the rest on the thread of the call that caused them): `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied(IReadOnlyList<GameEvent>)`, `PresenceChanged(Guid)`, `Pinged(Ping)`, `RefusedReceived(Refused)`, `ConnectionLost(SocketClosed, bool willReconnect)`;
  - login: `Task LoginWithPasswordAsync(email, password)`, `Task SignUpAsync(email, password)`, `Task<string> SendOtpAsync(email)`, `Task VerifyOtpAsync(methodId, code)`, `Task<bool> TryResumeAsync()`, `Task<bool> RefreshAsync()`, `Task LogoutAsync()`, `void UpdateSettings(Func<ClientSettings, ClientSettings>)`;
  - game: `Task ConnectAsync(Guid gameId)`, `Task DisconnectAsync()`, `Task LeaveAsync()`, `Task<Guid> SendCommandAsync(Command)`, `Task<Guid> SendCommandAsync(Command, Guid id)` (the caller names the message id), `Task<Guid> SendEditAsync(Edit)`, `Task UndoToAsync(long seq)`, `Task SendPresenceAsync(Presence)`, `Task SendPingAsync(Ping)`.
- Rules: `Rejected(UpgradeRequired)` and `UpdateRequiredException` both set `Phase = UpdateRequired`. A close the user did not ask for reconnects with backoff (1, 2, 5, 10, 10… s) **except** code `1000` after a `Welcome` (the server replaced this connection — `ConnectionLost(willReconnect: false)`) and any `Rejected`. Reconnect is `ConnectAsync` again: the applier, draft and presence table are discarded first. `UndoToAsync` sends `Undo`, keeps `PendingUndoTo`, and `Projection` returns `Applier.ProjectTo(seq)` until an `undone` event with that `ToSeq` arrives or the command is `Refused`.
- Rules, before `Welcome`: nothing but `Connect` goes out. `SendPresenceAsync` and `SendPingAsync` drop, and `SendCommandAsync` and `SendEditAsync` throw `InvalidOperationException`. An `UndoToAsync` whose send fails clears its prediction, raises `EventsApplied([])` and rethrows.
- Rules, refresh and sign-out: `TryResumeAsync` (the in-memory or stored JWT) and `RefreshAsync` (the in-memory JWT) share one refresh; a call while one is in flight gets the same task. Neither throws; the result is `true` only on success. Only a `401` signs out: disconnect, clear the JWT, the account and the store, `Phase = LoggedOut`. A `426` is the `UpdateRequired` phase. Any other failure, a transport exception included, keeps the token in memory and on disk, and refuses every refresh without a request for one minute from the attempt's start, read from the `TimeProvider`. `LogoutAsync` and every login bump an auth epoch; a refresh that started under an older epoch drops its outcome. The in-flight task is cleared and completed in a `finally`, so a throwing `store.Clear()` cannot wedge later refreshes.
- Rules, logout: `LogoutAsync` finishes locally. A failing disconnect, management call or `store.Clear()` is caught, and the phase is `LoggedOut` whether management answered or not.
- Rules, the desync: a `ProjectionDesyncException` out of `ApplyAll` or `ProjectTo` means this copy of the projection is unusable. `OnDesync` drops it, closes the socket and connects again, so the rebuild is the ordinary hydrate. The bound is **one attempt per sequence position**: `_desyncFloor` records `Projection.LastSeq` at the fault, a fault that reached past the floor gets its own attempt, and a fault at or before the floor is the same bad event out of the server's own log and instead sets `Phase = LoggedIn` with `LastCloseReason`. Both outcomes raise `ConnectionLost` with `SocketClosed.Error` carrying the exception, which is how `GameScreen` tells a desync from a dropped socket. The resync has no backoff, because time does not fix a bad event. `ConnectAsync` gains a private `resync` parameter: it forces `Reconnecting` and keeps `_desyncFloor` across the reset, and the public one-argument signature is unchanged.
- Rules, an uninteresting kind: `Handle`'s switch has **no default arm**, so a kind this client does not act on is dropped after a successful deserialization. `AtRest(long Seq)` is that kind. Nothing in the client waits on the server coming to rest: every panel draws from the projection as events arrive, and the only busy state is `Connecting`/`Reconnecting`, which `Welcome` ends. Acting on it would add a second, weaker source of truth for "the table is settled".

Spec: client shell → Structure (one session object outlives every screen; screens hold no truth), The connection and the projection (the connect sequence; undo local first; reconnect is connect; presence and setup edits bypass the projection; server wins), Screens (Home disconnects, Leave is a menu action calling `/leave`); accounts → Disconnecting is not leaving; game server plan → Interfaces needed → Client shell plan (Connect first; Welcome, Board, Events; `Rejected` then close; replaced sockets close with `1000`; `Refused.MessageId` echoes the id); management plan → Client-shell plan (refresh once a minute).

- [ ] **Step 1: Write the failing tests**

```csharp
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
        Assert.Equal(new Uri("ws://localhost:5200/ws"), sock.Url);
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
        await Task.Delay(50);
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
        await Task.Delay(50);
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
}
```

