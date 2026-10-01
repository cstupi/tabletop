using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Frames, positions, values and histories, read from the projection's stack and the effective log. Nothing here is stored.</summary>
public static class FrameReader
{
    public static IReadOnlyList<FrameView> Read(GameState state)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var views = new List<FrameView>();
        foreach (var frame in state.Projection.Stack)
        {
            var started = log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == frame.Id);
            var lastStep = log.FindLastIndex(e => e.Payload is StepCompleted s && s.FrameId == frame.Id);
            var values = new Dictionary<string, int>();
            foreach (var s in log.Where(e => e.Payload is StepCompleted sc && sc.FrameId == frame.Id).Select(e => (StepCompleted)e.Payload))
                foreach (var (k, v) in s.Values ?? new Dictionary<string, int>()) if (k != StepRepeat.Key) values[k] = v;   // the marker is position bookkeeping, read from the raw last step_completed below
            // A repeat completes the same step again: distinct consecutive-name runs give the index of the step after the run's
            // first completion, so a step_completed still marked $repeat (R-F2) holds position at the run's own step one more time.
            var runs = frame.Steps.Count - frame.Steps.Zip(frame.Steps.Skip(1)).Count(p => p.First.Step == p.Second.Step);
            var repeating = frame.Steps.Count > 0 && (frame.Steps[^1].Values?.ContainsKey(StepRepeat.Key) ?? false);
            var position = runs - (repeating ? 1 : 0);
            views.Add(new FrameView(frame, position, values, History(log, frame.Id, Math.Max(started, lastStep) + 1), views.LastOrDefault()));
        }
        return views;
    }

    /// <summary>
    /// The Ask a frame last opened with this question, as the log recorded it. A step reading an earlier step's answer reads it against
    /// this Ask's legal set, since warn and silent accept an answer outside it and the answer alone proves nothing.
    /// </summary>
    public static Ask? Recorded(GameState state, Guid frameId, string question) =>
        EventApplier.EffectiveLog(state.Applier.Log).Select(e => e.Payload).OfType<AskOpened>().LastOrDefault(o => o.Ask.FrameId == frameId && o.Ask.Question == question)?.Ask;

    /// <summary>Everything that happened to this frame since its current step began: its Asks, and the sequences it pushed (depth 1 only).</summary>
    static StepHistory History(IReadOnlyList<GameEvent> log, Guid frameId, int from)
    {
        var asks = new List<AskRecord>();
        var children = new List<ChildRecord>();
        var depth = 0;
        for (var i = from; i < log.Count; i++)
        {
            switch (log[i].Payload)
            {
                case AskOpened o when o.Ask.FrameId == frameId: asks.Add(new AskRecord(o.Ask, null)); break;
                case AskClosed c when asks.FindIndex(a => a.Ask.Id == c.AskId) is var k and >= 0:
                    asks[k] = asks[k] with { Answers = c.Answers.ToDictionary(a => a.TeamId, a => a.Answer) }; break;
                case SequenceStarted s when s.FrameId != frameId:
                    if (depth == 0) children.Add(new ChildRecord(s.FrameId, s.Sequence, s.Context, false, false));
                    depth++; break;
                case SequenceFinished f when f.FrameId != frameId: Close(children, f.FrameId, abandoned: false); depth = Math.Max(0, depth - 1); break;
                case SequenceAbandoned a when a.FrameId != frameId: Close(children, a.FrameId, abandoned: true); depth = Math.Max(0, depth - 1); break;
            }
        }
        return new StepHistory(asks, children);
    }

    static void Close(List<ChildRecord> children, Guid frameId, bool abandoned)
    {
        var k = children.FindIndex(c => c.FrameId == frameId);
        if (k >= 0) children[k] = children[k] with { Finished = !abandoned, Abandoned = abandoned };
    }
}
