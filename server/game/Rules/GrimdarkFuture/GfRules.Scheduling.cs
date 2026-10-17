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
        var options = ActivationOptions(state, props, teamId, actionRows).ToList();
        return options.Count == 0 ? null : new TurnAsk(AskQuestions.Activate, "Activate a unit", new Options(options), AllowPass: true);
    }

    /// <summary>
    /// Page 13: Aircraft "may only use Advance actions ... (even if Shaken)", and "their mandatory move doesn't break the staying idle
    /// requirement to stop being Shaken" — so Aircraft is read before Shaken, and a Shaken Aircraft is offered Advance, not Idle.
    /// Page 10: every other Shaken unit "must spend their activation being idle". Page 14: Immobile and page 13: Artillery mean Hold only.
    /// Page 7: Charge needs an enemy unit within charge distance.
    /// </summary>
    public static IReadOnlyList<string> ActionsFor(GameState state, PropertySet props, UnitInPlay unit, IReadOnlyList<string> actionRows) =>
        ActivationChoices(state, props, unit, actionRows).Actions;

    /// <summary>ActionsFor with the reason the set is limited, in the book's words and page, for AskOption.Note; null when nothing limits it.</summary>
    public static (IReadOnlyList<string> Actions, string? Note) ActivationChoices(GameState state, PropertySet props, UnitInPlay unit, IReadOnlyList<string> actionRows)
    {
        if (Lookup.UnitHas(state, props, unit.Id, Aircraft)) return (actionRows.Where(a => a == Advance).ToList(), AircraftNote);
        if (unit.Conditions.Contains(Shaken)) return ([ActivationOption.Idle], ShakenNote);
        if (Lookup.UnitHas(state, props, unit.Id, Immobile)) return (actionRows.Where(a => a == Hold).ToList(), ImmobileNote);
        if (Lookup.UnitHas(state, props, unit.Id, Artillery)) return (actionRows.Where(a => a == Hold).ToList(), ArtilleryNote);
        // Instinctive: a unit that can charge must charge; one that can shoot must take an action it shoots in (p.7: Rush "can't shoot").
        switch (InstinctiveAttack(state, props, unit))
        {
            case Charge: return (actionRows.Where(a => a == Charge).ToList(), InstinctiveNote);
            case Hold: return (actionRows.Where(a => a is not Rush and not Charge).ToList(), InstinctiveNote);
        }
        // p.7: "Units may only take a Charge action if at least one model is within charge distance of the target unit".
        return actionRows.Contains(Charge) && GfMelee.ChargeTargets(state, props, unit.Id).Count == 0
            ? (actionRows.Where(a => a != Charge).ToList(), NoChargeNote)
            : (actionRows, null);
    }

    public const string AircraftNote = "Aircraft: may only use Advance actions, even if Shaken (p.13)";
    public const string ShakenNote = "Shaken: must spend this activation being idle, which stops it being Shaken at the end of the activation (p.10)";
    public const string ImmobileNote = "Immobile: may only use Hold actions (p.14)";
    public const string ArtilleryNote = "Artillery: may only use Hold actions (p.13)";
    public const string NoChargeNote = "No Charge: no enemy unit within charge distance (p.7)";

    public Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => answer switch
    {
        // Re-Deployment's pick of a unit on the table in the Scouts phase: its models leave the table, to be deployed again.
        SelectedPieces { PieceIds: [var unit] } when state.Projection.Units.TryGetValue(unit, out var picked) && state.Projection.Scheduler.Phase == Scouts
            && TeamUnits.ModelsOf(state, picked).Any(m => m.Location is OnTable) && GfAmbush.ReDeployable(state, teamId).Contains(unit)
            => new Activation(unit, Deploy, false, [new ModelsMoved(TeamUnits.ModelsOf(state, picked).Where(m => m.Location is OnTable).Select(m => new ModelRelocation(m.Id, m.Location, new OnShelf())).ToList()),
                new ConditionSet(new Subject(SubjectKind.Unit, unit), GfAmbush.ReDeployed, true)]),
        SelectedPieces { PieceIds: [var unit] } when state.Projection.Units.ContainsKey(unit) => new Activation(unit, Deploy, false, []),
        // Delayed Action: the turn passes with the unit unrecorded, so it may still be activated later.
        Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out var action) && state.Projection.Units.ContainsKey(unit) && action == Delay
            => new Activation(unit, action, false, []),
        Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out var action) && state.Projection.Units.ContainsKey(unit)
            => new Activation(unit, action, true, ActivationChanges(state, unit, action)),
        _ => null,
    };

    /// <summary>Deploying ends when the last model of the unit has a placement (scheduler → Deployment). Idle runs its own sequence (p.10, p.11 "or is activated in it").</summary>
    public bool ActivationComplete(GameState state, Activation activation) =>
        activation.Action != Deploy || !TeamUnits.ModelsOf(state, state.Projection.Units[activation.UnitId]).Any(m => m.Location is OnShelf);
}
