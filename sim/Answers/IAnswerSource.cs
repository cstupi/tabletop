using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

public sealed record AnsweredAsk(string Question, int Ordinal, Guid AskId, AskAnswer Answer, bool FromScript);

/// <summary>
/// Where an answer comes from. The run loop knows only this. It is also the seam a state-reading
/// policy lives behind when someone builds one: the view it is handed carries the whole state, and a
/// named implementation makes the choice to read it opt-in rather than a default. Whatever implements
/// this must still answer from the legal set whole, and must not compute a rules outcome itself.
/// </summary>
public interface IAnswerSource
{
    AskAnswer Answer(Ask ask, PolicyView view);
    IReadOnlyList<AnsweredAsk> History { get; }

    /// <summary>An answer given from outside this source (a debugger): counted and ordinal-advanced as Answer would, without consulting anything.</summary>
    void Record(Ask ask, AskAnswer answer);
}

public sealed class UnscriptedAskException(Ask ask, int ordinal)
    : Exception($"ask '{ask.Question}' ordinal {ordinal} is unscripted and this run is script-only");

/// <summary>
/// Script first, then the policy. A null policy is a script-only run, and the first uncovered Ask fails it
/// (spec → Two ways to answer an Ask).
/// </summary>
public sealed class ComposedAnswers(AnswerScript script, AnswerPolicy? policy) : IAnswerSource
{
    private readonly Dictionary<string, int> _seen = [];
    private readonly List<AnsweredAsk> _history = [];
    public IReadOnlyList<AnsweredAsk> History => _history;

    public AskAnswer Answer(Ask ask, PolicyView view)
    {
        var ordinal = _seen.GetValueOrDefault(ask.Question);
        _seen[ask.Question] = ordinal + 1;
        if (script.TryGet(ask.Question, ordinal, out var scripted))
        {
            _history.Add(new AnsweredAsk(ask.Question, ordinal, ask.Id, scripted, FromScript: true));
            return scripted;
        }
        if (policy is null) throw new UnscriptedAskException(ask, ordinal);
        var answer = policy.Answer(ask, view);
        _history.Add(new AnsweredAsk(ask.Question, ordinal, ask.Id, answer, FromScript: false));
        return answer;
    }

    public void Record(Ask ask, AskAnswer answer)
    {
        var ordinal = _seen.GetValueOrDefault(ask.Question);
        _seen[ask.Question] = ordinal + 1;
        _history.Add(new AnsweredAsk(ask.Question, ordinal, ask.Id, answer, FromScript: false));
    }
}
