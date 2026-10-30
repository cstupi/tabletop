using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.15 Scout: "may be deployed anywhere fully within 9” of their deployment zone, over 9” away from non-Scout enemy units"; 3.5.1 p.14
/// "fully within 12” of their deployment zone". The sample table's zones are frontline: A holds y 36–48, B y 0–12; bases are 1.26" across.
/// </summary>
public class GfScout36Tests
{
    /// <summary>The Scouts phase with A's set-aside Scout picked. A's first unit stands at (10, 44), B's at (10, 4); B's second unit, a Scout
    /// when <paramref name="enemyScout"/>, stands outside its zone at (10, 20) (placed under warn).</summary>
    static (SchedulerHarness H, Guid Scout) ScoutPicked(bool enemyScout)
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]),
            TestArmies.Build("B", 2, 1, unitRules: enemyScout ? [(2, Scout, null)] : null)], enforcement: Enforcement.Warn, rules: GrimdarkFutureRules.Instance);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Dispatch(At(h, a[0], 10, 44), team: 0);
        h.Answer(1, new SelectedPieces([b[0]])); h.Dispatch(At(h, b[0], 10, 4), team: 1);
        h.Answer(1, new SelectedPieces([b[1]])); h.Dispatch(At(h, b[1], 10, 20), team: 1);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        h.Answer(0, new SelectedPieces([a[1]]));
        return (h, a[1]);
    }

    static MoveModels At(SchedulerHarness h, Guid unit, double x, double y) => new(h.P.Units[unit].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(new Placement(new Point3(x + i * 2, y, 0), new Yaw(0))))).ToList());

    static AnswerBreachFound? Breach(SchedulerHarness h, string rulesId, Guid unit, double x, double y)
    {
        var state = h.State.Under(rulesId);
        return state.Rules.PlacementBreach(state, At(h, unit, x, y).Moves);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, null)]
    [InlineData(RuleSets.Gf360, "beyond 9\" of its deployment zone: Scouts deploy fully within 9\" of it (3.6.0 p.15)")]
    public void A_scout_deploys_within_nine_inches_of_its_zone_under_3_6(string rulesId, string? refused)
    {
        var (h, scout) = ScoutPicked(enemyScout: false);
        var name = h.P.Units[scout].Definition.Models[0].Name;
        var far = Breach(h, rulesId, scout, 40, 26);                                   // 10" from the zone
        Assert.Equal(refused is null ? null : new AnswerBreachFound(DeployZoneRule, $"{name} would be placed {refused}"), far);
        Assert.Null(Breach(h, rulesId, scout, 40, 28));                                 // 8" from it
    }

    [Theory]
    [InlineData(RuleSets.Gf351, null)]
    [InlineData(RuleSets.Gf360, ScoutDistanceRule)]
    public void A_scout_within_nine_of_a_non_scout_enemy_is_scout_distance(string rulesId, string? rule)
    {
        var (h, scout) = ScoutPicked(enemyScout: false);
        Assert.Equal(rule, Breach(h, rulesId, scout, 10, 28)?.Rule);                    // 8" between centres of B's (10, 20)
        Assert.Null(Breach(h, rulesId, scout, 40, 28));
    }

    [Fact]
    public void An_enemy_scout_does_not_count()
    {
        var (h, scout) = ScoutPicked(enemyScout: true);
        Assert.Null(Breach(h, RuleSets.Gf360, scout, 10, 28));
    }
}
