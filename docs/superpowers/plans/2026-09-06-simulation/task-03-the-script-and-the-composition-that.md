# Task 3: The script, and the composition that makes a mixed run

**Files:**
- Create: `sim/Answers/AnswerScript.cs`, `sim/Answers/IAnswerSource.cs`
- Test: `tests/Tabletop.Sim.Tests/AnswerScriptTests.cs`

**Interfaces:**
- Consumes: `Ask`, `AskAnswer` and its members, `AskQuestions` (Protocol); `AnswerPolicy`, `PolicyView`, `PolicyOptions`, `NoLegalAnswerException` (Task 2); `Chooser` (Task 1).
- Produces: `sealed record ScriptKey(string Question, int Ordinal)`; `sealed record AnswerScript(IReadOnlyDictionary<ScriptKey, AskAnswer> Answers)` with `static AnswerScript Empty`, `static ScriptBuilder Build()`, `bool TryGet(string question, int ordinal, out AskAnswer answer)`, `int Count`; `sealed class ScriptBuilder` with `ScriptBuilder At(string question, int ordinal, AskAnswer answer)`, `ScriptBuilder Then(string question, AskAnswer answer)` (the next unused ordinal for that question, starting at 0), `AnswerScript Build()`; `sealed class UnscriptedAskException(Ask ask, int ordinal) : Exception`; `interface IAnswerSource { AskAnswer Answer(Ask ask, PolicyView view); IReadOnlyList<AnsweredAsk> History { get; } void Record(Ask ask, AskAnswer answer); }`; `sealed record AnsweredAsk(string Question, int Ordinal, Guid AskId, AskAnswer Answer, bool FromScript)`; `sealed class ComposedAnswers(AnswerScript script, AnswerPolicy? policy) : IAnswerSource` — script first, then the policy; a null policy is a script-only run and throws `UnscriptedAskException` on the first Ask the script does not cover.

Spec: spec → *Two ways to answer an Ask* — "**Both, and mixed in one run.** A scenario scripts the opening and hands the rest to the policy… An unscripted Ask under a script-only run is a failure; under a mixed run it goes to the policy." Module prompt → "scripted answers keyed by Ask question and ordinal … Scripted answers take precedence, the policy handles the rest, and a script-only run fails on an unscripted Ask."

**The ordinal is per question, counted over the run.** The *n*th `activate` Ask is `("activate", n)` regardless of how many `confirm` Asks fell between them, because that is the number a person writing a script can count. `Ask.Id` is server-minted and unusable as a key; the frame id is not stable across a rewrite of the sequence.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class AnswerScriptTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7();
    static Ask Ask(string question, LegalSet? legal = null, bool allowPass = false) =>
        new(Guid.CreateVersion7(), question, "p", [A], legal ?? new Options([new AskOption("x", "X", []), new AskOption("y", "Y", [])]), allowPass, false, null, null);
    static PolicyView View() => AnswerPolicyTests.EmptyViewForOtherTests();

    [Fact]
    public void The_builder_keys_by_question_and_ordinal_counting_from_zero()
    {
        var script = AnswerScript.Build()
            .Then(AskQuestions.Activate, new Chosen("x"))
            .Then(AskQuestions.Activate, new Passed())
            .At(AskQuestions.Confirm, 0, new Confirmed())
            .Build();
        Assert.Equal(3, script.Count);
        Assert.True(script.TryGet(AskQuestions.Activate, 0, out var first));
        Assert.Equal(new Chosen("x"), first);
        Assert.True(script.TryGet(AskQuestions.Activate, 1, out var second));
        Assert.Equal(new Passed(), second);
        Assert.False(script.TryGet(AskQuestions.Activate, 2, out _));
    }

    [Fact]
    public void A_duplicate_key_is_a_programming_error()
    {
        var builder = AnswerScript.Build().At(AskQuestions.Activate, 0, new Chosen("x"));
        Assert.Throws<ArgumentException>(() => builder.At(AskQuestions.Activate, 0, new Passed()));
    }

    [Fact]
    public void The_script_wins_and_the_policy_takes_the_rest()
    {
        var script = AnswerScript.Build().Then(AskQuestions.Activate, new Chosen("y")).Build();
        var source = new ComposedAnswers(script, new AnswerPolicy(new Chooser(1)));
        Assert.Equal(new Chosen("y"), source.Answer(Ask(AskQuestions.Activate), View()));
        Assert.IsType<Chosen>(source.Answer(Ask(AskQuestions.Activate), View()));      // second one: the policy
        Assert.IsType<Confirmed>(source.Answer(Ask(AskQuestions.Confirm, new Confirmation()), View()));
        Assert.Equal([true, false, false], source.History.Select(h => h.FromScript));
        Assert.Equal([0, 1, 0], source.History.Select(h => h.Ordinal));
    }

    [Fact]
    public void A_script_only_run_fails_on_the_first_unscripted_ask()
    {
        var script = AnswerScript.Build().Then(AskQuestions.Activate, new Chosen("y")).Build();
        var source = new ComposedAnswers(script, policy: null);
        Assert.Equal(new Chosen("y"), source.Answer(Ask(AskQuestions.Activate), View()));
        var thrown = Assert.Throws<UnscriptedAskException>(() => source.Answer(Ask(AskQuestions.Activate), View()));
        Assert.Contains("activate", thrown.Message);
        Assert.Contains("ordinal 1", thrown.Message);
    }

    [Fact]
    public void The_ordinal_counts_per_question_not_over_all_asks()
    {
        var source = new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(1)));
        source.Answer(Ask(AskQuestions.Activate), View());
        source.Answer(Ask(AskQuestions.Confirm, new Confirmation()), View());
        source.Answer(Ask(AskQuestions.Activate), View());
        Assert.Equal([("activate", 0), ("confirm", 0), ("activate", 1)], source.History.Select(h => (h.Question, h.Ordinal)));
    }
}
```

`AnswerPolicyTests.EmptyViewForOtherTests()` is Task 2's `internal static` bare view, so both test classes build it without a second copy.

- [ ] **Step 2: Run; expect failure, then write `AnswerScript.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter AnswerScriptTests`
Expected: `CS0117: 'AnswerScript' does not contain a definition for 'Build'`.

```csharp
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
```

- [ ] **Step 3: Write `IAnswerSource.cs`**

```csharp
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
```

The first Ask of every game is the roll-off `confirm` gate, addressed to both teams, so `("confirm", 0)` is that gate and not a deployment answer. An unsealed Ask addressed to several teams (the roll-off gate, `end_round`) is answered once, by the first-listed team that has not answered, so `ComposedAnswers.Answer` is called once for it and the ordinal advances once. A sealed Ask is answered by every addressed team, so `Answer` is called once per team and the ordinal advances each time. That is what a script writer counts: one line per answer given, not one per Ask opened. Task 6's run loop is what calls it for each team that still owes an answer.

- [ ] **Step 4: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter AnswerScriptTests
git add sim/Answers tests/Tabletop.Sim.Tests
git commit -m "Script answers by question and ordinal, and fall through to the policy"
```
