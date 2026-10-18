using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// A unit set aside (or returned to deployment) from the roster while deployment runs. Page 13 Ambush: "may be set aside before
/// deployment"; page 6: players alternate placing one unit. The open deploy Ask is withdrawn and asked again over the shelf as it is now,
/// and a team whose only shelved units are set aside has finished deploying.
/// </summary>
public class SetAsideDuringDeploymentTests
{
    /// <summary>A deploys first; both teams have deployed one unit, and A's Ask over its last unit, a1 (Ambush), is open.</summary>
    static (SchedulerHarness H, IReadOnlyList<Guid> A, IReadOnlyList<Guid> B) AtAsLastAsk()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 3, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();                               // A wins
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        h.Answer(1, new SelectedPieces([b[0]])); h.Deploy(b[0]);
        Assert.Equal([a[1]], Assert.IsType<PieceSelection>(h.OpenAsk(0)!.Legal).Eligible);
        return (h, a, b);
    }

    [Fact]
    public void Setting_aside_the_last_unit_during_its_own_deploy_turn_ends_that_teams_deployment()
    {
        var (h, a, b) = AtAsLastAsk();
        var asked = h.Open.Id;
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true), team: 0);
        Assert.Contains(h.Log, e => e.Payload is AskClosed { Answers.Count: 0 } c && c.AskId == asked);     // withdrawn, not answered
        Assert.Equal((h.Teams[1], AskQuestions.Deploy), (h.P.Scheduler.ActiveTeamId, h.Open.Question));    // A has nothing left to deploy: B goes on
        Assert.DoesNotContain(h.Teams[0], h.P.Scheduler.Passed);
        h.Answer(1, new SelectedPieces([b[1]])); h.Deploy(b[1]);
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                              // A is finished: B deploys again
        h.Answer(1, new SelectedPieces([b[2]])); h.Deploy(b[2]);
        Assert.Equal(1, h.P.Scheduler.Round);                                                             // no team has a deployable unit: rounds begin
        Assert.Contains(SetAside, h.P.Units[a[1]].Conditions);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Setting_aside_one_of_several_units_during_its_own_deploy_turn_asks_again_without_it()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();
        var a = h.Units(0);
        Assert.Equal([a[0], a[1]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        var seq = h.P.LastSeq;
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true), team: 0);
        Assert.Equal(["condition_set", "ask_closed", "ask_opened"], h.Kinds(seq));
        Assert.Equal((h.Teams[0], AskQuestions.Deploy), (h.P.Scheduler.ActiveTeamId, h.Open.Question));    // still A's turn
        Assert.Equal([a[0]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        seq = h.P.LastSeq;
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, false), team: 0);              // Return to deployment
        Assert.Equal(["condition_set", "ask_closed", "ask_opened"], h.Kinds(seq));
        Assert.Equal([a[0], a[1]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Setting_aside_the_unit_just_picked_before_any_model_is_placed_withdraws_the_pick()
    {
        var (h, a, _) = AtAsLastAsk();
        h.Answer(0, new SelectedPieces([a[1]]));
        Assert.Empty(h.P.OpenAsks);                                                                        // waiting for the models
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true), team: 0);
        Assert.Equal((h.Teams[1], AskQuestions.Deploy), (h.P.Scheduler.ActiveTeamId, h.Open.Question));
        var ended = h.Log.Select(e => e.Payload).OfType<TurnEnded>().Last();
        Assert.Equal(h.Teams[0], ended.TeamId);
        Assert.DoesNotContain(ended.Changes, c => c is ConditionSet { Condition: SetAside });                     // not a deploy: the unit stays set aside
        Assert.Contains(SetAside, h.P.Units[a[1]].Conditions);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Setting_aside_the_last_unit_out_of_turn_skips_that_team_and_return_to_deployment_brings_it_back()
    {
        var g = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 3, 1)]);
        g.Dice.Enqueue(6); g.Dice.Enqueue(1); g.Run(); g.ConfirmRollOff();
        var ga = g.Units(0); var gb = g.Units(1);
        g.Answer(0, new SelectedPieces([ga[0]])); g.Deploy(ga[0]);
        var bAsk = g.Open;
        g.Dispatch(new SetCondition(TestGames.UnitSubject(ga[1]), SetAside, true), team: 0);
        Assert.Equal(bAsk.Id, g.Open.Id);                                                                   // B's Ask is untouched
        g.Answer(1, new SelectedPieces([gb[0]])); g.Deploy(gb[0]);
        Assert.Equal(g.Teams[1], g.P.Scheduler.ActiveTeamId);                                              // A is skipped
        g.Dispatch(new SetCondition(TestGames.UnitSubject(ga[1]), SetAside, false), team: 0);             // Return to deployment while B chooses
        g.Answer(1, new SelectedPieces([gb[1]])); g.Deploy(gb[1]);
        Assert.Equal((g.Teams[0], AskQuestions.Deploy), (g.P.Scheduler.ActiveTeamId, g.Open.Question));    // A deploys it after all
        Assert.Equal([ga[1]], Assert.IsType<PieceSelection>(g.Open.Legal).Eligible);
        g.Rehydrate(); Assert.Empty(g.Run());
    }

    [Fact]
    public void A_game_stuck_on_a_deploy_Ask_over_a_set_aside_unit_recovers_on_hydration()
    {
        var (h, a, b) = AtAsLastAsk();
        h.Append([new ConditionSet(TestGames.UnitSubject(a[1]), SetAside, true)]);                         // the log a server without the withdrawal wrote: the Ask still stands
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        h.Rehydrate(); h.Run();
        Assert.Equal((h.Teams[1], AskQuestions.Deploy), (h.P.Scheduler.ActiveTeamId, h.Open.Question));
        Assert.Equal([b[1], b[2]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
    }

    [Fact]
    public void A_game_stuck_on_a_pick_of_a_set_aside_unit_recovers_on_hydration()
    {
        var (h, a, _) = AtAsLastAsk();
        h.Answer(0, new SelectedPieces([a[1]]));
        h.Append([new ConditionSet(TestGames.UnitSubject(a[1]), SetAside, true)]);
        h.Rehydrate(); h.Run();
        Assert.Equal((h.Teams[1], AskQuestions.Deploy), (h.P.Scheduler.ActiveTeamId, h.Open.Question));
    }
}
