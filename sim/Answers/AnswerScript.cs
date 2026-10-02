using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>The nth Ask of that question in the run, counted from zero. Server-minted Ask ids are not keys.</summary>
public sealed record ScriptKey(string Question, int Ordinal);

/// <summary>
/// The regression form: named answers in order (spec → Two ways to answer an Ask). A scenario scripts the
/// opening; whatever it does not cover goes to the policy, or fails the run when there is no policy.
/// </summary>
public sealed record AnswerScript(IReadOnlyDictionary<ScriptKey, AskAnswer> Answers)
{
    public static AnswerScript Empty { get; } = new(new Dictionary<ScriptKey, AskAnswer>());
    public static ScriptBuilder Build() => new();
    public int Count => Answers.Count;

    public bool TryGet(string question, int ordinal, out AskAnswer answer) =>
        Answers.TryGetValue(new ScriptKey(question, ordinal), out answer!);
}

public sealed class ScriptBuilder
{
    private readonly Dictionary<ScriptKey, AskAnswer> _answers = [];
    private readonly Dictionary<string, int> _next = [];

    public ScriptBuilder At(string question, int ordinal, AskAnswer answer)
    {
        var key = new ScriptKey(question, ordinal);
        if (!_answers.TryAdd(key, answer)) throw new ArgumentException($"'{question}' ordinal {ordinal} is already scripted", nameof(ordinal));
        _next[question] = Math.Max(_next.GetValueOrDefault(question), ordinal + 1);
        return this;
    }

    /// <summary>The next unscripted ordinal for that question.</summary>
    public ScriptBuilder Then(string question, AskAnswer answer) => At(question, _next.GetValueOrDefault(question), answer);

    public AnswerScript Build() => new(_answers);
}
