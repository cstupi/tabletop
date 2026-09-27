# Task 5: The session object

**Files:**
- Create: `client/src/Core/IDispatcher.cs`, `client/src/Core/Session.cs`
- Test: `tests/Tabletop.Client.Tests/SessionTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4; `EventApplier`, `Projection`, `GameInfo`, `Board`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `Refused`, `RefusalReason`, `Rejected`, `RejectReason`, `Connect`, `CommandMessage`, `EditMessage`, `Command`, `Edit`, `Undo`, `Undone`, `AtRest`, `ProjectionDesyncException`, `ProtocolVersion` (Protocol); `TableState` (Content).
- Produces: `interface IDispatcher { void Post(Action action); }`, `sealed class ImmediateDispatcher : IDispatcher`; `enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }`; `sealed class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings, Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null)` with:
  - state: `SessionPhase Phase`, `AccountRow? Account`, `ClientSettings Settings`, `Guid? GameId`, `Guid? ParticipantId`, `GameInfo? Game`, `TableState? Draft`, `Projection? Projection` (the undo prediction while one is pending), `IReadOnlyList<GameEvent> Log`, `EventApplier? Applier`, `PresenceTable Others`, `Guid? MyTeamId`, `bool IsCreator`, `long? PendingUndoTo`, `string? LastCloseReason`;
  - events (raised on the dispatcher): `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied(IReadOnlyList<GameEvent>)`, `PresenceChanged(Guid)`, `Pinged(Ping)`, `RefusedReceived(Refused)`, `ConnectionLost(SocketClosed, bool willReconnect)`;
  - login: `Task LoginWithPasswordAsync(email, password)`, `Task SignUpAsync(email, password)`, `Task<string> SendOtpAsync(email)`, `Task VerifyOtpAsync(methodId, code)`, `Task<bool> TryResumeAsync()`, `Task RefreshAsync()`, `Task LogoutAsync()`, `void UpdateSettings(Func<ClientSettings, ClientSettings>)`;
  - game: `Task ConnectAsync(Guid gameId)`, `Task DisconnectAsync()`, `Task LeaveAsync()`, `Task<Guid> SendCommandAsync(Command)`, `Task<Guid> SendEditAsync(Edit)`, `Task UndoToAsync(long seq)`, `Task SendPresenceAsync(Presence)`, `Task SendPingAsync(Ping)`.
- Rules: `Rejected(UpgradeRequired)` and `UpdateRequiredException` both set `Phase = UpdateRequired`. A close the user did not ask for reconnects with backoff (1, 2, 5, 10, 10… s) **except** code `1000` after a `Welcome` (the server replaced this connection — `ConnectionLost(willReconnect: false)`) and any `Rejected`. Reconnect is `ConnectAsync` again: the applier, draft and presence table are discarded first. `UndoToAsync` sends `Undo`, keeps `PendingUndoTo`, and `Projection` returns `Applier.ProjectTo(seq)` until an `undone` event with that `ToSeq` arrives or the command is `Refused`.
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
}
```

- [ ] **Step 2: Run; expect compile errors. Write `IDispatcher.cs`**

```csharp
namespace Tabletop.Client.Core;

/// <summary>Where Session raises its events. Godot posts to the main thread (CallDeferred); tests run the action at once.</summary>
public interface IDispatcher { void Post(Action action); }

public sealed class ImmediateDispatcher : IDispatcher { public void Post(Action action) => action(); }
```

- [ ] **Step 3: Write `Session.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }

/// <summary>
/// The one object that outlives every screen: the management client with the JWT, the socket while one is open, the
/// projection (EventApplier), the draft during setup, and others' presence. Screens read it and hold no truth of their own.
/// </summary>
public sealed class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings,
    Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null)
{
    static readonly TimeSpan[] DefaultBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
    readonly Func<int, TimeSpan> _backoff = backoff ?? (attempt => DefaultBackoff[Math.Min(attempt, DefaultBackoff.Length - 1)]);
    IGameSocket? _socket;
    bool _welcomed, _userClosed;
    int _attempt;
    long? _desyncFloor;                 // the projection's LastSeq at the last desync: one resync per position
    Guid? _pendingUndoCommand;
    Projection? _predicted;

    public SessionPhase Phase { get; private set; } = SessionPhase.LoggedOut;
    public AccountRow? Account { get; private set; }
    public ClientSettings Settings { get; private set; } = settings;
    public ManagementClient Management => management;
    public Guid? GameId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public GameInfo? Game { get; private set; }
    public TableState? Draft { get; private set; }
    public EventApplier? Applier { get; private set; }
    public Projection? Projection => _predicted ?? Applier?.Projection;
    public IReadOnlyList<GameEvent> Log => Applier?.Log ?? [];
    public PresenceTable Others { get; } = new();
    public long? PendingUndoTo { get; private set; }
    public string? LastCloseReason { get; private set; }
    public Guid? MyTeamId => Game?.Participants.FirstOrDefault(p => p.Id == ParticipantId)?.TeamId;
    public bool IsCreator => Game is not null && Game.Participants.FirstOrDefault(p => p.Id == ParticipantId)?.AccountId == Game.CreatedBy;

    public event Action? PhaseChanged;
    public event Action? GameChanged;
    public event Action? BoardChanged;
    public event Action<IReadOnlyList<GameEvent>>? EventsApplied;
    public event Action<Guid>? PresenceChanged;
    public event Action<Ping>? Pinged;
    public event Action<Refused>? RefusedReceived;
    public event Action<SocketClosed, bool>? ConnectionLost;

    // ---- login -------------------------------------------------------------------------------------------------------
    public Task LoginWithPasswordAsync(string email, string password) => Complete(() => management.LoginWithPassword(email, password));
    public Task SignUpAsync(string email, string password) => Complete(() => management.SignUp(email, password));
    public async Task<string> SendOtpAsync(string email) => (await Guard(() => management.SendOtp(email))).MethodId;
    public Task VerifyOtpAsync(string methodId, string code) => Complete(() => management.VerifyOtp(methodId, code));

    public async Task<bool> TryResumeAsync()
    {
        var jwt = management.Jwt ?? store.Read();
        if (jwt is null) return false;
        try { await Complete(() => management.Refresh(jwt)); return true; }
        catch (ManagementException e) when (e is not UpdateRequiredException) { management.Jwt = null; store.Clear(); SetPhase(SessionPhase.LoggedOut); return false; }
    }

    /// <summary>Once a minute from the Godot side: the JWT lives five minutes.</summary>
    public async Task RefreshAsync() { if (management.Jwt is { } jwt) await Complete(() => management.Refresh(jwt)); }

    public async Task LogoutAsync()
    {
        await DisconnectAsync();
        try { await management.Logout(); } catch (ManagementException) { }
        management.Jwt = null; Account = null; store.Clear();
        SetPhase(SessionPhase.LoggedOut);
    }

    public void UpdateSettings(Func<ClientSettings, ClientSettings> change)
    {
        Settings = change(Settings);
        persistSettings(Settings);
        if (!Settings.RememberMe) store.Clear(); else if (management.Jwt is { } jwt) store.Write(jwt);
    }

    async Task Complete(Func<Task<SessionResponse>> call)
    {
        var response = await Guard(call);
        management.Jwt = response.SessionJwt;
        Account = response.Account;
        if (Settings.RememberMe) store.Write(response.SessionJwt);
        if (Phase is SessionPhase.LoggedOut) SetPhase(SessionPhase.LoggedIn);
    }

    async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (UpdateRequiredException) { SetPhase(SessionPhase.UpdateRequired); throw; }
    }

    // ---- the game ----------------------------------------------------------------------------------------------------
    public Task ConnectAsync(Guid gameId) => ConnectAsync(gameId, resync: false);

    /// <summary>A resync keeps the desync floor and shows as Reconnecting; a player-initiated connect starts clean.</summary>
    async Task ConnectAsync(Guid gameId, bool resync)
    {
        if (Phase == SessionPhase.UpdateRequired || management.Jwt is null) return;
        if (_socket is not null) await TearDownSocket();
        GameId = gameId; _userClosed = false; _welcomed = false;
        if (!resync) { _desyncFloor = null; LastCloseReason = null; }
        Reset();
        SetPhase(_attempt == 0 && !resync ? SessionPhase.Connecting : SessionPhase.Reconnecting);
        var socket = sockets.Create();
        socket.FrameReceived += frame => dispatcher.Post(() => { if (ReferenceEquals(socket, _socket)) Handle(frame); });
        socket.Closed += closed => dispatcher.Post(() => { if (ReferenceEquals(socket, _socket)) OnClosed(closed); });
        _socket = socket;
        try
        {
            await socket.ConnectAsync(new Uri(Settings.GameServerUrl), CancellationToken.None);
            await socket.SendAsync(new Connect(management.Jwt, gameId, ProtocolVersion.Current));
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or System.Net.Sockets.SocketException or IOException)
        { OnClosed(new SocketClosed(null, e.Message, e)); }
    }

    /// <summary>The Home button: closes the socket; the participant row stays (disconnecting is not leaving).</summary>
    public async Task DisconnectAsync()
    {
        _userClosed = true;
        LastCloseReason = null;             // the player asked to leave: Home has nothing to explain
        await TearDownSocket();
        GameId = null; Reset();
        if (Phase is not (SessionPhase.LoggedOut or SessionPhase.UpdateRequired)) SetPhase(SessionPhase.LoggedIn);
    }

    /// <summary>The menu's Leave: the row goes, then the socket.</summary>
    public async Task LeaveAsync()
    {
        if (GameId is { } id) await Guard(async () => { await management.LeaveGame(id); return 0; });
        await DisconnectAsync();
    }

    public async Task<Guid> SendCommandAsync(Command command)
    {
        var id = Guid.CreateVersion7();
        if (_socket is not null) await _socket.SendAsync(new CommandMessage(id, command));
        return id;
    }

    public async Task<Guid> SendEditAsync(Edit edit)
    {
        var id = Guid.CreateVersion7();
        if (_socket is not null) await _socket.SendAsync(new EditMessage(id, edit));
        return id;
    }

    /// <summary>Undo is local first: the prediction shows until the server's undone or refused arrives.</summary>
    public async Task UndoToAsync(long seq)
    {
        if (Applier is null) return;
        // ProjectTo replays a prefix of a log this applier already applied in order, so it cannot fault where
        // ApplyAll did not. The catch holds the invariant rather than a known case.
        try { _predicted = Applier.ProjectTo(seq); }
        catch (ProjectionDesyncException desync) { OnDesync(desync); return; }
        PendingUndoTo = seq;
        _pendingUndoCommand = await SendCommandAsync(new Undo(seq));
        EventsApplied?.Invoke([]);
    }

    public Task SendPresenceAsync(Presence value) => _socket?.SendAsync(new PresenceMessage(value)) ?? Task.CompletedTask;
    public Task SendPingAsync(Ping ping) => _socket?.SendAsync(new PingMessage(ping)) ?? Task.CompletedTask;
```


The second half of `Session.cs` (same class, same file) — the frame handler, the close handler and the reset:

```csharp
    // ---- frames (on the dispatcher) ----------------------------------------------------------------------------------
    void Handle(SocketMessage frame)
    {
        switch (frame)
        {
            case Welcome w:
                _welcomed = true; _attempt = 0;
                ParticipantId = w.ParticipantId; Game = w.Game;
                SetPhase(SessionPhase.Connected);
                GameChanged?.Invoke();
                break;
            case GameInfoMessage g:
                Game = g.Game;
                GameChanged?.Invoke();
                break;
            case BoardMessage b when Game is { Status: GameStatus.Playing or GameStatus.Ended }:
                Draft = null; _predicted = null; PendingUndoTo = null;
                Applier = new EventApplier(b.Board, Game);
                BoardChanged?.Invoke();
                break;
            case BoardMessage b:
                Draft = b.Board.Table;          // a setup edit replaces the draft
                BoardChanged?.Invoke();
                break;
            case EventsMessage e when Applier is not null:
                try { Applier.ApplyAll(e.Events); }
                catch (ProjectionDesyncException desync) { OnDesync(desync); return; }   // partially applied: unusable
                if (PendingUndoTo is { } to && e.Events.Any(x => x.Payload is Undone u && u.ToSeq == to)) ClearPrediction();
                EventsApplied?.Invoke(e.Events);
                break;
            case PresenceMessage p:
                Others.Set(p.Presence); PresenceChanged?.Invoke(p.Presence.ParticipantId);
                break;
            case PresenceEnded p:
                Others.Remove(p.ParticipantId); PresenceChanged?.Invoke(p.ParticipantId);
                break;
            case PingMessage p: Pinged?.Invoke(p.Ping); break;
            case Refused r:
                if (r.MessageId == _pendingUndoCommand) { ClearPrediction(); EventsApplied?.Invoke([]); }
                RefusedReceived?.Invoke(r);
                break;
            case Rejected r:
                _userClosed = true;             // the server closes after this; do not reconnect
                LastCloseReason = r.Reason.ToString();
                if (r.Reason == RejectReason.UpgradeRequired) SetPhase(SessionPhase.UpdateRequired);
                else { GameId = null; SetPhase(SessionPhase.LoggedIn); }
                break;
            // No default arm. A kind this client does not act on is dropped: AtRest, which says the server has
            // nothing further to emit and which nothing here waits for, and anything a later protocol version adds.
            // Deserialization has already succeeded by this point, so an unhandled kind is not an error.
        }
    }

    void ClearPrediction() { _predicted = null; PendingUndoTo = null; _pendingUndoCommand = null; }

    /// <summary>
    /// The applier faulted, so this copy of the projection is unusable: drop it, reopen the socket and rebuild from
    /// the log. One attempt per sequence position. A fault whose projection reached past the last one is a new bad
    /// event and gets its own attempt; a fault at or before it is the same event out of the server's own log, which
    /// replaying cannot fix, so the player goes Home with the reason instead of resyncing forever.
    /// </summary>
    void OnDesync(ProjectionDesyncException desync)
    {
        var reached = Applier?.Projection.LastSeq ?? 0;
        var closed = new SocketClosed(null, desync.Message, desync);
        LastCloseReason = desync.Message;
        if (_desyncFloor is { } floor && reached <= floor)
        {
            _ = TearDownSocket();
            GameId = null; Reset();
            SetPhase(SessionPhase.LoggedIn);
            ConnectionLost?.Invoke(closed, false);
            return;
        }
        _desyncFloor = reached;
        ConnectionLost?.Invoke(closed, true);
        if (GameId is { } id) _ = ConnectAsync(id, resync: true);     // no backoff: time does not fix a bad event
    }

    void OnClosed(SocketClosed closed)
    {
        LastCloseReason = closed.Reason;
        _socket = null;
        Reset();
        if (_userClosed || Phase == SessionPhase.UpdateRequired) { ConnectionLost?.Invoke(closed, false); return; }
        var replaced = _welcomed && closed.Code == 1000;
        if (replaced || GameId is null) { GameId = null; SetPhase(SessionPhase.LoggedIn); ConnectionLost?.Invoke(closed, false); return; }
        ConnectionLost?.Invoke(closed, true);
        SetPhase(SessionPhase.Reconnecting);
        var attempt = _attempt++;
        var gameId = GameId.Value;
        _ = Task.Run(async () => { await Task.Delay(_backoff(attempt)); dispatcher.Post(() => { if (GameId == gameId && _socket is null && !_userClosed) _ = ConnectAsync(gameId); }); });
    }

    async Task TearDownSocket()
    {
        var s = _socket; _socket = null;
        if (s is null) return;
        try { await s.CloseAsync(); await s.DisposeAsync(); } catch (Exception) { }
    }

    /// <summary>Reconnect is connect: the projection, the draft and presence are rebuilt from what the server sends.</summary>
    void Reset()
    {
        Applier = null; Draft = null; Game = null; ParticipantId = null;
        ClearPrediction(); Others.Clear();
        BoardChanged?.Invoke();
    }

    void SetPhase(SessionPhase phase) { if (Phase == phase) return; Phase = phase; PhaseChanged?.Invoke(); }
}
```

`Reset()` clears `Game`, which `IsCreator`/`MyTeamId` read; after a reconnect the `Welcome` restores it. `ConnectAsync` calling `Reset()` before the first frame is what makes "reconnect is connect" literal.

`MyTeamId` is derived, never picked or remembered: it reads `Game.Participants` for `ParticipantId`, and the server fills that from the participant's row. So a reconnecting player is back on the team they were on, and the client has nothing to re-pick — teams are locked from `playing` on, and only the server's `Welcome` says which one is yours (game flow → Teams are frozen at the flip, and a team is a slot). `MyTeamId` is null in two unrelated cases, and no code may conflate them: a spectator has a `Game` with no team on their participant row, while a client mid-reconnect has no `Game` at all. Both paths that read it return early on a null `Game`: `GameScreen.Rebuild` tears the team panels down and rebuilds them from the `Welcome`, and `AskPanel.Render` draws nothing. So a returning player shows as `Reconnecting…` and then as themselves, never as a watcher.

A resync is that same path, so the projection, the draft, the undo prediction and the presence table go through the one discard. The old socket's close does not reach `OnClosed`: `TearDownSocket` nulls `_socket` before closing it, and each handler is gated on `ReferenceEquals(socket, _socket)`. The `1000`-after-`welcome` inference is therefore not consulted on a resync and is left as it is.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Client.Tests`; expect 25 passed. Commit: `Add the session object: login, the connect sequence, the projection, local undo, reconnect and the desync resync`.**
