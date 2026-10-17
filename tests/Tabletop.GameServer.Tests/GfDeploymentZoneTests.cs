using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.6: a unit deploys "fully within 12” of their table edge" (the team's deployment zone); p.14 Scout: "fully within 12” of their deployment zone".
/// The sample table's zones are frontline: team 1 (A) holds y 36–48, team 2 (B) y 0–12. A wins the roll-off.
/// </summary>
public class GfDeploymentZoneTests
{
    static SchedulerHarness AtDeployment(Enforcement enforcement, Army? a = null, Army? b = null)
    {
        var h = new SchedulerHarness([a ?? TestArmies.Build("A", 2, 2), b ?? TestArmies.Build("B", 1, 2)], enforcement: enforcement, rules: GrimdarkFutureRules.Instance);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        return h;
    }

    /// <summary>Every model of the unit in a row from (x, y), 2" apart.</summary>
    static MoveModels At(SchedulerHarness h, Guid unit, double x, double y) => new(h.P.Units[unit].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(new Placement(new Point3(x + i * 2, y, 0), new Yaw(0))))).ToList());

    static Actor TeamOf(SchedulerHarness h, int team) => new(Guid.CreateVersion7(), h.Teams[team]);

    [Fact]
    public void Strict_refuses_a_unit_placed_outside_its_deployment_zone_and_accepts_one_fully_within()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var name = h.P.Units[a[0]].Definition.Models[0].Name;
        var refused = Assert.Throws<InvalidOperationException>(() => h.Dispatch(At(h, a[0], 10, 6), team: 0));          // B's zone
        Assert.Equal($"{RefusalReason.Illegal}: {name} would be placed outside its deployment zone: units deploy fully within it (p.6)", refused.Message);
        Assert.Throws<InvalidOperationException>(() => h.Dispatch(At(h, a[0], 10, 36.2), team: 0));                       // straddles the zone's edge
        h.Dispatch(At(h, a[0], 10, 40), team: 0);
        Assert.Equal(h.Teams[1], h.Open.Teams.Single());
    }

    [Fact]
    public void Warn_accepts_a_placement_outside_the_zone_with_the_warning()
    {
        var h = AtDeployment(Enforcement.Warn);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var seq = h.P.LastSeq;
        h.Dispatch(At(h, a[0], 10, 6), team: 0);
        var moved = h.Log.Single(e => e.Seq == seq + 1);
        Assert.IsType<ModelsMoved>(moved.Payload);
        Assert.Contains("outside its deployment zone", moved.Warning);
    }

    [Fact]
    public void A_game_with_no_zones_deploys_anywhere()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var bare = new GameState(h.State.Info, h.State.Board with { Table = h.State.Board.Table with { Zones = [] } }, h.State.Applier, h.State.Rules);
        Assert.Null(HandMoves.Judge(bare, At(h, a[0], 10, 6), TeamOf(h, 0)));
        Assert.NotNull(HandMoves.Judge(h.State, At(h, a[0], 10, 6), TeamOf(h, 0)));
    }

    [Fact]
    public void A_Scout_deploys_fully_within_12_inches_of_its_zone()
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]), TestArmies.Build("B", 1, 1));
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Dispatch(At(h, a[0], 10, 44), team: 0);
        h.Answer(1, new SelectedPieces([b[0]])); h.Dispatch(At(h, b[0], 10, 4), team: 1);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        h.Answer(0, new SelectedPieces([a[1]]));
        var name = h.P.Units[a[1]].Definition.Models[0].Name;
        var refused = Assert.Throws<InvalidOperationException>(() => h.Dispatch(At(h, a[1], 30, 20), team: 0));        // 16" from the zone
        Assert.Equal($"{RefusalReason.Illegal}: {name} would be placed beyond 12\" of its deployment zone: Scouts deploy fully within 12\" of it (p.14)", refused.Message);
        h.Dispatch(At(h, a[1], 30, 26), team: 0);                                                                        // outside the zone, within 12" of it
        Assert.Equal(1, h.P.Scheduler.Round);
    }

    [Fact]
    public void An_Ambusher_is_not_held_to_the_zone()
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1));
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Dispatch(At(h, a[0], 10, 44), team: 0);
        h.Answer(1, new SelectedPieces([b[0]])); h.Dispatch(At(h, b[0], 10, 4), team: 1);
        Assert.Equal(1, h.P.Scheduler.Round);
        Assert.Null(GrimdarkFutureRules.ZoneBreach(h.State, At(h, a[1], 40, 24).Moves));        // p.13: from round two, "anywhere over 9” away from enemy units"
    }
}
