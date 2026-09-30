using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }

/// <summary>
/// The one object that outlives every screen: the management client with the JWT, the socket while one is open, the
/// projection (EventApplier), the draft during setup, and others' presence. Screens read it and hold no truth of their own.
/// </summary>
public sealed class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings,
    Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null, TimeProvider? time = null)
{
    static readonly TimeSpan RefreshBackoff = TimeSpan.FromMinutes(1);
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _refreshGate = new();
    Task<bool>? _refreshing;            // the in-flight refresh, shared by concurrent callers
    DateTimeOffset _noRefreshBefore;    // set after a failed refresh: one full minute from when it started
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

    /// <summary>Boot: refreshes the in-memory or stored JWT. True only on success; a 401 signs out, anything else keeps the token. Never throws.</summary>
    public Task<bool> TryResumeAsync() => (management.Jwt ?? store.Read()) is { } jwt ? RefreshCore(jwt) : Task.FromResult(false);

    /// <summary>
    /// Once a minute from the Godot side: the JWT lives five minutes. Only a 401 signs out; a transport failure or any
    /// other status keeps the token and waits a full minute before the next request. Concurrent calls share one request. Never throws.
    /// </summary>
    public Task<bool> RefreshAsync() => management.Jwt is { } jwt ? RefreshCore(jwt) : Task.FromResult(false);

    Task<bool> RefreshCore(string jwt)
    {
        TaskCompletionSource<bool> done;
        lock (_refreshGate)
        {
            if (_refreshing is not null) return _refreshing;
            if (_time.GetUtcNow() < _noRefreshBefore) return Task.FromResult(false);
            done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _refreshing = done.Task;
        }
        _ = RunRefresh(jwt, done);
        return done.Task;
    }

    async Task RunRefresh(string jwt, TaskCompletionSource<bool> done)
    {
        var started = _time.GetUtcNow();
        bool ok;
        try
        {
            await Complete(() => management.Refresh(jwt));
            _noRefreshBefore = default;
            ok = true;
        }
        catch (UpdateRequiredException) { ok = false; }         // Guard has set the UpdateRequired phase
        catch (ManagementException e) when (e.Status == 401)
        {
            ok = false;
            try { await DisconnectAsync(); } catch (Exception) { }
            management.Jwt = null; Account = null; store.Clear();
            SetPhase(SessionPhase.LoggedOut);
        }
        catch (Exception)
        {
            ok = false;
            management.Jwt = jwt;                               // Main's timer retries after the back-off
            _noRefreshBefore = started + RefreshBackoff;
        }
        lock (_refreshGate) _refreshing = null;
        done.SetResult(ok);
    }

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

    public Task<Guid> SendCommandAsync(Command command) => SendCommandAsync(command, Guid.CreateVersion7());

    /// <summary>The caller names the message id, so it can match a Refused that arrives before the send's continuation runs.</summary>
    public async Task<Guid> SendCommandAsync(Command command, Guid id)
    {
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
