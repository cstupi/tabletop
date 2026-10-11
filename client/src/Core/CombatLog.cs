using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>An action frame's entries, from its sequence_started to its finish or abandonment, or an undo to before its start, under one
/// header. A live block closes at a live finish; an undone block at its own (undone) finish. Targets: every target of every
/// sequence_started in the block, its own and its nested frames', in order.</summary>
public sealed record ActivationBlock(long Seq, GameEvent Event, bool Undone, string Header, Guid? Actor, IReadOnlyList<Guid> Targets,
    IReadOnlyList<LogEntry> Entries, IReadOnlyList<string> ChainParts) : LogEntry(Seq, Event, Undone)
{
    public string Chain => string.Join(" → ", ChainParts);
}

/// <summary>The play log (client UX spec → Combat log): every event, undone ones flagged; action frames grouped; one RollEntry per
/// RollRecord with its rerolls and edits attached by RollId; anything else LogText's line. Copies the log's words and names no rule.</summary>
public static class CombatLog
{
    static readonly SequenceContext NoContext = new(null, [], [], null);

    public static string Pip(int face) => face is >= 1 and <= 6 ? char.ConvertFromUtf32(0x2680 + face - 1) : face.ToString();

    public static IReadOnlyList<LogEntry> Build(IReadOnlyList<GameEvent> log, Names n)
    {
        var live = EventApplier.EffectiveLog(log).Select(e => e.Seq).ToHashSet();
        bool Dead(GameEvent e) => !live.Contains(e.Seq) && e.Payload is not Undone;
        var frames = new Dictionary<Guid, SequenceContext>();
        foreach (var s in log.Select(e => e.Payload).OfType<SequenceStarted>()) frames[s.FrameId] = s.Context;
        var rolled = log.Select(e => e.Payload).OfType<StepCompleted>().SelectMany(s => s.Rolls).Select(r => r.Id).ToHashSet();
        var amendments = log.Where(e => e.Payload is DiceRerolled or DiceEdited)
            .ToLookup(e => e.Payload is DiceRerolled r ? r.RollId : ((DiceEdited)e.Payload).RollId, e => (e, Dead(e)));
        var top = new List<LogEntry>(); Draft? open = null; var round = 0;
        foreach (var e in log)
        {
            var dead = Dead(e);
            if (e.Payload is RoundStarted r && !dead) round = r.Round;
            if (open is not null && e.Payload is Undone u && u.ToSeq < open.Start.Seq) { top.Add(open.Close(Dead(open.Start))); open = null; }
            if (open is null && e.Payload is SequenceStarted { Context.Action: { } action } started) { open = new Draft(e, started, Header(round, started.Context, action, n)); continue; }
            var into = open?.Entries ?? top;
            if (open is not null && e.Payload is SequenceStarted nested) open.Target(nested.Context.TargetUnitIds);
            switch (e.Payload)
            {
                case DiceRerolled d when rolled.Contains(d.RollId): break;
                case DiceEdited d when rolled.Contains(d.RollId): break;
                case StepCompleted step:
                    foreach (var roll in step.Rolls)
                    {
                        var entry = RollEntry.Of(e, dead, step, roll, frames.GetValueOrDefault(step.FrameId) ?? NoContext, amendments[roll.Id], n);
                        into.Add(entry);
                        if (!dead) open?.Chain.Add(entry.Chain);
                    }
                    into.AddRange(step.Changes.Select(c => new LineEntry(e.Seq, e, dead, Change(c, e.Seq, n))));
                    if (step.Rolls.Count == 0 && step.Changes.Count == 0) into.Add(new LineEntry(e.Seq, e, dead, LogText.Describe(e, n)));
                    if (!dead) open?.Chain.AddRange(Changes(step.Changes));
                    break;
                default: into.Add(new LineEntry(e.Seq, e, dead, LogText.Describe(e, n))); break;
            }
            var ended = e.Payload switch { SequenceFinished x => x.FrameId, SequenceAbandoned x => x.FrameId, _ => (Guid?)null };
            if (open is not null && ended == open.S.FrameId && (!dead || Dead(open.Start))) { top.Add(open.Close(Dead(open.Start))); open = null; }
        }
        if (open is not null) top.Add(open.Close(Dead(open.Start)));
        return top;
    }

    public static IReadOnlyList<LogEntry> Filter(IReadOnlyList<LogEntry> entries, LogFilter filter, Func<Guid, bool> mine, Guid? unitId) => filter switch
    {
        LogFilter.Rolls => entries.Select(RollsOnly).OfType<LogEntry>().ToList(),
        LogFilter.Mine => entries.Where(e => Involves(e, mine)).ToList(),
        LogFilter.Unit => unitId is { } id ? entries.Where(e => Involves(e, u => u == id)).ToList() : [],
        _ => entries,
    };

    /// <summary>The last roll still standing: what the dice tray shows.</summary>
    public static RollEntry? Latest(IReadOnlyList<GameEvent> log, Names n) => Latest(Build(log, n));

    /// <summary>The last roll still standing in entries already built.</summary>
    public static RollEntry? Latest(IReadOnlyList<LogEntry> entries) =>
        entries.SelectMany(e => e is ActivationBlock b ? b.Entries : (IEnumerable<LogEntry>)[e]).OfType<RollEntry>().LastOrDefault(r => !r.Undone);

    static LogEntry? RollsOnly(LogEntry e) => e switch
    {
        RollEntry => e,
        ActivationBlock b when b.Entries.OfType<RollEntry>().Any() => b with { Entries = b.Entries.OfType<RollEntry>().ToList() },
        _ => null,
    };

    static bool Involves(LogEntry e, Func<Guid, bool> unit) => e switch
    {
        ActivationBlock b => b.Actor is { } a && unit(a) || b.Targets.Any(unit),
        RollEntry r => r.Context.ActorUnitId is { } a && unit(a) || r.Context.TargetUnitIds.Any(unit),
        _ => false,
    };

    static string Header(int round, SequenceContext c, string action, Names n) => string.Join(" · ", new[]
        { $"Round {round}", c.ActorUnitId is { } a && n.TeamOfUnit(a) is { } t ? n.Team(t) : null, c.ActorUnitId is { } u ? n.Unit(u) : null, action }.OfType<string>());

    /// <summary>LogText's words for a step's change, without the actor: the engine applied it.</summary>
    static string Change(StateChange c, long seq, Names n)
    {
        var text = LogText.Describe(new GameEvent(seq, null, c, null), n); var engine = n.Participant(null) + " ";
        return text.StartsWith(engine, StringComparison.Ordinal) ? text[engine.Length..] : text;
    }

    /// <summary>A model a step wounds and sends to the shelf reads removed, not wounded.</summary>
    static IEnumerable<string> Changes(IReadOnlyList<StateChange> changes)
    {
        var shelved = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.To is OnShelf).Select(m => m.ModelId).ToHashSet();
        var wounded = changes.OfType<WoundsSet>().Select(w => w.ModelId).Where(m => !shelved.Contains(m)).Distinct().Count();
        var removed = shelved.Count;
        if (wounded > 0) yield return $"{wounded} model{(wounded == 1 ? "" : "s")} wounded";
        if (removed > 0) yield return $"{removed} model{(removed == 1 ? "" : "s")} removed";
        foreach (var c in changes.OfType<ConditionSet>()) yield return RollEntry.Capital(c.Condition) + (c.Present ? "" : " cleared");
    }

    sealed class Draft(GameEvent start, SequenceStarted s, string header)
    {
        public GameEvent Start { get; } = start; public SequenceStarted S { get; } = s;
        public List<LogEntry> Entries { get; } = []; public List<string> Chain { get; } = [];
        readonly List<Guid> _targets = [.. s.Context.TargetUnitIds.Distinct()];
        public void Target(IEnumerable<Guid> ids) => _targets.AddRange(ids.Where(id => !_targets.Contains(id)).Distinct());
        public ActivationBlock Close(bool undone) => new(Start.Seq, Start, undone, header, S.Context.ActorUnitId, _targets, Entries, Chain);
    }
}
