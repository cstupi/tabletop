using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>The nth Ask of that question in the run, counted from zero. Server-minted Ask ids are not keys.</summary>
public sealed record ScriptKey(string Question, int Ordinal);

/// <summary>
/// An answer resolved against the live Ask, for what a keyed answer cannot name: option and piece ids are minted at compile time, so a script
/// picks by option label, unit name or spell name instead (AskPicks). Null falls through to the next match, then the policy. A pick reads labels
/// and names only; it never computes a rules outcome, and it is a pure function of the Ask and the projection, so a run stays deterministic.
/// </summary>
public sealed record ScriptMatch(string Question, Func<Ask, Projection, AskAnswer?> Pick);

/// <summary>
/// The regression form: named answers in order (spec → Two ways to answer an Ask). A scenario scripts the
/// opening; whatever it does not cover goes to the policy, or fails the run when there is no policy.
/// </summary>
public sealed record AnswerScript(IReadOnlyDictionary<ScriptKey, AskAnswer> Answers)
{
    public static AnswerScript Empty { get; } = new(new Dictionary<ScriptKey, AskAnswer>());
    public static ScriptBuilder Build() => new();
    public int Count => Answers.Count;

    /// <summary>Consulted, in order, for an Ask no keyed answer covers.</summary>
    public IReadOnlyList<ScriptMatch> Matches { get; init; } = [];

    /// <summary>The first match for that question whose pick answers, or null.</summary>
    public AskAnswer? Match(Ask ask, Projection projection) =>
        Matches.Where(m => m.Question == ask.Question).Select(m => m.Pick(ask, projection)).FirstOrDefault(a => a is not null);

    public bool TryGet(string question, int ordinal, out AskAnswer answer) =>
        Answers.TryGetValue(new ScriptKey(question, ordinal), out answer!);
}

public sealed class ScriptBuilder
{
    private readonly Dictionary<ScriptKey, AskAnswer> _answers = [];
    private readonly Dictionary<string, int> _next = [];
    private readonly List<ScriptMatch> _matches = [];

    public ScriptBuilder At(string question, int ordinal, AskAnswer answer)
    {
        var key = new ScriptKey(question, ordinal);
        if (!_answers.TryAdd(key, answer)) throw new ArgumentException($"'{question}' ordinal {ordinal} is already scripted", nameof(ordinal));
        _next[question] = Math.Max(_next.GetValueOrDefault(question), ordinal + 1);
        return this;
    }

    /// <summary>The next unscripted ordinal for that question.</summary>
    public ScriptBuilder Then(string question, AskAnswer answer) => At(question, _next.GetValueOrDefault(question), answer);

    /// <summary>Answer every Ask of that question the pick answers (non-null); a keyed answer for the same ordinal wins.</summary>
    public ScriptBuilder Match(string question, Func<Ask, Projection, AskAnswer?> pick) { _matches.Add(new ScriptMatch(question, pick)); return this; }

    public AnswerScript Build() => new(_answers) { Matches = _matches.ToList() };
}
