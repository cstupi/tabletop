using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

// The scheduler's side of the shared engine is Tabletop.GameServer.Engine.IScheduler (this plan's Task 10):
// IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter). The scheduler plan's SchedulerBase (its Task 5) implements it.
// These records are the seam between the two rules plans. This plan declares them because it runs first and its own IGameRules
// members and StubRules reference them; the scheduler plan reads them and adds nothing here.

/// <summary>A team's turn Ask as GameRules computes it: the question, a prompt, the legal set, whether pass is allowed. Null from the hook means the legal set is empty.</summary>
public sealed record TurnAsk(string Question, string Prompt, LegalSet Legal, bool AllowPass);

/// <summary>What a turn answer means. RunsSequence: the interpreter starts the action's sequence. Changes: what the activation writes when it starts (OPR: action_recorded).</summary>
public sealed record Activation(Guid UnitId, string Action, bool RunsSequence, IReadOnlyList<StateChange> Changes);

/// <summary>The turn the end-of-turn hooks see. ActivationEndedAtSeq is the seq at which the activation's own work ended (its frame finished, or the answer closed for a sequence-less activation).</summary>
public sealed record TurnContext(Guid TeamId, Activation? Activation, long AnsweredAtSeq, long ActivationEndedAtSeq);

/// <summary>What a stepwise hook says: done, waiting on something in the log (an open Ask, models still on the shelf), events to emit, or sequences for the interpreter to start.</summary>
public abstract record HookResult;
public sealed record HookDone : HookResult;
public sealed record HookWaiting : HookResult;
public sealed record HookEmit(IReadOnlyList<EventPayload> Events) : HookResult;
/// <summary>Sequences for the interpreter to start, as SequenceStart(Sequence, Context) — the record beside IGameRules.</summary>
public sealed record HookStartSequences(IReadOnlyList<SequenceStart> Sequences) : HookResult;

/// <summary>
/// Who goes first: an order, or "roll off" — the scheduler starts the named ruleset sequence through the interpreter and reads
/// the descending order back from its step_completed.Values place_1..place_n (pages 7, 50; scheduler plan Task 3).
/// </summary>
public abstract record FirstTeamDecision;
public sealed record OrderedTeams(IReadOnlyList<Guid> Order) : FirstTeamDecision;
public sealed record RollOffNeeded(string Sequence) : FirstTeamDecision;
