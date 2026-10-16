using Tabletop.Geometry;
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

/// <summary>
/// The play log (combat-log spec → The log): an action header per action frame with its lines beneath it, one RollEntry per RollRecord
/// with its rerolls and edits on its line, a change line per thing a step did to the table, and the few lines outside actions (the round,
/// deployment, objectives, menu edits, undos, the end). Questions, answers, empty steps and frame bookkeeping are not drawn.
/// Copies the log's words and names no rule.
/// </summary>
public static class CombatLog
{
    static readonly SequenceContext NoContext = new(null, [], [], null);

    public static string Pip(int face) => face is >= 1 and <= 6 ? char.ConvertFromUtf32(0x2680 + face - 1) : face.ToString();

    /// <summary>The events the log keeps and does not draw.</summary>
    static bool Dropped(EventPayload p) => p is AskOpened or AskAnswered or AskClosed or SequenceStarted or SequenceFinished or SequenceAbandoned
        or TurnStarted or TurnEnded or StepCompleted { Rolls.Count: 0, Changes.Count: 0 } or RoundEnded { Changes.Count: 0 };

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
        var started = new List<(long Seq, string Header)>();
        foreach (var e in log)
        {
            var dead = Dead(e);
            if (e.Payload is RoundStarted r && !dead) round = r.Round;
            if (open is not null && e.Payload is Undone u && u.ToSeq < open.Start.Seq) { top.Add(open.Close(Dead(open.Start), n)); open = null; }
            if (open is null && e.Payload is SequenceStarted { Context.Action: { } action } begun)
            {
                open = new Draft(e, begun, Header(round, begun.Context, action, n));
                started.Add((e.Seq, open.BaseHeader));
                continue;
            }
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
                    var changes = ChangeLines(step.Changes, n, open is null).ToList();
                    into.AddRange(changes.Select(c => new LineEntry(e.Seq, e, dead, c)));
                    if (!dead) { open?.Chain.AddRange(changes); open?.Moved(step.Changes); }
                    break;
                case RoundStarted rs: into.Add(new LineEntry(e.Seq, e, dead, $"Round {rs.Round}")); break;
                case Undone undo:
                    // The earliest action the undo took back, else the action it stepped back into.
                    var header = started.FirstOrDefault(s => s.Seq > undo.ToSeq).Header ?? started.LastOrDefault(s => s.Seq <= undo.ToSeq).Header;
                    into.Add(new LineEntry(e.Seq, e, dead, header is null ? $"Undone to #{undo.ToSeq}" : $"Undone to: {header}"));
                    break;
                case var p when Dropped(p): break;
                case StateChange when e.Actor is not null: into.Add(new LineEntry(e.Seq, e, dead, $"{LogText.Describe(e, n)} · by hand")); break;
                default: into.Add(new LineEntry(e.Seq, e, dead, LogText.Describe(e, n))); break;
            }
            var ended = e.Payload switch { SequenceFinished x => x.FrameId, SequenceAbandoned x => x.FrameId, _ => (Guid?)null };
            if (open is not null && ended == open.S.FrameId && (!dead || Dead(open.Start))) { top.Add(open.Close(Dead(open.Start), n)); open = null; }
        }
        if (open is not null) top.Add(open.Close(Dead(open.Start), n));
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
        { $"R{round}", c.ActorUnitId is { } a && n.TeamOfUnit(a) is { } t ? n.Team(t) : null, c.ActorUnitId is { } u ? n.Unit(u) : null, action }.OfType<string>());

    /// <summary>
    /// What a step did to the table: models wounded, models removed (wounded to the shelf), a unit that left the table unwounded fled,
    /// each condition set or cleared. Outside an action a deployment and an objective's control also read, in LogText's words.
    /// </summary>
    static IEnumerable<string> ChangeLines(IReadOnlyList<StateChange> changes, Names n, bool outside)
    {
        var shelved = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.To is OnShelf).Select(m => m.ModelId).ToHashSet();
        var woundedIds = changes.OfType<WoundsSet>().Select(w => w.ModelId).ToHashSet();
        var wounded = woundedIds.Count(m => !shelved.Contains(m));
        var removed = shelved.Count(woundedIds.Contains);
        if (wounded > 0) yield return $"{wounded} model{(wounded == 1 ? "" : "s")} wounded";
        if (removed > 0) yield return $"{removed} model{(removed == 1 ? "" : "s")} removed";
        foreach (var unit in shelved.Where(m => !woundedIds.Contains(m)).Select(m => n.UnitOfModel(m)).OfType<Guid>().Distinct())
            yield return $"{n.Unit(unit)} Fled!";
        foreach (var c in changes.OfType<ConditionSet>()) yield return RollEntry.Capital(c.Condition) + (c.Present ? "" : " cleared");
        if (!outside) yield break;
        foreach (var c in changes)
            if (c is ModelsMoved { Moves: [{ From: OnShelf, To: OnTable }, ..] } or ObjectiveControlSet) yield return Change(c, n);
    }

    /// <summary>LogText's words for a step's change, without the actor: the engine applied it.</summary>
    static string Change(StateChange c, Names n)
    {
        var text = LogText.Describe(new GameEvent(0, null, c, null), n); var engine = n.Participant(null) + " ";
        return text.StartsWith(engine, StringComparison.Ordinal) ? text[engine.Length..] : text;
    }

    sealed class Draft(GameEvent start, SequenceStarted s, string header)
    {
        public GameEvent Start { get; } = start; public SequenceStarted S { get; } = s;
        public string BaseHeader { get; } = header;
        public List<LogEntry> Entries { get; } = []; public List<string> Chain { get; } = [];
        readonly List<Guid> _targets = [.. s.Context.TargetUnitIds.Distinct()];
        readonly Dictionary<Guid, double> _moved = [];
        public void Target(IEnumerable<Guid> ids) => _targets.AddRange(ids.Where(id => !_targets.Contains(id)).Distinct());

        /// <summary>Each model's table-to-table distance, summed over the block's moves.</summary>
        public void Moved(IEnumerable<StateChange> changes)
        {
            foreach (var m in changes.OfType<ModelsMoved>().SelectMany(x => x.Moves))
                if ((m.From, m.To) is (OnTable a, OnTable b))
                    _moved[m.ModelId] = _moved.GetValueOrDefault(m.ModelId) + a.Placement.Position.Horizontal.DistanceTo(b.Placement.Position.Horizontal);
        }

        /// <summary>The header with its targets and the farthest any of the actor's models moved, to 0.5".</summary>
        public ActivationBlock Close(bool undone, Names n)
        {
            var actor = S.Context.ActorUnitId;
            var farthest = _moved.Where(kv => actor is null || n.UnitOfModel(kv.Key) == actor).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            var text = BaseHeader + (_targets.Count == 0 ? "" : $" → {string.Join(", ", _targets.Select(n.Unit))}")
                + (farthest >= 0.25 ? $" · moved {Math.Round(farthest * 2, MidpointRounding.AwayFromZero) / 2:0.#}\"" : "");
            return new(Start.Seq, Start, undone, text, actor, _targets, Entries, Chain);
        }
    }
}
