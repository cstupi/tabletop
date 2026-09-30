using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// A team holds the turn for every activation until its legal set is empty or it passes; the turn is GameRules' list of phases, each an
/// act-or-end-phase Ask loop; the team's record clears at its turn_ended; the round ends when every team has had a turn
/// (scheduler → The two shapes). Before round one the base alternates one unit a turn as under the other shape.
/// </summary>
public sealed class PlayerTurnScheduler(PropertySet properties) : SchedulerBase(properties)
{
    protected override bool IsFinished(GameState state, SchedulerLog log, Guid teamId) =>
        state.Projection.Scheduler.Round == 0
            ? base.IsFinished(state, log, teamId)
            : log.LastTurnEndedByTeam(log.SinceRound).ContainsKey(teamId) || state.Rules.LegalTurn(state, Properties, teamId) is null;

    protected override TurnAsk ShapeAsk(GameState state, TurnAsk ask) => ask with { Question = AskQuestions.ActOrEndPhase, AllowPass = true };

    protected override IReadOnlyList<EventPayload> BeforeFirstAsk(GameState state, SchedulerLog log, Guid teamId)
    {
        var phases = state.Rules.PhasesInTurn;
        return phases.Count > 0 && !log.SinceTurn.Any(e => e.Payload is PhaseStarted) ? [new PhaseStarted(phases[0])] : [];
    }

    protected override IReadOnlyList<EventPayload> OnComplete(GameState state, SchedulerLog log, TurnContext turn)
    {
        var ask = state.Rules.LegalTurn(state, Properties, turn.TeamId);
        return ask is null ? NextPhaseOrEnd(state, log, turn, passed: false) : [new AskOpened(TurnCycle.AskFor(turn.TeamId, ShapeAsk(state, ask)))];
    }

    protected override IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn) => NextPhaseOrEnd(state, log, turn, passed: true);
    protected override IReadOnlyList<EventPayload> OnEmpty(GameState state, SchedulerLog log, TurnContext turn) => NextPhaseOrEnd(state, log, turn, passed: false);

    /// <summary>The next of GameRules' phases inside the turn, or turn_ended after the last (OPR has none: the turn ends).</summary>
    private IReadOnlyList<EventPayload> NextPhaseOrEnd(GameState state, SchedulerLog log, TurnContext turn, bool passed)
    {
        var phases = state.Rules.PhasesInTurn;
        var current = log.SinceTurn.Select(e => e.Payload).OfType<PhaseStarted>().LastOrDefault()?.Phase;
        var index = current is null ? -1 : phases.ToList().IndexOf(current);
        return index + 1 < phases.Count ? [new PhaseStarted(phases[index + 1])] : EndTurn(state, turn, passed);
    }

    /// <summary>The team's records clear when its turn ends (game flow → Two schedulers).</summary>
    protected override IReadOnlyList<StateChange> ShapeTurnEndChanges(GameState state, TurnContext turn) =>
        state.Projection.Scheduler.Round == 0 ? []
            : ClearRecords(TeamUnits.Of(state, turn.TeamId));
}
