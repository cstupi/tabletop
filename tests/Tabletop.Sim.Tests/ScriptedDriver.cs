using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

/// <summary>
/// An IGameDriver with no engine behind it: each StepAsync runs the next scripted step, which emits the events
/// it wants through <see cref="Emit"/>. Past the end of the script a step is quiescent. No session, no Postgres.
/// </summary>
public sealed class ScriptedDriver : IGameDriver
{
    private readonly CompiledScenario _scenario;
    private readonly EventApplier _applier;
    private readonly Queue<Func<ScriptedDriver, Task<StepResult>>> _steps;
    private long _seq;

    public ScriptedDriver(CompiledScenario scenario, params Func<ScriptedDriver, Task<StepResult>>[] steps)
    {
        _scenario = scenario;
        _applier = new EventApplier(scenario.Board, scenario.Info);
        _steps = new Queue<Func<ScriptedDriver, Task<StepResult>>>(steps);
    }

    public Func<Command, SubmitResult> OnSubmit { get; set; } = _ => new SubmitResult.Accepted(null);
    public List<Command> Submitted { get; } = [];
    public Guid FirstTeam => _scenario.TeamIds[0];

    public string Name => "scripted";
    public Guid GameId => _scenario.Info.Id;
    public GameInfo Info => _scenario.Info;
    public Board Board => _scenario.Board;
    public IReadOnlyList<Guid> TeamIds => _scenario.TeamIds;
    public Guid ParticipantFor(Guid teamId) => _scenario.ParticipantFor(teamId);
    public DriverMode Mode { get; set; } = DriverMode.Run;

    public GameEvent Emit(EventPayload payload)
    {
        var e = new GameEvent(++_seq, null, payload, null);
        _applier.Apply(e);
        return e;
    }

    /// <summary>A confirmation to every team, unsealed, no pass.</summary>
    public Ask Confirmation(string question = AskQuestions.Confirm, LegalSet? legal = null, bool allowPass = false) =>
        new(Guid.CreateVersion7(), question, "scripted", TeamIds.ToList(), legal ?? new Confirmation(), allowPass, false, null, null);

    /// <summary>Emit AskOpened and report the projection's open asks, as the real drivers do.</summary>
    public StepResult Open(Ask ask)
    {
        var e = Emit(new AskOpened(ask));
        return new StepResult([e], _applier.Projection.OpenAsks.Values.ToList(), false);
    }

    public static StepResult Nothing => new([], [], true);

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        Submitted.Add(command);
        return Task.FromResult(OnSubmit(command));
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<GameEvent>>([]);
    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    public Task<StepResult> StepAsync(CancellationToken ct) =>
        _steps.TryDequeue(out var step) ? step(this) : Task.FromResult(Nothing);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
