using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>An action frame, or a casting frame nested in one (Nested), and its entries, from its sequence_started to its finish or
/// abandonment, or an undo to before its start. A live block closes at a live finish; an undone block at its own (undone) finish.
/// Header: `R{round} · {Team} · {Unit} · {Action}[ → {verb }{targets}][ · moved {d}"]`; nested: `Cast {spell} → {targets}`.
/// Targets: the action frame's, its nested frames' whose actor is the block's actor, and a piece its own steps were answered with.
/// Entries hold RollEntry, LineEntry and nested ActivationBlock, in log order. ChainParts: its live rolls and changes, nested ones
/// included, for the folded header.</summary>
public sealed record ActivationBlock(long Seq, GameEvent Event, bool Undone, string Header, Guid? Actor, IReadOnlyList<Guid> Targets,
    IReadOnlyList<LogEntry> Entries, IReadOnlyList<string> ChainParts, bool Nested = false) : LogEntry(Seq, Event, Undone)
{
    public string Chain => string.Join(" → ", ChainParts);
}

/// <summary>
/// The play log (combat-log spec → The log): an action header per action frame with its lines beneath it (a cast inside it a nested
/// block), one RollEntry per RollRecord with its rerolls and edits on its line, a change line per thing a step did to the table, and
/// the few lines outside actions (the round, deployment, objectives, menu edits, undos, the end). Questions, answers, empty steps and
/// frame bookkeeping are not drawn. Copies the log's words and names no rule.
/// </summary>
public static partial class CombatLog
{
    static readonly SequenceContext NoContext = new(null, [], [], null);

    public static string Pip(int face) => face is >= 1 and <= 6 ? char.ConvertFromUtf32(0x2680 + face - 1) : face.ToString();

    /// <summary>The events the log keeps and does not draw.</summary>
    static bool Dropped(EventPayload p) => p is AskOpened or AskAnswered or AskClosed or TurnStarted or TurnEnded or StepCompleted { Rolls.Count: 0, Changes.Count: 0 };

    public static IReadOnlyList<LogEntry> Build(IReadOnlyList<GameEvent> log, Names n)
    {
        var live = EventApplier.EffectiveLog(log).Select(e => e.Seq).ToHashSet();
        bool Dead(GameEvent e) => !live.Contains(e.Seq) && e.Payload is not Undone;
        var frames = new Dictionary<Guid, SequenceStarted>();
        foreach (var s in log.Select(e => e.Payload).OfType<SequenceStarted>()) frames[s.FrameId] = s;
        var rolled = log.Select(e => e.Payload).OfType<StepCompleted>().SelectMany(s => s.Rolls).Select(r => r.Id).ToHashSet();
        var amendments = log.Where(e => e.Payload is DiceRerolled or DiceEdited)
            .ToLookup(e => e.Payload is DiceRerolled r ? r.RollId : ((DiceEdited)e.Payload).RollId, e => (e, Dead(e)));
        var after = Aftermath.Of(log, frames, Dead, n, amendments);

        var top = new List<LogEntry>(); var drafts = new List<Draft>(); var open = new Dictionary<Guid, GameEvent>(); var round = 0;
        var started = new List<(long Seq, string Header)>(); var undos = new List<(long Seq, long ToSeq)>();
        List<LogEntry> Into() => drafts.Count > 0 ? drafts[^1].Entries : top;
        void CloseFrom(long seq)   // every open block started at or after seq, innermost first
        {
            while (drafts.Count > 0 && drafts[^1].Start.Seq >= seq)
            {
                var block = drafts[^1].Close(Dead(drafts[^1].Start)); drafts.RemoveAt(drafts.Count - 1); Into().Add(block);
            }
            foreach (var id in open.Where(f => f.Value.Seq >= seq).Select(f => f.Key).ToList()) open.Remove(id);
        }

        foreach (var e in log)
        {
            var dead = Dead(e);
            if (e.Payload is RoundStarted r && !dead) round = r.Round;
            if (e.Payload is Undone back) CloseFrom(back.ToSeq + 1);
            foreach (var d in drafts) d.See(e, dead);
            var into = Into();
            LineEntry Line(string text) => new(e.Seq, e, dead, text);
            void Chain(string part) { if (!dead) foreach (var d in drafts) d.Chain.Add(part); }
            switch (e.Payload)
            {
                case SequenceStarted s:
                    open[s.FrameId] = e;
                    if (s.Context.Action is { } action && drafts.Count == 0)
                    {
                        drafts.Add(new Draft(e, s, Dead(e), n, round, action));
                        started.Add((e.Seq, drafts[^1].BaseHeader));
                    }
                    else if (s.Sequence == "casting") drafts.Add(new Draft(e, s, Dead(e), n, round, "Cast", nested: true));
                    break;
                case SequenceFinished or SequenceAbandoned:
                    var frame = e.Payload is SequenceFinished f ? f.FrameId : ((SequenceAbandoned)e.Payload).FrameId;
                    if (open.TryGetValue(frame, out var start) && (!dead || Dead(start))) CloseFrom(start.Seq);
                    break;
                case DiceRerolled d when rolled.Contains(d.RollId): break;
                case DiceEdited d when rolled.Contains(d.RollId): break;
                case StepCompleted step:
                    var frameStart = frames.GetValueOrDefault(step.FrameId);
                    foreach (var roll in step.Rolls)
                    {
                        var entry = RollEntry.Of(e, dead, step, roll, frameStart?.Context ?? NoContext, frameStart?.Sequence, amendments[roll.Id], n)
                            with { Consequence = after.Consequence(roll.Id), DieLabels = after.Labels.GetValueOrDefault(roll.Id) };
                        into.Add(entry); Chain(entry.Chain);
                    }
                    if (after.Absorbed.Contains(e.Seq)) break;
                    foreach (var c in ChangeLines(step.Changes, n, drafts.Count == 0, drafts.Count > 0 ? drafts[^1].Actor : null)) { into.Add(Line(c)); Chain(c); }
                    break;
                case RoundStarted rs: into.Add(Line($"Round {rs.Round}")); break;
                case RoundEnded re: into.AddRange(re.Changes.Select(c => Line(LogText.RoundChange(c, n)))); break;
                case Undone u:
                    // The first action the undo took back that no earlier undo had, else the action it stepped back into.
                    var header = FirstAfter(top, drafts, u.ToSeq, undos) ?? started.LastOrDefault(s => s.Seq <= u.ToSeq).Header;
                    into.Add(Line(header is null ? $"Undone to #{u.ToSeq}" : $"Undone to: {header}"));
                    undos.Add((e.Seq, u.ToSeq));
                    break;
                case ModelsMoved m when e.Actor is not null && open.Count == 0 && m.Moves.All(x => x is { From: OnShelf, To: OnTable }):
                    into.AddRange(Deployed(m, n).Select(Line)); break;
                case var p when Dropped(p): break;
                case StateChange when e.Actor is not null: into.Add(Line($"{LogText.Describe(e, n)} · by hand")); break;
                default: into.Add(Line(LogText.Describe(e, n))); break;
            }
        }
        CloseFrom(long.MinValue);
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

    /// <summary>The last roll still standing in entries already built, nested blocks included.</summary>
    public static RollEntry? Latest(IReadOnlyList<LogEntry> entries) => Flatten(entries).OfType<RollEntry>().LastOrDefault(r => !r.Undone);

    /// <summary>Every entry in log order, each block's entries after it.</summary>
    public static IEnumerable<LogEntry> Flatten(IEnumerable<LogEntry> entries) =>
        entries.SelectMany(e => e is ActivationBlock b ? Flatten(b.Entries).Prepend(e) : [e]);

    /// <summary>Rolls, and the blocks holding one at any depth with only those rolls and blocks.</summary>
    static LogEntry? RollsOnly(LogEntry e) => e switch
    {
        RollEntry => e,
        ActivationBlock b => b.Entries.Select(RollsOnly).OfType<LogEntry>().ToList() is { Count: > 0 } kept ? b with { Entries = kept } : null,
        _ => null,
    };

    static bool Involves(LogEntry e, Func<Guid, bool> unit) => e switch
    {
        ActivationBlock b => b.Actor is { } a && unit(a) || b.Targets.Any(unit),
        RollEntry r => r.Context.ActorUnitId is { } a && unit(a) || r.Context.TargetUnitIds.Any(unit),
        _ => false,
    };

    /// <summary>The header of the first block, closed by now, that starts after seq and that no earlier undo already took back: what an
    /// undo to seq took back.</summary>
    static string? FirstAfter(List<LogEntry> top, List<Draft> drafts, long seq, List<(long Seq, long ToSeq)> undos) =>
        Flatten(top.Concat(drafts.SelectMany(d => d.Entries))).OfType<ActivationBlock>()
            .Where(b => b.Seq > seq && !undos.Any(u => u.ToSeq < b.Seq && b.Seq < u.Seq)).MinBy(b => b.Seq)?.Header;
}
