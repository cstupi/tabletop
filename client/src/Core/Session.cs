using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }

/// <summary>
/// The one object that outlives every screen: the management client with the JWT, the socket while one is open, the
/// projection (EventApplier), the draft during setup, and others' presence. Screens read it and hold no truth of their own.
/// </summary>
public sealed partial class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings,
    Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null, TimeProvider? time = null)
{
    static readonly TimeSpan RefreshBackoff = TimeSpan.FromMinutes(1);
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _refreshGate = new();
    int _authEpoch;                     // bumped by logout and by every login: a refresh started before it is stale
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
    readonly Dictionary<string, RuleTexts> _ruleTexts = new(StringComparer.Ordinal);   // by properties hash: a pinned set never changes
    readonly HashSet<string> _fetchingRules = new(StringComparer.Ordinal);
    IReadOnlyDictionary<Guid, int> _maxWounds = new Dictionary<Guid, int>();
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> _deployInside = new Dictionary<Guid, IReadOnlyList<Guid>>();
    IReadOnlySet<Guid> _maySetAside = new HashSet<Guid>();
    IReadOnlyDictionary<Guid, UnitThreat> _threats = new Dictionary<Guid, UnitThreat>();
    IReadOnlySet<Guid> _inCover = new HashSet<Guid>();

    public SessionPhase Phase { get; private set; } = SessionPhase.LoggedOut;
    public AccountRow? Account { get; private set; }
    public ClientSettings Settings { get; private set; } = settings;
    public ManagementClient Management => management;
    public Guid? GameId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public GameInfo? Game { get; private set; }
    /// <summary>The game ruleset's optional rules and presets, from Welcome; the lobby builds its controls from it.</summary>
    public RulesOptionCatalog Options { get; private set; } = RulesOptionCatalog.Empty;
    /// <summary>Side Battle's legs under the game's rule set, from Welcome; every zone shape the client draws or measures resolves with them.</summary>
    public Tabletop.Geometry.ZoneLegs Zones { get; private set; } = Tabletop.Geometry.ZoneLegs.Default;
    /// <summary>The Welcome's terrain words by declaration name (the game rule set's words); TerrainLabel and TerrainGroups keep their own for a name not listed.</summary>
    public IReadOnlyDictionary<string, string> TerrainWords { get; private set; } = new Dictionary<string, string>();
    /// <summary>What each terrain declaration does to a unit, one sentence by its snake_case name (Welcome.TerrainRules); the terrain card shows it.</summary>
    public IReadOnlyDictionary<string, string> TerrainRules { get; private set; } = new Dictionary<string, string>();
    /// <summary>The seize range about each objective marker's centre, from Welcome.MarkerRange; null draws none.</summary>
    public double? MarkerRange { get; private set; }
    public TableState? Draft { get; private set; }
    public EventApplier? Applier { get; private set; }
    /// <summary>What the table shows: an undo's prediction, else the replay's position (Session.Replay), else the live projection.</summary>
    public Projection? Projection => _predicted ?? Replay?.Projection ?? Applier?.Projection;
    /// <summary>The log the combat log reads: the replay's shown events while replaying, else the live log.</summary>
    public IReadOnlyList<GameEvent> Log => Replay?.Shown ?? Applier?.Log ?? [];
    public PresenceTable Others { get; } = new();
    public long? PendingUndoTo { get; private set; }
    /// <summary>Why the last game closed without the player asking, in the player's words (Feedback); Home prints it.</summary>
    public string? LastCloseReason { get; private set; }
    /// <summary>Every team this account plays, by ordinal: one, or several in self-play (Seats).</summary>
    public IReadOnlyList<Guid> MyTeamIds => Game is { } g ? Seats.Teams(g, ParticipantId) : [];
    /// <summary>The team this client acts for now (Seats.Acting): with several, the team of the first Ask that waits on one of them.</summary>
    public Guid? MyTeamId => Seats.Acting(Projection, MyTeamIds, Log);
    /// <summary>The teams deployment waits on (WaitingForPlayers): from the end of setup until every team has a player and an army; empty otherwise.</summary>
    public IReadOnlyList<TeamWaiting> WaitingTeams => Game is { Status: GameStatus.Playing } g && Applier is { } a && WaitingForPlayers.Holds(g, a.Projection.Board, a.Projection.LastSeq)
        ? WaitingForPlayers.Teams(g, a.Projection.Board) : [];
    /// <summary>The server answers this client as a watcher: a replay or an admin watcher (participant id empty), or from playing on a participant
    /// with no team, except while deployment waits for players, when it may still take an empty team. False without a game, so a client
    /// mid-reconnect is never taken for one.</summary>
    public bool IsWatching => Game is { } g && (Replaying || ParticipantId == Guid.Empty || g.Status is GameStatus.Playing or GameStatus.Ended && MyTeamIds.Count == 0 && WaitingTeams.Count == 0);
    /// <summary>Reconnects tried since the last Welcome; 0 while connected or on a first connect.</summary>
    public int ReconnectAttempt => _attempt;
    public bool IsCreator => Game is not null && Seats.Mine(Game, ParticipantId).FirstOrDefault()?.AccountId == Game.CreatedBy;
    /// <summary>The pinned catalog of the game's property set; Empty until fetched (in-game UX spec → Rule text).</summary>
    public RuleTexts Rules => Game?.PropertiesHash is { } hash && _ruleTexts.TryGetValue(hash, out var rules) ? rules : RuleTexts.Empty;
    /// <summary>The pinned set's grantable effects, for the menu's Add a grant; empty until fetched with the rule texts.</summary>
    public IReadOnlyList<GrantChoice> Grantable => Game?.PropertiesHash is { } hash && _grantable.TryGetValue(hash, out var g) ? g : [];
    readonly Dictionary<string, IReadOnlyList<GrantChoice>> _grantable = new(StringComparer.Ordinal);
    /// <summary>Wounds each model takes before it is killed, from the latest ModelFacts; an absent id is 1 (in-game UX spec → Model facts).</summary>
    public IReadOnlyDictionary<Guid, int> MaxWounds => _maxWounds;
    /// <summary>For each unit that may be put inside a Transport now, the Transport models it may go in, from the latest ModelFacts (passengers spec).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DeployInside => _deployInside;
    /// <summary>The units that may be set aside before deployment now, or returned to it, from the latest ModelFacts.</summary>
    public IReadOnlySet<Guid> MaySetAside => _maySetAside;
    /// <summary>Each unit's reach in its next activation, from the latest ModelFacts (threat range plan); an absent unit has none.</summary>
    public IReadOnlyDictionary<Guid, UnitThreat> Threats => _threats;
    /// <summary>The units in cover where they stand, from the latest ModelFacts (the unit card's shield).</summary>
    public IReadOnlySet<Guid> InCover => _inCover;
    /// <summary>From a board until the join's log has arrived: the first ModelFacts after it, or an empty page (the end of setup).
    /// The server pages the log 500 events at a time, so one batch is not the whole catch-up.</summary>
    public bool CatchingUp { get; private set; }
    /// <summary>The coalesced render: marked with each EventsApplied, GameChanged, BoardChanged and FactsChanged, raised once per
    /// dispatcher turn. Parts that redraw from the current state subscribe here; parts that read the batch keep EventsApplied.</summary>
    public RenderScheduler Frame { get; } = new(dispatcher);
    readonly CombatLogCache _combatLog = new();
    /// <summary>The combat log of the current log state, built once and shared by the log panel, the dice tray and the undo button.
    /// Null without a game.</summary>
    public CombatLogState? CombatLog => Game is { } game ? _combatLog.Of(Log, PendingUndoTo, game, Projection) : null;

    public event Action? PhaseChanged;
    public event Action? GameChanged;
    public event Action? BoardChanged;
    public event Action<IReadOnlyList<GameEvent>>? EventsApplied;
    /// <summary>The lasers a batch of events fired (ShotLines), raised after EventsApplied.</summary>
    public event Action<IReadOnlyList<Shot>>? Fired;
    public event Action<Guid>? PresenceChanged;
    public event Action<Ping>? Pinged;
    public event Action<Refused>? RefusedReceived;
    public event Action<SocketClosed, bool>? ConnectionLost;
    /// <summary>ModelFacts arrived, or the game's rule texts loaded.</summary>
    public event Action? FactsChanged;
    /// <summary>The server's answer to a SightQuery, outside the log. A caller matches its own QueryId.</summary>
    public event Action<SightAnswer>? SightAnswered;

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
        var epoch = _authEpoch;
        var ok = false;
        try
        {
            try
            {
                var response = await Guard(() => management.Refresh(jwt));
                if (epoch != _authEpoch) return;
                Accept(response);
                _noRefreshBefore = default;
                ok = true;
                SendReauth(response.SessionJwt);
            }
            catch (UpdateRequiredException) { }                 // Guard has set the UpdateRequired phase
            catch (ManagementException e) when (e.Status == 401)
            {
                if (epoch != _authEpoch) return;
                try { await DisconnectAsync(); } catch (Exception) { }
                management.Jwt = null; Account = null;
                try { store.Clear(); } catch (Exception) { }
                SetPhase(SessionPhase.LoggedOut);
            }
            catch (Exception)
            {
                if (epoch != _authEpoch) return;
                management.Jwt = jwt;                           // Main's timer retries after the back-off
                _noRefreshBefore = started + RefreshBackoff;
            }
        }
        catch (Exception) { ok = false; }                       // nothing above may leave the refresh unfinished
        finally
        {
            lock (_refreshGate) _refreshing = null;
            done.SetResult(ok);
        }
    }

    /// <summary>The game server closes a socket at its JWT's expiry: a welcomed socket gets each refreshed JWT. A socket not yet welcomed
    /// sent the JWT that was current in its Connect, and a send failure is the close the socket reports anyway.</summary>
    void SendReauth(string jwt) => dispatcher.Post(() =>
    {
        if (Welcomed() is not { } socket) return;
        _ = socket.SendAsync(new Reauth(Guid.CreateVersion7(), jwt)).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    });

    public async Task LogoutAsync()
    {
        _authEpoch++; _noRefreshBefore = default;
        try { await DisconnectAsync(); } catch (Exception) { }
        try { await management.Logout(); } catch (Exception) { }
        management.Jwt = null; Account = null;
        try { store.Clear(); } catch (Exception) { }
        SetPhase(SessionPhase.LoggedOut);
    }

    public void UpdateSettings(Func<ClientSettings, ClientSettings> change)
    {
        Settings = change(Settings);
        persistSettings(Settings);
        if (!Settings.RememberMe) store.Clear(); else if (management.Jwt is { } jwt) store.Write(jwt);
        SettingsChanged?.Invoke();
    }

    public event Action? SettingsChanged;

    /// <summary>PATCH /v1/account: the name everyone sees. The server's row replaces Account; a refusal throws and keeps the old one.</summary>
    public async Task RenameAsync(string name)
    {
        Account = await Guard(() => management.RenameAccount(name.Trim()));
        AccountChanged?.Invoke();
    }

    public event Action? AccountChanged;

    /// <summary>A login path: its response supersedes any refresh still in flight.</summary>
    async Task Complete(Func<Task<SessionResponse>> call)
    {
        var response = await Guard(call);
        _authEpoch++; _noRefreshBefore = default;
        Accept(response);
    }

    void Accept(SessionResponse response)
    {
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
    /// <summary>Plays the game: Home's Join. ReplayAsync opens it read-only instead.</summary>
    public Task ConnectAsync(Guid gameId) { _replay = false; _resumeAt = 0; return ConnectAsync(gameId, resync: false); }

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
            await socket.ConnectAsync(SocketUri(), CancellationToken.None);
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
        GameId = null; Reset(); _replay = false; _resumeAt = 0;
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
        await SendOrQueue(new CommandMessage(id, command));
        return id;
    }

    public async Task<Guid> SendEditAsync(Edit edit)
    {
        var id = Guid.CreateVersion7();
        await SendOrQueue(new EditMessage(id, edit));
        return id;
    }

    /// <summary>A reconnect is under way: commands and edits wait for the next Welcome and go out in order after it.</summary>
    public bool Queueing => !_welcomed && !_userClosed && GameId is not null && Phase == SessionPhase.Reconnecting;
    const int MaxQueued = 16;
    readonly List<(SocketMessage Frame, TaskCompletionSource Sent)> _queued = new();

    /// <summary>Welcomed: sent now. Reconnecting: queued, and the task completes once it is sent, or fails if the session goes home first.</summary>
    Task SendOrQueue(SocketMessage frame)
    {
        if (Replaying) throw new InvalidOperationException("A replay changes nothing.");
        if (Welcomed() is { } socket) return socket.SendAsync(frame);
        if (!Queueing) throw NotConnected();
        if (_queued.Count >= MaxQueued) throw new InvalidOperationException("Reconnecting to the game; too many actions are already waiting.");
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queued.Add((frame, sent));
        return sent.Task;
    }

    async Task FlushQueued(IGameSocket socket)
    {
        var queued = _queued.ToList(); _queued.Clear();
        foreach (var (frame, sent) in queued)
        {
            try { await socket.SendAsync(frame); sent.TrySetResult(); }
            catch (Exception e) { sent.TrySetException(e); }
        }
    }

    void FailQueued()
    {
        var queued = _queued.ToList(); _queued.Clear();
        foreach (var (_, sent) in queued) sent.TrySetException(new InvalidOperationException("Disconnected from the game before this was sent."));
    }

    /// <summary>Undo is local first: the prediction shows until the server's undone or refused arrives.</summary>
    public async Task UndoToAsync(long seq)
    {
        if (Applier is null || Replaying) return;
        // ProjectTo replays a prefix of a log this applier already applied in order, so it cannot fault where
        // ApplyAll did not. The catch holds the invariant rather than a known case.
        try { _predicted = Applier.ProjectTo(seq); }
        catch (ProjectionDesyncException desync) { OnDesync(desync); return; }
        PendingUndoTo = seq;
        try { _pendingUndoCommand = await SendCommandAsync(new Undo(seq)); }
        catch (Exception)
        {
            _predicted = null; PendingUndoTo = null;
            RaiseEvents([]);
            throw;
        }
        RaiseEvents([]);
    }

    /// <summary>Presence and pings: dropped before Welcome and while watching, where the server would refuse every one.</summary>
    public Task SendPresenceAsync(Presence value) => IsWatching ? Task.CompletedTask : Welcomed()?.SendAsync(new PresenceMessage(value)) ?? Task.CompletedTask;
    public Task SendPingAsync(Ping ping) => IsWatching ? Task.CompletedTask : Welcomed()?.SendAsync(new PingMessage(ping)) ?? Task.CompletedTask;

    /// <summary>Asks the server what the observer's models see of the target's (in-game UX spec → Sight check). Returns the query id.</summary>
    /// <summary>The caller names the query, so it knows which answer is its newest before the send returns.</summary>
    /// <summary>Not while replaying: the server answers for the live table, not the replay's position.</summary>
    public async Task QuerySightAsync(Guid queryId, Guid observerUnitId, Guid targetUnitId)
    {
        if (Replaying) return;
        await (Welcomed() ?? throw NotConnected()).SendAsync(new SightQuery(queryId, observerUnitId, targetUnitId));
    }

    /// <summary>The socket once the server has welcomed it; nothing but Connect goes out before that.</summary>
    IGameSocket? Welcomed() => _welcomed ? _socket : null;
    static InvalidOperationException NotConnected() => new("Not connected to the game yet.");

    // ---- frames (on the dispatcher) ----------------------------------------------------------------------------------
    void Handle(SocketMessage frame)
    {
        switch (frame)
        {
            case Welcome w:
                _welcomed = true; _attempt = 0;
                ParticipantId = w.ParticipantId; Game = w.Game; Options = w.Options; Zones = w.Zones; TerrainWords = w.TerrainWords; TerrainRules = w.TerrainRules; MarkerRange = w.MarkerRange;
                SetPhase(SessionPhase.Connected);
                RaiseGame();
                EnsureRuleTexts();
                if (_queued.Count > 0 && _socket is { } welcomed) _ = FlushQueued(welcomed);
                break;
            case RulesOptionsMessage r:
                Options = r.Options; Zones = r.Zones; TerrainWords = r.TerrainWords; TerrainRules = r.TerrainRules; MarkerRange = r.MarkerRange;   // sent before the GameInfo that follows, which raises the change
                break;
            case GameInfoMessage g:
                Game = g.Game;
                RaiseGame();
                EnsureRuleTexts();
                break;
            case BoardMessage b when Game is { Status: GameStatus.Playing or GameStatus.Ended }:
                Draft = null; _predicted = null; PendingUndoTo = null;
                Applier = new EventApplier(b.Board, Game);
                StartReplay(b.Board, Game);
                CatchingUp = true;
                RaiseBoard();
                break;
            case BoardMessage b:
                Draft = b.Board.Table;          // a setup edit replaces the draft
                CatchingUp = true;
                RaiseBoard();
                break;
            case EventsMessage e when Applier is not null && Replay is not null: OnReplayEvents(e.Events); break;
            case EventsMessage e when Applier is not null:
                var shots = new List<Shot>();
                try
                {
                    // One at a time, so a shooting group's lasers are read while its targets still stand.
                    foreach (var x in e.Events) { Applier.Apply(x); if (x.Payload is StepCompleted step) shots.AddRange(ShotLines.Of(Applier.Projection, step)); }
                }
                catch (ProjectionDesyncException desync) { OnDesync(desync); return; }   // partially applied: unusable
                if (PendingUndoTo is { } to && e.Events.Any(x => x.Payload is Undone u && u.ToSeq == to)) ClearPrediction();
                if (e.Events.Count == 0) CatchingUp = false;
                RaiseEvents(e.Events);
                if (shots.Count > 0) Fired?.Invoke(shots);
                break;
            case PresenceMessage p when !Replaying:            // a replay shows the past: no one's live cursor
                Others.Set(p.Presence); PresenceChanged?.Invoke(p.Presence.ParticipantId);
                break;
            case PresenceEnded p:
                Others.Remove(p.ParticipantId); PresenceChanged?.Invoke(p.ParticipantId);
                break;
            case PingMessage p when !Replaying: Pinged?.Invoke(p.Ping); break;
            case ModelFacts f:
                _maxWounds = new Dictionary<Guid, int>(f.MaxWounds);
                _deployInside = f.DeployInside is { } inside ? new Dictionary<Guid, IReadOnlyList<Guid>>(inside) : new Dictionary<Guid, IReadOnlyList<Guid>>();
                _maySetAside = new HashSet<Guid>(f.MaySetAside ?? []);
                _threats = f.Threats is { } threats ? new Dictionary<Guid, UnitThreat>(threats) : new Dictionary<Guid, UnitThreat>();
                _inCover = new HashSet<Guid>(f.InCover ?? []);
                CatchingUp = false;
                RaiseFacts();
                break;
            case SightAnswer a: SightAnswered?.Invoke(a); break;
            case Refused r:
                if (r.MessageId == _pendingUndoCommand) { ClearPrediction(); RaiseEvents([]); }
                RefusedReceived?.Invoke(r);
                break;
            case Rejected r:
                _userClosed = true;             // the server closes after this; do not reconnect
                LastCloseReason = Feedback.Rejected(r.Reason);
                if (r.Reason == RejectReason.UpgradeRequired) SetPhase(SessionPhase.UpdateRequired);
                else { GameId = null; SetPhase(SessionPhase.LoggedIn); }
                break;
            // No default arm. A kind this client does not act on is dropped: AtRest, which says the server has
            // nothing further to emit and which nothing here waits for, and anything a later protocol version adds.
            // Deserialization has already succeeded by this point, so an unhandled kind is not an error.
        }
    }

    void ClearPrediction() { _predicted = null; PendingUndoTo = null; _pendingUndoCommand = null; }

    /// <summary>Fetches the game's catalog once per properties hash, from playing on: the hash is final at the flip.</summary>
    void EnsureRuleTexts()
    {
        if (Game is not { Status: GameStatus.Playing or GameStatus.Ended } game) return;
        var hash = game.PropertiesHash;
        if (_ruleTexts.ContainsKey(hash) || !_fetchingRules.Add(hash)) return;
        _ = FetchRuleTexts(hash);
    }

    /// <summary>A failure caches nothing, so the next Welcome or game message tries again; the sheet shows no texts meanwhile.</summary>
    async Task FetchRuleTexts(string hash)
    {
        RuleTexts? loaded = null; IReadOnlyList<GrantChoice> grants = [];
        try { loaded = RuleTexts.From(await management.GetRuleTexts(hash)); }
        catch (Exception) { }
        if (loaded is not null)                                     // the menu's Add a grant; a failure leaves it out until a reconnect refetches
            try { grants = GrantChoices.Of(await management.GetGrantableEffects(hash)); }
            catch (Exception) { }
        dispatcher.Post(() =>
        {
            _fetchingRules.Remove(hash);
            if (loaded is null) return;
            _ruleTexts[hash] = loaded; _grantable[hash] = grants;
            if (Game?.PropertiesHash == hash) RaiseFacts();
        });
    }

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
        LastCloseReason = Feedback.Closed(closed, willReconnect: true);
        if (_desyncFloor is { } floor && reached <= floor)
        {
            LastCloseReason = Feedback.Closed(closed, willReconnect: false);
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
        _socket = null;
        Reset();
        if (_userClosed || Phase == SessionPhase.UpdateRequired) { LastCloseReason ??= Feedback.Closed(closed, false); ConnectionLost?.Invoke(closed, false); return; }
        var replaced = _welcomed && closed.Code == 1000;
        if (replaced || GameId is null) { LastCloseReason = Feedback.Closed(closed, false); GameId = null; SetPhase(SessionPhase.LoggedIn); ConnectionLost?.Invoke(closed, false); return; }
        LastCloseReason = Feedback.Closed(closed, true);
        ConnectionLost?.Invoke(closed, true);
        SetPhase(SessionPhase.Reconnecting);
        var attempt = _attempt++;
        var gameId = GameId.Value;
        var refresh = closed.Code == 1008;
        _ = Task.Run(async () => { await Task.Delay(_backoff(attempt)); dispatcher.Post(() => _ = Retry(gameId, refresh)); });
    }

    /// <summary>A 1008 is the server's copy of the JWT expiring: the refresh runs first, so the reconnect carries a token the server
    /// accepts (a 401 there signs out, which ends the retry).</summary>
    async Task Retry(Guid gameId, bool refresh)
    {
        if (refresh) await RefreshAsync();
        if (GameId == gameId && _socket is null && !_userClosed) await ConnectAsync(gameId, resync: false);
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
        if (Replay is { } replay) _resumeAt = CatchingUp ? Math.Max(_resumeAt, replay.Position) : replay.Position;   // a reconnect returns the replay to where it was
        Applier = null; Draft = null; Game = null; ParticipantId = null; Replay = null;
        ClearPrediction(); Others.Clear();
        _maxWounds = new Dictionary<Guid, int>();
        _deployInside = new Dictionary<Guid, IReadOnlyList<Guid>>();
        _maySetAside = new HashSet<Guid>();
        _threats = new Dictionary<Guid, UnitThreat>();
        _inCover = new HashSet<Guid>();
        CatchingUp = true;
        RaiseBoard();
    }

    // Each change raises its own event first (per-batch readers), then marks the coalesced render.
    // Handlers.Raise: a subscriber that throws leaves the others and the render mark to run, so one failing part never freezes the screen.
    void RaiseGame() => Handlers.Raise(GameChanged, h => ((Action)h)(), () => Frame.Mark(RenderCause.Game));
    void RaiseBoard() => Handlers.Raise(BoardChanged, h => ((Action)h)(), () => Frame.Mark(RenderCause.Board));
    void RaiseFacts() => Handlers.Raise(FactsChanged, h => ((Action)h)(), () => Frame.Mark(RenderCause.Facts));
    void RaiseEvents(IReadOnlyList<GameEvent> events) => Handlers.Raise(EventsApplied, h => ((Action<IReadOnlyList<GameEvent>>)h)(events), () => Frame.Mark(RenderCause.Events));

    void SetPhase(SessionPhase phase)
    {
        if (Phase == phase) return;
        Phase = phase;
        if (phase is SessionPhase.LoggedOut or SessionPhase.LoggedIn or SessionPhase.UpdateRequired) FailQueued();   // home: nothing will welcome them
        PhaseChanged?.Invoke();
    }
}
