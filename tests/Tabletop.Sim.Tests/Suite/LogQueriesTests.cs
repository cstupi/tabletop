using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>LogQueries over a hand-built log on the stalemate board: only the log's shape matters.</summary>
public class LogQueriesTests
{
    static readonly CompiledScenario C = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
    static readonly Projection Final = new EventApplier(C.Board, C.Info).Projection;
    static readonly Unit Red = C.Board.Armies[0].Snapshot.Units[0], Blue = C.Board.Armies[1].Snapshot.Units[0];
    static readonly Guid Attack = Guid.CreateVersion7(), Deal = Guid.CreateVersion7(), HitRoll = Guid.CreateVersion7();
    static readonly Ask Pick = new(Guid.CreateVersion7(), "pick", "p", [C.TeamIds[0]], new Options([new AskOption("a", "Alpha", [])]), false, false, null, Attack);
    static readonly Ask Deploy = new(Guid.CreateVersion7(), AskQuestions.Deploy, "p", [C.TeamIds[1]], new PieceSelection([Blue.Id], 1, 1), false, false, null, null);

    static GameEvent E(long seq, EventPayload p) => new(seq, null, p, null);
    static SequenceContext Ctx => new(Red.Id, [Blue.Id], [], "Advance");

    static IReadOnlyList<GameEvent> Log(params GameEvent[] tail) =>
    [
        E(1, new AskOpened(Deploy)),
        E(2, new AskClosed(Deploy.Id, [new TeamAnswer(C.TeamIds[1], new SelectedPieces([Blue.Id]))])),
        E(3, new ModelsMoved([new ModelRelocation(Blue.Models[0].Id, new OnShelf(), new Inside(Red.Models[0].Id))])),
        E(4, new RoundStarted(1, C.TeamIds, [])),
        E(5, new TurnStarted(C.TeamIds[0])),
        E(6, new SequenceStarted(Attack, "attack-sequence", Ctx)),
        E(7, new AskOpened(Pick)),
        E(8, new AskClosed(Pick.Id, [new TeamAnswer(C.TeamIds[0], new Chosen("a"))])),
        E(9, new StepCompleted(Attack, "roll-to-hit", new Chosen("a"),
            [new RollRecord(HitRoll, "hit", 4, [], [new Die(2, 2, false), new Die(5, 5, true)])], [new ActionRecorded(Red.Id, "Advance")], ["Furious"])),
        E(10, new DiceEdited(Attack, HitRoll, [new DieReplacement(0, new Die(2, 2, false), new Die(6, 6, true))])),
        E(11, new SequenceStarted(Deal, "deal-wounds", Ctx)),
        E(12, new StepCompleted(Deal, "assign", null, [], [new WoundsSet(Blue.Models[0].Id, 1)])),
        E(13, new StepCompleted(Deal, "remove", null, [],
            [new ModelsMoved([new ModelRelocation(Blue.Models[0].Id, new Inside(Red.Models[0].Id), new OnShelf())]), new ConditionSet(new Subject(SubjectKind.Unit, Blue.Id), "shaken", true)])),
        E(14, new SequenceFinished(Deal)),
        E(15, new SequenceFinished(Attack)),
        .. tail,
    ];

    [Fact]
    public void Frames_nest_and_steps_carry_the_asks_they_answered()
    {
        var q = new LogQueries(Log(), Final);
        var attack = q.FramesOf("attack-sequence").Single();
        var deal = q.FramesOf("deal-wounds").Single();
        Assert.Same(attack, deal.Parent);
        Assert.Same(attack, deal.Nearest("attack-sequence"));
        Assert.Equal([deal], q.Descendants(attack));
        Assert.Equal(1, attack.Round);
        Assert.Equal(["roll-to-hit"], attack.Steps.Select(s => s.Name));
        Assert.Equal([Pick], attack.Steps[0].Asks);
        Assert.Equal(["assign", "remove"], q.StepsOf("deal-wounds").Select(s => s.Name));
        Assert.Equal(["remove"], q.StepsOf("deal-wounds", "remove").Select(s => s.Name));
        Assert.Equal([9L], q.Listing("Furious").Select(s => s.Seq));
    }

    [Fact]
    public void A_frame_is_under_itself_and_its_ancestors_and_steps_under_it_include_its_children()
    {
        var q = new LogQueries(Log(), Final);
        var (attack, deal) = (q.FramesOf("attack-sequence").Single(), q.FramesOf("deal-wounds").Single());
        Assert.True(attack.Under(attack));
        Assert.True(deal.Under(attack));
        Assert.False(attack.Under(deal));
        Assert.Equal(["roll-to-hit", "assign", "remove"], q.StepsUnder(attack).Select(s => s.Name));
        Assert.Equal(["assign", "remove"], q.StepsUnder(deal).Select(s => s.Name));
    }

    [Fact]
    public void Tokens_before_a_seq_are_the_last_set_on_the_model()
    {
        var model = Red.Models[0].Id;
        var q = new LogQueries(Log(E(16, new SpellTokensSet(model, 3)), E(17, new SpellTokensSet(Blue.Models[0].Id, 1)), E(18, new SpellTokensSet(model, 1))), Final);
        Assert.Equal(0, q.TokensBefore(model, 16));
        Assert.Equal(3, q.TokensBefore(model, 18));
        Assert.Equal(1, q.TokensBefore(model, 19));
    }

    [Fact]
    public void Rolls_of_a_kind_have_their_dice_as_they_stand()
    {
        var roll = new LogQueries(Log(), Final).Rolls("hit").Single();
        Assert.True(roll.Edited);
        Assert.Equal([6, 5], roll.Dice.Select(d => d.Raw));
        Assert.Equal(2, roll.Successes);
        Assert.Equal(1, roll.Raw(6));
        Assert.Equal("attack-sequence", roll.Frame.Sequence);
        Assert.Equal([2, 5], roll.Roll.Dice.Select(d => d.Raw));     // the record itself is untouched
    }

    [Fact]
    public void A_steps_rolls_show_the_edited_faces()
    {
        var step = new LogQueries(Log(), Final).StepsOf("attack-sequence", "roll-to-hit").Single();
        var roll = step.Rolls("hit").Single();
        Assert.Equal([6, 5], roll.Dice.Select(d => d.Raw));                   // the dice_edited a SetDice logs is applied
        Assert.Same(roll, step.AllRolls.Single());
        Assert.Equal("hit 4+ [] 6/6 5/5", LogQueries.Show(roll));
    }

    [Fact]
    public void Asks_by_question_carry_their_answers_and_deployments_are_read_off_them()
    {
        var q = new LogQueries(Log(), Final);
        var pick = q.AsksOf("pick").Single();
        Assert.Equal(new Chosen("a"), pick.Answer);
        Assert.Equal("Alpha", pick.ChosenOption!.Label);
        Assert.Equal("attack-sequence", pick.Frame!.Sequence);
        Assert.Null(q.AsksOf(AskQuestions.Deploy).Single().Frame);
        Assert.Equal([new LoggedDeployment(2, AskQuestions.Deploy, Blue.Id, C.TeamIds[1])], q.Deployments);
        Assert.Equal([new LoggedActivation(9, Red.Id, "Advance", C.TeamIds[0], 1)], q.Activations);
    }

    [Fact]
    public void Removals_wounds_conditions_and_locations()
    {
        var q = new LogQueries(Log(), Final);
        var removal = q.ModelsRemoved.Single();
        Assert.Equal((13L, Blue.Models[0].Id, Blue.Id), (removal.Seq, removal.Model, removal.Unit));
        Assert.Equal("remove", removal.Step!.Name);
        Assert.Equal([removal], q.RemovedFrom(Blue.Id));
        Assert.Equal([removal], q.RemovedUnder(q.FramesOf("attack-sequence").Single()));
        Assert.Equal(1, q.Living(Blue.Id, 13));
        Assert.Equal(0, q.Living(Blue.Id, 14));
        Assert.Equal(1, q.WoundsTaken(Blue.Id, 1, 100));
        Assert.Equal(0, q.WoundsTaken(Blue.Id, 13, 100));
        Assert.False(q.HasCondition(Blue.Id, "shaken", 13));
        Assert.True(q.HasCondition(Blue.Id, "shaken", 14));
        Assert.Equal(Blue.Name, q.UnitName(Blue.Id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public async Task A_steps_rolls_after_a_dice_fix_show_the_fixed_face(int face)
    {
        var check = new GameCheck("the step reads the fixed face", run =>
        {
            var roll = LogQueries.Of(run).Steps.SelectMany(s => s.Rolls("hit")).FirstOrDefault();
            return roll is null ? "no hit roll" : roll is { Edited: true, Dice: [{ Raw: var raw }] } && raw == face ? null : LogQueries.Show(roll);
        });
        var game = SuiteFixtures.Game(dice: [new DiceFix("hit", 0, [face])], checks: [check]);
        var report = await new SuiteRunner([game]).RunAsync(new SuiteOptions([game.Name], DriverKind.InProcess, false, SuiteFixtures.TempDir()), null, SuiteFixtures.Ct);
        Assert.True(report.Passed, string.Join("\n", report.Games.SelectMany(g => g.Failures)));
    }

    [Fact]
    public void An_undone_step_is_not_read()
    {
        var q = new LogQueries(Log(E(16, new Undone(12))), Final);
        Assert.Empty(q.ModelsRemoved);
        Assert.Equal(["assign"], q.StepsOf("deal-wounds").Select(s => s.Name));
    }

    [Fact]
    public void Each_fails_on_no_case_and_reports_the_first_failure()
    {
        Assert.Equal("no hit happened", LogQueries.Each(Array.Empty<int>(), "hit", _ => null));
        Assert.Equal("1 of the 2 needed happened: hit", LogQueries.Each([1], "hit", _ => null, need: 2));
        Assert.Equal("two", LogQueries.Each([1, 2, 3], "hit", i => i == 2 ? "two" : null));
        Assert.Null(LogQueries.Each([1], "hit", _ => null));
    }
}
