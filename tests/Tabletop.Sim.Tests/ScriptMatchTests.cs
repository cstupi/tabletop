using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

/// <summary>ScriptBuilder.Match: an answer resolved against the live Ask by label or name, falling through to the policy on null.</summary>
public class ScriptMatchTests
{
    static readonly Guid A = Guid.CreateVersion7();
    static Ask Ask(string question, LegalSet legal) => new(Guid.CreateVersion7(), question, "p", [A], legal, true, false, null, null);
    static PolicyView View() => AnswerPolicyTests.EmptyViewForOtherTests();
    static readonly Options XY = new([new AskOption("x", "X", []), new AskOption("y", "Y", [])]);

    [Fact]
    public void A_match_answers_and_a_null_falls_through_to_the_next_match_then_the_policy()
    {
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (_, _) => null)
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Labeled(ask, "Y"))
            .Build();
        var source = new ComposedAnswers(script, new AnswerPolicy(new Chooser(1)));
        Assert.Equal(new Chosen("y"), source.Answer(Ask(AskQuestions.Activate, XY), View()));
        Assert.IsType<Chosen>(source.Answer(Ask(AskQuestions.Activate, new Options([new AskOption("z", "Z", [])])), View()));   // no Y: the policy
        Assert.Equal([true, false], source.History.Select(h => h.FromScript));
        Assert.Equal([0, 1], source.History.Select(h => h.Ordinal));
    }

    [Fact]
    public void A_keyed_answer_wins_over_a_match()
    {
        var script = AnswerScript.Build().Then(AskQuestions.Activate, new Chosen("x")).Match(AskQuestions.Activate, (ask, _) => AskPicks.Labeled(ask, "Y")).Build();
        var source = new ComposedAnswers(script, policy: null);
        Assert.Equal(new Chosen("x"), source.Answer(Ask(AskQuestions.Activate, XY), View()));
        Assert.Equal(new Chosen("y"), source.Answer(Ask(AskQuestions.Activate, XY), View()));
    }

    [Fact]
    public void A_script_only_run_fails_when_no_match_answers()
    {
        var source = new ComposedAnswers(AnswerScript.Build().Match(AskQuestions.Activate, (_, _) => null).Build(), policy: null);
        Assert.Throws<UnscriptedAskException>(() => source.Answer(Ask(AskQuestions.Activate, XY), View()));
    }

    const string Book = "z65fgu0l29i4lnlu";
    const string Spell = "Calculated Foresight";

    /// <summary>
    /// Red unit 1's first model is a Caster(2); the unit and is activated first with Hold; it reacts with its cast, picks Calculated Foresight and targets Blue unit 2
    /// whenever those are offered. Everything else is the policy's. Seed 6 passes a casting roll, so a spell_targets Ask opens.
    /// </summary>
    static Scenario Casting(ulong seed) => new ScenarioBuilder("named-cast", seed).Table(36, 36).RoundCap(2)
        .Army("Red", 2, 2, models: [new ScenarioModel(1, 1, [new ScenarioRule(0, "Caster", 2)])], armyBook: Book)
        .Army("Blue", 2, 2)
        .Scripted(AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Hold"))
            .Match(AskQuestions.React, (ask, _) => AskPicks.Labeled(ask, "Cast a spell"))
            .Match("cast", (ask, p) => AskPicks.Spell(ask, p, Spell, casterUnit: "Red unit 1"))
            .Match("spell_targets", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 2"))
            .Build())
        .Build();

    [Fact]
    public async Task A_run_picks_the_named_unit_spell_and_target_and_stays_deterministic()
    {
        var first = await PlayAsync(Casting(6));
        var second = await PlayAsync(Casting(6));
        Assert.Equal(Shape(first.Loop), Shape(second.Loop));

        var (c, loop) = first;
        var asks = loop.Log.Select(e => e.Payload).OfType<AskOpened>().ToDictionary(o => o.Ask.Id, o => o.Ask);
        var red1 = c.Board.Armies[0].Snapshot.Units[0];
        var blue2 = c.Board.Armies[1].Snapshot.Units[1];

        // Activate: Red's first activation is Red unit 1's Hold, by its label's option.
        var activate = loop.Answers.History.First(h => h.Question == AskQuestions.Activate && asks[h.AskId].Teams[0] == c.TeamIds[0]);
        Assert.True(activate.FromScript);
        Assert.Equal(new Chosen(((Options)asks[activate.AskId].Legal).Choices.Single(o => o.Label == "Red unit 1: Hold").Id), activate.Answer);

        // Cast: every cast Ask offering the spell is answered with it.
        var casts = loop.Answers.History.Where(h => h.Question == "cast" && ((Options)asks[h.AskId].Legal).Choices.Any(o => o.Label.StartsWith($"{Spell} ("))).ToList();
        Assert.NotEmpty(casts);
        Assert.All(casts, h =>
        {
            var option = ((Options)asks[h.AskId].Legal).Choices.Single(o => o.Id == ((Chosen)h.Answer).OptionId);
            Assert.StartsWith($"{Spell} (", option.Label);
            Assert.Equal(red1.Models[0].Id, option.Pieces.Single());
        });

        // Targets: Blue unit 2, whenever eligible; there is at least one such Ask.
        var targets = loop.Answers.History.Where(h => h.Question == "spell_targets" && ((PieceSelection)asks[h.AskId].Legal).Eligible.Contains(blue2.Id)).ToList();
        Assert.NotEmpty(targets);
        Assert.All(targets, h => Assert.Equal([blue2.Id], Assert.IsType<SelectedPieces>(h.Answer).PieceIds));
    }

    static IEnumerable<string> Shape(RunLoop loop) => loop.Answers.History.Select(h => $"{h.Question}#{h.Ordinal}:{h.FromScript}:{h.Answer.GetType().Name}");

    static async Task<(CompiledScenario C, RunLoop Loop)> PlayAsync(Scenario s)
    {
        var c = ScenarioCompiler.Compile(s);
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var loop = new RunLoop(driver, c, new ComposedAnswers(s.Script, s.Policy()), HandPlacement.ChooserFor(s.Seed));
        var report = await loop.RunAsync(CancellationToken.None);
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        return (c, loop);
    }
}
