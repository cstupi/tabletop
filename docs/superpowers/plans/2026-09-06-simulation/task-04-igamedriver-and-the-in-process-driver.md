# Task 4: `IGameDriver`, and the in-process driver

**Files:**
- Create: `sim/Drivers/IGameDriver.cs`, `sim/Drivers/ScenarioRows.cs`, `sim/Drivers/RecorderClient.cs`, `sim/Drivers/InProcessDriver.cs`
- Test: `tests/Tabletop.Sim.Tests/InProcessDriverTests.cs`

**Interfaces:**
- Consumes: `IGameRows`, `OwnedArmy`, `ISessionClient`, `SessionServices`, `GameSession`, `Hydrator`, `InMemoryEventLog`, `GameServerOptions`, `Replay` (game server skeleton); `RulesEngineFactory`, `InMemoryPropertiesStore`, `DiceSource`, `IDiceSource` (interpreter plan); `SchedulerFactory` (scheduler plan); `SetConfiguration`, `SocketMessage` (`Welcome`, `BoardMessage`, `EventsMessage`, `CommandMessage`, `Refused`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `GameInfoMessage`, `AtRest`), `GameEvent`, `Command`, `AnswerAsk`, `AskAnswer`, `Projection`, `OpenAsk`, `EventApplier`, `RefusalReason`, `Board`, `GameInfo` (Protocol); `TableState`, `ArmySnapshot` (Content); `CompiledScenario`, `ScenarioCompiler` (Task 1); `PolicyView` (Task 2).
- Produces: `abstract record SubmitResult` with `Accepted(string? Warning)` and `Rejected(RefusalReason Reason, string Detail)`; `sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent)`; `enum DriverMode { Run, StepAndWait }`; `sealed class RestTimeoutException(TimeSpan timeout, long lastSeq, Exception? cause = null) : Exception` with `TimeSpan Timeout` and `long LastSeq`; `sealed class DriverFaultException(string message) : Exception`; `interface IGameDriver : IAsyncDisposable` with `Guid GameId`, `GameInfo Info`, `Board Board`, `IReadOnlyList<Guid> TeamIds`, `Guid ParticipantFor(Guid teamId)`, `DriverMode Mode { get; set; }`, `string Name`, `Task StartAsync(CancellationToken ct)`, `Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct)`, `Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)`, `Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)`, `Task<Projection> ProjectionAsync(CancellationToken ct)`, `Task<StepResult> StepAsync(CancellationToken ct)`, and the default method `Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer answer, CancellationToken ct)`; `sealed class ScenarioRows(CompiledScenario scenario) : IGameRows`; `sealed class RecorderClient(Guid participantId) : ISessionClient` with `IReadOnlyList<SocketMessage> Frames`, `IReadOnlyList<SocketMessage> TakeFrames()`, `bool Closed`, `WebSocketCloseStatus? CloseStatus`, `string? CloseReason`; `sealed class InProcessDriver : IGameDriver` with `static Task<InProcessDriver> StartAsync(CompiledScenario, CancellationToken)`, `GameSession Session`, `Task<Projection> ServerProjectionAsync(CancellationToken)`, `IReadOnlyList<GameEvent> Log`.

Spec: spec → *Two drivers behind one interface* — "In process. The session, the engine and an in-memory log, constructed directly. No socket, no Postgres, no Docker. This is the default because a full game must cost milliseconds"; "A driver interface with one implementation would not have been worth the abstraction." `architecture.md` → Contracts to freeze ("nobody instantiates a game session: a client opens a socket naming a game, and if no session for that game is in memory the server hydrates it from the log") — which is why the in-process driver goes through `Hydrator` and `GameSession.ConnectAsync` rather than building a `GameState` by hand: the two drivers then differ in transport and nothing else.

**The driver's projection is its own.** Every driver keeps an `EventApplier` over the scenario's board and info, fed by the events it drains from the **recorder participant's** stream — a participant with no team, which is what a spectator is. That is the same applier the client uses, so the run loop, the invariants and the recording all read a projection built the way a client builds one, and the socket driver needs no second mechanism. `InProcessDriver.ServerProjectionAsync` additionally exposes the session's own projection, and Task 7's first invariant compares them.

**Quiescence is exact on both drivers.** `StepAsync` returns when the server has said it is at rest. In process the signal is a marker: `GameSession.RunAsync` puts one at the back of the mailbox, so when it returns every queued turn has run. Over the socket the signal is the `AtRest(long Seq)` frame — sent when the game server has finished processing an inbound `connect` or `command`, after the last frame that processing produced. A quiet window would guess at rest, read a slow server as a stuck game, and cost its whole length per step; the rest frame is exact.

**Who receives it, per the game server skeleton plan.** `Seq` is the log's high-water mark, `Play?.Projection.LastSeq ?? 0`, so it is `0` before the log opens and unchanged when the batch appended nothing. The frame is broadcast to every socket on the game when the batch appended at least one event, and sent to the provoking client alone when it appended none, so a refusal disturbs nobody but its sender (game server skeleton plan → Task 9, *The rest frame*). Two things follow for `SocketDriver`, and both are in the code below. `SubmitAsync` reads the submitting socket's own rest frame, which arrives whatever the outcome and always after a `Refused` for that command, so the refusal check needs no timing window. And `StepAsync` waits on the recorder's socket **only when the last command appended**, which the submitting socket's `AtRest.Seq` says by standing above what the recorder has applied; otherwise no batch is coming to the recorder and the step is an empty one. Every driver's absorb path selects `EventsMessage` and ignores every other kind, which is what lets a consumer with no use for `at_rest` (or for any kind added later) see it without faulting.

**The timeout is a liveness guard, not a quiescence guess.** `SocketDriver.RestTimeout` is 30 seconds and bounds one wait for `AtRest`. Any value above the slowest real step is correct, and no designed step approaches it, which is why the number is two orders of magnitude larger than the window it replaces rather than a tuned one. Expiry throws `RestTimeoutException`; `RunLoop.RunAsync` catches it and the run ends `Failed` with the last frames dumped, so a socket soak exits `1`. Expiry is never `Stuck`: stuck means the engine made no progress, and a rest timeout means the harness stopped observing.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class InProcessDriverTests
{
    static CompiledScenario Scenario() => ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));

    [Fact]
    public async Task Starting_hydrates_a_playing_game_and_opens_the_first_ask()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        Assert.Equal(c.Info.Id, driver.GameId);
        Assert.Equal(c.TeamIds, driver.TeamIds);
        Assert.Equal("in-process", driver.Name);

        var asks = await driver.OpenAsksAsync(CancellationToken.None);
        var ask = Assert.Single(asks).Ask;
        Assert.Equal(AskQuestions.Confirm, ask.Question);                       // the roll-off gate comes first
        Assert.Equal(2, ask.Teams.Count);
        Assert.All(ask.Teams, t => Assert.Contains(t, c.TeamIds));
        Assert.IsType<Confirmation>(ask.Legal);
    }

    [Fact]
    public async Task The_drivers_projection_holds_every_model_on_the_shelf_before_deployment()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var projection = await driver.ProjectionAsync(CancellationToken.None);
        Assert.Equal(18, projection.Models.Count);                             // two armies of three units of three
        Assert.All(projection.Models.Values, m => Assert.IsType<OnShelf>(m.Location));
        Assert.Equal(0, projection.Scheduler.Round);
    }

    static async Task<Ask> RollOffGate(IGameDriver driver) =>
        (await driver.OpenAsksAsync(CancellationToken.None)).Single().Ask;

    static async Task<Ask> PastTheGateToDeploy(IGameDriver driver)
    {
        var gate = await RollOffGate(driver);
        Assert.IsType<SubmitResult.Accepted>(await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), CancellationToken.None));
        await driver.StepAsync(CancellationToken.None);
        var ask = (await driver.OpenAsksAsync(CancellationToken.None)).Single().Ask;
        Assert.Equal(AskQuestions.Deploy, ask.Question);
        return ask;
    }

    [Fact]
    public async Task An_answer_is_accepted_and_produces_events()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var gate = await RollOffGate(driver);
        var result = await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), CancellationToken.None);
        Assert.IsType<SubmitResult.Accepted>(result);
        var step = await driver.StepAsync(CancellationToken.None);
        Assert.NotEmpty(step.Events);                                   // the submit's events are handed out once, by the step
        Assert.Contains(step.Events, e => e.Payload is AskAnswered);
        Assert.Equal(step.Events.Select(e => e.Seq).Order(), step.Events.Select(e => e.Seq));
        Assert.Equal(AskQuestions.Deploy, step.OpenAsks.Single().Ask.Question);

        var again = await driver.StepAsync(CancellationToken.None);
        Assert.Empty(again.Events);
        Assert.True(again.Quiescent);
    }

    [Fact]
    public async Task Every_event_is_returned_exactly_once_across_submit_and_drain()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var gate = await RollOffGate(driver);
        await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), CancellationToken.None);
        var drained = await driver.DrainEventsAsync(CancellationToken.None);
        var step = await driver.StepAsync(CancellationToken.None);
        Assert.NotEmpty(drained);
        Assert.Empty(step.Events);
        var log = ((InProcessDriver)driver).Log;
        Assert.Equal(log.Select(e => e.Seq), log.Select(e => e.Seq).Distinct());
        Assert.Equal(log.Count, (await driver.ProjectionAsync(CancellationToken.None)).LastSeq);
    }

    [Fact]
    public async Task Deployment_selects_a_unit_then_the_hand_places_it()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var ask = await PastTheGateToDeploy(driver);
        var eligible = Assert.IsType<PieceSelection>(ask.Legal).Eligible;
        var result = await driver.AnswerAsync(ask.Teams[0], ask.Id, new SelectedPieces([eligible[0]]), CancellationToken.None);
        Assert.IsType<SubmitResult.Accepted>(result);
    }

    [Fact]
    public async Task An_answer_outside_the_legal_set_is_rejected_under_strict()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var ask = await PastTheGateToDeploy(driver);
        var result = await driver.AnswerAsync(ask.Teams[0], ask.Id, new SelectedPieces([Guid.CreateVersion7()]), CancellationToken.None);
        var rejected = Assert.IsType<SubmitResult.Rejected>(result);
        Assert.Equal(RefusalReason.OutsideLegalSet, rejected.Reason);
    }

    [Fact]
    public async Task The_drivers_projection_and_the_servers_agree_after_every_step()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var mine = await driver.ProjectionAsync(CancellationToken.None);
        var theirs = await driver.ServerProjectionAsync(CancellationToken.None);
        Assert.Equal(theirs.LastSeq, mine.LastSeq);
        Assert.Equal(theirs.Models.Count, mine.Models.Count);
        Assert.Equal(theirs.OpenAsks.Keys.Order(), mine.OpenAsks.Keys.Order());
    }

    [Fact]
    public async Task Draining_twice_does_not_return_the_same_event_twice()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var first = await driver.DrainEventsAsync(CancellationToken.None);
        var second = await driver.DrainEventsAsync(CancellationToken.None);
        Assert.Empty(second);
        Assert.Equal(first.Select(e => e.Seq).Order(), first.Select(e => e.Seq));
    }
}
```

The tests pass `CancellationToken.None`, because the project is on xunit 2.9.3 and `TestContext.Current` is xunit 3's. Tests type the driver as `IGameDriver` where they call `AnswerAsync`, because it is a default interface method and is not reachable through `InProcessDriver`. The first Ask of every game is the roll-off `confirm` gate, addressed to both teams, and `deploy` follows it.
- [ ] **Step 2: Run; expect failure, then write `IGameDriver.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter InProcessDriverTests`
Expected: `CS0246: ... 'InProcessDriver' could not be found`.

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

public abstract record SubmitResult
{
    public sealed record Accepted(string? Warning) : SubmitResult;
    public sealed record Rejected(RefusalReason Reason, string Detail) : SubmitResult;
}

/// <summary>What one step produced. Quiescent means the server rested without emitting anything.</summary>
public sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent);

/// <summary>Run answers every Ask; StepAndWait stops with one open and answers nothing (spec → Interactive debugging is a door).</summary>
public enum DriverMode { Run, StepAndWait }

/// <summary>
/// The server did not say it was at rest inside the driver's timeout. A hung server or a dead
/// connection, never a stuck game: RunLoop turns it into RunOutcome.Failed.
/// </summary>
public sealed class RestTimeoutException(TimeSpan timeout, long lastSeq, Exception? cause = null)
    : Exception($"the server did not reach rest within {timeout.TotalSeconds:0.#}s; last applied seq {lastSeq}"
        + (cause is null ? "" : $" (the socket failed: {cause.GetType().Name}: {cause.Message})"), cause)
{
    public TimeSpan Timeout { get; } = timeout;
    public long LastSeq { get; } = lastSeq;
}

/// <summary>A driver's own stream broke an invariant (a lost, reordered or duplicated frame). RunLoop turns it into RunOutcome.Failed.</summary>
public sealed class DriverFaultException(string message) : Exception(message);

/// <summary>
/// One game, played from outside. Two implementations: in process and over the socket, and the same
/// scenario must pass against either (spec → Two drivers behind one interface).
/// </summary>
public interface IGameDriver : IAsyncDisposable
{
    string Name { get; }
    Guid GameId { get; }
    GameInfo Info { get; }
    Board Board { get; }
    IReadOnlyList<Guid> TeamIds { get; }
    Guid ParticipantFor(Guid teamId);
    DriverMode Mode { get; set; }

    /// <summary>The scenario's board and armies become a playing game, and the driver is connected to it.</summary>
    Task StartAsync(CancellationToken ct);

    Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct);
    Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct);

    /// <summary>Every event that arrived since the last drain, in sequence order, applied to the driver's own projection.</summary>
    Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct);

    Task<Projection> ProjectionAsync(CancellationToken ct);

    /// <summary>Let the server rest, then report what it produced.</summary>
    Task<StepResult> StepAsync(CancellationToken ct);

    Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer answer, CancellationToken ct) =>
        SubmitAsync(ParticipantFor(teamId), new AnswerAsk(askId, answer), ct);
}
```

- [ ] **Step 3: Write `ScenarioRows.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// Management's rows, in memory, for exactly one scenario at status playing. The writes belong to lobby and
/// setup, which a scenario skips: it declares the setup result directly, and the setup phase is not logged
/// anyway (setup content → The setup phase is not logged).
///
/// This is also how an in-process run sets the RNG seed: ReadGameAsync hands back the scenario's own
/// GameInfo, whose RngSeed the compiler set from the scenario seed, so Hydrator reaches GameState.RngSeed
/// by the ordinary path. The socket driver has to write the rng_seed column instead (Task 8).
/// </summary>
public sealed class ScenarioRows(Scenarios.CompiledScenario scenario) : IGameRows
{
    public Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(gameId == scenario.Info.Id ? scenario.Info : null);

    public Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct) =>
        Task.FromResult(scenario.Info.Participants.FirstOrDefault(p => p.AccountId == accountId)?.Id);

    public Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<TableState?>(gameId == scenario.Info.Id ? scenario.Snapshot : null);

    public Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(scenario.Board.Armies);

    public Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct) => Task.FromResult<OwnedArmy?>(null);
    public Task<bool> PropertiesExistAsync(string hash, CancellationToken ct) =>
        Task.FromResult(hash == scenario.Properties.ContentHash);

    private static Task Unsupported(string member) =>
        throw new NotSupportedException($"a scenario is already playing; {member} belongs to lobby or setup");

    public Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct) => throw new NotSupportedException("a scenario declares its teams");
    public Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct) => Unsupported(nameof(SetParticipantTeamAsync));
    public Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct) => Unsupported(nameof(SetParticipantArmyAsync));
    public Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct) => Unsupported(nameof(SetConfigurationAsync));
    public Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct) => Unsupported(nameof(StartSetupAsync));
    public Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct) => Unsupported(nameof(WriteDraftAsync));
    public Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshots, CancellationToken ct) =>
        Unsupported(nameof(EndSetupAsync));
}
```

- [ ] **Step 4: Write `RecorderClient.cs`**

```csharp
using System.Net.WebSockets;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>An ISessionClient that keeps its frames instead of writing them to a socket.</summary>
public sealed class RecorderClient(Guid participantId) : ISessionClient
{
    private readonly List<SocketMessage> _frames = [];
    private readonly object _gate = new();

    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public bool Closed { get; private set; }
    public WebSocketCloseStatus? CloseStatus { get; private set; }
    public string? CloseReason { get; private set; }

    public IReadOnlyList<SocketMessage> Frames { get { lock (_gate) return _frames.ToList(); } }

    public IReadOnlyList<SocketMessage> TakeFrames()
    {
        lock (_gate) { var taken = _frames.ToList(); _frames.Clear(); return taken; }
    }

    public ValueTask SendAsync(SocketMessage message) { lock (_gate) _frames.Add(message); return ValueTask.CompletedTask; }
    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason) { Closed = true; CloseStatus = status; CloseReason = reason; return ValueTask.CompletedTask; }
}
```

`ConnectionId` is the one `Guid.CreateVersion7()` in `sim/`: it is a connection, not game state, and never reaches a recording.

- [ ] **Step 5: Write `InProcessDriver.cs`**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// The session, the engine and an in-memory log, constructed directly — no socket, no Postgres, no Docker
/// (spec → Two drivers behind one interface). It still goes through Hydrator and GameSession.ConnectAsync,
/// so the two drivers differ in transport and nothing else.
/// </summary>
public sealed class InProcessDriver : IGameDriver
{
    private readonly CompiledScenario _scenario;
    private readonly EventApplier _applier;
    private readonly List<RecorderClient> _players = [];
    private RecorderClient _recorder = null!;
    private GameSession? _session;
    private readonly List<GameEvent> _pending = [];              // applied, not yet handed to a caller

    private InProcessDriver(CompiledScenario scenario)
    {
        _scenario = scenario;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    public static async Task<InProcessDriver> StartAsync(CompiledScenario scenario, CancellationToken ct)
    {
        var driver = new InProcessDriver(scenario);
        await driver.StartAsync(ct);
        return driver;
    }

    public string Name => "in-process";
    public Guid GameId => _scenario.Info.Id;
    public GameInfo Info => _scenario.Info;
    public Board Board => _scenario.Board;
    public IReadOnlyList<Guid> TeamIds => _scenario.TeamIds;
    public Guid ParticipantFor(Guid teamId) => _scenario.ParticipantFor(teamId);
    public DriverMode Mode { get; set; } = DriverMode.Run;
    public GameSession Session => _session ?? throw new InvalidOperationException("the driver has not started");
    public IReadOnlyList<GameEvent> Log => _applier.Log;

    public async Task StartAsync(CancellationToken ct)
    {
        var services = new SessionServices(
            new ScenarioRows(_scenario),
            new InMemoryEventLog(),
            new RulesEngineFactory(new InMemoryPropertiesStore().Add(_scenario.Properties), new DiceSource(), new SchedulerFactory()),
            new GameServerOptions { DbConnection = "", StytchProjectId = "sim", StytchJwksUrl = "unused" },
            NullLoggerFactory.Instance);

        var session = _session = await Hydrator.HydrateAsync(GameId, services, ct);
        await session.StartAsync();                                   // the engine catches up: roll-off, round 0, deployment

        _recorder = new RecorderClient(_scenario.RecorderParticipantId);
        await session.ConnectAsync(_recorder);
        foreach (var participantId in _scenario.ParticipantIds)
        {
            var client = new RecorderClient(participantId);
            _players.Add(client);
            await session.ConnectAsync(client);
        }
        Absorb();
    }

    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var client = _players.FirstOrDefault(c => c.ParticipantId == participantId)
            ?? throw new ArgumentException($"no connected client for participant {participantId}", nameof(participantId));
        await Session.ReceiveAsync(client, new CommandMessage(Guid.CreateVersion7(), command));
        var refused = client.TakeFrames().OfType<Refused>().LastOrDefault();
        Absorb();
        return refused is null
            ? new SubmitResult.Accepted(null)
            : new SubmitResult.Rejected(refused.Reason, refused.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct) => Task.FromResult(Handout());

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    /// <summary>The session's own projection, for the invariant that the two agree. In-process only.</summary>
    public Task<Projection> ServerProjectionAsync(CancellationToken ct) =>
        Session.RunAsync(() => Task.FromResult(Session.Play!.Projection));

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        // The mailbox is FIFO, so a marker at the back means every queued turn — every Advance — has run.
        await Session.RunAsync(() => Task.FromResult(0));
        Absorb();
        var events = Handout();
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    /// <summary>
    /// Moves everything the recorder has received into the driver's projection and onto _pending. Each event is
    /// applied once (the seq guard) and handed out once (Handout empties _pending). The players' own frames are
    /// discarded so they do not pile up: the recorder sees the same broadcasts.
    /// </summary>
    private void Absorb()
    {
        // The session closes every client when the engine faults and then drops every later frame, so a closed recorder is the cause.
        if (_recorder.Closed)
            throw new DriverFaultException($"the session closed the recorder: {_recorder.CloseStatus} {_recorder.CloseReason}");
        foreach (var player in _players) player.TakeFrames();
        var events = _recorder.TakeFrames().OfType<EventsMessage>().SelectMany(m => m.Events).OrderBy(e => e.Seq);
        foreach (var e in events)
        {
            if (e.Seq <= _applier.Projection.LastSeq) continue;
            _applier.Apply(e);
            _pending.Add(e);
        }
    }

    private IReadOnlyList<GameEvent> Handout()
    {
        Absorb();
        var taken = _pending.ToList();
        _pending.Clear();
        return taken;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null) await _session.DrainAsync("sim finished");
    }
}
```

`_pending` holds events that are applied and not yet handed to a caller. Submit absorption and the next step or drain put events on it, and each event leaves it once, so every event is returned exactly once. `DisposeAsync` is null-guarded, so a driver that failed to start disposes cleanly.

`GameServerOptions` needs no real values here: nothing verifies a JWT, nothing opens a connection, and the presence timer is `SessionHost`'s, which is not running.

- [ ] **Step 6: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter InProcessDriverTests
git add sim/Drivers tests/Tabletop.Sim.Tests
git commit -m "Drive a hydrated session in process behind IGameDriver"
```
