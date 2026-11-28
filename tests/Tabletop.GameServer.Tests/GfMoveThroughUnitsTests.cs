using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.7: "Models may never be within 1” of models from other units, unless they are using a Charge action, and may never move through other models
/// or units (friendly or enemy), even if they are using a Charge action." Owner ruling (2026-11-27): both hold along the whole path; a path that
/// slips between two models of another unit moves through that unit.
/// </summary>
public class GfMoveThroughUnitsTests
{
    public static TheoryData<string> Editions => [RuleSets.Gf360, RuleSets.Gf351];

    /// <summary>A's one model at (2, -10); F, a friendly unit, with models at (5, -13) and (5, -7): 4.74" between their bases. E, the enemy, with
    /// models at (14, -10) (behind F) and (2, -1).</summary>
    static Sim Game(string rules, Enforcement e, out Unit a, out Unit f, out Unit enemy)
    {
        a = Unit("A", "4+", "4+", [], 1, [], Melee("Claws", 1));
        f = Unit("F", "4+", "4+", [], 2, []);
        enemy = Unit("E", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(rules, State([a, f], [enemy], enforcement: e), 6, 6, 6, 6, 6, 6);
        sim.Append([new ModelsMoved([At(sim, a.Models[0].Id, 2, -10), At(sim, f.Models[0].Id, 5, -13), At(sim, f.Models[1].Id, 5, -7),
            At(sim, enemy.Models[0].Id, 14, -10), At(sim, enemy.Models[1].Id, 2, -1)])]);
        return sim;
    }

    static ModelRelocation At(Sim sim, Guid m, double x, double y) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
    static IGameRules Rules(Sim sim) => GrimdarkFutureRules.Instance;   // the breaches read the edition off the state (GfEdition.Of)
    static AnswerBreachFound? Breach(Sim sim, params ModelRelocation[] moves) => Rules(sim).AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations(moves));
    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves) => sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);

    static Sim Rushing(string rules, Enforcement e, out Guid a)
    {
        var sim = Game(rules, e, out var ua, out _, out _);
        sim.Start(GfNames.RushSeq, new SequenceContext(ua.Id, [], [], GfNames.Rush));
        a = ua.Models[0].Id;
        return sim;
    }

    [Theory, MemberData(nameof(Editions))]
    public void A_rush_between_two_models_of_a_friendly_unit_moves_through_it(string rules)
    {
        var sim = Rushing(rules, Enforcement.Strict, out var a);
        var found = Breach(sim, At(sim, a, 11, -10));                                       // 9" straight between them; round the unit over 12"
        Assert.Equal(GfNames.MoveThroughModelsRule, found?.Rule);
        Assert.Equal("A 1 would move through F: models may never move through other models or units", found?.Detail);
        Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, At(sim, a, 11, -10))).Reason);
    }

    [Theory, MemberData(nameof(Editions))]
    public void A_path_round_the_unit_but_within_an_inch_of_it_breaks_the_separation(string rules)
    {
        var sim = Rushing(rules, Enforcement.Strict, out var a);
        var found = Breach(sim, At(sim, a, 8, -10));                                        // round F about 10.8"; an inch out, about 13.3"
        Assert.Equal(GfNames.MoveSeparationRule, found?.Rule);
        Assert.Equal("A 1 would pass within 1\" of F: models may never be within 1\" of models from other units", found?.Detail);
        Assert.IsType<Refused>(Move(sim, At(sim, a, 8, -10)));
        Assert.Null(Breach(sim, At(sim, a, 0, -10)));                                       // a move away from F
    }

    [Theory, MemberData(nameof(Editions))]
    public void A_landing_within_an_inch_is_judged_before_the_path(string rules)
    {
        var sim = Rushing(rules, Enforcement.Strict, out var a);
        var found = Breach(sim, At(sim, a, 6.4, -7.5));                                     // 0.23" from F's model, its path through F
        Assert.Equal(GfNames.MoveSeparationRule, found?.Rule);
        Assert.StartsWith("A 1 would end within 1\"", found?.Detail);
    }

    [Theory, MemberData(nameof(Editions))]
    public void Under_warn_a_path_through_a_unit_is_applied_with_a_warning(string rules)
    {
        var sim = Rushing(rules, Enforcement.Warn, out var a);
        Assert.NotNull(Assert.IsType<Applied>(Move(sim, At(sim, a, 11, -10))).Warning);
    }

    [Theory, MemberData(nameof(Editions))]
    public void A_charge_path_between_two_models_of_a_unit_that_is_not_the_target_is_refused(string rules)
    {
        var sim = Game(rules, Enforcement.Strict, out var ua, out _, out var enemy);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(ua.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id]));                          // reached by its model at (2, -1)
        var a = ua.Models[0].Id;
        var found = Breach(sim, At(sim, a, 12.74, -10));                                    // touching E's model behind F: straight between F's models
        Assert.Equal(GfNames.MoveThroughModelsRule, found?.Rule);
        Assert.Contains("through F", found?.Detail);
        Assert.Null(Breach(sim, At(sim, a, 2, -2.26)));                                     // touching E's other model, clear of F
    }

    [Theory, MemberData(nameof(Editions))]
    public void A_charge_target_reached_only_between_two_models_of_another_unit_is_not_offered(string rules)
    {
        var sim = Game(rules, Enforcement.Strict, out var ua, out _, out var enemy);
        sim.Append([new ModelsMoved([At(sim, enemy.Models[1].Id, 30, 20)])]);              // E's other model out of reach
        Assert.DoesNotContain(enemy.Id, GfMelee.ChargeTargets(sim.State, sim.Engine.Properties, ua.Id));
    }
}
