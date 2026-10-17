using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>
/// The scheduler tests' IGameRules: Grimdark Future's members unless a hook is set. By default no phases before round one, the same order
/// every round, no boundary hook, and every action sequence EMPTY — the interpreter finishes it in the Advance that started it, with no
/// step_completed — except the roll-off, which is Grimdark Future's real sequence. Gf() keeps Grimdark Future's deployment and roll-off.
/// </summary>
public sealed class FakeRules : IGameRules
{
    readonly IGameRules _inner = GrimdarkFutureRules.Instance;
    public string Id => "fake";

    /// <summary>Grimdark Future's scheduling — phases, first team, Ambushers — with the empty action sequences: the harness's default.</summary>
    public static FakeRules Gf() => new()
    {
        PhasesBeforeRounds = GrimdarkFutureRules.Instance.PhasesBeforeRounds,
        FirstTeamHook = GrimdarkFutureRules.Instance.FirstTeam,
        RoundBoundaryHook = GrimdarkFutureRules.Instance.RoundBoundaryStep,
    };

    // The scheduler plan's members.
    public IReadOnlyList<string> PhasesBeforeRounds { get; set; } = [];
    public IReadOnlyList<string> PhasesInTurn { get; set; } = [];
    public Func<GameState, int, IReadOnlyList<Guid>, FirstTeamDecision>? FirstTeamHook { get; set; }
    public Func<GameState, PropertySet, Guid, TurnAsk?>? LegalTurnHook { get; set; }
    public Func<GameState, PropertySet, int, IReadOnlyList<Guid>, HookResult>? RoundBoundaryHook { get; set; }
    public Func<GameState, Guid?, IReadOnlyList<Guid>, IReadOnlyList<Guid>?>? OrderReactionsHook { get; set; }
    public Func<GameState, PropertySet, IReadOnlyList<EventPayload>, GameEndingBreach?>? JudgeAdvanceHook { get; set; }
    public Func<GameState, PropertySet, int, IReadOnlyList<StateChange>>? RoundEndedHook { get; set; }
    /// <summary>Replaces Grimdark Future's MaxWounds: how a session test makes the facts change between batches.</summary>
    public Func<GameState, Guid, int>? MaxWoundsHook { get; set; }

    public FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams) => FirstTeamHook?.Invoke(state, round, teams) ?? new OrderedTeams(teams);
    public TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId) => LegalTurnHook is { } h ? h(state, properties, teamId) : _inner.LegalTurn(state, properties, teamId);
    public Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => _inner.ReadAnswer(state, teamId, answer);
    public bool ActivationComplete(GameState state, Activation activation) => _inner.ActivationComplete(state, activation);
    public HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order) => RoundBoundaryHook?.Invoke(state, properties, round, order) ?? new HookDone();
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams) => OrderReactionsHook?.Invoke(state, actorTeamId, reactingTeams);
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => JudgeAdvanceHook?.Invoke(state, properties, proposed);
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => RoundEndedHook?.Invoke(state, properties, round) ?? [];
    public int MaxWounds(GameState state, Guid modelId) => MaxWoundsHook?.Invoke(state, modelId) ?? _inner.MaxWounds(state, modelId);
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => _inner.Shooters(state, properties, unitId, targetUnitId);
    public bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId, IReadOnlySet<Guid> deploying) => _inner.MayDeployInside(state, unitId, containerModelId, deploying);

    // The interpreter plan's members.
    public Func<string, Sequence>? SequenceHook { get; set; }
    public Func<GameState, PropertySet, TurnContext, IReadOnlyList<SequenceStart>>? EndOfTurnHook { get; set; }

    public Sequence Sequence(string name) => name == GfNames.RollOffSeq ? _inner.Sequence(name) : SequenceHook?.Invoke(name) ?? new Sequence(name, []);
    public string? ActionSequence(string actionName) => _inner.ActionSequence(actionName);
    public RollSpec RollSpec(string roll) => _inner.RollSpec(roll);
    public string HitRoll => _inner.HitRoll;
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => [];
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn) => EndOfTurnHook?.Invoke(state, properties, turn) ?? _inner.EndOfTurnSequences(state, properties, turn);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => _inner.OnRoundStarted(state, properties, round);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn) => _inner.OnTurnEnded(state, properties, turn);
}
