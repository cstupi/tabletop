using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Where one activation stands, read from the log since turn_started.</summary>
public abstract record CycleState;
public sealed record NoAsk : CycleState;
public sealed record AskOpen(Ask Ask) : CycleState;
public sealed record PassAnswered(long AnsweredAt) : CycleState;
public sealed record NeedsStart(Activation Activation, long AnsweredAt) : CycleState;
public sealed record Running(Activation Activation, long AnsweredAt, Guid FrameId) : CycleState;
public sealed record WaitingCompletion(Activation Activation, long AnsweredAt) : CycleState;
public sealed record ActivationEnded(Activation Activation, long AnsweredAt, long EndedAt) : CycleState;

/// <summary>What the scheduler does about it.</summary>
public abstract record CycleStep;
public sealed record CycleEmit(IReadOnlyList<EventPayload> Events) : CycleStep;
public sealed record CycleWaiting : CycleStep;
/// <summary>No Ask yet and the legal set is empty: the team is skipped.</summary>
public sealed record CycleEmpty : CycleStep;
public sealed record CyclePassed(TurnContext Turn) : CycleStep;
/// <summary>The activation's work and the end-of-turn hook are both done.</summary>
public sealed record CycleComplete(TurnContext Turn) : CycleStep;

/// <summary>
/// One activation: Ask → answer → the activation's changes and its sequence → the end-of-turn sequences → complete. Nothing is held
/// between calls: the last scheduler Ask since turn_started is the cycle, what follows it in the log says how far it got. Sequences are
/// started through the interpreter's ISequenceStarter; the pinned set is what LegalTurn and EndOfTurnSequences read.
/// </summary>
public sealed class TurnCycle(PropertySet properties)
{
    public static Ask AskFor(Guid teamId, TurnAsk ask) => new(Guid.CreateVersion7(), ask.Question, ask.Prompt, [teamId], ask.Legal, ask.AllowPass, Sealed: false, WindowKind: null, FrameId: null);

    public CycleState Read(GameState state, Guid teamId, SchedulerLog log)
    {
        var slice = log.SinceCycle;                                                                     // the turn, or the phase started inside it
        var found = log.LastSchedulerAsk(slice);
        if (found is null) return new NoAsk();
        var (opened, _, closed, closedAt) = found.Value;
        if (closed is null) return new AskOpen(opened.Ask);
        var answer = (closed.Answers.FirstOrDefault(a => a.TeamId == teamId) ?? closed.Answers.FirstOrDefault())?.Answer;
        if (answer is null or Passed) return new PassAnswered(closedAt);
        var act = state.Rules.ReadAnswer(state, teamId, answer);
        if (act is null) return new PassAnswered(closedAt);                                                // names nothing: a pass, and under strict the dispatcher refused it, so nothing like this is ever in the log of a strict game

        var after = slice.Where(e => e.Seq > closedAt).ToList();
        var started = act.Changes.Count == 0 || act.Changes.Any(c => after.Any(e => e.Payload.Equals(c)));
        if (!act.RunsSequence)
        {
            if (!started) return new NeedsStart(act, closedAt);
            return state.Rules.ActivationComplete(state, act) ? new ActivationEnded(act, closedAt, closedAt) : new WaitingCompletion(act, closedAt);
        }
        var start = after.FirstOrDefault(e => e.Payload is SequenceStarted s && s.Context.ActorUnitId == act.UnitId && s.Context.Action == act.Action);
        if (start is null) return started ? new ActivationEnded(act, closedAt, closedAt) : new NeedsStart(act, closedAt);
        var frameId = ((SequenceStarted)start.Payload).FrameId;
        if (state.Projection.Stack.Any(f => f.Id == frameId)) return new Running(act, closedAt, frameId);
        var end = after.FirstOrDefault(e => (e.Payload is SequenceFinished f && f.FrameId == frameId) || (e.Payload is SequenceAbandoned a && a.FrameId == frameId));
        return new ActivationEnded(act, closedAt, end?.Seq ?? start.Seq);
    }

    /// <param name="starter">The interpreter: starts the declared action's sequence and the end-of-turn ones.</param>
    /// <param name="shape">The shape's last word on the Ask (player-turn asks act-or-end-phase); identity when null.</param>
    public CycleStep Step(GameState state, Guid teamId, SchedulerLog log, ISequenceStarter starter, Func<TurnAsk, TurnAsk>? shape = null)
    {
        switch (Read(state, teamId, log))
        {
            case NoAsk:
                var ask = state.Rules.LegalTurn(state, properties, teamId);
                return ask is null ? new CycleEmpty() : new CycleEmit([new AskOpened(AskFor(teamId, shape is null ? ask : shape(ask)))]);
            case AskOpen or Running or WaitingCompletion:
                return new CycleWaiting();
            case PassAnswered p:
                return new CyclePassed(new TurnContext(teamId, null, p.AnsweredAt, p.AnsweredAt));
            case NeedsStart n:
            {
                var events = new List<EventPayload>(n.Activation.Changes);
                if (n.Activation.RunsSequence && state.Rules.ActionSequence(n.Activation.Action) is { } sequence)   // null: an out-of-set action, recorded and nothing more
                    events.AddRange(starter.Start(state, sequence, new SequenceContext(n.Activation.UnitId, [], [], n.Activation.Action)));
                return new CycleEmit(events);
            }
            case ActivationEnded e:
            {
                var turn = new TurnContext(teamId, e.Activation, e.AnsweredAt, e.EndedAt);
                var hookRan = log.SinceCycle.Any(ev => ev.Seq > e.EndedAt && ev.Payload is SequenceStarted);
                if (hookRan) return state.Projection.Stack.Count == 0 ? new CycleComplete(turn) : new CycleWaiting();
                var starts = state.Rules.EndOfTurnSequences(state, properties, turn).SelectMany(s => starter.Start(state, s.Sequence, s.Context)).ToList();
                return starts.Count == 0 ? new CycleComplete(turn) : new CycleEmit(starts);
            }
            default: throw new NotSupportedException();
        }
    }
}
