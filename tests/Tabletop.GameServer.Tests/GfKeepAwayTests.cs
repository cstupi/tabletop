using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// The distance from enemy units an Ask carries for a placement (Protocol.KeepAway), the rule's own: p.13 / 3.6.0 p.13 Ambush "may be deployed anywhere
/// over 9” away from enemy units" (Infiltrate "over 3”", Repel Ambushers "over 12”", Ambush Beacon "within 6” of this model"); 3.6.0 p.15 Scout "over 9”
/// away from non-Scout enemy units" (3.5.1 p.14 keeps none); 3.6 Reinforcement "over 9” away from enemy units"; 3.6 Fanatic and Vanguard "over 9” away
/// from non-Vanguard enemy units". The sample table's zones are frontline: A holds y 36–48, B y 0–12.
/// </summary>
public class GfKeepAwayTests
{
    static MoveModels At(SchedulerHarness h, Guid unit, double x, double y) => new(h.P.Units[unit].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(new Placement(new Point3(x + i * 2, y, 0), new Yaw(0))))).ToList());

    /// <summary>A's unit 2 set aside with Ambush (and the rules given), A's unit 1 at (10, ownY), B's units deployed; round two's deploy_ambusher Ask, open.</summary>
    static (SchedulerHarness H, Guid Ambusher, Guid Own, Guid[] Enemies, Ask Ask) AmbushAsk((int, string, int?)[]? ambusher = null, (int, string, int?)[]? own = null,
        (int, string, int?)[]? enemy = null, double ownY = 44)
    {
        var a = TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, (int?)null), .. ambusher ?? []], modelRules: own);
        var h = new SchedulerHarness([a, TestArmies.Build("B", 2, 1, modelRules: enemy)], enforcement: Enforcement.Warn, rules: GrimdarkFutureRules.Instance);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run();
        var au = h.Units(0); var bu = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(au[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([au[0]])); h.Dispatch(At(h, au[0], 10, ownY), team: 0);
        h.Answer(1, new SelectedPieces([bu[0]])); h.Dispatch(At(h, bu[0], 10, 4), team: 1);
        h.Answer(1, new SelectedPieces([bu[1]])); h.Dispatch(At(h, bu[1], 40, 4), team: 1);
        h.Dispatch(new SetRound(2), team: 0);
        Assert.Equal(AskQuestions.DeployAmbusher, h.Open.Question);
        return (h, au[1], au[0], [bu[0], bu[1]], h.Open);
    }

    static KeepAway KeptFor(Ask ask, Guid unit) => Assert.IsType<PieceSelection>(ask.Legal).KeepAway![unit];

    [Fact]
    public void The_ambush_pick_carries_9_inches_from_every_enemy_unit_and_none_from_its_own()
    {
        var (_, ambusher, own, enemies, ask) = AmbushAsk();
        var keep = KeptFor(ask, ambusher);
        Assert.Equal(enemies.Order().Select(u => new AwayFrom(u, 9)), keep.Units);
        Assert.DoesNotContain(keep.Units, u => u.UnitId == own);
        Assert.Null(keep.IgnoredNear);
    }

    [Fact]
    public void Infiltrate_carries_3_inches_and_Repel_Ambushers_12_from_its_own_unit()
    {
        var (_, infiltrator, _, enemies, ask) = AmbushAsk(ambusher: [(2, GfAmbush.Infiltrate, null)]);
        Assert.All(KeptFor(ask, infiltrator).Units, u => Assert.Equal(3, u.Over));
        var (_, ambusher, _, foes, repelled) = AmbushAsk(enemy: [(1, GfAmbush.RepelAmbushers, null)]);
        var kept = KeptFor(repelled, ambusher).Units.ToDictionary(u => u.UnitId, u => u.Over);
        Assert.Equal(12, kept[foes[0]]);
        Assert.Equal(9, kept[foes[1]]);
    }

    [Fact]
    public void A_friendly_Ambush_Beacon_is_carried_as_ignoring_the_distance_within_6_inches()
    {
        var (h, ambusher, own, _, ask) = AmbushAsk(own: [(1, GfAmbush.AmbushBeacon, null)]);
        var keep = KeptFor(ask, ambusher);
        Assert.Equal(h.P.Units[own].Definition.Models.Select(m => m.Id), keep.IgnoredNear);
        Assert.Equal(6, keep.IgnoredWithin);
    }

    [Fact]
    public void The_carried_distance_is_the_one_the_placement_is_judged_by()
    {
        var (h, ambusher, _, _, ask) = AmbushAsk(enemy: [(1, GfAmbush.RepelAmbushers, null)]);
        h.Answer(h.Teams.ToList().IndexOf(h.Open.Teams.Single()), new SelectedPieces([ambusher]));
        var keep = KeptFor(ask, ambusher);
        foreach (var (x, y) in new[] { (10.0, 16.0), (10.0, 19.0), (25.0, 14.0), (40.0, 13.0), (40.0, 16.0) })
        {
            var moves = At(h, ambusher, x, y).Moves;
            var placed = Battlefield.At(h.State, moves[0].ModelId, ((OnTable)moves[0].To).Placement);
            Assert.Equal(GfKeepAway.Broken(h.State, keep, placed, new HashSet<Guid>()) is not null, GrimdarkFutureRules.Instance.PlacementBreach(h.State, moves) is not null);
        }
    }

    [Theory]
    [InlineData(RuleSets.Gf351, false)]
    [InlineData(RuleSets.Gf360, true)]
    public void The_scout_pick_carries_9_inches_from_non_scout_enemy_units_under_3_6(string rulesId, bool carried)
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]), TestArmies.Build("B", 2, 1, unitRules: [(2, Scout, null)])],
            enforcement: Enforcement.Warn, rules: GrimdarkFutureRules.Instance);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Dispatch(At(h, a[0], 10, 44), team: 0);
        h.Answer(1, new SelectedPieces([b[0]])); h.Dispatch(At(h, b[0], 10, 4), team: 1);
        h.Answer(1, new SelectedPieces([b[1]])); h.Dispatch(At(h, b[1], 10, 20), team: 1);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        var state = h.State.Under(rulesId);
        var turn = state.Rules.LegalTurn(state, TestProperties.Core, h.Teams[0])!;
        var pick = Assert.IsType<PieceSelection>(turn.Legal);
        if (!carried) { Assert.Null(pick.KeepAway); return; }
        Assert.Equal([new AwayFrom(b[0], 9)], pick.KeepAway![a[1]].Units);                // B's second unit is a Scout
    }

    [Theory]
    [InlineData(RuleSets.Gf351, null)]
    [InlineData(RuleSets.Gf360, 9.0)]
    public void A_reinforcement_copy_keeps_9_inches_from_enemy_units_under_3_6(string rules, double? over)
    {
        var rats = Unit("Rats", "4+", "4+", [P("Reinforcement")], 2, []);
        var copy = Unit("Rats", "4+", "4+", [], 2, []) with { Reserve = new UnitReserve("Reinforcement", rats.Id) };
        var enemy = Unit("E", "4+", "4+", [], 1, []);
        var state = State([rats, copy], [enemy]).Under(rules);
        Assert.Equal(over is { } d ? [new AwayFrom(enemy.Id, d)] : null, GfKeepAway.Reinforcement(state, copy.Id)?.Units);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Vanguard", false)]
    [InlineData(RuleSets.Gf360, "Vanguard", true)]
    [InlineData(RuleSets.Gf360, "Fanatic", true)]
    public void The_vanguard_placement_Ask_carries_9_inches_from_non_vanguard_enemy_units_under_3_6(string rules, string rule, bool carried)
    {
        var f = Unit("F", "4+", "4+", [], 1, [P(rule)]);
        var e = Unit("E", "4+", "4+", [], 1, []);
        var v = Unit("V", "4+", "4+", [], 1, [P("Vanguard")]);
        var sim = Sim.Gf(rules, State([f], [e, v], x1: -12, x2: 6), 1);
        sim.Start(RepositionSeq, new SequenceContext(f.Id, [], [], rule));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(carried ? [new AwayFrom(e.Id, 9)] : null, region.KeepAway?.Units);
    }
}
