using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>A sequence frame of the effective log: where it started, the round, the frame open when it started, and its steps.</summary>
public sealed class LoggedFrame(SequenceStarted start, long seq, int round, LoggedFrame? parent)
{
    internal readonly List<LoggedStep> StepList = [];

    public SequenceStarted Start { get; } = start;
    public Guid Id => Start.FrameId;
    public string Sequence => Start.Sequence;
    public SequenceContext Context => Start.Context;
    public long Seq { get; } = seq;
    public int Round { get; } = round;
    public LoggedFrame? Parent { get; } = parent;
    public IReadOnlyList<LoggedStep> Steps => StepList;

    /// <summary>The frames this one was started under, nearest first.</summary>
    public IEnumerable<LoggedFrame> Ancestors { get { for (var p = Parent; p is not null; p = p.Parent) yield return p; } }

    /// <summary>This frame or its nearest ancestor running one of those sequences.</summary>
    public LoggedFrame? Nearest(params string[] sequences) => Ancestors.Prepend(this).FirstOrDefault(f => sequences.Contains(f.Sequence));
}

/// <summary>One step_completed, its frame, and the Asks the frame opened since its previous step (what this step answered).</summary>
public sealed record LoggedStep(long Seq, StepCompleted Step, LoggedFrame Frame, IReadOnlyList<Ask> Asks)
{
    public string Name => Step.Step;
    /// <summary>Whether the step's Applied names the entry.</summary>
    public bool Lists(string entry) => Step.Applied?.Contains(entry) == true;
    public int Value(string key) => Step.Values?.GetValueOrDefault(key) ?? 0;
    internal readonly List<LoggedRoll> RollList = [];
    /// <summary>The step's rolls with their dice as they stand (edits and rerolls applied). Step.Rolls holds the dice as first rolled.</summary>
    public IReadOnlyList<LoggedRoll> AllRolls => RollList;
    /// <summary>The step's rolls of that kind, dice as they stand.</summary>
    public IEnumerable<LoggedRoll> Rolls(string kind) => RollList.Where(r => r.Kind == kind);
}

/// <summary>A roll as it stands: its dice after every edit and reroll, whether a dice fix edited it, and its rerolls.
/// Roll is the record as logged, dice as first rolled; read Dice.</summary>
public sealed record LoggedRoll(RollRecord Roll, IReadOnlyList<Die> Dice, LoggedStep Step, bool Edited, IReadOnlyList<DiceRerolled> Rerolls)
{
    public string Kind => Roll.Roll;
    public LoggedFrame Frame => Step.Frame;
    public int Successes => Dice.Count(d => d.Success);
    /// <summary>Dice whose unmodified face is one of those.</summary>
    public int Raw(params int[] faces) => Dice.Count(d => faces.Contains(d.Raw));
}

/// <summary>An Ask, the frame it was asked in (null for the scheduler's), and the answers it closed with (empty when it never closed).</summary>
public sealed record LoggedAsk(long Seq, Ask Ask, LoggedFrame? Frame, IReadOnlyList<TeamAnswer> Answers)
{
    public AskAnswer? Answer => Answers.FirstOrDefault()?.Answer;
    /// <summary>The option the Ask was answered with, when it was an options Ask answered with Chosen.</summary>
    public AskOption? ChosenOption => Ask.Legal is Options o && Answer is Chosen c ? o.Choices.FirstOrDefault(x => x.Id == c.OptionId) : null;
}

/// <summary>A model leaving the table (or a container) for the shelf: the step that did it, null for a menu move.</summary>
public sealed record LoggedRemoval(long Seq, Guid Model, Guid Unit, Location From, LoggedStep? Step);

/// <summary>A unit picked at a deploy, deploy_scouts or deploy_ambusher Ask.</summary>
public sealed record LoggedDeployment(long Seq, string Question, Guid Unit, Guid Team);

/// <summary>An action recorded for a unit, with the team whose turn it was and the round.</summary>
public sealed record LoggedActivation(long Seq, Guid Unit, string Action, Guid? Team, int Round);

/// <summary>
/// The effective log read the way game checks need it: frames with their parents, steps, rolls with their dice as they stand, Asks with
/// their answers, removals, deployments, activations, conditions and locations at any seq. A reading of the log, never of the rules.
/// </summary>
public sealed class LogQueries
{
    readonly List<LoggedFrame> _frames = [];
    readonly List<LoggedStep> _steps = [];
    readonly List<LoggedRoll> _rolls = [];
    readonly List<LoggedAsk> _asks = [];
    readonly List<LoggedRemoval> _removals = [];
    readonly List<LoggedDeployment> _deployments = [];
    readonly List<LoggedActivation> _activations = [];
    readonly List<(long Seq, ModelRelocation Move)> _moves = [];
    readonly List<(long Seq, Guid ModelId, int Wounds)> _wounds = [];
    readonly List<(long Seq, Subject Subject, string Condition, bool Present)> _conditions = [];

    public IReadOnlyList<GameEvent> Events { get; }
    public Projection Final { get; }

    public static LogQueries Of(SuiteGameRun run) => new(run.EffectiveLog, run.Final);

    public LogQueries(IReadOnlyList<GameEvent> log, Projection final)
    {
        Final = final;
        Events = EventApplier.EffectiveLog(log);
        var byId = new Dictionary<Guid, LoggedFrame>();
        var open = new List<LoggedFrame>();
        var pending = new Dictionary<Guid, List<Ask>>();
        var asks = new Dictionary<Guid, (LoggedAsk Ask, int Index)>();
        var rolls = new Dictionary<Guid, (Die[] Dice, LoggedStep Step, List<DiceRerolled> Rerolls, bool Edited)>();
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
                    var frame = new LoggedFrame(s, e.Seq, round, open.Count > 0 ? open[^1] : null);
                    _frames.Add(frame); byId[s.FrameId] = frame; open.Add(frame);
                    break;
                case SequenceFinished f: open.RemoveAll(x => x.Id == f.FrameId); break;
                case SequenceAbandoned a:
                    if (open.FindIndex(x => x.Id == a.FrameId) is >= 0 and var i) open.RemoveRange(i, open.Count - i);
                    break;
                case AskOpened o:
                    if (o.Ask.FrameId is { } owner) (pending.TryGetValue(owner, out var list) ? list : pending[owner] = []).Add(o.Ask);
                    asks[o.Ask.Id] = (new LoggedAsk(e.Seq, o.Ask, o.Ask.FrameId is { } f2 ? byId.GetValueOrDefault(f2) : null, []), _asks.Count);
                    _asks.Add(asks[o.Ask.Id].Ask);
                    break;
                case AskClosed c when asks.TryGetValue(c.AskId, out var asked):
                    _asks[asked.Index] = asked.Ask with { Answers = c.Answers };
                    if (asked.Ask.Ask.Question is AskQuestions.Deploy or AskQuestions.DeployScouts or AskQuestions.DeployAmbusher or AskQuestions.DeployReinforcement)
                        foreach (var a in c.Answers)
                            if (a.Answer is SelectedPieces p)
                                _deployments.AddRange(p.PieceIds.Select(u => new LoggedDeployment(e.Seq, asked.Ask.Ask.Question, u, a.TeamId)));
                    break;
                case StepCompleted c when byId.TryGetValue(c.FrameId, out var stepFrame):
                    var step = new LoggedStep(e.Seq, c, stepFrame, pending.Remove(c.FrameId, out var answered) ? answered : []);
                    stepFrame.StepList.Add(step);
                    _steps.Add(step);
                    foreach (var roll in c.Rolls) rolls[roll.Id] = ([.. roll.Dice], step, [], false);
                    break;
                case DiceEdited d when rolls.TryGetValue(d.RollId, out var edited):
                    foreach (var r in d.Replaced) edited.Dice[r.Index] = r.New;
                    rolls[d.RollId] = edited with { Edited = true };
                    break;
                case DiceRerolled d when rolls.TryGetValue(d.RollId, out var rerolled):
                    foreach (var r in d.Replaced) rerolled.Dice[r.Index] = r.New;
                    rerolled.Rerolls.Add(d);
                    break;
            }
            var by = e.Payload is StepCompleted sc && byId.TryGetValue(sc.FrameId, out _) ? _steps[^1] : null;
            foreach (var change in ChangesOf(e.Payload))
                switch (change)
                {
                    case ModelsMoved mm:
                        foreach (var m in mm.Moves)
                        {
                            _moves.Add((e.Seq, m));
                            if (m is { To: OnShelf, From: not OnShelf } && final.Models.TryGetValue(m.ModelId, out var model))
                                _removals.Add(new LoggedRemoval(e.Seq, m.ModelId, model.UnitId, m.From, by));
                        }
                        break;
                    case WoundsSet w: _wounds.Add((e.Seq, w.ModelId, w.Wounds)); break;
                    case ConditionSet cs: _conditions.Add((e.Seq, cs.Subject, cs.Condition, cs.Present)); break;
                    case ActionRecorded a: _activations.Add(new LoggedActivation(e.Seq, a.UnitId, a.Action, turn, round)); break;
                }
        }
        foreach (var (roll, entry) in Rolls())
        {
            var logged = new LoggedRoll(roll, entry.Dice, entry.Step, entry.Edited, entry.Rerolls);
            _rolls.Add(logged);
            entry.Step.RollList.Add(logged);
        }

        IEnumerable<(RollRecord, (Die[] Dice, LoggedStep Step, List<DiceRerolled> Rerolls, bool Edited))> Rolls() =>
            _steps.SelectMany(s => s.Step.Rolls).Select(r => (r, rolls[r.Id]));
    }

    /// <summary>The state changes an event carries: a menu change itself, or a step's, round's or turn's.</summary>
    public static IEnumerable<StateChange> ChangesOf(EventPayload payload) => payload switch
    {
        StateChange c => [c],
        StepCompleted s => s.Changes,
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        _ => [],
    };

    /// <summary>
    /// Null when every case passes and at least <paramref name="need"/> happened; else the first failure, or what never happened. A check over
    /// no case fails, so a seed that never brings the rule into play cannot pass for it.
    /// </summary>
    public static string? Each<T>(IEnumerable<T> cases, string what, Func<T, string?> failure, int need = 1)
    {
        var all = cases.ToList();
        if (all.Count < need) return need == 1 ? $"no {what} happened" : $"{all.Count} of the {need} needed happened: {what}";
        return all.Select(failure).FirstOrDefault(f => f is not null);
    }

    // Frames and steps.
    public IReadOnlyList<LoggedFrame> Frames => _frames;
    public IEnumerable<LoggedFrame> FramesOf(string sequence) => _frames.Where(f => f.Sequence == sequence);
    public LoggedFrame? Frame(Guid? id) => id is { } i ? _frames.FirstOrDefault(f => f.Id == i) : null;
    public IEnumerable<LoggedFrame> Children(LoggedFrame parent) => _frames.Where(f => f.Parent == parent);
    public IEnumerable<LoggedFrame> Descendants(LoggedFrame parent) => Children(parent).SelectMany(c => Descendants(c).Prepend(c));

    public IReadOnlyList<LoggedStep> Steps => _steps;
    /// <summary>Every step of every frame running that sequence, optionally only the steps of that name, in log order.</summary>
    public IEnumerable<LoggedStep> StepsOf(string sequence, string? step = null) =>
        _steps.Where(s => s.Frame.Sequence == sequence && (step is null || s.Name == step));
    /// <summary>Every step whose Applied names the entry.</summary>
    public IEnumerable<LoggedStep> Listing(string entry) => _steps.Where(s => s.Lists(entry));

    // Rolls.
    public IReadOnlyList<LoggedRoll> AllRolls => _rolls;
    /// <summary>Every roll of that kind in log order, dice as they stand.</summary>
    public IReadOnlyList<LoggedRoll> Rolls(string kind) => _rolls.Where(r => r.Roll.Roll == kind).ToList();

    // Asks.
    public IReadOnlyList<LoggedAsk> Asks => _asks;
    public IEnumerable<LoggedAsk> AsksOf(string question) => _asks.Where(a => a.Ask.Question == question);

    // Models and units.
    /// <summary>Every model sent to the shelf from the table or a container, in log order.</summary>
    public IReadOnlyList<LoggedRemoval> ModelsRemoved => _removals;
    public IEnumerable<LoggedRemoval> RemovedFrom(Guid unit) => _removals.Where(r => r.Unit == unit);
    /// <summary>Removals made by the frame's own steps and every frame started under it.</summary>
    public IEnumerable<LoggedRemoval> RemovedUnder(LoggedFrame frame)
    {
        var frames = Descendants(frame).Prepend(frame).ToHashSet();
        return _removals.Where(r => r.Step is { } s && frames.Contains(s.Frame));
    }

    public IReadOnlyList<LoggedDeployment> Deployments => _deployments;
    public IReadOnlyList<LoggedActivation> Activations => _activations;
    public IReadOnlyList<(long Seq, ModelRelocation Move)> Moves => _moves;

    /// <summary>Where every model stood before the event at that seq.</summary>
    public IReadOnlyDictionary<Guid, Location> LocationsBefore(long seq)
    {
        var at = Final.Models.Keys.ToDictionary(m => m, _ => (Location)new OnShelf());
        foreach (var (s, move) in _moves) { if (s >= seq) break; at[move.ModelId] = move.To; }
        return at;
    }

    /// <summary>The unit's models on the table or inside a container before that seq.</summary>
    public int Living(Guid unit, long seq) => LocationsBefore(seq).Count(kv => UnitOf(kv.Key) == unit && kv.Value is not OnShelf);

    /// <summary>The wounds the unit's models took in [from, to): the rise of each model's WoundsSet.</summary>
    public int WoundsTaken(Guid unit, long from, long to)
    {
        var standing = new Dictionary<Guid, int>();
        var taken = 0;
        foreach (var (seq, model, wounds) in _wounds)
        {
            if (seq >= to) break;
            if (seq >= from && UnitOf(model) == unit) taken += Math.Max(0, wounds - standing.GetValueOrDefault(model));
            standing[model] = wounds;
        }
        return taken;
    }

    /// <summary>Whether the subject (a unit or model id) carried the condition before that seq.</summary>
    public bool HasCondition(Guid subject, string condition, long seq) =>
        _conditions.LastOrDefault(c => c.Seq < seq && c.Subject.Id == subject && c.Condition == condition) is { Present: true };
    public IReadOnlyList<(long Seq, Subject Subject, string Condition, bool Present)> Conditions => _conditions;

    public Guid UnitOf(Guid model) => Final.Models[model].UnitId;
    public string UnitName(Guid unit) => Final.Units.TryGetValue(unit, out var u) ? u.Definition.Name : unit.ToString();
    public string ModelName(Guid model) => Final.Models.TryGetValue(model, out var m) ? m.Slot.Name : model.ToString();
    public string WeaponName(Guid weapon) =>
        Final.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == weapon)?.Name ?? weapon.ToString();
    /// <summary>The unit with that name, by its definition.</summary>
    public Guid UnitNamed(string name) => Final.Units.Values.Single(u => u.Definition.Name == name).Id;

    /// <summary>A roll for a failure message: kind, threshold, modifiers and each die as it stands, raw/modified.</summary>
    public static string Show(LoggedRoll r) =>
        $"{r.Kind} {r.Roll.Threshold}+ [{string.Join(",", r.Roll.Modifiers.Select(m => $"{m.Source}{m.Amount:+0;-0}"))}] {string.Join(" ", r.Dice.Select(d => $"{d.Raw}/{d.Modified}"))}";
}
