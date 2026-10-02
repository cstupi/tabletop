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
