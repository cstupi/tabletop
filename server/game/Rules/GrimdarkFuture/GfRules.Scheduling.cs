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
        // p.7: "Players must activate one unit that has not been activated yet". An Ask exists only while the team holds a legal activation,
        // so strict offers no pass; warn and silent keep it in the set, and warn's AnswerBreach (PassBreach) records it.
        return options.Count == 0 ? null
            : new TurnAsk(AskQuestions.Activate, "Activate a unit", new Options(options), AllowPass: state.Info.Enforcement != Enforcement.Strict);
    }

    /// <summary>p.7: "Players must activate one unit that has not been activated yet". A pass to an activate Ask, which is open only while the team holds a legal activation.</summary>
    static AnswerBreachFound? PassBreach(Ask ask, AskAnswer answer) =>
        answer is Passed && ask.FrameId is null && ask.Question == AskQuestions.Activate
            ? new(GfNames.ActivationRequiredRule, "a team with an unactivated unit must activate one (p.7)")
            : null;

    /// <summary>
    /// Page 13: Aircraft "may only use Advance actions ... (even if Shaken)", and "their mandatory move doesn't break the staying idle
    /// requirement to stop being Shaken" — so Aircraft is read before Shaken, and a Shaken Aircraft is offered Advance, not Idle.
    /// Page 10: every other Shaken unit "must spend their activation being idle". Page 14: Immobile and 3.5.1 p.13 Artillery mean Hold only (3.6.0 p.13 Artillery may move: GfEdition.ArtilleryHoldOnly);
    /// 3.6.0 p.14 Cumbersome means Hold or Advance (GfEdition.HasCumbersome).
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
        if (GfEdition.Of(state) is { ArtilleryHoldOnly: true } edition && Lookup.UnitHas(state, props, unit.Id, Artillery))
            return (actionRows.Where(a => a == Hold).ToList(), edition.ArtilleryNote);
        if (GfEdition.Of(state) is { HasCumbersome: true } cumbersome && Lookup.UnitHas(state, props, unit.Id, GfNames.Cumbersome))
            return (actionRows.Where(a => a is Hold or Advance).ToList(), cumbersome.CumbersomeNote);
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

    /// <summary>
    /// A deploy pick whose unit, every model still on the shelf, was set aside since (Deployment: p.13 Ambush "may be set aside before
    /// deployment") or returned to deployment since (Scouts: no longer a set-aside Scout). Re-Deployment's units are not set aside and stand.
    /// </summary>
    public bool ActivationWithdrawn(GameState state, Activation activation, long answeredAt)
    {
        var p = state.Projection;
        if (activation.Action != Deploy || p.Scheduler.Round != 0 || !p.Units.TryGetValue(activation.UnitId, out var unit) || !TeamUnits.AllShelved(state, unit)) return false;
        var aside = unit.Conditions.Contains(SetAside);
        var outOfPhase = p.Scheduler.Phase switch
        {
            Deployment => aside,
            Scouts => !aside && !unit.Conditions.Contains(GfAmbush.ReDeployed),
            _ => false,
        };
        return outOfPhase && EventApplier.EffectiveLog(state.Applier.Log).Any(e => e.Seq > answeredAt && e.Payload is ConditionSet c
            && c.Subject.Id == unit.Id && c.Condition == SetAside);
    }
}
