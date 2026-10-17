using System.Globalization;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The combat log's headers and change words (combat-log spec → The log).</summary>
public static partial class CombatLog
{
    /// <summary>
    /// What a step did to the table: models wounded, models removed (wounded to the shelf), a unit that left the table unwounded fled,
    /// each condition set or cleared (a unit other than actor, or a model's unit, named first), each grant (`{unit} gets {entry}`).
    /// Outside an action a deployment and an objective's control also read, in LogText's words.
    /// </summary>
    static IEnumerable<string> ChangeLines(IReadOnlyList<StateChange> changes, Names n, bool outside, Guid? actor)
    {
        var shelved = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.To is OnShelf).Select(m => m.ModelId).ToHashSet();
        var woundedIds = changes.OfType<WoundsSet>().Select(w => w.ModelId).ToHashSet();
        var wounded = woundedIds.Count(m => !shelved.Contains(m));
        var removed = shelved.Count(woundedIds.Contains);
        if (wounded > 0) yield return $"{RollWords.Plural(wounded, "model")} wounded";
        if (removed > 0) yield return $"{RollWords.Plural(removed, "model")} removed";
        foreach (var unit in shelved.Where(m => !woundedIds.Contains(m)).Select(m => n.UnitOfModel(m)).OfType<Guid>().Distinct())
            yield return $"{n.Unit(unit)} Fled!";
        foreach (var c in changes.OfType<ConditionSet>())
        {
            var unit = c.Subject.Kind == SubjectKind.Unit ? c.Subject.Id : n.UnitOfModel(c.Subject.Id) ?? c.Subject.Id;
            yield return (unit == actor ? "" : $"{n.Unit(unit)} · ") + RollEntry.Capital(c.Condition) + (c.Present ? "" : " cleared");
        }
        foreach (var g in changes.OfType<GrantAdded>()) yield return $"{n.Unit(g.UnitId)} gets {g.Grant.EntryName}";
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

    /// <summary>One line per unit a player placed from the shelf: `{Team} · {unit} deployed`.</summary>
    static IEnumerable<string> Deployed(ModelsMoved m, Names n) => m.Moves.Select(x => n.UnitOfModel(x.ModelId) ?? x.ModelId).Distinct()
        .Select(u => string.Join(" · ", new[] { n.TeamOfUnit(u) is { } t ? n.Team(t) : null, n.Unit(u) }.OfType<string>()) + " deployed");

    /// <summary>What later steps settle about a roll, read ahead of the build. A judged single die (RollEntry.IsTest) takes the change
    /// words of its frame's later steps up to the frame's finish or the next step with a roll (those steps draw no change line:
    /// Absorbed). A roll-off record's dice belong, in ordinal order, to the teams whose faces so far (live amendments applied) equal
    /// another team's, every team for the first record, the reading the server's roll-off makes; the record after which no team is
    /// tied (else the step's last) takes winner_ordinal.</summary>
    sealed class Aftermath
    {
        readonly Dictionary<Guid, List<string>> _words = [];
        public Dictionary<Guid, IReadOnlyList<string>> Labels { get; } = [];
        public HashSet<long> Absorbed { get; } = [];

        public string? Consequence(Guid roll) => _words.TryGetValue(roll, out var w) && w.Count > 0 ? string.Join(", ", w) : null;

        public static Aftermath Of(IReadOnlyList<GameEvent> log, Dictionary<Guid, SequenceStarted> frames, Func<GameEvent, bool> isDead, Names n,
            ILookup<Guid, (GameEvent Event, bool Dead)> amendments)
        {
            var a = new Aftermath();
            var test = new Dictionary<Guid, (Guid Roll, bool Dead)>(); var rollOff = new Dictionary<Guid, (Guid Roll, bool Dead)>();
            var teams = n.Teams;
            foreach (var e in log)
            {
                var dead = isDead(e);
                switch (e.Payload)
                {
                    case SequenceStarted s: test.Remove(s.FrameId); rollOff.Remove(s.FrameId); break;
                    case SequenceFinished f when test.TryGetValue(f.FrameId, out var t) && t.Dead == dead: test.Remove(f.FrameId); break;
                    case SequenceAbandoned f when test.TryGetValue(f.FrameId, out var t) && t.Dead == dead: test.Remove(f.FrameId); break;
                    case StepCompleted step:
                        var started = frames.GetValueOrDefault(step.FrameId);
                        if (step.Rolls.Count > 0) test.Remove(step.FrameId);   // a later roll's changes are its own
                        else if (test.TryGetValue(step.FrameId, out var pending) && pending.Dead == dead
                            && ChangeLines(step.Changes, n, false, started?.Context.ActorUnitId).ToList() is { Count: > 0 } words)
                        {
                            a._words[pending.Roll].AddRange(words); a.Absorbed.Add(e.Seq);
                        }
                        foreach (var r in step.Rolls.Where(r => RollEntry.IsTest(r, started?.Sequence))) { test[step.FrameId] = (r.Id, dead); a._words[r.Id] = []; }
                        if (step.Rolls.Where(r => r.Roll == "roll-off").ToList() is { Count: > 0 } offs) rollOff[step.FrameId] = (a.RollOff(offs, teams, n, amendments), dead);
                        if (step.Values is { } values && values.TryGetValue("winner_ordinal", out var k) && rollOff.TryGetValue(step.FrameId, out var won) && won.Dead == dead
                            && teams.FirstOrDefault(x => x.Ordinal == k) is { } winner)
                            a._words[won.Roll] = [$"{n.Team(winner.Id)} wins"];
                        break;
                }
            }
            return a;
        }

        /// <summary>Labels one roll-off step's records by the teams tied before each; the record the winner goes on.</summary>
        Guid RollOff(List<RollRecord> records, IReadOnlyList<Team> teams, Names n, ILookup<Guid, (GameEvent Event, bool Dead)> amendments)
        {
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            List<Team> Tied() => teams.Where(t => teams.Any(o => o.Id != t.Id && keys[o.Id].SequenceEqual(keys[t.Id]))).ToList();
            Guid? settled = null;
            foreach (var r in records)
            {
                var tied = Tied();
                var faces = r.Dice.Select(d => d.Raw).ToArray();
                foreach (var (x, _) in amendments[r.Id].Where(x => !x.Dead))
                    foreach (var d in x.Payload is DiceRerolled re ? re.Replaced : ((DiceEdited)x.Payload).Replaced)
                        if (d.Index >= 0 && d.Index < faces.Length) faces[d.Index] = d.New.Raw;
                Labels[r.Id] = faces.Length == tied.Count ? tied.Select(t => n.Team(t.Id)).ToList() : [];
                for (var i = 0; i < Math.Min(tied.Count, faces.Length); i++) keys[tied[i].Id].Add(faces[i]);
                if (settled is null && tied.Count > 0 && Tied().Count == 0) settled = r.Id;
            }
            return settled ?? records[^1].Id;
        }
    }

    /// <summary>An open block: its entries and chain so far, and what its header gathers from the events seen while it is open. A live
    /// block reads only live events; an undone one reads all of its own.</summary>
    sealed class Draft
    {
        readonly SequenceStarted s; readonly bool startDead, nested; readonly Names n; readonly int round; readonly string word;
        readonly List<Guid> _targets = [];
        readonly Dictionary<string, string> _labels = [];
        readonly Dictionary<Guid, double> _moved = [];
        string? _verb, _chosen;

        /// <summary>Word: the action, or `Cast` for a nested casting frame, whose own targets are not its header's.</summary>
        public Draft(GameEvent start, SequenceStarted s, bool startDead, Names n, int round, string word, bool nested = false)
        {
            Start = start; this.s = s; this.startDead = startDead; this.n = n; this.round = round; this.word = word; this.nested = nested;
            if (!nested) Target(s.Context.TargetUnitIds);
        }

        public GameEvent Start { get; }
        public List<LogEntry> Entries { get; } = [];
        public List<string> Chain { get; } = [];
        public Guid? Actor => s.Context.ActorUnitId;

        /// <summary>`R{round} · {Team} · {Unit} · {Action}`: the header before its targets and move, what an undo into it names.</summary>
        public string BaseHeader => string.Join(" · ", new[]
            { $"R{round}", Actor is { } a && n.TeamOfUnit(a) is { } t ? n.Team(t) : null, Actor is { } u ? n.Unit(u) : null, word }.OfType<string>());

        public void See(GameEvent e, bool dead)
        {
            if (dead && !startDead) return;
            switch (e.Payload)
            {
                case SequenceStarted inner when inner.FrameId != s.FrameId && (nested || inner.Context.ActorUnitId == Actor):
                    Target(inner.Context.TargetUnitIds);
                    if (!nested && inner.Sequence == "shooting") _verb ??= "shoots";   // the server pushes `shooting` with no targets; its children carry them
                    break;
                case StepCompleted step:
                    if (step.FrameId == s.FrameId && step.Answer is SelectedPieces p) Target(p.PieceIds);
                    if (nested && step.FrameId == s.FrameId && step.Answer is Chosen c) _chosen = c.OptionId;
                    if (!nested) Moved(step.Changes);
                    break;
                case AskOpened { Ask: { Legal: Options o } ask } when nested && ask.FrameId == s.FrameId:
                    foreach (var choice in o.Choices) _labels[choice.Id] = choice.Label;
                    break;
            }
        }

        void Target(IEnumerable<Guid> ids) => _targets.AddRange(ids.Where(id => !_targets.Contains(id)).Distinct().ToList());

        /// <summary>Each model's table-to-table distance, summed over the block's moves.</summary>
        void Moved(IEnumerable<StateChange> changes)
        {
            foreach (var m in changes.OfType<ModelsMoved>().SelectMany(x => x.Moves))
                if ((m.From, m.To) is (OnTable a, OnTable b))
                    _moved[m.ModelId] = _moved.GetValueOrDefault(m.ModelId) + a.Placement.Position.Horizontal.DistanceTo(b.Placement.Position.Horizontal);
        }

        /// <summary>The header with its targets and the farthest any of the actor's models moved, to 0.5".</summary>
        string Header()
        {
            var targets = _targets.Count == 0 ? "" : $" → {(_verb is { } verb ? verb + " " : "")}{string.Join(", ", _targets.Select(n.Piece))}";
            if (nested) return $"{word}{(_chosen is { } c && _labels.GetValueOrDefault(c) is { } label ? " " + label : "")}{targets}";
            var farthest = _moved.Where(kv => Actor is null || n.UnitOfModel(kv.Key) == Actor).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            var moved = Math.Round(farthest * 2, MidpointRounding.AwayFromZero) / 2;
            return BaseHeader + targets + (moved > 0 ? $" · moved {moved.ToString("0.#", CultureInfo.InvariantCulture)}\"" : "");
        }

        public ActivationBlock Close(bool undone) => new(Start.Seq, Start, undone, Header(), Actor, _targets, Entries, Chain, nested);
    }
}
