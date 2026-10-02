using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.GameServer.Log;

namespace Tabletop.Sim.Tests;

public class InvariantTests
{
    static async Task<(CompiledScenario Scenario, RunLoop Loop, RunReport Report, IGameDriver Driver)> Played(ulong seed = 1)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        var report = await loop.RunAsync(CancellationToken.None);
        return (compiled, loop, report, driver);
    }

    static GameEvent Next(IReadOnlyList<GameEvent> log, EventPayload payload, Guid? actor = null) =>
        new(log[^1].Seq + 1, actor, payload, null);

    [Fact]
    public async Task Every_invariant_holds_over_a_whole_game()
    {
        var (scenario, loop, report, driver) = await Played();
        await using var _ = driver;
        var results = Invariants.Invariants.CheckAll(await driver.ProjectionAsync(CancellationToken.None), loop.Log, scenario);
        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Held, $"{r.Name}: {r.Detail}"));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task The_round_invariant_fails_on_a_round_that_started_with_no_boundary_ask()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        // A round that started with no boundary Ask and no actor: the round advanced on its own, which the scheduler forbids.
        var forged = loop.Log.Append(Next(loop.Log, new RoundStarted(9, scenario.TeamIds, []))).ToList();
        var result = Invariants.Invariants.TheRoundAdvancedOnlyThroughTheBoundaryAsk(forged);
        Assert.False(result.Held);
        Assert.Contains("round 9", result.Detail);
        // The same event with an actor is the menu's set_round override, which is logged and so allowed.
        var overridden = loop.Log.Append(Next(loop.Log, new RoundStarted(9, scenario.TeamIds, []), Guid.NewGuid())).ToList();
        Assert.True(Invariants.Invariants.TheRoundAdvancedOnlyThroughTheBoundaryAsk(overridden).Held);
    }

    [Fact]
    public async Task A_cold_replay_the_incremental_projection_and_the_servers_all_agree()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var mine = await driver.ProjectionAsync(CancellationToken.None);
        var cold = Replay.To(scenario.Board, scenario.Info, loop.Log, loop.Log[^1].Seq);
        Assert.Empty(ProjectionComparer.Differences(cold, mine));
        var server = await ((InProcessDriver)driver).ServerProjectionAsync(CancellationToken.None);
        Assert.Empty(ProjectionComparer.Differences(server, mine));
    }

    [Fact]
    public async Task The_replay_invariant_fails_when_the_projection_is_not_the_logs()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var stale = Replay.To(scenario.Board, scenario.Info, loop.Log, loop.Log[^1].Seq - 1);
        var result = Invariants.Invariants.ReplayReproducesTheProjection(stale, loop.Log, scenario);
        Assert.False(result.Held);
        Assert.Contains("LastSeq", result.Detail);
    }

    /// <summary>
    /// Checks the effective-log prefix only: EffectiveLog after an Undone(N) is the prefix at or below N by
    /// construction, so no input makes this fail. The engine-level undo and redo check, which re-answers over the
    /// socket after a real Undo, is Task 8's DriverAgreementTests. What this test pins is the one way the check
    /// itself can go wrong: a log that already holds an undone range must be measured as its effective log.
    /// </summary>
    [Fact]
    public async Task An_undo_to_any_dice_bearing_sequence_reproduces_the_dice()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        Assert.True(Invariants.Invariants.UndoAndRedoReproduceTheDice(loop.Log, scenario).Held);

        // Dice, an undo before them, then fresh dice: the first set is undone and must not count as the game's.
        var first = loop.Log.First(e => e.Payload is StepCompleted { Rolls.Count: > 0 });
        var log = loop.Log.TakeWhile(e => e.Seq <= first.Seq).ToList();
        log.Add(Next(log, new Undone(first.Seq - 1)));
        var original = (StepCompleted)first.Payload;
        var other = original.Rolls[0] with { Dice = [new Die(6, 6, true)] };
        log.Add(Next(log, original with { Rolls = [other] }));
        var result = Invariants.Invariants.UndoAndRedoReproduceTheDice(log, scenario);
        Assert.True(result.Held, result.Detail);
    }

    [Fact]
    public async Task The_ask_invariant_fails_when_a_team_the_ask_did_not_address_answers()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var ask = loop.Log.Select(e => e.Payload).OfType<AskOpened>().First().Ask;
        var stranger = Guid.NewGuid();
        var forged = loop.Log.Append(Next(loop.Log, new AskAnswered(ask.Id, stranger, null, false))).ToList();
        var result = Invariants.Invariants.EveryAskAnsweredByAnAddressedTeam(forged);
        Assert.False(result.Held);
        Assert.Contains(stranger.ToString("N"), result.Detail);

        var unopened = loop.Log.Append(Next(loop.Log, new AskAnswered(Guid.NewGuid(), scenario.TeamIds[0], null, false))).ToList();
        Assert.False(Invariants.Invariants.EveryAskAnsweredByAnAddressedTeam(unopened).Held);
    }

    [Fact]
    public async Task The_slab_invariant_fails_off_the_slab_and_on_a_move_from_somewhere_the_model_was_not()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var model = scenario.Board.Armies[0].Snapshot.Units[0].Models[0].Id;
        var size = scenario.Board.Table.Size;
        Location At(double x, double y) => new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0)));

        // The model starts on the shelf, so a move that says it left the table lies about where it was.
        var invented = new List<GameEvent> { new(1, null, new ModelsMoved([new ModelRelocation(model, At(1, 1), At(2, 2))]), null) };
        var lied = Invariants.Invariants.NoModelInTwoPlacesOrOffTheSlab(invented, scenario);
        Assert.False(lied.Held);
        Assert.Contains("but it was at", lied.Detail);

        var off = new List<GameEvent> { new(1, null, new ModelsMoved([new ModelRelocation(model, new OnShelf(), At(size.Width + 1, 2))]), null) };
        var result = Invariants.Invariants.NoModelInTwoPlacesOrOffTheSlab(off, scenario);
        Assert.False(result.Held);
        Assert.Contains("off a", result.Detail);

        var unknown = new List<GameEvent> { new(1, null, new ModelsMoved([new ModelRelocation(Guid.NewGuid(), new OnShelf(), At(2, 2))]), null) };
        Assert.False(Invariants.Invariants.NoModelInTwoPlacesOrOffTheSlab(unknown, scenario).Held);
    }

    [Fact]
    public void The_comparer_names_what_differs()
    {
        var scenario = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        var a = Projection.Initial(scenario.Board, scenario.Info);
        var applier = new EventApplier(scenario.Board, scenario.Info);
        var model = scenario.Board.Armies[0].Snapshot.Units[0].Models[0].Id;
        applier.Apply(new GameEvent(1, null, new WoundsSet(model, 2), null));
        var differences = ProjectionComparer.Differences(a, applier.Projection);
        Assert.Contains(differences, d => d.Contains("LastSeq"));
        Assert.Contains(differences, d => d.Contains("Wounds"));
    }

    [Fact]
    public async Task Mid_run_assertion_throws_with_every_failure_named()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        MidRun.Assert(await driver.ProjectionAsync(CancellationToken.None), loop.Log, scenario);   // does not throw
        var forged = loop.Log.Append(Next(loop.Log, new RoundStarted(9, scenario.TeamIds, []))).ToList();
        var thrown = Assert.Throws<InvariantViolatedException>(() =>
            MidRun.Assert(Replay.To(scenario.Board, scenario.Info, forged, forged[^1].Seq), forged, scenario));
        Assert.Contains("round", thrown.Message);
    }
}
