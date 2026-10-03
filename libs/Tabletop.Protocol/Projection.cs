using Tabletop.Content;

namespace Tabletop.Protocol;

/// <summary>Everything the client draws from, rebuilt from the board and the log. Only EventApplier writes it.</summary>
public sealed class Projection
{
    internal readonly Dictionary<Guid, UnitInPlay> UnitsMut = new();
    internal readonly Dictionary<Guid, ModelInPlay> ModelsMut = new();
    internal readonly Dictionary<Guid, Marker> MarkersMut = new();
    internal readonly Dictionary<Guid, int> ScoresMut = new();
    internal readonly List<Frame> StackMut = new();
    internal readonly Dictionary<Guid, OpenAsk> OpenAsksMut = new();

    private Projection(Board board) { Board = board; }

    public Board Board { get; }
    public IReadOnlyDictionary<Guid, UnitInPlay> Units => UnitsMut;
    public IReadOnlyDictionary<Guid, ModelInPlay> Models => ModelsMut;
    public IReadOnlyDictionary<Guid, Marker> Markers => MarkersMut;
    public IReadOnlyDictionary<Guid, int> Scores => ScoresMut;
    public SchedulerPosition Scheduler { get; } = new();
    public IReadOnlyList<Frame> Stack => StackMut;
    public IReadOnlyDictionary<Guid, OpenAsk> OpenAsks => OpenAsksMut;
    public long LastSeq { get; internal set; }
    /// <summary>The live game_ended, or null. A round_started after it clears it: a round started from the menu reopens play.</summary>
    public GameEnded? Ended { get; internal set; }

    public static Projection Initial(Board board, GameInfo game)
    {
        var p = new Projection(board);
        foreach (var team in game.Teams) p.ScoresMut[team.Id] = 0;
        foreach (var army in board.Armies)
            foreach (var unit in army.Snapshot.Units)
            {
                p.UnitsMut[unit.Id] = new UnitInPlay(unit.Id, army.ParticipantId, unit);
                foreach (var slot in unit.Models) p.ModelsMut[slot.Id] = new ModelInPlay(slot.Id, unit.Id, slot);
            }
        return p;
    }
}

public sealed class UnitInPlay(Guid id, Guid participantId, Unit definition)
{
    internal readonly HashSet<string> ActionsMut = new();
    internal readonly HashSet<string> ConditionsMut = new();
    internal readonly List<Grant> GrantsMut = new();
    public Guid Id { get; } = id;
    public Guid ParticipantId { get; } = participantId;
    public Unit Definition { get; } = definition;
    public IReadOnlySet<string> Actions => ActionsMut;
    public IReadOnlySet<string> Conditions => ConditionsMut;
    public IReadOnlyList<Grant> Grants => GrantsMut;
    public int WoundsThisActivation { get; internal set; }
}

public sealed class ModelInPlay(Guid id, Guid unitId, ModelSlot slot)
{
    internal readonly HashSet<string> ConditionsMut = new();
    public Guid Id { get; } = id;
    public Guid UnitId { get; } = unitId;
    public ModelSlot Slot { get; } = slot;
    public Location Location { get; internal set; } = new OnShelf();
    public int Wounds { get; internal set; }
    public IReadOnlySet<string> Conditions => ConditionsMut;
    public int SpellTokens { get; internal set; }
}

/// <summary>Round, order over teams, whose turn, who has passed. Phase is deployment, a GameRules phase name, or null.</summary>
public sealed class SchedulerPosition
{
    internal readonly List<Guid> OrderMut = new();
    internal readonly HashSet<Guid> PassedMut = new();
    public int Round { get; internal set; }
    public IReadOnlyList<Guid> Order => OrderMut;
    public Guid? ActiveTeamId { get; internal set; }
    public string? Phase { get; internal set; }
    public IReadOnlySet<Guid> Passed => PassedMut;
}

public sealed class Frame(Guid id, string sequence, SequenceContext context)
{
    internal readonly List<CompletedStep> StepsMut = new();
    internal readonly Dictionary<string, List<RollModifier>> PendingMut = new();
    public Guid Id { get; } = id;
    public string Sequence { get; } = sequence;
    public SequenceContext Context { get; } = context;
    public IReadOnlyList<CompletedStep> Steps => StepsMut;
    public IReadOnlyDictionary<string, IReadOnlyList<RollModifier>> PendingModifiers =>
        PendingMut.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RollModifier>)kv.Value);
}

public sealed class CompletedStep(string step, AskAnswer? answer, IReadOnlyList<RollRecord> rolls, IReadOnlyList<StateChange> changes, IReadOnlyDictionary<string, int>? values)
{
    internal readonly List<RollRecord> RollsMut = rolls.ToList();
    public string Step { get; } = step;
    public AskAnswer? Answer { get; } = answer;
    public IReadOnlyList<RollRecord> Rolls => RollsMut;
    public IReadOnlyList<StateChange> Changes { get; } = changes;
    /// <summary>The step's settled parameters, copied from StepCompleted.Values; null when the step recorded none.</summary>
    public IReadOnlyDictionary<string, int>? Values { get; } = values;
}

public sealed class OpenAsk(Ask ask)
{
    internal readonly Dictionary<Guid, AskAnswer?> AnswersMut = new();
    public Ask Ask { get; } = ask;
    public IReadOnlyDictionary<Guid, AskAnswer?> Answers => AnswersMut;
}
