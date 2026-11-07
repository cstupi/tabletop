using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

public sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, TargetRead?> Threshold);
public sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context);
public sealed record SequenceStart(string Sequence, SequenceContext Context);

/// <summary>A breach found in the engine's own proposal (enforcement spec → Who raises a breach). Under strict it always ends the game, with these winners.</summary>
public sealed record GameEndingBreach(string Rule, string Detail, IReadOnlyList<Guid> Winners);
/// <summary>A rule an Ask answer breaks (enforcement spec → The breach): its snake_case name and the sentence players read. Under strict it refuses the answer.</summary>
public sealed record AnswerBreachFound(string Rule, string Detail);
public sealed class UnknownSequenceException(string name) : Exception($"No sequence named '{name}'.");

/// <summary>
/// The per-ruleset C# behind an interface, selected by game.game_rules. This plan gives it an identity and nothing else;
/// the scheduler plan adds its hooks (first team, activatable units, phases, round-boundary hooks) and the interpreter plan
/// adds its (sequences, rolls' conventions, thresholds, caps). Extend; do not replace. The merged interface is listed in
/// full under Interfaces → Produces.
/// </summary>
public interface IGameRules
{
    string Id { get; }

    // Added by the interpreter plan. Sequences and steps: the step lists are per-ruleset C# (architecture.md → Where data stops).
    Sequence Sequence(string name);
    /// <summary>The sequence an action row starts; null when the action has none (an out-of-set name accepted under warn/silent).</summary>
    string? ActionSequence(string actionName);
    // Per roll: the dice convention and the threshold derivation, a function (action sequences → constraints 2–4).
    RollSpec RollSpec(string roll);
    string HitRoll { get; }
    string DefenseRoll { get; }
    // Windows: who can react at a named point, and with what (action sequences → Windows).
    IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame);
    // Hooks the scheduler calls (scheduler → The line), each once per turn or round, with the scheduler's TurnContext.
    /// <summary>Sequences to run after the activation's own work, before turn_ended (OPR: end-of-activation morale). Empty: nothing to run.</summary>
    IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn);
    /// <summary>Changes riding on round_started (OPR: fatigue cleared, spell tokens granted). Round 0 — the phases before round one — gets none.</summary>
    IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round);
    /// <summary>Changes riding on turn_ended (OPR: wounds-this-activation to 0; an idle activation clears Shaken).</summary>
    IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn);
    /// <summary>The scheduler's reaction ordering, when it has one; null leaves Windows.ReactionOrder's own policy in force.</summary>
    IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams);

    // Added by the scheduler plan. Every hook reads state.Projection and the log and returns; none holds state.

    /// <summary>Phases before round one, in order; each alternates one unit a turn. OPR: deployment, scouts. Empty: rounds start at once.</summary>
    IReadOnlyList<string> PhasesBeforeRounds { get; }
    /// <summary>Phases inside one team's turn under the player-turn shape, in order. Empty means one unnamed phase (OPR).</summary>
    IReadOnlyList<string> PhasesInTurn { get; }
    /// <summary>Who goes first in <paramref name="round"/> (0 is the phase before round one). Teams are in ordinal order. RollOffNeeded names the ruleset sequence that decides.</summary>
    FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams);
    /// <summary>The team's turn Ask now, read from the phase in state.Projection.Scheduler.Phase; the action rows are properties.Actions; null when its legal set is empty.</summary>
    TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId);
    /// <summary>What a turn answer means; null when it names nothing (treated as a pass).</summary>
    Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer);
    /// <summary>For an activation with no sequence: is its work done (deploy: every model placed).</summary>
    bool ActivationComplete(GameState state, Activation activation);
    /// <summary>Before round one, for an activation still waiting on its work: does the pick no longer stand, because a menu edit since
    /// <paramref name="answeredAt"/> took the unit out of the phase (OPR: set aside, or returned to deployment, before any model left the shelf)?
    /// The scheduler then asks the turn again. Wrapping rulesets must forward it.</summary>
    bool ActivationWithdrawn(GameState state, Activation activation, long answeredAt) => false;
    /// <summary>The interactive round-boundary step, called each Advance after round_started until HookDone (OPR: Ambushers).</summary>
    HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order);

    // Added by the enforcement plan.
    /// <summary>Judges what the interpreter or scheduler proposes before RulesEngine returns it. Null: nothing broken. Wrapping rulesets must forward it.</summary>
    GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => null;
    /// <summary>Changes written as one round_ended event when a player has ended <paramref name="round"/>, before the next round_started (OPR: objectives seized). Empty: no event.</summary>
    IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => [];

    /// <summary>A sequence under which a once grant a step reads is held rather than spent, to the step that closes it (OPR: the attack is "next time the
    /// effect would apply"; StepContext.Consume). None by default. Wrapping rulesets must forward it.</summary>
    bool HoldsOnceGrants(string sequence) => false;

    // Facts the client is told rather than reading a rule name.
    /// <summary>Wounds the model takes before it is killed (OPR: Tough(X), else 1). Wrapping rulesets must forward it.</summary>
    int MaxWounds(GameState state, Guid modelId) => 1;
    /// <summary>The unit's models that may fire at the target unit now (OPR p.8, Who can shoot). Empty when either unit is unknown or off the table. Wrapping rulesets must forward it.</summary>
    IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => [];
    /// <summary>For each unit that may be put inside a container now, before either deploys, the container models it may go in (OPR p.15: "Transports may
    /// deploy with units inside"; passengers spec). Empty by default. Wrapping rulesets must forward it.</summary>
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DeployInside(GameState state) => new Dictionary<Guid, IReadOnlyList<Guid>>();
    /// <summary>The units a player may set aside before deployment now, or return to deployment (OPR p.13 Ambush, p.14 Scout: "May be set aside before
    /// deployment", and whatever counts as having either). Empty by default. Wrapping rulesets must forward it.</summary>
    IReadOnlyList<Guid> MaySetAside(GameState state, PropertySet properties) => [];
    /// <summary>Per unit with a living model on the table, how far its next activation reaches (threat range plan; OPR p.7 Advance and Charge, p.8 Who can
    /// shoot). Empty by default. Wrapping rulesets must forward it.</summary>
    IReadOnlyDictionary<Guid, UnitThreat> Threats(GameState state, PropertySet properties) => new Dictionary<Guid, UnitThreat>();

    // Optional rules (optional-rules spec). The map is GameInfo.OptionalRules; nothing outside the ruleset reads its keys.
    /// <summary>The ruleset's optional rules and presets, sent in Welcome for the lobby. Empty by default. Wrapping rulesets must forward it.</summary>
    RulesOptionCatalog OptionCatalog => RulesOptionCatalog.Empty;
    /// <summary>Side Battle's legs under this rule set, sent in Welcome and read by the deployment-zone breach. ZoneLegs.Default by default. Wrapping rulesets must forward it.</summary>
    Tabletop.Geometry.ZoneLegs Zones => Tabletop.Geometry.ZoneLegs.Default;
    /// <summary>The words a client shows for a terrain declaration under this rule set, keyed by its snake_case name (BuiltinTerrain.DeclarationNames),
    /// sent in Welcome. Empty by default: a client keeps its own words. Wrapping rulesets must forward it.</summary>
    IReadOnlyDictionary<string, string> TerrainWords => new Dictionary<string, string>();
    /// <summary>What each terrain declaration does to a unit under this rule set, one sentence keyed by its snake_case name, sent in Welcome
    /// for the terrain card. Empty by default. Wrapping rulesets must forward it.</summary>
    IReadOnlyDictionary<string, string> TerrainRules => new Dictionary<string, string>();
    /// <summary>How far from an objective marker's centre a model's base edge holds it, sent in Welcome for the clients to draw. Null by default:
    /// no range. Wrapping rulesets must forward it.</summary>
    double? MarkerRange => null;
    /// <summary>What is wrong with an optional-rules map: an unknown key or value. A set_configuration with any is refused. Wrapping rulesets must forward it.</summary>
    IReadOnlyList<string> OptionProblems(IReadOnlyDictionary<string, string> optionalRules) => optionalRules.Keys.Select(k => $"unknown option {k}").ToList();
    /// <summary>What blocks sight in this state under the game's optional rules (Sight.From reads it). Each model's body, by default. Wrapping rulesets must forward it.</summary>
    ISightPolicy SightPolicy(GameState state) => ISightPolicy.PerModel;

    // Asked by dispatch's answer judge.
    /// <summary>A rule an Ask answer inside its legal set still breaks, which set membership cannot see because it needs the table (OPR p.11: every model of a
    /// unit held to 6" once any moved in or through difficult terrain). Null when nothing is broken. Decided under the enforcement setting like a hand move:
    /// strict refuses, warn warns. Wrapping rulesets must forward it.</summary>
    AnswerBreachFound? AnswerBreach(GameState state, Ask ask, AskAnswer answer) => null;

    // Asked by dispatch's hand-move judge.
    /// <summary>May the unit deploy from the shelf inside this model while <paramref name="deploying"/> are the units the actor's team picked to deploy
    /// (OPR p.15: a friendly Transport whose own deployment this is, or one that has not deployed yet; passengers spec). False by default. Wrapping rulesets must forward it.</summary>
    bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId, IReadOnlySet<Guid> deploying) => false;
    /// <summary>A rule a hand placement breaks wherever it is legal to make (OPR p.7: a model on another's base). Null when nothing is broken. Wrapping rulesets must forward it.</summary>
    AnswerBreachFound? PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => null;
}

public sealed class UnknownGameRulesException(string id) : Exception($"No GameRules named '{id}' ships with this build.");

public static class GameRulesRegistry
{
    public static IGameRules Resolve(string id) => id switch
    {
        Tabletop.Content.RuleSets.Gf351 => GrimdarkFutureRules.V351,
        Tabletop.Content.RuleSets.Gf360 => GrimdarkFutureRules.V360,
        _ => throw new UnknownGameRulesException(id),
    };
}
