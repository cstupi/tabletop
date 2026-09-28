using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The interpreter plan's partial: sequences, rolls' conventions and thresholds, windows, and the scheduler's hooks.
/// Every member throws until its own task fills it in (Tasks 11–17), except OrderReactions, ruled null until the scheduler
/// plan implements it (R-F1).</summary>
public sealed partial class GrimdarkFutureRules
{
    public Sequence Sequence(string name) => throw new NotImplementedException();
    public string? ActionSequence(string actionName) => throw new NotImplementedException();
    public RollSpec RollSpec(string roll) => throw new NotImplementedException();
    public string HitRoll => throw new NotImplementedException();
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => throw new NotImplementedException();
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn) => throw new NotImplementedException();
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => throw new NotImplementedException();
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn) => throw new NotImplementedException();
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams) => null;
}
