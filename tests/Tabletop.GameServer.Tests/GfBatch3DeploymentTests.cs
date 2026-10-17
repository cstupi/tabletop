using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Army-book rules spec, batch 3: the deployment rules read by name where an Ambusher is placed (GfAmbush). The sample table's zones are frontline:
/// team 1 (A) holds y 36–48, team 2 (B) y 0–12; A wins the roll-off. B's one model stands at (10, 4).
/// </summary>
public class GfBatch3DeploymentTests
{
    static MoveModels At(SchedulerHarness h, Guid unit, double x, double y) => new(h.P.Units[unit].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(new Placement(new Point3(x + i * 2, y, 0), new Yaw(0))))).ToList());

    /// <summary>A's unit 2 set aside with Ambush and the rules given, its unit 1 at (10, 44) with A's own, B's unit at (10, 4) with B's; round two's Ambusher picked.</summary>
    static (SchedulerHarness H, Guid Ambusher) Picked((int, string, int?)[] ambusher, (int, string, int?)[]? own = null, (int, string, int?)[]? enemy = null, double ownY = 44,
        Enforcement enforcement = Enforcement.Strict)
    {
        var a = TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null), .. ambusher], modelRules: own);
        var h = new SchedulerHarness([a, TestArmies.Build("B", 1, 1, modelRules: enemy)], enforcement: enforcement, rules: GrimdarkFutureRules.Instance);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        var au = h.Units(0); var bu = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(au[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([au[0]])); h.Dispatch(At(h, au[0], 10, ownY), team: 0);
        h.Answer(1, new SelectedPieces([bu[0]])); h.Dispatch(At(h, bu[0], 10, 4), team: 1);
        h.Dispatch(new SetRound(2), team: 0);
        Assert.Equal(AskQuestions.DeployAmbusher, h.Open.Question);
        h.Answer(h.Teams.ToList().IndexOf(h.Open.Teams.Single()), new SelectedPieces([au[1]]));
        return (h, au[1]);
    }

    [Fact]
    public void Infiltrate_deploys_an_Ambusher_over_3_inches_from_enemy_units()
    {
        var (h, ambusher) = Picked([(2, GfAmbush.Infiltrate, null)]);
        Assert.NotNull(GrimdarkFutureRules.Instance.PlacementBreach(h.State, At(h, ambusher, 10, 6).Moves));          // centres 2" apart: touching
        Assert.Null(GrimdarkFutureRules.Instance.PlacementBreach(h.State, At(h, ambusher, 10, 9).Moves));             // a gap of 3.74"
        h.Dispatch(At(h, ambusher, 10, 9), team: 0);
        Assert.Contains(GfAmbush.AmbushedThisRound, h.P.Units[ambusher].Conditions);
    }

    [Fact]
    public void Repel_Ambushers_holds_an_Ambusher_over_12_inches_from_its_unit()
    {
        var (h, ambusher) = Picked([], enemy: [(1, GfAmbush.RepelAmbushers, null)]);
        var breach = GrimdarkFutureRules.Instance.PlacementBreach(h.State, At(h, ambusher, 10, 16).Moves);            // a gap of 10.74"
        Assert.Contains("Repel Ambushers", breach!.Detail);
        Assert.Null(GrimdarkFutureRules.Instance.PlacementBreach(h.State, At(h, ambusher, 10, 19).Moves));            // 13.74"
    }

    [Fact]
    public void Ambush_Beacon_lifts_the_enemy_distance_within_6_inches_of_a_friendly_beacon()
    {
        var (h, ambusher) = Picked([], own: [(1, GfAmbush.AmbushBeacon, null)], ownY: 12, enforcement: Enforcement.Warn);     // the beacon at (10, 12), out of A's zone under warn
        Assert.Null(GrimdarkFutureRules.Instance.PlacementBreach(h.State, At(h, ambusher, 14, 10).Moves));            // 4.7" from B, 1.95" from the beacon
        Assert.NotNull(GrimdarkFutureRules.Instance.PlacementBreach(h.State, At(h, ambusher, 2, 4).Moves));           // 5.48" from B, 8.8" from the beacon
    }

    [Fact]
    public void Rapid_Ambush_deploys_in_round_one_and_plain_Ambush_does_not()
    {
        var a = TestArmies.Build("A", 3, 1, unitRules: [(2, Ambush, null), (3, Ambush, null), (3, GfAmbush.RapidAmbush, null)]);
        var h = new SchedulerHarness([a, TestArmies.Build("B", 1, 1)], enforcement: Enforcement.Strict, rules: GrimdarkFutureRules.Instance);
        var units = h.Units(0);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(units[1]), SetAside, true));
        h.Dispatch(new SetCondition(TestGames.UnitSubject(units[2]), SetAside, true));
        h.Dispatch(new SetRound(1), team: 0);
        Assert.Equal([units[2]], GrimdarkFutureRules.Ambushers(h.State, TestProperties.Core, h.Teams[0]));
        h.Dispatch(new SetRound(2), team: 0);
        Assert.Equal([units[1], units[2]], GrimdarkFutureRules.Ambushers(h.State, TestProperties.Core, h.Teams[0]));
    }

    [Fact]
    public void Re_Deployment_offers_up_to_two_units_on_the_table_once_the_rest_are_deployed_and_they_deploy_again_in_the_zone()
    {
        var a = TestArmies.Build("A", 3, 1, unitRules: [(1, GfAmbush.ReDeployment, null)]);
        var h = new SchedulerHarness([a, TestArmies.Build("B", 1, 1)], enforcement: Enforcement.Strict, rules: GrimdarkFutureRules.Instance);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        var au = h.Units(0); var bu = h.Units(1);
        h.Answer(0, new SelectedPieces([au[0]])); h.Dispatch(At(h, au[0], 10, 44), team: 0);
        h.Answer(1, new SelectedPieces([bu[0]])); h.Dispatch(At(h, bu[0], 10, 4), team: 1);
        h.Answer(0, new SelectedPieces([au[1]])); h.Dispatch(At(h, au[1], 20, 44), team: 0);
        h.Answer(0, new SelectedPieces([au[2]])); h.Dispatch(At(h, au[2], 30, 44), team: 0);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        Assert.True(h.Open.AllowPass);
        Assert.Equal(au.Order(), Assert.IsType<PieceSelection>(h.Open.Legal).Eligible.Order());
        h.Answer(0, new SelectedPieces([au[1]]));
        Assert.All(h.P.Units[au[1]].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));
        Assert.NotNull(GrimdarkFutureRules.ZoneBreach(h.State, At(h, au[1], 20, 30).Moves));                       // again within the zone, not a Scout's 12" beyond it
        h.Dispatch(At(h, au[1], 20, 40), team: 0);
        h.Answer(0, new SelectedPieces([au[2]])); h.Dispatch(At(h, au[2], 30, 40), team: 0);
        Assert.NotEqual(AskQuestions.DeployScouts, h.Open.Question);                                                  // two re-deployed: none left
    }
}
