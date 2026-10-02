using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class RunLoopTests
{
    static async Task<RunReport> Run(string name, ulong seed = 1, Action<ScenarioBuilder>? tweak = null)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed ^ 0xF00D));
        return await loop.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_skirmish_reaches_an_end_and_never_hangs()
    {
        var report = await Run("skirmish");
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.Rounds >= 1, $"only reached round {report.Rounds}");
        Assert.True(report.Frames > 20, $"only {report.Frames} frames");
        Assert.True(report.AsksAnswered > 10);
    }

    [Fact]
    public async Task Every_model_is_deployed_off_the_shelf_before_round_one()
    {
        var scenario = ScenarioCatalog.Get("skirmish");
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(1))), new Chooser(2));
        await loop.RunAsync(CancellationToken.None);
        var placed = loop.Projector.Frames
            .First(f => f.Round >= 1 && f.Kind == EventKinds.Of(new RoundStarted(1, [], [])))
            .Models.Count(m => m.Where == "table");
        Assert.Equal(18, placed);
    }

    // Reproducibility is same-dice and same-answers, not same-ids: the seed is stored on the game and
    // the ids are ordinary v7 GUIDs, so nothing here may compare an id across two runs (Global
    // Constraints → Reproducibility). RunOutcome.Decided carries a team *id*, so the comparison is on
    // Kind plus the winner's ordinal.
    static async Task<(RunReport Report, int? WinnerOrdinal, IReadOnlyList<int> Dice)> RunTwice(string name, ulong seed)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed ^ 0xF00D));
        var report = await loop.RunAsync(CancellationToken.None);
        var winner = report.Outcome is RunOutcome.Decided d
            ? compiled.Info.Teams.Single(t => t.Id == d.WinningTeamId).Ordinal
            : (int?)null;
        var dice = driver.Log
            .Select(e => e.Payload).OfType<StepCompleted>()
            .SelectMany(step => step.Rolls)
            .SelectMany(r => r.Dice.Select(x => x.Raw))
            .ToList();
        return (report, winner, dice);
    }

    [Fact]
    public async Task The_same_seed_produces_the_same_dice_the_same_answers_and_a_different_game_id()
    {
        var one = await RunTwice("skirmish", 12);
        var two = await RunTwice("skirmish", 12);
        Assert.Equal(one.Report.Outcome.Kind, two.Report.Outcome.Kind);
        Assert.Equal(one.WinnerOrdinal, two.WinnerOrdinal);
        Assert.Equal(one.Report.Rounds, two.Report.Rounds);
        Assert.Equal(one.Report.LastSeq, two.Report.LastSeq);
        Assert.Equal(one.Report.Frames, two.Report.Frames);
        Assert.Equal(one.Report.AsksAnswered, two.Report.AsksAnswered);
        Assert.NotEmpty(one.Dice);
        Assert.Equal(one.Dice, two.Dice);                                // the stored seed, doing its job
    }

    [Fact]
    public void The_scenario_seed_is_what_the_game_carries()
    {
        var compiled = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(12));
        Assert.Equal(12L, compiled.Info.RngSeed);
    }

    [Fact]
    public async Task Different_seeds_produce_different_runs()
    {
        var seeds = new[] { 1ul, 2ul, 3ul, 4ul, 5ul };
        var lengths = new List<long>();
        foreach (var seed in seeds) lengths.Add((await Run("skirmish", seed)).LastSeq);
        Assert.True(lengths.Distinct().Count() > 1, "five seeds produced identical logs");
    }

    [Fact]
    public async Task A_round_cap_terminates_a_stalemate()
    {
        var report = await Run("stalemate");
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.Rounds <= 3, $"the cap of 3 was exceeded: {report.Rounds}");
        if (report.Outcome is RunOutcome.RoundLimit limit) Assert.Equal(3, limit.Rounds);
    }

    // A cap of n means n whole rounds, so the loop stops on round n's boundary Ask and round n + 1
    // never starts. The two readings differ by exactly this frame, which is why it is asserted twice:
    // on the reported round and on the frames.
    [Fact]
    public async Task A_cap_of_two_stops_at_the_end_of_the_second_round()
    {
        var scenario = ScenarioCatalog.Get("stalemate").WithSeed(7) with { RoundCap = 2 };
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(7))), new Chooser(7));
        var report = await loop.RunAsync(CancellationToken.None);

        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.DoesNotContain(loop.Projector.Frames, f => f.Round > 2);
        if (report.Outcome is RunOutcome.RoundLimit limit)
        {
            Assert.Equal(2, limit.Rounds);
            Assert.Equal(2, report.Rounds);
        }
    }

    [Fact]
    public async Task A_script_only_run_fails_on_the_first_unscripted_ask()
    {
        var scenario = new ScenarioBuilder("scripted", 1).Army("A", 1, 1).Army("B", 1, 1)
            .Scripted(AnswerScript.Empty, only: true).Build();
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, null), new Chooser(1));
        var report = await loop.RunAsync(CancellationToken.None);
        Assert.False(report.Passed);
        Assert.Equal("failed", report.Outcome.Kind);
        Assert.Contains("script-only", Assert.IsType<RunOutcome.Failed>(report.Outcome).Detail);
    }

    // The meta block is the viewer's input, so its enums carry the Protocol's spelling and not C#'s.
    [Fact]
    public async Task The_meta_block_spells_its_enums_the_way_the_wire_does()
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(3) with { RoundCap = 1 };
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(3))), new Chooser(3));
        var report = await loop.RunAsync(CancellationToken.None);

        var meta = loop.BuildRecording(report).Meta;
        Assert.Equal("alternating-activation", meta.SchedulerShape);
        Assert.Equal("strict", meta.Enforcement);
        Assert.Equal(1, meta.RoundCap);
        Assert.Contains(meta.Outcome, (string[])["decided", "round_limit"]);
    }

    [Fact]
    public void An_ask_fingerprint_ignores_the_id_and_notices_the_legal_set()
    {
        var team = Guid.CreateVersion7();
        Ask ask(Guid id, int options) => new(id, AskQuestions.Activate, "p", [team],
            new Options(Enumerable.Range(0, options).Select(i => new AskOption($"o{i}", "", [])).ToList()), true, false, null, null);
        Assert.Equal(AskFingerprint.Of(ask(Guid.CreateVersion7(), 2)), AskFingerprint.Of(ask(Guid.CreateVersion7(), 2)));
        Assert.NotEqual(AskFingerprint.Of(ask(Guid.CreateVersion7(), 2)), AskFingerprint.Of(ask(Guid.CreateVersion7(), 3)));
    }

    [Fact]
    public void Hand_placement_puts_every_model_of_the_unit_inside_the_teams_zone()
    {
        var compiled = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        var projection = Projection.Initial(compiled.Board, compiled.Info);
        var team = compiled.TeamIds[0];
        var unit = compiled.Board.Armies[0].Snapshot.Units[0];
        var moves = HandPlacement.IntoZone(projection, compiled, team, unit.Id, new Chooser(5));
        Assert.Equal(unit.Models.Count, moves.Count);
        var shape = compiled.Board.Table.Zones.Single(z => z.TeamOrdinal == 1).Shape;
        foreach (var move in moves)
        {
            var to = Assert.IsType<OnTable>(move.To);
            Assert.IsType<OnShelf>(move.From);
            Assert.True(ZoneStyles.Contains(shape, compiled.Board.Table.Size, to.Placement.Position.Horizontal),
                $"{to.Placement.Position} is outside {shape}");
        }
    }
}
