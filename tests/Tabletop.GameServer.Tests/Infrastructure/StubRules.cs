using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A ruleset made of the sequences a test hands it. Rolls: D6, threshold 4, no cap. Never scheduled (Sim runs it under NoScheduler), so the scheduler's hooks are inert.</summary>
public sealed class StubRules(params Sequence[] sequences) : IGameRules
{
    public string Id => "stub";
    public string HitRoll => "hit";
    public Sequence Sequence(string name) => sequences.FirstOrDefault(s => s.Name == name) ?? throw new UnknownSequenceException(name);
    public string? ActionSequence(string actionName) => actionName.ToLowerInvariant();
    public RollSpec RollSpec(string roll) => new(roll, new RollConvention(6, null), (_, _) => 4);
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => Reactions?.Invoke(state, point, frame) ?? [];
    public Func<GameState, string, FrameView, IReadOnlyList<Reaction>>? Reactions { get; init; }
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, Scheduler.TurnContext turn) => [];
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => [];
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, Scheduler.TurnContext turn) => [];
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams) => null;

    // The scheduler plan's members (its Task 2), inert here.
    public IReadOnlyList<string> PhasesBeforeRounds => [];
    public IReadOnlyList<string> PhasesInTurn => [];
    public Scheduler.FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams) => new Scheduler.OrderedTeams(teams);
    public Scheduler.TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId) => null;
    public Scheduler.Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => null;
    public bool ActivationComplete(GameState state, Scheduler.Activation activation) => true;
    public Scheduler.HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order) => new Scheduler.HookDone();
    public Func<IReadOnlyList<EventPayload>, GameEndingBreach?>? Judge { get; init; }
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => Judge?.Invoke(proposed);
}

public sealed class LambdaStep(string name, Func<StepContext, StepOutcome> run) : IStep
{
    public string Name => name;
    public StepOutcome Run(StepContext ctx) => run(ctx);
}

/// <summary>Another ruleset plus a few sequences resolved first: how a test parks a parent frame the child reads from.</summary>
public sealed class WithSequences(IGameRules inner, params Sequence[] extra) : IGameRules
{
    public static Sequence Parent(string name, params string[] steps) =>
        new(name, steps.Select(s => (IStep)new LambdaStep(s, _ => throw new InvalidOperationException($"{name}: a parked parent never runs"))).ToList());
    public string Id => inner.Id;
    public string HitRoll => inner.HitRoll;
    public Sequence Sequence(string name) => extra.FirstOrDefault(s => s.Name == name) ?? inner.Sequence(name);
    public string? ActionSequence(string actionName) => inner.ActionSequence(actionName);
    public RollSpec RollSpec(string roll) => inner.RollSpec(roll);
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => inner.ReactionsAt(state, properties, point, frame);
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, Scheduler.TurnContext turn) => inner.EndOfTurnSequences(state, properties, turn);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => inner.OnRoundStarted(state, properties, round);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, Scheduler.TurnContext turn) => inner.OnTurnEnded(state, properties, turn);
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams) => inner.OrderReactions(state, actorTeam, reactingTeams);
    public IReadOnlyList<string> PhasesBeforeRounds => inner.PhasesBeforeRounds;
    public IReadOnlyList<string> PhasesInTurn => inner.PhasesInTurn;
    public Scheduler.FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams) => inner.FirstTeam(state, round, teams);
    public Scheduler.TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId) => inner.LegalTurn(state, properties, teamId);
    public Scheduler.Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => inner.ReadAnswer(state, teamId, answer);
    public bool ActivationComplete(GameState state, Scheduler.Activation activation) => inner.ActivationComplete(state, activation);
    public Scheduler.HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order) => inner.RoundBoundaryStep(state, properties, round, order);
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => inner.JudgeAdvance(state, properties, proposed);
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => inner.OnRoundEnded(state, properties, round);
    public int MaxWounds(GameState state, Guid modelId) => inner.MaxWounds(state, modelId);
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => inner.Shooters(state, properties, unitId, targetUnitId);
    public bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId) => inner.MayDeployInside(state, unitId, containerModelId);
    public string? AnswerBreach(GameState state, Ask ask, AskAnswer answer) => inner.AnswerBreach(state, ask, answer);
}
