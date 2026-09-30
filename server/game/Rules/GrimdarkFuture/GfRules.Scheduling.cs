using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The scheduler plan's partial: phases, first team, the turn's legal set and how an answer is read. Every member is a pure reading of GameState.</summary>
public sealed partial class GrimdarkFutureRules
{
    public IReadOnlyList<string> PhasesBeforeRounds { get; } = [Deployment, Scouts];
    public IReadOnlyList<string> PhasesInTurn { get; } = [];

    /// <summary>
    /// Page 6: deployment order is a roll-off, and page 50 keeps one for a free-for-all's deploy order. Turn order is not a roll-off
    /// at any team count: page 7 gives round one to the team that deployed first and every later round to whoever finished activating
    /// first, and page 50 says the same for 3 and 4 players. One rule for two teams and for many, so nothing branches on the team count.
    /// </summary>
    public FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams)
    {
        if (round == 0) return new RollOffNeeded(RollOffSeq);
        var previous = state.Projection.Scheduler.Order.Count > 0 ? state.Projection.Scheduler.Order : teams;
        if (round == 1) return new OrderedTeams(previous);
        var log = SchedulerLog.Of(state);
        var ended = log.LastTurnEndedByTeam(log.SinceRound);           // the round just finished: its round_started is still the last
        return new OrderedTeams(previous.OrderBy(t => ended.GetValueOrDefault(t, 0L)).ThenBy(t => TeamUnits.IndexOf(previous, t)).ToList());
    }

    public TurnAsk? LegalTurn(GameState state, PropertySet props, Guid teamId)
    {
        var phase = state.Projection.Scheduler.Phase;
        if (phase is Deployment or Scouts) return DeployAsk(state, props, teamId, phase);
        var actionRows = props.Actions.Select(a => a.Name).ToList();
        var options = TeamUnits.Of(state, teamId)
            .Where(u => u.Actions.Count == 0 && TeamUnits.AnyOnTable(state, u))
            .SelectMany(u => ActionsFor(state, props, u, actionRows).Select(a => ActivationOption.For(u, a))).ToList();
        return options.Count == 0 ? null : new TurnAsk(AskQuestions.Activate, "Activate a unit", new Options(options), AllowPass: true);
    }

    /// <summary>
    /// Page 13: Aircraft "may only use Advance actions ... (even if Shaken)", and "their mandatory move doesn't break the staying idle
    /// requirement to stop being Shaken" — so Aircraft is read before Shaken, and a Shaken Aircraft is offered Advance, not Idle.
    /// Page 10: every other Shaken unit "must spend their activation being idle". Page 14: Immobile and page 13: Artillery mean Hold only.
    /// </summary>
    public static IReadOnlyList<string> ActionsFor(GameState state, PropertySet props, UnitInPlay unit, IReadOnlyList<string> actionRows)
    {
        if (Lookup.UnitHas(state, props, unit.Id, Aircraft)) return actionRows.Where(a => a == Advance).ToList();
        if (unit.Conditions.Contains(Shaken)) return [ActivationOption.Idle];
        if (Lookup.UnitHas(state, props, unit.Id, Immobile) || Lookup.UnitHas(state, props, unit.Id, Artillery)) return actionRows.Where(a => a == Hold).ToList();
        return actionRows;
    }

    public Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => answer switch
    {
        SelectedPieces { PieceIds: [var unit] } when state.Projection.Units.ContainsKey(unit) => new Activation(unit, Deploy, false, []),
        Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out var action) && state.Projection.Units.ContainsKey(unit)
            => new Activation(unit, action, action != ActivationOption.Idle, [new ActionRecorded(unit, action)]),
        _ => null,
    };

    /// <summary>Deploying ends when the last model of the unit has a placement (scheduler → Deployment). Idle is complete at once.</summary>
    public bool ActivationComplete(GameState state, Activation activation) =>
        activation.Action != Deploy || !TeamUnits.ModelsOf(state, state.Projection.Units[activation.UnitId]).Any(m => m.Location is OnShelf);
}
