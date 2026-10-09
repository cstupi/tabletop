# Task 12: Session: sight, facts, rule texts

**Wave:** 2 · **Depends on:** 1 · **Model:** sonnet

**Files:**
- Modify: `client/src/Core/Session.cs` (rule-text cache and fetch, `MaxWounds`, `FactsChanged`, `QuerySightAsync`,
  `SightAnswered`; `Handle` takes `ModelFacts` and `SightAnswer`; `Reset` clears the facts)
- Test: `tests/Tabletop.Client.Tests/SessionFactsTests.cs` (new)

**Interfaces:**
- Consumes (Task 1): `SightQuery(Guid QueryId, Guid ObserverUnitId, Guid TargetUnitId)`,
  `SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot)`,
  `ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds)`, `ManagementClient.GetRuleTexts(string propertiesHash)`,
  `RuleTextDto`, `RuleTexts.From`, `RuleTexts.Empty`.
- Produces (README *Fixed interfaces → Session.cs*):

```csharp
public RuleTexts Rules { get; }                                  // Empty until fetched for Game.PropertiesHash
public IReadOnlyDictionary<Guid, int> MaxWounds { get; }         // from the latest ModelFacts; absent id = 1
public event Action? FactsChanged;                               // ModelFacts arrived or rule texts loaded
public Task<Guid> QuerySightAsync(Guid observerUnitId, Guid targetUnitId);
public event Action<SightAnswer>? SightAnswered;
```

Spec: *Sight question*, *Model facts*, *Rule text*; client `AGENTS.md` → *How it works* (frames on the dispatcher).

Behaviour settled here:

- **Rule texts.** On `Welcome` and on every `GameInfoMessage`, once the game is `Playing` or `Ended`, the session fetches
  `GetRuleTexts(Game.PropertiesHash)` unless that hash is cached or already being fetched. A pinned set never changes,
  so the cache is a dictionary by hash on the `Session`, which outlives screens and reconnects. A failed fetch caches
  nothing and leaves `Rules` empty; the next `Welcome` or `GameInfoMessage` tries again. The result is applied on the
  dispatcher and raises `FactsChanged` when the hash is still the game's. A lobby or setup game fetches nothing:
  `SetConfiguration` changes the hash until the flip.
- **Model facts.** Task 4b sends `ModelFacts` for every model in play, on connect and after any batch that changes a
  value, so a frame replaces `MaxWounds` whole. `Reset` (connect, reconnect, close) empties it; the server sends it
  again after the log.
- **Sight.** `QuerySightAsync` mints the query id, sends `SightQuery` on the welcomed socket and returns the id; before
  `Welcome` it throws as `SendCommandAsync` does. `SightAnswer` raises `SightAnswered` and touches nothing else: the
  answer is outside the log. A caller matches its own `QueryId`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.Client.Tests/SessionFactsTests.cs`:

```csharp
using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class SessionFactsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-session-facts-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;
    static readonly GameInfo Game = Samples.Game;                       // read once: the property mints new ids per read
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Clay");
    static readonly RuleTextDto Tough = new("Tough", "model", true, "Must take X wounds to be killed.");
    static readonly Guid Target = Guid.Parse("0198b7ac-0031-7000-8000-000000000031");

    public SessionFactsTests()
    {
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets,
            new SessionStore(Path.Combine(_dir, "session.bin")), ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    int RuleRequests => _http.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/properties/sha256:abc/rules");

    static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    async Task LoggedIn()
    {
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-1.z", Clay));
        await _s.LoginWithPasswordAsync("c@example.com", "pw");
    }

    async Task<FakeGameSocket> Connected(GameInfo game)
    {
        await LoggedIn();
        await _s.ConnectAsync(game.Id);
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, game));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage([]));
        return sock;
    }

    [Fact]
    public async Task Model_facts_replace_max_wounds_and_a_reconnect_clears_them()
    {
        var sock = await Connected(Game);
        var raised = 0; _s.FactsChanged += () => raised++;
        Assert.Empty(_s.MaxWounds);
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int> { [Samples.Model] = 3 }));
        Assert.Equal((3, 1), (_s.MaxWounds[Samples.Model], raised));
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int> { [Target] = 2 }));
        Assert.Equal(new Dictionary<Guid, int> { [Target] = 2 }, _s.MaxWounds);
        Assert.Equal(2, raised);
        Assert.Empty(_s.Log);                                   // facts are not events
        sock.Drop(1001, "server shutting down");
        await Task.Delay(50);
        Assert.Empty(_s.MaxWounds);
    }

    [Fact]
    public async Task A_sight_query_goes_out_and_only_its_answer_comes_back()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.QuerySightAsync(Samples.Unit, Target));   // not welcomed yet
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, Game));
        sock.Deliver(new BoardMessage(Samples.Board));
        var id = await _s.QuerySightAsync(Samples.Unit, Target);
        var query = Assert.IsType<SightQuery>(sock.Sent[^1]);
        Assert.Equal((id, Samples.Unit, Target), (query.QueryId, query.ObserverUnitId, query.TargetUnitId));
        SightAnswer? got = null; _s.SightAnswered += a => got = a;
        var applied = 0; _s.EventsApplied += _ => applied++;
        var answer = new SightAnswer(id, [new SightLine(Samples.Model, Guid.CreateVersion7(), true, false, [])], [Samples.Model]);
        sock.Deliver(answer);
        Assert.Same(answer, got);
        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task Rule_texts_load_once_per_pinned_set_and_survive_a_reconnect()
    {
        await LoggedIn();
        _http.Answer(HttpStatusCode.OK, new[] { Tough });
        var raised = 0; _s.FactsChanged += () => raised++;
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        await Until(() => _s.Rules.IsAutomated("Tough"));
        Assert.Equal("Must take X wounds to be killed.", _s.Rules.TextOf("Tough"));
        Assert.Equal((1, 1), (RuleRequests, raised));
        _sockets.Last.Drop(1001, "server shutting down");
        await Task.Delay(50);
        Assert.Same(RuleTexts.Empty, _s.Rules);                   // no game, no set
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new GameInfoMessage(Game));
        Assert.True(_s.Rules.IsAutomated("Tough"));
        Assert.Equal(1, RuleRequests);
    }

    [Fact]
    public async Task A_lobby_fetches_nothing_and_a_failed_fetch_is_tried_again()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game with { Status = GameStatus.Lobby }));
        Assert.Equal(0, RuleRequests);
        _http.Answer(HttpStatusCode.InternalServerError, "{\"title\":\"boom\"}", "application/problem+json");
        _sockets.Last.Deliver(new GameInfoMessage(Game));                       // the flip to playing
        await Until(() => RuleRequests == 1);
        await Task.Delay(50);
        Assert.Same(RuleTexts.Empty, _s.Rules);
        _http.Answer(HttpStatusCode.OK, new[] { Tough });
        _sockets.Last.Deliver(new GameInfoMessage(Game));
        await Until(() => _s.Rules.IsAutomated("Tough"));
        Assert.Equal(2, RuleRequests);
    }
}
```

The fake socket's `Deliver` and the `ImmediateDispatcher` run `Handle` inline, and `FakeManagement` records each request
before its first await, so request counts read right after a `Deliver` are exact; the fetched texts land on a
continuation, hence `Until`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter SessionFactsTests`
Expected: compile errors: `Session` has no `MaxWounds`, `FactsChanged`, `QuerySightAsync`, `SightAnswered` or `Rules`.

- [ ] **Step 3: Add the state and the events**

In `client/src/Core/Session.cs`, after the field `Projection? _predicted;`:

```csharp
    readonly Dictionary<string, RuleTexts> _ruleTexts = new(StringComparer.Ordinal);   // by properties hash: a pinned set never changes
    readonly HashSet<string> _fetchingRules = new(StringComparer.Ordinal);
    IReadOnlyDictionary<Guid, int> _maxWounds = new Dictionary<Guid, int>();
```

After the property `public bool IsCreator => …;`:

```csharp
    /// <summary>The pinned catalog of the game's property set; Empty until fetched (in-game UX spec → Rule text).</summary>
    public RuleTexts Rules => Game?.PropertiesHash is { } hash && _ruleTexts.TryGetValue(hash, out var rules) ? rules : RuleTexts.Empty;
    /// <summary>Wounds each model takes before it is killed, from the latest ModelFacts; an absent id is 1 (in-game UX spec → Model facts).</summary>
    public IReadOnlyDictionary<Guid, int> MaxWounds => _maxWounds;
```

After the event `public event Action<SocketClosed, bool>? ConnectionLost;`:

```csharp
    /// <summary>ModelFacts arrived, or the game's rule texts loaded.</summary>
    public event Action? FactsChanged;
    /// <summary>The server's answer to a SightQuery, outside the log. A caller matches its own QueryId.</summary>
    public event Action<SightAnswer>? SightAnswered;
```

- [ ] **Step 4: Send the sight query**

After `SendPingAsync`:

```csharp
    /// <summary>Asks the server what the observer's models see of the target's (in-game UX spec → Sight check). Returns the query id.</summary>
    public async Task<Guid> QuerySightAsync(Guid observerUnitId, Guid targetUnitId)
    {
        var id = Guid.CreateVersion7();
        await (Welcomed() ?? throw NotConnected()).SendAsync(new SightQuery(id, observerUnitId, targetUnitId));
        return id;
    }
```

- [ ] **Step 5: Handle the frames and fetch the texts**

In `Handle`, replace the `Welcome` and `GameInfoMessage` arms:

```csharp
            case Welcome w:
                _welcomed = true; _attempt = 0;
                ParticipantId = w.ParticipantId; Game = w.Game;
                SetPhase(SessionPhase.Connected);
                GameChanged?.Invoke();
                EnsureRuleTexts();
                break;
            case GameInfoMessage g:
                Game = g.Game;
                GameChanged?.Invoke();
                EnsureRuleTexts();
                break;
```

and add, before the `case Refused r:` arm:

```csharp
            case ModelFacts f:
                _maxWounds = new Dictionary<Guid, int>(f.MaxWounds);
                FactsChanged?.Invoke();
                break;
            case SightAnswer a: SightAnswered?.Invoke(a); break;
```

After `ClearPrediction`:

```csharp
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
        RuleTexts? loaded = null;
        try { loaded = RuleTexts.From(await management.GetRuleTexts(hash)); }
        catch (Exception) { }
        dispatcher.Post(() =>
        {
            _fetchingRules.Remove(hash);
            if (loaded is null) return;
            _ruleTexts[hash] = loaded;
            if (Game?.PropertiesHash == hash) FactsChanged?.Invoke();
        });
    }
```

In `Reset`, after `ClearPrediction(); Others.Clear();`:

```csharp
        _maxWounds = new Dictionary<Guid, int>();
```

`Reset` leaves `_ruleTexts` alone: the cache is by hash and outlives the socket.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "SessionFactsTests|SessionTests|SessionRefreshTests"`
Expected: PASS. The existing `SessionTests` connect at `Playing`, so each `Welcome` now also fetches rule texts; the
fake answers its default `{}`, which does not parse as a list, and the fetch caches nothing. `FakeManagement` dequeues
that default synchronously inside the `Deliver`, so the answers those tests queue afterwards (the `/leave` 204) still
reach the request they were queued for.

- [ ] **Step 7: Check the client**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: every test passes, the build succeeds, `check-client` prints nothing.

- [ ] **Step 8: Commit**

```bash
git add client/src/Core/Session.cs tests/Tabletop.Client.Tests/SessionFactsTests.cs
git commit -m "Hold model facts and the pinned rule texts in the session, and ask the server about sight" \
  -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
