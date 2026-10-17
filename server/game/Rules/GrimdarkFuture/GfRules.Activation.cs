using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book rules that change which units may activate and how (army-book rules spec, batch 4): Delayed Action's pass, Inquisitorial Agent's and
/// Martial Prowess's second activation, and Instinctive's forced attack. Every member is a pure reading of GameState and the log.
/// </summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>"Once per game … it may be activated even if it had already activated this round".</summary>
    static readonly string[] SecondActivationRules = [InquisitorialAgent, MartialProwess];

    /// <summary>The team's units offered beyond those that have not activated: a unit that may activate a second time this round.</summary>
    static IEnumerable<UnitInPlay> SecondActivations(GameState state, PropertySet props, Guid teamId)
    {
        var units = TeamUnits.Of(state, teamId);
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var roundStart = log.FindLastIndex(e => e.Payload is RoundStarted);
        foreach (var rule in SecondActivationRules)
        {
            // "Only up to one third of the units in the army with this rule at the beginning of the game (rounding up) may use it in a single round."
            var holders = units.Count(u => Holds(u, rule));
            if (holders == 0) continue;
            var thisRound = SecondActivationUnits(log.Skip(Math.Max(0, roundStart))).Count(u => units.Any(x => x.Id == u && Holds(x, rule)));
            if (thisRound >= (holders + 2) / 3) continue;
            var used = SecondActivationUnits(log);
            foreach (var u in units.Where(u => u.Actions.Count == 1 && !used.Contains(u.Id) && Lookup.UnitHas(state, props, u.Id, rule) && TeamUnits.AnyOnTable(state, u)))
                yield return u;
        }
    }

    /// <summary>A rule held at the beginning of the game: every model of the definition carries it, or the unit does.</summary>
    static bool Holds(UnitInPlay u, string rule) =>
        u.Definition.Properties.Any(p => p.Name == rule) || (u.Definition.Models.Count > 0 && u.Definition.Models.All(m => m.Properties.Any(p => p.Name == rule)));

    /// <summary>The units recorded with a second action in one round: an action_recorded for a unit that already had one since the round began.</summary>
    static IReadOnlyCollection<Guid> SecondActivationUnits(IEnumerable<GameEvent> log)
    {
        var once = new HashSet<Guid>(); var twice = new HashSet<Guid>();
        foreach (var p in log.Select(e => e.Payload))
            switch (p)
            {
                case RoundStarted: once.Clear(); break;
                case ActionRecorded a when !once.Add(a.UnitId): twice.Add(a.UnitId); break;
            }
        return twice;
    }

    /// <summary>
    /// Delayed Action: "Once per round, if your opponent has more units left to activate than you, then this model's unit may pass its turn instead of
    /// activating (may still be activated later)." Units left to activate: on the table and not yet activated this round.
    /// </summary>
    static bool MayDelay(GameState state, PropertySet props, UnitInPlay unit, Guid teamId)
    {
        if (unit.Conditions.Contains(Shaken) || !Lookup.UnitHas(state, props, unit.Id, DelayedAction)) return false;
        int Left(Guid team) => TeamUnits.Of(state, team).Count(u => u.Actions.Count == 0 && TeamUnits.AnyOnTable(state, u));
        if (!state.Info.Teams.Any(t => t.Id != teamId && Left(t.Id) > Left(teamId))) return false;
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var delay = ActivationOption.Id(unit.Id, Delay);
        return !log.Skip(Math.Max(0, log.FindLastIndex(e => e.Payload is RoundStarted)))
            .Any(e => e.Payload is AskClosed c && c.Answers.Any(a => a.Answer is Chosen { OptionId: var id } && id == delay));
    }

    /// <summary>The options of the activate Ask: each unit not yet activated (or activating a second time) with its actions, and Delay where it applies.</summary>
    static IEnumerable<AskOption> ActivationOptions(GameState state, PropertySet props, Guid teamId, IReadOnlyList<string> actionRows)
    {
        var fresh = TeamUnits.Of(state, teamId).Where(u => u.Actions.Count == 0 && TeamUnits.AnyOnTable(state, u)).ToList();
        foreach (var u in fresh)
        {
            var (actions, note) = ActivationChoices(state, props, u, actionRows);
            foreach (var a in actions) yield return ActivationOption.For(u, a, note);
            if (MayDelay(state, props, u, teamId)) yield return ActivationOption.For(u, Delay, DelayNote);
        }
        foreach (var u in SecondActivations(state, props, teamId).DistinctBy(u => u.Id))
        {
            var (actions, note) = ActivationChoices(state, props, u, actionRows);
            foreach (var a in actions.Where(a => a != ActivationOption.Idle)) yield return ActivationOption.For(u, a, note ?? SecondNote);
        }
    }

    public const string DelayNote = "Delayed Action: pass this turn instead of activating, and activate later (once per round)";
    public const string SecondNote = "Activated again (once per game)";

    /// <summary>
    /// What an activation writes as it starts: its action on record and, on a second activation, fatigue cleared ("stops being fatigued when activated
    /// for the second time"). Instinctive's mark is written by the activation's first step, which reads the properties.
    /// </summary>
    public static IReadOnlyList<StateChange> ActivationChanges(GameState state, Guid unitId, string action)
    {
        var unit = state.Projection.Units[unitId];
        var changes = new List<StateChange> { new ActionRecorded(unitId, action) };
        if (unit.Actions.Count > 0 && unit.Conditions.Contains(Fatigued)) changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unitId), Fatigued, false));
        return changes;
    }

    /// <summary>
    /// Instinctive: "When this model is activated, if it is able to shoot/charge an enemy unit, then it must immediately attack the closest valid target":
    /// Charge when a charge is possible, else a shot when one is possible (Rush cannot shoot, p.7); null when it can do neither or does not hold it.
    /// </summary>
    public static string? InstinctiveAttack(GameState state, PropertySet props, UnitInPlay unit)
    {
        if (unit.Conditions.Contains(Shaken) || !Lookup.UnitHas(state, props, unit.Id, Instinctive)) return null;
        if (GfMelee.ChargeTargets(state, props, unit.Id).Count > 0) return Charge;
        var team = Teams.Of(state, unit.Id);
        return state.Projection.Units.Values.Any(t => Teams.Of(state, t.Id) != team && Instance.Shooters(state, props, unit.Id, t.Id).Count > 0) ? Hold : null;
    }

    public const string InstinctiveNote = "Instinctive: must attack the closest valid target, with +1 to hit";

    /// <summary>The closest of the candidates by the units' distance (p.5, base to base); ties keep the first.</summary>
    public static IReadOnlyList<Guid> Closest(GameState state, Guid unit, IReadOnlyList<Guid> candidates) =>
        candidates.Count <= 1 ? candidates : [candidates.MinBy(t => Battlefield.UnitDistance(state, unit, t) ?? double.MaxValue)];
}
