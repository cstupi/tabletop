using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One line of a unit's This round tab. Dice are the raw faces, after any reroll or edit; Seq is the event the line came from, 0 for a line no single event wrote.</summary>
public sealed record HistoryLine(string Text, IReadOnlyList<int> Dice, long Seq);

/// <summary>
/// A unit's round, from the effective log since the last round_started (in-game UX spec → Stat sheet, This round tab): first the
/// action it took or "Not yet activated", then one line per step_completed of every frame where it is the actor or a target, in log
/// order. A step with no answer, roll or change writes no line. Rules-free: it copies what the log says in LogText's words.
/// </summary>
public static class RoundHistory
{
    public const string NotYetActivated = "Not yet activated";

    public static IReadOnlyList<HistoryLine> For(IReadOnlyList<GameEvent> effectiveLog, Projection p, Names n, Guid unitId)
    {
        var start = 0;
        for (var i = effectiveLog.Count - 1; i >= 0; i--)
            if (effectiveLog[i].Payload is RoundStarted) { start = i + 1; break; }

        var frames = new Dictionary<Guid, SequenceStarted>();
        var steps = new List<StepLine>();
        var byRoll = new Dictionary<Guid, StepLine>();
        HistoryLine? action = null;
        for (var i = start; i < effectiveLog.Count; i++)
        {
            var e = effectiveLog[i];
            switch (e.Payload)
            {
                case SequenceStarted s:
                    frames[s.FrameId] = s;
                    if (action is null && s.Context.ActorUnitId == unitId && s.Context.Action is { } name)
                        action = new HistoryLine($"Activated: {name}", [], e.Seq);
                    break;
                case StepCompleted s when frames.TryGetValue(s.FrameId, out var frame) && Involves(frame.Context, unitId)
                                          && (s.Answer is not null || s.Rolls.Count > 0 || s.Changes.Count > 0):
                    var added = new StepLine(e.Seq, LabelOf(frame, s.Step), s.Answer, s.Rolls.ToList(), s.Changes);
                    steps.Add(added);
                    foreach (var r in s.Rolls) byRoll[r.Id] = added;
                    break;
                case DiceRerolled d when byRoll.TryGetValue(d.RollId, out var rerolled): rerolled.Replace(d.RollId, d.Replaced); break;
                case DiceEdited d when byRoll.TryGetValue(d.RollId, out var edited): edited.Replace(d.RollId, d.Replaced); break;
            }
        }

        var head = action
            ?? (p.Units.GetValueOrDefault(unitId)?.Actions is { Count: > 0 } recorded
                ? new HistoryLine($"Activated: {string.Join(", ", recorded.Order(StringComparer.Ordinal))}", [], 0)
                : new HistoryLine(NotYetActivated, [], 0));
        return [head, .. steps.Select(s => s.ToLine(n))];
    }

    static bool Involves(SequenceContext c, Guid unitId) => c.ActorUnitId == unitId || c.TargetUnitIds.Contains(unitId);

    /// <summary>The outline's label when the frame carried one for this step, else the step's own name.</summary>
    static string LabelOf(SequenceStarted frame, string step) => frame.Outline?.FirstOrDefault(o => o.Step == step)?.Label ?? step;

    sealed class StepLine(long seq, string label, AskAnswer? answer, List<RollRecord> rolls, IReadOnlyList<StateChange> changes)
    {
        public void Replace(Guid rollId, IReadOnlyList<DieReplacement> replaced)
        {
            var i = rolls.FindIndex(r => r.Id == rollId);
            var dice = rolls[i].Dice.ToList();
            foreach (var r in replaced)
                if (r.Index >= 0 && r.Index < dice.Count) dice[r.Index] = r.New;
            rolls[i] = rolls[i] with { Dice = dice };
        }

        public HistoryLine ToLine(Names n)
        {
            var parts = new List<string>();
            if (answer is not null) parts.Add(LogText.Answer(answer, n));
            parts.AddRange(rolls.Select(r => $"{r.Roll} {r.Dice.Count(d => d.Success)} of {r.Dice.Count} succeeded"));
            parts.AddRange(changes.Select(c => Change(c, seq, n)));
            return new HistoryLine($"{label}: {string.Join("; ", parts)}", rolls.SelectMany(r => r.Dice).Select(d => d.Raw).ToList(), seq);
        }
    }

    /// <summary>LogText's words for the change, without the actor: the engine applied it as part of the step.</summary>
    static string Change(StateChange c, long seq, Names n)
    {
        var text = LogText.Describe(new GameEvent(seq, null, c, null), n);
        var engine = n.Participant(null) + " ";
        return text.StartsWith(engine, StringComparison.Ordinal) ? text[engine.Length..] : text;
    }
}
