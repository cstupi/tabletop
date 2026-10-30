using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.AppliedKindsTests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Reinforcement by edition. 3.5.1: "When a unit where all models have this rule is Shaken or fully destroyed, you may remove it from the table as destroyed and
/// place a new copy of it fully within 12" of any table edge at the beginning of the next round after Ambushers have been deployed." 3.6: "When a unit where all
/// models have this rule is Shaken, you may remove it from the table as destroyed. At the beginning of the next round after a unit where all models have this rule
/// is destroyed, after Ambushers have been deployed, you may place a new copy of it fully within 9" of any table edge, over 9" away from enemy units."
/// </summary>
public class GfReserves36Tests
{
    static (Unit Rats, Unit Copy, Unit Enemy, GameState State) Game(string rules)
    {
        var rats = Unit("Rats", "4+", "4+", [P("Reinforcement")], 2, []);
        var copy = Unit("Rats", "4+", "4+", [], 2, []) with { Reserve = new UnitReserve("Reinforcement", rats.Id) };
        var enemy = Unit("E", "4+", "4+", [], 1, []);
        return (rats, copy, enemy, State([rats, copy], [enemy]).Under(rules));
    }

    static ModelRelocation From(GameState state, Guid model, double x, double y) =>
        new(model, state.Projection.Models[model].Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    static void Stand(Sim sim, params (Guid Model, double X, double Y)[] at) => sim.Append([new ModelsMoved(at.Select(a => From(sim.State, a.Model, a.X, a.Y)).ToList())]);

    [Theory]
    [InlineData(RuleSets.Gf351, null)]
    [InlineData(RuleSets.Gf360, "Rats 1 would be placed beyond 9\" of every table edge: Reinforcement places the new copy fully within 9\" of any table edge")]
    public void A_copy_stands_fully_within_12_inches_of_an_edge_under_3_5_1_and_9_under_3_6(string rules, string? detail)
    {
        var (_, copy, enemy, state) = Game(rules);
        var sim = Sim.Gf(rules, state);
        Stand(sim, (enemy.Models[0].Id, 60, 24));
        var y = sim.State.Board.Table.Size.Length / 2;
        Assert.Null(GfReserves.EdgeBreach(sim.State, [From(sim.State, copy.Models[0].Id, 8, y)]));
        Assert.Equal(detail, GfReserves.EdgeBreach(sim.State, [From(sim.State, copy.Models[0].Id, 10.5, y)])?.Detail);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, null)]
    [InlineData(RuleSets.Gf360, "Rats 1 would be placed within 9\" of an enemy unit: Reinforcement places the new copy over 9\" away from enemy units")]
    public void A_copy_stands_over_9_inches_from_enemy_units_under_3_6_only(string rules, string? detail)
    {
        var (_, copy, enemy, state) = Game(rules);
        var sim = Sim.Gf(rules, state);
        Stand(sim, (enemy.Models[0].Id, 5, 15));
        var near = GfReserves.EdgeBreach(sim.State, [From(sim.State, copy.Models[0].Id, 5, 23)]);
        Assert.Equal(detail, near?.Detail);
        if (near is not null) Assert.Equal(GfReserves.DistanceRule, near.Rule);
        Assert.Null(GfReserves.EdgeBreach(sim.State, [From(sim.State, copy.Models[0].Id, 5, 26)]));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, true)]
    [InlineData(RuleSets.Gf360, false)]
    public void The_round_start_offers_a_shaken_originals_copy_under_3_5_1_and_only_a_destroyed_ones_under_3_6(string rules, bool shakenOffered)
    {
        var (rats, copy, _, state) = Game(rules);
        var sim = Sim.Gf(rules, state);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, rats.Id), GfNames.Shaken, true)]);
        Assert.Equal(shakenOffered ? [copy.Id] : [], GfReserves.Reinforcements(sim.State, Samples.Team1));
        sim.Append([new ModelsMoved(rats.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf())).ToList())]);
        Assert.Equal([copy.Id], GfReserves.Reinforcements(sim.State, Samples.Team1));                               // destroyed: both editions
    }

    [Theory]
    [InlineData(RuleSets.Gf351, false)]
    [InlineData(RuleSets.Gf360, true)]
    public void A_unit_failing_morale_into_shaken_is_offered_removal_as_destroyed_under_3_6(string rules, bool offered)
    {
        var (rats, copy, _, state) = Game(rules);
        var sim = Sim.Gf(rules, state, 1);
        sim.Start(GfNames.MoraleTest, new SequenceContext(rats.Id, [], [], "wounds"));
        Assert.Contains(GfNames.Shaken, sim.State.Projection.Units[rats.Id].Conditions);
        var ask = sim.AskForOrNull(Samples.Team1);
        Assert.Equal(offered, ask is { Question: GfReserves.RemoveQuestion });
        if (!offered) return;
        sim.Answer(Samples.Team1, new Chosen("remove"));
        Assert.All(rats.Models, m => Assert.IsType<OnShelf>(sim.Model(m.Id).Location));
        Assert.Contains("Reinforcement", Applied(sim, GfReserves.RemoveStep));
        Assert.Equal([copy.Id], GfReserves.Reinforcements(sim.State, Samples.Team1));
    }

    [Fact]
    public void A_shaken_unit_kept_on_the_table_under_3_6_stays_and_its_copy_waits()
    {
        var (rats, _, _, state) = Game(RuleSets.Gf360);
        var sim = Sim.Gf36(state, 1);
        sim.Start(GfNames.MoraleTest, new SequenceContext(rats.Id, [], [], "wounds"));
        sim.Answer(Samples.Team1, new Chosen("pass"));
        Assert.All(rats.Models, m => Assert.IsType<OnTable>(sim.Model(m.Id).Location));
        Assert.Empty(GfReserves.Reinforcements(sim.State, Samples.Team1));
    }

    [Fact]
    public void A_unit_with_no_waiting_copy_is_not_offered_removal_under_3_6()
    {
        var plain = Unit("Plain", "4+", "4+", [], 2, []);
        var sim = Sim.Gf36(State([plain], [Unit("E", "4+", "4+", [], 1, [])]), 1);
        sim.Start(GfNames.MoraleTest, new SequenceContext(plain.Id, [], [], "wounds"));
        Assert.Contains(GfNames.Shaken, sim.State.Projection.Units[plain.Id].Conditions);
        Assert.Null(sim.AskForOrNull(Samples.Team1));
    }
}
