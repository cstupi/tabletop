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
}

public sealed class LambdaStep(string name, Func<StepContext, StepOutcome> run) : IStep
{
    public string Name => name;
    public StepOutcome Run(StepContext ctx) => run(ctx);
}
