using System.Reflection;
using System.Text.Json.Serialization;

namespace Tabletop.Protocol;

/// <summary>Everything the log holds. Replay applies these and never re-derives them.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RoundStarted), "round_started")]
[JsonDerivedType(typeof(RoundEnded), "round_ended")]
[JsonDerivedType(typeof(GameEnded), "game_ended")]
[JsonDerivedType(typeof(TurnStarted), "turn_started")]
[JsonDerivedType(typeof(TurnEnded), "turn_ended")]
[JsonDerivedType(typeof(PhaseStarted), "phase_started")]
[JsonDerivedType(typeof(SequenceStarted), "sequence_started")]
[JsonDerivedType(typeof(StepCompleted), "step_completed")]
[JsonDerivedType(typeof(SequenceFinished), "sequence_finished")]
[JsonDerivedType(typeof(SequenceAbandoned), "sequence_abandoned")]
[JsonDerivedType(typeof(AskOpened), "ask_opened")]
[JsonDerivedType(typeof(AskAnswered), "ask_answered")]
[JsonDerivedType(typeof(AskClosed), "ask_closed")]
[JsonDerivedType(typeof(DiceRerolled), "dice_rerolled")]
[JsonDerivedType(typeof(DiceEdited), "dice_edited")]
[JsonDerivedType(typeof(ModifierAdded), "modifier_added")]
[JsonDerivedType(typeof(Undone), "undone")]
[JsonDerivedType(typeof(ModelsMoved), "models_moved")]
[JsonDerivedType(typeof(WoundsSet), "wounds_set")]
[JsonDerivedType(typeof(ConditionSet), "condition_set")]
[JsonDerivedType(typeof(ActionRecorded), "action_recorded")]
[JsonDerivedType(typeof(ActionRecordCleared), "action_record_cleared")]
[JsonDerivedType(typeof(GrantAdded), "grant_added")]
[JsonDerivedType(typeof(GrantRemoved), "grant_removed")]
[JsonDerivedType(typeof(SpellTokensSet), "spell_tokens_set")]
[JsonDerivedType(typeof(WoundsThisActivationSet), "wounds_this_activation_set")]
[JsonDerivedType(typeof(ScoreSet), "score_set")]
[JsonDerivedType(typeof(MarkerPlaced), "marker_placed")]
[JsonDerivedType(typeof(MarkerMoved), "marker_moved")]
[JsonDerivedType(typeof(MarkerRemoved), "marker_removed")]
[JsonDerivedType(typeof(ObjectiveControlSet), "objective_control_set")]
public abstract record EventPayload;

// Scheduler
/// <summary>Order is the order over teams this round. Changes are the round-boundary hooks' effects (record clears, tokens, fatigue).</summary>
public sealed record RoundStarted(int Round, IReadOnlyList<Guid> Order, IReadOnlyList<StateChange> Changes) : EventPayload;
/// <summary>The end of a round, after a player ended it and before the next round starts: the ruleset's changes at that moment (OPR p.6: objectives seized, contested, kept). Written only when there are changes.</summary>
public sealed record RoundEnded(int Round, IReadOnlyList<StateChange> Changes) : EventPayload;
public sealed record TurnStarted(Guid TeamId) : EventPayload;
/// <summary>Passed is the logged pass that stops a team being re-asked this round. Changes: the end-of-turn hook's effects.</summary>
public sealed record TurnEnded(Guid TeamId, bool Passed, IReadOnlyList<StateChange> Changes) : EventPayload;
public sealed record PhaseStarted(string Phase) : EventPayload;
/// <summary>The game is over (enforcement spec → The game_ended event). Reason is the sentence players read. Winners: one team is the
/// winner, several are a tie between them, none is no result. Actor is null when the engine ended it.</summary>
public sealed record GameEnded(string Reason, IReadOnlyList<Guid> Winners) : EventPayload;

// Resolution stack
/// <summary>Outline: the player-facing steps of the sequence, in order; Step matches StepCompleted.Step. Null in logs written before it existed.</summary>
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null) : EventPayload;
public sealed record OutlineStep(string Step, string Label);
/// <summary>
/// Everything the step produced: the answer that resumed it, its rolls, the state changes it applied, and Values — a flat map of
/// named integers holding the step's settled parameters (attacks counted, a capped multiplier, a distance used in tenths, a token spend,
/// a winner's ordinal) so a later step or nested frame reads them without re-deriving. No schema per step kind; omitted when null.
/// Applied: the catalog entries (rules, spells) whose effect changed this step's result, each once, in order of first application; null in logs written before it and when none acted.
/// A step may complete and run again (the same name twice in a row) — a second step_completed, a fresh history.
/// </summary>
public sealed record StepCompleted(Guid FrameId, string Step, AskAnswer? Answer, IReadOnlyList<RollRecord> Rolls, IReadOnlyList<StateChange> Changes, IReadOnlyList<string>? Applied = null) : EventPayload
{
    public IReadOnlyDictionary<string, int>? Values { get; init; }
}
public sealed record SequenceFinished(Guid FrameId) : EventPayload;
/// <summary>Pops the frame and everything above it. Applied effects stay applied.</summary>
public sealed record SequenceAbandoned(Guid FrameId) : EventPayload;

// Asks
public sealed record AskOpened(Ask Ask) : EventPayload;
/// <summary>Answer is null when withheld from this viewer (a sealed window still open). Breach: the rule name when an answer inside the legal set broke a rule the table answers (IGameRules.AnswerBreach) and was accepted under warn or silent; OutsideLegalSet is then false. Omitted when null.</summary>
public sealed record AskAnswered(Guid AskId, Guid TeamId, AskAnswer? Answer, bool OutsideLegalSet, string? Breach = null) : EventPayload;
public sealed record TeamAnswer(Guid TeamId, AskAnswer Answer);
public sealed record AskClosed(Guid AskId, IReadOnlyList<TeamAnswer> Answers) : EventPayload;

// Dice
public sealed record DieReplacement(int Index, Die Old, Die New);
public sealed record DiceRerolled(Guid FrameId, Guid RollId, IReadOnlyList<DieReplacement> Replaced) : EventPayload;
public sealed record DiceEdited(Guid FrameId, Guid RollId, IReadOnlyList<DieReplacement> Replaced) : EventPayload;
public sealed record ModifierAdded(Guid FrameId, string Roll, RollModifier Modifier) : EventPayload;

// Undo
/// <summary>Replay to ToSeq from the initial state. The log stays append-only; the applier drops the undone range.</summary>
public sealed record Undone(long ToSeq) : EventPayload;

/// <summary>One row of the log. Actor is null when the engine acted. Warning is set under warn enforcement.</summary>
public sealed record GameEvent(long Seq, Guid? Actor, EventPayload Payload, string? Warning)
{
    [JsonIgnore] public string Kind => EventKinds.Of(Payload);
}

public static class EventKinds
{
    static readonly IReadOnlyDictionary<Type, string> ByType = typeof(EventPayload)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    public static IReadOnlyCollection<string> All => ByType.Values.ToArray();
    public static string Of(Type payloadType) => ByType[payloadType];
    public static string Of(EventPayload payload) => ByType[payload.GetType()];
}
