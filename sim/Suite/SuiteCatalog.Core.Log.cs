using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>A sequence frame of the effective log: its context, the frame it was started under, and its steps with the Asks each answered.</summary>
internal sealed record CoreFrame(Guid Id, string Sequence, Guid? Actor, IReadOnlyList<Guid> Targets, string? Action, Guid? Parent, long StartSeq, int Round, List<CoreStep> Steps);

/// <summary>One step_completed and the Asks its frame opened since the previous step.</summary>
internal sealed record CoreStep(StepCompleted Step, long Seq, IReadOnlyList<Ask> Asks);

/// <summary>An activation: the unit, its team, the round, and the seq of the action's record.</summary>
internal sealed record CoreActivation(Guid Unit, Guid Team, int Round, long Seq);

/// <summary>
/// The effective log read the way the core checks need it: frames with their parents, every roll with its dice as edited or rerolled, the
/// activations, and model locations at any seq. A reading of the log, not of the rules.
/// </summary>
internal sealed class CoreLog
{
    readonly Dictionary<Guid, CoreFrame> _frames = [];
    readonly Dictionary<Guid, Die[]> _dice = [];
    readonly List<(long Seq, ModelRelocation Move)> _moves = [];
    readonly List<(long Seq, Guid Unit, string Condition, bool Present)> _conditions = [];

    public SuiteGameRun Run { get; }
    public IReadOnlyList<GameEvent> Events { get; }
    public IReadOnlyList<CoreFrame> Frames { get; }
    public IReadOnlyList<CoreActivation> Activations { get; }
    public IReadOnlyList<RoundStarted> Rounds { get; }
    /// <summary>Each turn's turn_started and turn_ended seqs.</summary>
    public IReadOnlyList<(long Start, long End)> Turns { get; }

    public CoreLog(SuiteGameRun run)
    {
        Run = run;
        Events = EventApplier.EffectiveLog(run.EffectiveLog);
        var frames = new List<CoreFrame>();
        var stack = new List<Guid>();
        var pending = new Dictionary<Guid, List<Ask>>();
        var activations = new List<CoreActivation>();
        var round = 0;
        Guid? turn = null;
        foreach (var e in Events)
        {
            switch (e.Payload)
            {
                case RoundStarted r: round = r.Round; break;
                case TurnStarted t: turn = t.TeamId; break;
                case TurnEnded: turn = null; break;
                case SequenceStarted s:
                    var frame = new CoreFrame(s.FrameId, s.Sequence, s.Context.ActorUnitId, s.Context.TargetUnitIds, s.Context.Action, stack.Count > 0 ? stack[^1] : null, e.Seq, round, []);
                    frames.Add(frame); _frames[s.FrameId] = frame; stack.Add(s.FrameId);
                    break;
                case SequenceFinished f: stack.Remove(f.FrameId); break;
                case SequenceAbandoned a: if (stack.IndexOf(a.FrameId) is >= 0 and var i) stack.RemoveRange(i, stack.Count - i); break;
                case AskOpened { Ask.FrameId: { } owner } o: (pending.TryGetValue(owner, out var list) ? list : pending[owner] = []).Add(o.Ask); break;
                case StepCompleted c when _frames.TryGetValue(c.FrameId, out var owner):
                    owner.Steps.Add(new CoreStep(c, e.Seq, pending.Remove(c.FrameId, out var asks) ? asks : []));
                    foreach (var roll in c.Rolls) _dice[roll.Id] = [.. roll.Dice];
                    break;
                case DiceEdited d: Replace(d.RollId, d.Replaced); break;
                case DiceRerolled d: Replace(d.RollId, d.Replaced); break;
            }
            foreach (var change in ChangesOf(e.Payload))
            {
                if (change is ModelsMoved mm) _moves.AddRange(mm.Moves.Select(m => (e.Seq, m)));
                if (change is ConditionSet { Subject.Kind: SubjectKind.Unit } cs) _conditions.Add((e.Seq, cs.Subject.Id, cs.Condition, cs.Present));
                if (change is ActionRecorded a && turn is { } team) activations.Add(new CoreActivation(a.UnitId, team, round, e.Seq));
            }
        }
        Frames = frames;
        Activations = activations;
        var questions = Events.Select(e => e.Payload).OfType<AskOpened>().ToDictionary(o => o.Ask.Id, o => o.Ask.Question);
        Deployments = [.. Events.Where(e => e.Payload is AskClosed).SelectMany(e => ((AskClosed)e.Payload).Answers
            .Where(a => questions.GetValueOrDefault(((AskClosed)e.Payload).AskId) is AskQuestions.Deploy or AskQuestions.DeployScouts or AskQuestions.DeployAmbusher)
            .SelectMany(a => a.Answer is SelectedPieces p ? p.PieceIds.Select(u => (questions[((AskClosed)e.Payload).AskId], u, a.TeamId, e.Seq)) : []))];
        Rounds = Events.Select(e => e.Payload).OfType<RoundStarted>().ToList();
        Turns = Events.Where(e => e.Payload is TurnStarted)
            .Select(s => (s.Seq, Events.FirstOrDefault(e => e.Seq > s.Seq && e.Payload is TurnEnded)?.Seq ?? long.MaxValue - 1)).ToList();
    }

    void Replace(Guid roll, IReadOnlyList<DieReplacement> replaced)
    {
        if (!_dice.TryGetValue(roll, out var dice)) return;
        foreach (var r in replaced) dice[r.Index] = r.New;
    }

    static IEnumerable<StateChange> ChangesOf(EventPayload p) => p switch
    {
        StateChange c => [c],
        StepCompleted s => s.Changes,
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        _ => [],
    };

    public CoreFrame? Frame(Guid? id) => id is { } i && _frames.TryGetValue(i, out var f) ? f : null;
    public IEnumerable<CoreFrame> Children(CoreFrame parent) => Frames.Where(f => f.Parent == parent.Id);
    public IEnumerable<CoreFrame> Descendants(CoreFrame parent) => Children(parent).SelectMany(c => (IEnumerable<CoreFrame>)[c, .. Descendants(c)]);

    /// <summary>A roll's dice as they stand: edits and rerolls applied.</summary>
    public IReadOnlyList<Die> Dice(RollRecord roll) => _dice.TryGetValue(roll.Id, out var d) ? d : roll.Dice;

    /// <summary>Every roll of that kind in log order, with the frame that made it.</summary>
    public IReadOnlyList<(RollRecord Roll, CoreFrame Frame)> Rolls(string kind) =>
        Frames.SelectMany(f => f.Steps.SelectMany(s => s.Step.Rolls.Where(r => r.Roll == kind).Select(r => (s.Seq, Roll: r, Frame: f))))
              .OrderBy(x => x.Seq).Select(x => (x.Roll, x.Frame)).ToList();

    /// <summary>The ordinal-th roll of that kind, when a dice fix edited it.</summary>
    public IReadOnlyList<Die>? Edited(string kind, int ordinal) =>
        Rolls(kind).ElementAtOrDefault(ordinal) is { Roll: { } roll } && Events.Any(e => e.Payload is DiceEdited d && d.RollId == roll.Id) ? Dice(roll) : null;

    public Guid UnitOf(Guid model) => Run.Final.Models[model].UnitId;

    /// <summary>Where every model stood before the event at that seq.</summary>
    public IReadOnlyDictionary<Guid, Location> LocationsBefore(long seq)
    {
        var at = Run.Final.Models.Keys.ToDictionary(m => m, _ => (Location)new OnShelf());
        foreach (var (s, move) in _moves) { if (s >= seq) break; at[move.ModelId] = move.To; }
        return at;
    }

    /// <summary>The unit's models on the table or inside a container before that seq.</summary>
    public int LivingModels(Guid unit, long seq) =>
        LocationsBefore(seq).Count(kv => UnitOf(kv.Key) == unit && kv.Value is not OnShelf);

    public bool UnitDestroyedBefore(Guid unit, long seq) => LivingModels(unit, seq) == 0;

    /// <summary>The unit's models' table positions before that seq.</summary>
    public IReadOnlyList<Point2> Placed(Guid unit, long seq) =>
        [.. LocationsBefore(seq).Where(kv => UnitOf(kv.Key) == unit && kv.Value is OnTable).Select(kv => ((OnTable)kv.Value).Placement.Position.Horizontal)];

    /// <summary>The team that deployed the unit.</summary>
    public Guid? TeamOf(Guid unit) => Deployments.FirstOrDefault(d => d.Unit == unit) is { Question: not null } d ? d.Team : null;

    /// <summary>Every unit picked at a deploy, deploy_scouts or deploy_ambusher Ask: the question, the unit, its team and the seq of the answer.</summary>
    public IReadOnlyList<(string Question, Guid Unit, Guid Team, long Seq)> Deployments { get; private set; } = [];

    /// <summary>Where the unit's models were first put on the table.</summary>
    public IReadOnlyList<Point2> FirstPlacement(Guid unit) =>
        [.. _moves.Where(m => UnitOf(m.Move.ModelId) == unit && m.Move is { From: OnShelf, To: OnTable }).GroupBy(m => m.Move.ModelId)
            .Select(g => ((OnTable)g.First().Move.To).Placement.Position.Horizontal)];

    /// <summary>Every model relocation, in log order.</summary>
    public IReadOnlyList<(long Seq, ModelRelocation Move)> Moves => _moves;

    /// <summary>
    /// Every model that left a container for the table: the seq, and whether its base stood fully within 6" of the container where the container
    /// stood (p.15), measured by Tabletop.Geometry.
    /// </summary>
    public IEnumerable<(long Seq, bool Within)> Exits()
    {
        foreach (var (seq, move) in _moves)
        {
            if (move is not { From: Inside from, To: OnTable to }) continue;
            if (LocationsBefore(seq)[from.ContainerId] is not OnTable container) { yield return (seq, false); continue; }
            yield return (seq, Containment.BaseFullyWithin(new PlacedModel(Body(move.ModelId), to.Placement), new PlacedModel(Body(from.ContainerId), container.Placement), 6));
        }
    }

    public ModelBody Body(Guid model) => new(Run.Final.Models[model].Slot.Base, Run.Final.Models[model].Slot.Height);

    /// <summary>The units of every other participant.</summary>
    public IEnumerable<Guid> EnemiesOf(Guid unit) =>
        Run.Final.Units.Values.Where(u => u.ParticipantId != Run.Final.Units[unit].ParticipantId).Select(u => u.Id);

    /// <summary>Whether the unit carried the condition before that seq.</summary>
    public bool HasCondition(Guid unit, string condition, long seq) =>
        _conditions.LastOrDefault(c => c.Seq < seq && c.Unit == unit && c.Condition == condition) is { Present: true };

    /// <summary>Every condition set on a unit, in log order.</summary>
    public IReadOnlyList<(long Seq, Guid Unit, string Condition, bool Present)> Conditions => _conditions;

    /// <summary>p.10's "starting size": the models the unit's definition holds.</summary>
    public int StartingSize(Guid unit) => Run.Final.Units[unit].Definition.Models.Count;

    /// <summary>The attack sequences a unit made in melee (under a melee frame), in log order.</summary>
    public IEnumerable<CoreFrame> MeleeAttacks => Frames.Where(f => f.Sequence == "attack-sequence" && Frame(f.Parent)?.Sequence == "melee");

    /// <summary>The morale tests, each with its logged roll (null when none was rolled) and the step that settled it.</summary>
    public IEnumerable<(CoreFrame Frame, CoreStep Test, RollRecord? Roll)> MoraleTests =>
        Frames.Where(f => f.Sequence == "morale-test").SelectMany(f => f.Steps.Where(s => s.Step.Step == "test").Select(s => (f, s, s.Step.Rolls.FirstOrDefault(r => r.Roll == "morale"))));

    /// <summary>Models of the frame's target sent to the shelf by this deal-wounds frame's own steps.</summary>
    public int CasualtiesIn(CoreFrame deal) => deal.Steps.SelectMany(s => s.Step.Changes.OfType<ModelsMoved>()).SelectMany(m => m.Moves).Count(m => m.To is OnShelf);

    /// <summary>Casualties under a frame: its own steps and every frame started beneath it.</summary>
    public int CasualtiesUnder(CoreFrame frame) => CasualtiesIn(frame) + Descendants(frame).Sum(CasualtiesIn);
}
