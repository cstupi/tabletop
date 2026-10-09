namespace Tabletop.Protocol;

/// <summary>An event named an id the projection does not hold. The projection is unusable and must be rebuilt:
/// the client drops it, reconnects and replays the log; the game server faults the session, because it is applying
/// events it wrote itself and a rebuild throws again at the same event. Nothing is unwound, and the failed event is
/// already in Log, so the caller discards the whole applier rather than the event.</summary>
public sealed class ProjectionDesyncException(string kind, Guid unknownId)
    : Exception($"Event {kind} names {unknownId}, which the projection does not hold.")
{
    /// <summary>The kind of the event or state change that could not be applied.</summary>
    public string Kind { get; } = kind;
    /// <summary>The model, unit, marker, Ask, frame or roll id the projection has no entry for.</summary>
    public Guid UnknownId { get; } = unknownId;
}

/// <summary>
/// Builds a Projection by copying what events say. Rules-free: it decides nothing and derives nothing. The only stores it clears
/// on its own are the three the specs name — the round's order, active team and passes on round_started, the active team on
/// turn_ended, and a roll's pending modifiers when a step_completed records that roll.
/// </summary>
public sealed class EventApplier(Board board, GameInfo game)
{
    private readonly List<GameEvent> _log = new();

    /// <summary>Replaced by a fresh instance on every Undone. Read it through the applier each time; never cache it.</summary>
    public Projection Projection { get; private set; } = Projection.Initial(board, game);
    public IReadOnlyList<GameEvent> Log => _log;

    public void Apply(GameEvent e)
    {
        _log.Add(e);
        if (e.Payload is Undone) Projection = Rebuild(EffectiveLog(_log), e.Seq);
        else ApplyTo(Projection, e);
    }

    public void ApplyAll(IEnumerable<GameEvent> events) { foreach (var e in events) Apply(e); }

    /// <summary>A fresh projection of the live log up to and including <paramref name="seq"/>; what the client shows while an Undo travels.</summary>
    public Projection ProjectTo(long seq) => Rebuild(EffectiveLog(_log).Where(e => e.Seq <= seq).ToList(), seq);

    private Projection Rebuild(IReadOnlyList<GameEvent> live, long lastSeq)
    {
        var p = Projection.Initial(board, game);
        foreach (var e in live) ApplyTo(p, e);
        p.LastSeq = lastSeq;
        return p;
    }

    /// <summary>The log with every undone range removed. Undone markers never appear in the result.</summary>
    public static IReadOnlyList<GameEvent> EffectiveLog(IReadOnlyList<GameEvent> log)
    {
        var live = new List<GameEvent>();
        foreach (var e in log)
        {
            if (e.Payload is Undone u) live.RemoveAll(x => x.Seq > u.ToSeq);
            else live.Add(e);
        }
        return live;
    }

    public static void ApplyTo(Projection p, GameEvent e)
    {
        switch (e.Payload)
        {
            case StateChange c: ApplyChange(p, c); break;
            case RoundStarted r:
                p.Ended = null;
                p.Scheduler.Round = r.Round; var order = r.Order.ToList(); p.Scheduler.OrderMut.Clear(); p.Scheduler.OrderMut.AddRange(order);
                p.Scheduler.ActiveTeamId = null; p.Scheduler.PassedMut.Clear();
                foreach (var c in r.Changes) ApplyChange(p, c);
                break;
            case RoundEnded r: foreach (var c in r.Changes) ApplyChange(p, c); break;
            case GameEnded g: p.Ended = g; break;
            case TurnStarted t: p.Scheduler.ActiveTeamId = t.TeamId; break;
            case TurnEnded t:
                if (t.Passed) p.Scheduler.PassedMut.Add(t.TeamId);
                if (p.Scheduler.ActiveTeamId == t.TeamId) p.Scheduler.ActiveTeamId = null;
                foreach (var c in t.Changes) ApplyChange(p, c);
                break;
            case PhaseStarted ph: p.Scheduler.Phase = ph.Phase; break;
            case SequenceStarted s: p.StackMut.Add(new Frame(s.FrameId, s.Sequence, s.Context, s.Outline)); break;
            case StepCompleted s:
            {
                var frame = FrameOf(p, s.FrameId, s);
                frame.StepsMut.Add(new CompletedStep(s.Step, s.Answer, s.Rolls, s.Changes, s.Values));
                foreach (var roll in s.Rolls) frame.PendingMut.Remove(roll.Roll);
                foreach (var c in s.Changes) ApplyChange(p, c);
                break;
            }
            case SequenceFinished f: Pop(p, f.FrameId, f); break;
            case SequenceAbandoned f: Pop(p, f.FrameId, f); break;
            case AskOpened a: p.OpenAsksMut[a.Ask.Id] = new OpenAsk(a.Ask); break;
            case AskAnswered a: Need(p.OpenAsksMut, a.AskId, a).AnswersMut[a.TeamId] = a.Answer; break;
            case AskClosed a: p.OpenAsksMut.Remove(a.AskId); break;
            case DiceRerolled d: ReplaceDice(p, d, d.FrameId, d.RollId, d.Replaced); break;
            case DiceEdited d: ReplaceDice(p, d, d.FrameId, d.RollId, d.Replaced); break;
            case ModifierAdded m:
            {
                var frame = FrameOf(p, m.FrameId, m);
                if (!frame.PendingMut.TryGetValue(m.Roll, out var list)) frame.PendingMut[m.Roll] = list = new List<RollModifier>();
                list.Add(m.Modifier);
                break;
            }
            case Undone: throw new InvalidOperationException("Undone is applied by rebuilding; use EventApplier.Apply.");
            default: throw new NotSupportedException($"Unknown payload {e.Payload.GetType().Name}");
        }
        p.LastSeq = e.Seq;
    }

    public static void ApplyChange(Projection p, StateChange change)
    {
        switch (change)
        {
            case ModelsMoved m: foreach (var r in m.Moves) Need(p.ModelsMut, r.ModelId, m).Location = r.To; break;
            case WoundsSet w: Need(p.ModelsMut, w.ModelId, w).Wounds = w.Wounds; break;
            case ConditionSet c:
            {
                var set = c.Subject.Kind == SubjectKind.Unit
                    ? Need(p.UnitsMut, c.Subject.Id, c).ConditionsMut
                    : Need(p.ModelsMut, c.Subject.Id, c).ConditionsMut;
                if (c.Present) set.Add(c.Condition); else set.Remove(c.Condition);
                break;
            }
            case ActionRecorded a: Need(p.UnitsMut, a.UnitId, a).ActionsMut.Add(a.Action); break;
            case ActionRecordCleared a: Need(p.UnitsMut, a.UnitId, a).ActionsMut.Clear(); break;
            case GrantAdded g: Need(p.UnitsMut, g.UnitId, g).GrantsMut.Add(g.Grant); break;
            case GrantRemoved g: Need(p.UnitsMut, g.UnitId, g).GrantsMut.RemoveAll(x => x.Id == g.GrantId); break;
            case SpellTokensSet s: Need(p.ModelsMut, s.ModelId, s).SpellTokens = s.Tokens; break;
            case WoundsThisActivationSet w: Need(p.UnitsMut, w.UnitId, w).WoundsThisActivation = w.Wounds; break;
            case ScoreSet s: p.ScoresMut[s.TeamId] = s.Score; break;                       // an assignment cannot miss
            case MarkerPlaced m: p.MarkersMut[m.Marker.Id] = m.Marker; break;
            case MarkerMoved m: p.MarkersMut[m.MarkerId] = Need(p.MarkersMut, m.MarkerId, m) with { Position = m.Position, Yaw = m.Yaw }; break;
            case MarkerRemoved m: p.MarkersMut.Remove(m.MarkerId); p.ControlMut.Remove(m.MarkerId); break;
            case ObjectiveControlSet o: if (o.TeamId is { } team) p.ControlMut[o.MarkerId] = team; else p.ControlMut.Remove(o.MarkerId); break;   // an assignment cannot miss
            default: throw new NotSupportedException($"Unknown state change {change.GetType().Name}");
        }
    }

    /// <summary>The entry, or a desync naming the payload's kind and the id that was not there.</summary>
    private static TValue Need<TValue>(Dictionary<Guid, TValue> from, Guid id, EventPayload payload) =>
        from.TryGetValue(id, out var value) ? value : throw new ProjectionDesyncException(EventKinds.Of(payload), id);

    private static Frame FrameOf(Projection p, Guid frameId, EventPayload payload) =>
        p.StackMut.FirstOrDefault(f => f.Id == frameId) ?? throw new ProjectionDesyncException(EventKinds.Of(payload), frameId);

    private static void Pop(Projection p, Guid frameId, EventPayload payload)
    {
        var index = p.StackMut.FindIndex(f => f.Id == frameId);
        if (index < 0) throw new ProjectionDesyncException(EventKinds.Of(payload), frameId);
        p.StackMut.RemoveRange(index, p.StackMut.Count - index);
    }

    private static void ReplaceDice(Projection p, EventPayload payload, Guid frameId, Guid rollId, IReadOnlyList<DieReplacement> replaced)
    {
        var step = FrameOf(p, frameId, payload).StepsMut.FirstOrDefault(s => s.RollsMut.Any(r => r.Id == rollId))
            ?? throw new ProjectionDesyncException(EventKinds.Of(payload), rollId);
        var i = step.RollsMut.FindIndex(r => r.Id == rollId);
        var dice = step.RollsMut[i].Dice.ToList();
        foreach (var r in replaced)
        {
            if (r.Index < 0 || r.Index >= dice.Count) throw new ProjectionDesyncException(EventKinds.Of(payload), rollId);
            dice[r.Index] = r.New;
        }
        step.RollsMut[i] = step.RollsMut[i] with { Dice = dice };
    }
}
