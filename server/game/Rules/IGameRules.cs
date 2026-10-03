using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

public sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, int?> Threshold);
public sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context);
public sealed record SequenceStart(string Sequence, SequenceContext Context);

/// <summary>A breach found in the engine's own proposal (enforcement spec → Who raises a breach). Under strict it always ends the game, with these winners.</summary>
public sealed record GameEndingBreach(string Rule, string Detail, IReadOnlyList<Guid> Winners);
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
    /// <summary>The interactive round-boundary step, called each Advance after round_started until HookDone (OPR: Ambushers).</summary>
    HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order);

    // Added by the enforcement plan.
    /// <summary>Judges what the interpreter or scheduler proposes before RulesEngine returns it. Null: nothing broken. Wrapping rulesets must forward it.</summary>
    GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => null;
}

public sealed class UnknownGameRulesException(string id) : Exception($"No GameRules named '{id}' ships with this build.");

public static class GameRulesRegistry
{
    public static IGameRules Resolve(string id) => id == GrimdarkFutureRules.RulesId ? GrimdarkFutureRules.Instance : throw new UnknownGameRulesException(id);
}
