using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Passengers spec: p.15 "Transports may deploy with units inside" and "units inside Transports are deployed at the same time as the Transport".
/// A unit is put in a Transport still on the shelf by a hand move, at any time before the Transport deploys, without answering an Ask.
/// </summary>
public class PassengersTests
{
    /// <summary>A: unit 1 plain, unit 2 a Transport(X) of one model, unit 3 plain; B: two plain units. A wins the roll-off: A's deploy Ask is open.</summary>
    static (SchedulerHarness H, IReadOnlyList<Guid> A, Guid Transport) AtDeployment(int spaces = 11, int modelsPerUnit = 2,
        (int, string, int?)[]? unitRules = null, (int, string, int?)[]? modelRules = null, IGameRules? rules = null)
    {
        var a = TestArmies.Build("A", 3, modelsPerUnit, unitRules, [(2, Transport, spaces), .. modelRules ?? []]);
        var h = new SchedulerHarness([a, TestArmies.Build("B", 2, 1)], enforcement: Enforcement.Strict, rules: rules);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        var units = h.Units(0);
        return (h, units, h.P.Units[units[1]].Definition.Models[0].Id);
    }

    static Actor TeamOf(SchedulerHarness h, int team) => new(Guid.CreateVersion7(), h.Teams[team]);

    static MoveModels PutIn(SchedulerHarness h, Guid unit, Guid container) =>
        new(h.P.Units[unit].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new Inside(container))).ToList());

    static MoveModels TakeOut(SchedulerHarness h, Guid unit) =>
        new(h.P.Units[unit].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList());

    [Fact]
    public void A_unit_goes_in_a_Transport_on_the_shelf_without_answering_the_deploy_Ask()
    {
        var (h, a, transport) = AtDeployment();
        var ask = h.Open.Id;
        Assert.Null(HandMoves.Judge(h.State, PutIn(h, a[0], transport), TeamOf(h, 0)));
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        Assert.All(h.P.Units[a[0]].Definition.Models, m => Assert.Equal(new Inside(transport), h.P.Models[m.Id].Location));
        Assert.NotEqual(ask, h.Open.Id);                                                        // no turn spent: A is asked again, without the passenger
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        Assert.DoesNotContain(a[0], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        h.Answer(0, new SelectedPieces([a[1]])); h.Deploy(a[1]);
        Assert.All(h.P.Units[a[0]].Definition.Models, m => Assert.Equal(new Inside(transport), h.P.Models[m.Id].Location));
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal([a[2]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);              // deployed with its Transport: not offered again
    }

    [Fact]
    public void A_passenger_is_not_offered_as_a_deploy_pick()
    {
        var (h, a, transport) = AtDeployment();
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        h.Answer(0, new SelectedPieces([a[2]])); h.Deploy(a[2]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal([a[1]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
    }

    [Fact]
    public void A_deploy_answer_naming_a_unit_already_inside_is_the_deploy_inside_breach()
    {
        var (h, a, transport) = AtDeployment(rules: GrimdarkFutureRules.Instance);
        h.Dispatch(PutIn(h, a[0], transport), team: 0);                                        // the open Ask was offered before it went in
        var found = GrimdarkFutureRules.Instance.AnswerBreach(h.State, h.Open, new SelectedPieces([a[0]]));
        Assert.Equal(DeployInsideRule, found!.Rule);
        Assert.Throws<InvalidOperationException>(() => h.Answer(0, new SelectedPieces([a[0]])));   // strict refuses
        Assert.Null(GrimdarkFutureRules.Instance.AnswerBreach(h.State, h.Open, new SelectedPieces([a[1]])));
    }

    [Fact]
    public void Taking_a_unit_out_returns_it_to_the_shelf_and_it_may_go_in_again()
    {
        var (h, a, transport) = AtDeployment();
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        Assert.Null(HandMoves.Judge(h.State, TakeOut(h, a[0]), TeamOf(h, 0)));
        h.Dispatch(TakeOut(h, a[0]), team: 0);
        Assert.All(h.P.Units[a[0]].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));
        Assert.Null(HandMoves.Judge(h.State, PutIn(h, a[0], transport), TeamOf(h, 0)));
    }

    [Fact]
    public void Another_team_may_not_put_my_unit_in_or_take_it_out()
    {
        var (h, a, transport) = AtDeployment();
        Assert.NotNull(HandMoves.Judge(h.State, PutIn(h, a[0], transport), TeamOf(h, 1)));
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        Assert.NotNull(HandMoves.Judge(h.State, TakeOut(h, a[0]), TeamOf(h, 1)));
    }

    [Fact]
    public void Taking_a_unit_out_of_a_deployed_Transport_is_a_breach()
    {
        var (h, a, transport) = AtDeployment();
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        h.Answer(0, new SelectedPieces([a[1]])); h.Deploy(a[1]);
        Assert.NotNull(HandMoves.Judge(h.State, TakeOut(h, a[0]), TeamOf(h, 0)));
    }

    [Fact]
    public void A_unit_may_not_go_in_a_Transport_that_has_deployed()
    {
        var (h, a, transport) = AtDeployment();
        h.Answer(0, new SelectedPieces([a[1]])); h.Deploy(a[1]);
        Assert.NotNull(HandMoves.Judge(h.State, PutIn(h, a[0], transport), TeamOf(h, 0)));
    }

    [Fact]
    public void A_unit_that_deployed_and_came_off_the_table_may_not_go_in()
    {
        var a = TestArmies.Build("A", 3, 2, modelRules: [(2, Transport, 11)]);
        var h = new SchedulerHarness([a, TestArmies.Build("B", 2, 1)], enforcement: Enforcement.Warn);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();
        var units = h.Units(0); var transport = h.P.Units[units[1]].Definition.Models[0].Id;
        h.Answer(0, new SelectedPieces([units[0]])); h.Deploy(units[0]);
        h.Dispatch(TakeOut(h, units[0]), team: 0);                                             // removed, as casualties are
        Assert.NotNull(HandMoves.Judge(h.State, PutIn(h, units[0], transport), TeamOf(h, 0)));
    }

    [Fact]
    public void There_must_be_room_p15_spaces()
    {
        // p.15 example: "Regular models and Heroes with Tough(3) or Tough(6) occupy 1 space, Tough(3) models occupy 3 spaces, and models with Tough(6) or higher can't be transported."
        var (h, a, transport) = AtDeployment(spaces: 3, modelsPerUnit: 2);
        Assert.Null(HandMoves.Judge(h.State, PutIn(h, a[0], transport), TeamOf(h, 0)));         // 2 of 3
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        Assert.NotNull(HandMoves.Judge(h.State, PutIn(h, a[2], transport), TeamOf(h, 0)));      // 4 of 3

        var (big, b, bigTransport) = AtDeployment(spaces: 6, modelsPerUnit: 2, modelRules: [(1, Tough, 3), (3, Tough, 6), (3, Hero, null)]);
        Assert.Null(HandMoves.Judge(big.State, PutIn(big, b[0], bigTransport), TeamOf(big, 0)));   // two Tough(3): 6 spaces
        Assert.Null(HandMoves.Judge(big.State, PutIn(big, b[2], bigTransport), TeamOf(big, 0)));   // two Tough(6) Heroes: 2 spaces

        var (huge, c, hugeTransport) = AtDeployment(spaces: 20, modelsPerUnit: 1, modelRules: [(1, Tough, 6), (3, Tough, 9), (3, Hero, null)]);
        Assert.NotNull(HandMoves.Judge(huge.State, PutIn(huge, c[0], hugeTransport), TeamOf(huge, 0)));   // a non-Hero Tough(6)
        Assert.NotNull(HandMoves.Judge(huge.State, PutIn(huge, c[2], hugeTransport), TeamOf(huge, 0)));   // a Hero Tough(9)
    }

    [Fact]
    public void A_set_aside_unit_goes_only_in_a_set_aside_Transport()
    {
        var (h, a, transport) = AtDeployment(unitRules: [(2, Scout, null), (3, Scout, null)]);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[2]), SetAside, true));
        Assert.NotNull(HandMoves.Judge(h.State, PutIn(h, a[2], transport), TeamOf(h, 0)));
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        Assert.Null(HandMoves.Judge(h.State, PutIn(h, a[2], transport), TeamOf(h, 0)));
        Assert.NotNull(HandMoves.Judge(h.State, PutIn(h, a[0], transport), TeamOf(h, 0)));
    }

    [Fact]
    public void Placing_a_Transport_set_aside_after_its_passenger_went_in_is_a_breach()
    {
        var (h, a, transport) = AtDeployment(unitRules: [(2, Scout, null)]);
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[2]])); h.Deploy(a[2]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        h.Answer(1, new SelectedPieces([h.Units(1)[1]])); h.Deploy(h.Units(1)[1]);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        h.Answer(0, new SelectedPieces([a[1]]));
        var breach = HandMoves.Judge(h.State, TestGames.Move(h.State, a[1]), TeamOf(h, 0));
        Assert.Equal($"{h.P.Units[a[0]].Definition.Name} deployed inside a container the rules do not allow", breach!.Detail);
    }

    [Fact]
    public void A_passenger_of_an_Ambush_Transport_is_not_activated_before_it_arrives()
    {
        var (h, a, transport) = AtDeployment(unitRules: [(1, Ambush, null), (2, Ambush, null)]);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[0]), SetAside, true));
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        h.Answer(0, new SelectedPieces([a[2]])); h.Deploy(a[2]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        h.Answer(1, new SelectedPieces([h.Units(1)[1]])); h.Deploy(h.Units(1)[1]);
        Assert.Equal(1, h.P.Scheduler.Round);
        var options = Assert.IsType<Options>(h.OpenAsk(0)!.Legal).Choices.Select(o => o.Id).ToList();
        Assert.DoesNotContain(options, o => ActivationOption.TryParse(o, out var unit, out _) && unit == a[0]);
        Assert.Empty(Tabletop.GameServer.Effects.Lookup.AliveModels(h.State, a[0]));
    }

    [Fact]
    public void DeployInside_lists_each_shelved_unit_and_the_Transports_it_may_go_in()
    {
        var (h, a, transport) = AtDeployment(spaces: 1, modelsPerUnit: 1);
        var rules = GrimdarkFutureRules.Instance;
        var facts = rules.DeployInside(h.State);
        Assert.Equal([transport], facts[a[0]]);
        Assert.Equal([transport], facts[a[2]]);
        Assert.False(facts.ContainsKey(a[1]));                                                  // not inside itself
        Assert.False(facts.ContainsKey(h.Units(1)[0]));                                         // no Transport on B's side
        h.Dispatch(PutIn(h, a[0], transport), team: 0);
        facts = rules.DeployInside(h.State);
        Assert.False(facts.ContainsKey(a[0]));                                                  // inside already
        Assert.False(facts.ContainsKey(a[2]));                                                  // no room left
        h.Answer(0, new SelectedPieces([a[1]])); h.Deploy(a[1]);
        Assert.Empty(rules.DeployInside(h.State));
    }

    [Fact]
    public void A_wrapping_ruleset_forwards_DeployInside()
    {
        var (h, a, transport) = AtDeployment(modelsPerUnit: 1);
        Assert.Equal([transport], new WithSequences(GrimdarkFutureRules.Instance).DeployInside(h.State)[a[0]]);
    }
}
