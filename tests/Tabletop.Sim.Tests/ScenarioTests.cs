using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class ScenarioTests
{
    [Fact]
    public void The_builder_produces_two_teams_one_participant_each_and_a_snapshot_per_team()
    {
        var s = new ScenarioBuilder("two-by-two", seed: 5)
            .Table(72, 48)
            .Army("A", units: 2, modelsPerUnit: 3)
            .Army("B", units: 2, modelsPerUnit: 3)
            .Build();
        var c = ScenarioCompiler.Compile(s);

        Assert.Equal(GameStatus.Playing, c.Info.Status);
        Assert.Equal(2, c.TeamIds.Count);
        Assert.Equal(2, c.ParticipantIds.Count);
        Assert.Equal(3, c.Info.Participants.Count);                                     // two players and the recorder
        Assert.Null(c.Info.Participants.Single(p => p.Id == c.RecorderParticipantId).TeamId);
        Assert.Equal([1, 2], c.Info.Teams.Select(t => t.Ordinal));
        Assert.Equal(2, c.Board.Armies.Count);
        Assert.All(c.Board.Armies, a => Assert.Equal(2, a.Snapshot.Units.Count));
        Assert.Equal(c.ParticipantFor(c.TeamIds[0]), c.Info.Participants[0].Id);
        Assert.Equal(new Tabletop.Geometry.TableSize(72, 48), c.Board.Table.Size);
    }

    [Fact]
    public void Compiling_twice_repeats_the_seed_and_not_the_ids()
    {
        var s = ScenarioCatalog.Get("skirmish");
        var a = ScenarioCompiler.Compile(s);
        var b = ScenarioCompiler.Compile(s);
        Assert.Equal(unchecked((long)s.Seed), a.Info.RngSeed);           // the seed is the scenario's, stored
        Assert.Equal(a.Info.RngSeed, b.Info.RngSeed);
        Assert.NotEqual(a.Info.Id, b.Info.Id);                           // ids are ordinary v7 GUIDs
        Assert.NotEqual(a.Board.Armies[0].Snapshot.Units[0].Models[0].Id,
                        b.Board.Armies[0].Snapshot.Units[0].Models[0].Id);
        Assert.NotEqual(a.Info.RngSeed, ScenarioCompiler.Compile(s.WithSeed(s.Seed + 1)).Info.RngSeed);
    }

    [Fact]
    public void The_properties_hash_is_the_embedded_core_set_and_the_rules_are_grimdark_future()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        Assert.Equal(ScenarioCompiler.CoreProperties.ContentHash, c.Info.PropertiesHash);
        Assert.Equal("grimdark-future", c.Info.GameRules);
        Assert.Contains("Hold", ScenarioCompiler.CoreProperties.Actions.Select(a => a.Name));
    }

    [Fact]
    public void Every_catalog_scenario_compiles()
    {
        Assert.NotEmpty(ScenarioCatalog.Names);
        foreach (var name in ScenarioCatalog.Names) Assert.NotNull(ScenarioCompiler.Compile(ScenarioCatalog.Get(name)).Board);
        Assert.Throws<KeyNotFoundException>(() => ScenarioCatalog.Get("no-such-scenario"));
    }

    [Fact]
    public void A_scenario_defaults_to_strict_so_an_out_of_set_answer_is_refused() =>
        Assert.Equal(Enforcement.Strict, new ScenarioBuilder("d", 1).Army("A", 1, 1).Army("B", 1, 1).Build().Enforcement);

    [Theory]
    [InlineData("final-round")]
    [InlineData("final-round-warn")]
    [InlineData("final-round-silent")]
    public async Task The_final_round_scenarios_meet_their_expectations(string name)
    {
        var scenario = ScenarioCatalog.Get(name);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(scenario.Seed))), HandPlacement.ChooserFor(scenario.Seed));
        var report = await loop.RunAsync(CancellationToken.None);
        Assert.True(report.Passed, string.Join("\n", report.Failures));
    }
}
