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
        var head = _applier.Projection.LastSeq;
        await Session.ReceiveAsync(client, new CommandMessage(Guid.CreateVersion7(), command));
        var refused = client.TakeFrames().OfType<Refused>().LastOrDefault();
        Absorb();
        return refused is null
            ? new SubmitResult.Accepted(_applier.Log.FirstOrDefault(e => e.Seq > head && e.Actor == participantId)?.Warning)
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
