using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class InProcessDriverTests
{
    static CompiledScenario Scenario() => ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));

    [Fact]
    public async Task Starting_hydrates_a_playing_game_and_opens_the_first_ask()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        Assert.Equal(c.Info.Id, driver.GameId);
        Assert.Equal(c.TeamIds, driver.TeamIds);
        Assert.Equal("in-process", driver.Name);

        var asks = await driver.OpenAsksAsync(CancellationToken.None);
        var ask = Assert.Single(asks).Ask;
        Assert.Equal(AskQuestions.Confirm, ask.Question);                       // the roll-off gate comes first
        Assert.Equal(2, ask.Teams.Count);
        Assert.All(ask.Teams, t => Assert.Contains(t, c.TeamIds));
        Assert.IsType<Confirmation>(ask.Legal);
    }

    [Fact]
    public async Task The_drivers_projection_holds_every_model_on_the_shelf_before_deployment()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var projection = await driver.ProjectionAsync(CancellationToken.None);
        Assert.Equal(18, projection.Models.Count);                             // two armies of three units of three
        Assert.All(projection.Models.Values, m => Assert.IsType<OnShelf>(m.Location));
        Assert.Equal(0, projection.Scheduler.Round);
    }

    static async Task<Ask> RollOffGate(IGameDriver driver) =>
        (await driver.OpenAsksAsync(CancellationToken.None)).Single().Ask;

    static async Task<Ask> PastTheGateToDeploy(IGameDriver driver)
    {
        var gate = await RollOffGate(driver);
        Assert.IsType<SubmitResult.Accepted>(await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), CancellationToken.None));
        await driver.StepAsync(CancellationToken.None);
        var ask = (await driver.OpenAsksAsync(CancellationToken.None)).Single().Ask;
        Assert.Equal(AskQuestions.Deploy, ask.Question);
        return ask;
    }

    [Fact]
    public async Task An_answer_is_accepted_and_produces_events()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var gate = await RollOffGate(driver);
        var result = await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), CancellationToken.None);
        Assert.IsType<SubmitResult.Accepted>(result);
        var step = await driver.StepAsync(CancellationToken.None);
        Assert.NotEmpty(step.Events);                                   // the submit's events are handed out once, by the step
        Assert.Contains(step.Events, e => e.Payload is AskAnswered);
        Assert.Equal(step.Events.Select(e => e.Seq).Order(), step.Events.Select(e => e.Seq));
        Assert.Equal(AskQuestions.Deploy, step.OpenAsks.Single().Ask.Question);

        var again = await driver.StepAsync(CancellationToken.None);
        Assert.Empty(again.Events);
        Assert.True(again.Quiescent);
    }

    [Fact]
    public async Task Every_event_is_returned_exactly_once_across_submit_and_drain()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var gate = await RollOffGate(driver);
        await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), CancellationToken.None);
        var drained = await driver.DrainEventsAsync(CancellationToken.None);
        var step = await driver.StepAsync(CancellationToken.None);
        Assert.NotEmpty(drained);
        Assert.Empty(step.Events);
        var log = ((InProcessDriver)driver).Log;
        Assert.Equal(log.Select(e => e.Seq), log.Select(e => e.Seq).Distinct());
        Assert.Equal(log.Count, (await driver.ProjectionAsync(CancellationToken.None)).LastSeq);
    }

    [Fact]
    public async Task Deployment_selects_a_unit_then_the_hand_places_it()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var ask = await PastTheGateToDeploy(driver);
        var eligible = Assert.IsType<PieceSelection>(ask.Legal).Eligible;
        var result = await driver.AnswerAsync(ask.Teams[0], ask.Id, new SelectedPieces([eligible[0]]), CancellationToken.None);
        Assert.IsType<SubmitResult.Accepted>(result);
    }

    [Fact]
    public async Task An_answer_outside_the_legal_set_is_rejected_under_strict()
    {
        var c = Scenario();
        IGameDriver driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        await using var _ = driver;
        var ask = await PastTheGateToDeploy(driver);
        var result = await driver.AnswerAsync(ask.Teams[0], ask.Id, new SelectedPieces([Guid.CreateVersion7()]), CancellationToken.None);
        var rejected = Assert.IsType<SubmitResult.Rejected>(result);
        Assert.Equal(RefusalReason.OutsideLegalSet, rejected.Reason);
    }

    [Fact]
    public async Task The_drivers_projection_and_the_servers_agree_after_every_step()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var mine = await driver.ProjectionAsync(CancellationToken.None);
        var theirs = await driver.ServerProjectionAsync(CancellationToken.None);
        Assert.Equal(theirs.LastSeq, mine.LastSeq);
        Assert.Equal(theirs.Models.Count, mine.Models.Count);
        Assert.Equal(theirs.OpenAsks.Keys.Order(), mine.OpenAsks.Keys.Order());
    }

    [Fact]
    public async Task Draining_twice_does_not_return_the_same_event_twice()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, CancellationToken.None);
        var first = await driver.DrainEventsAsync(CancellationToken.None);
        var second = await driver.DrainEventsAsync(CancellationToken.None);
        Assert.Empty(second);
        Assert.Equal(first.Select(e => e.Seq).Order(), first.Select(e => e.Seq));
    }
}
