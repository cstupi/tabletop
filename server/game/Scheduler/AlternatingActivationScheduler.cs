using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>One activation per turn; the record clears at round_started (scheduler → The two shapes; game flow → Two schedulers).</summary>
public sealed class AlternatingActivationScheduler(PropertySet properties) : SchedulerBase(properties)
{
    protected override IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn) => EndTurn(state, turn, passed: true);
    protected override IReadOnlyList<EventPayload> OnComplete(GameState state, SchedulerLog log, TurnContext turn) => EndTurn(state, turn, passed: false);

    protected override IReadOnlyList<StateChange> ShapeRoundStartChanges(GameState state, int round) =>
        round == 0 ? [] : ClearRecords(state.Projection.Units.Values);
}
