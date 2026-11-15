using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.9 WHO CAN STRIKE bounds base contact on a charge. 3.5.1 p.9: "All models that are within 2” horizontally and 4” vertically of an enemy model from
/// the target unit, may attack it." 3.6.0 p.9: "All models that are within 3” of an enemy model from the target unit (horizontally or vertically), may
/// attack it." Owner ruling: "The fight cant happen if no one can strike so the charge should be rejected if nothing would reach the 3" threshold."
/// The target model stands on a pillar its own size, so every landing beside it rests on the table.
/// </summary>
public class GfChargeReachTests
{
    static TerrainPlacement Pillar(double x, double y, double height) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("pillar"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Circle(0.63), new Point2(0, 0), new Yaw(0), 0, height, TerrainDeclarations.None, null))]);

    static void Place(Sim sim, Guid model, double x, double y, double z = 0) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, z), new Yaw(0))))])]);

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    /// <summary>A one-model charger at (20, 24) and a one-model target on a pillar `height` tall at (28, 24), standing on its top.</summary>
    static Sim Scene(string rules, double height, out Unit charger, out Unit target, Enforcement enforcement = Enforcement.Strict)
    {
        charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([charger], [target], enforcement: enforcement, terrain: [Pillar(28, 24, height)]), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24, height);
        return sim;
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 2.5, true)]
    [InlineData(RuleSets.Gf360, 2.5, true)]
    [InlineData(RuleSets.Gf351, 3.5, true)]                                                             // 4" vertically
    [InlineData(RuleSets.Gf360, 3.5, false)]                                                            // 3" vertically
    [InlineData(RuleSets.Gf351, 5, false)]
    [InlineData(RuleSets.Gf360, 5, false)]
    public void A_target_is_offered_only_when_a_landing_in_base_contact_is_within_the_vertical_strike_reach(string rules, double height, bool offered)
    {
        var sim = Scene(rules, height, out var charger, out var target);
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        Assert.Equal(offered, GfMelee.ChargeTargets(sim.State, props, charger.Id).Contains(target.Id));
    }

    [Fact]
    public void A_target_on_a_wide_floor_is_reached_by_a_landing_on_that_floor()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var floor = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("platform"), new Point3(28, 24, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 5, TerrainDeclarations.None, null))]);
        var sim = Sim.Gf36(State([charger], [target], enforcement: Enforcement.Strict, terrain: [floor]), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24, 5);
        Assert.Contains(target.Id, GfMelee.ChargeTargets(sim.State, TestProperties.Core36, charger.Id));
    }

    /// <summary>A two-model target: one on a pillar 3.5" up, beyond 3.6's reach, one on the table 4" behind it.</summary>
    static Sim Split(out Unit charger, out Unit target, Enforcement enforcement = Enforcement.Strict)
    {
        charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        target = Unit("T", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var sim = Sim.Gf36(State([charger], [target], enforcement: enforcement, terrain: [Pillar(28, 24, 3.5)]), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24, 3.5); Place(sim, target.Models[1].Id, 28, 28);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        return sim;
    }

    [Fact]
    public void A_landing_beside_a_model_beyond_the_vertical_reach_is_drawn_to_the_model_it_can_reach_not_against_that_one()
    {
        var sim = Split(out var charger, out var target);
        var ask = sim.AskFor(Samples.Team1);
        var region = Assert.IsType<MoveRegion>(ask.Legal);
        Assert.Equal(GfEdition.Of(sim.State).MeleeVertical, region.ContactVertical);
        Assert.False(ask.AllowPass);                                                                         // the model on the table can be reached
        // 0.3" short of the pillar's model, on the table: no contact there, so the charge's movement draws it in to the model on the table.
        var beside = new Destinations([At(sim, charger.Models[0].Id, 26.44, 24)]);
        Assert.Null(GrimdarkFutureRules.Instance.AnswerBreach(sim.State, ask, beside));
        sim.Answer(Samples.Team1, beside);
        var landed = Battlefield.Placed(sim.State, charger.Models[0].Id)!.Value;
        Assert.InRange(Distance.EdgeToEdge(landed, Battlefield.Placed(sim.State, target.Models[1].Id)!.Value), 0, 1e-6);
        Assert.True(Distance.EdgeToEdge(landed, Battlefield.Placed(sim.State, target.Models[0].Id)!.Value) > GfNames.ChargeContactGrace);
    }

    [Fact]
    public void Without_the_movement_to_reach_the_model_on_the_table_a_landing_beside_one_beyond_the_reach_is_refused()
    {
        var sim = Split(out var charger, out _);
        var ask = sim.AskFor(Samples.Team1);
        var shorter = Assert.IsType<MoveRegion>(ask.Legal) with { Allowance = 7.9 };   // contact with the far model is 7.7" away, but drawn in from beside the pillar 8.05"
        var beside = new Destinations([At(sim, charger.Models[0].Id, 26.44, 24)]);
        Assert.Equal(26.44, ((OnTable)GfAnswers.Moves(sim.State, shorter, beside).Single().To).Placement.Position.X, 9);
        Assert.Equal(GfNames.ChargeContactRule, GfMoves.ChargeContactBreach(sim.State, shorter, beside)?.Rule);
    }

    [Fact]
    public void A_charge_whose_only_target_model_is_beyond_the_vertical_reach_is_not_judged_for_contact()
    {
        // The region built by hand: the target is not offered (ChargeTargets), so no Ask names it.
        var sim = Scene(RuleSets.Gf360, 3.5, out var charger, out var target);
        var region = new MoveRegion([charger.Models[0].Id], 12, [], [], 6, ContactWith: [target.Models[0].Id], ContactGrace: GfNames.ChargeContactGrace, ContactVertical: 3);
        var beside = new Destinations([At(sim, charger.Models[0].Id, 26.44, 24)]);
        Assert.Null(GfMoves.ChargeContactBreach(sim.State, region, beside));
        Assert.Null(GfMoves.ChargeContactBreach(sim.State, region, null));
        Assert.Equal(26.44, ((OnTable)GfAnswers.Moves(sim.State, region, beside).Single().To).Placement.Position.X, 9);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 3.5)]
    [InlineData(RuleSets.Gf360, 2.5)]
    public void A_target_on_a_pillar_within_the_vertical_reach_is_charged_into_contact_and_fought(string rules, double height)
    {
        var sim = Scene(rules, height, out var charger, out var target);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        sim.Answer(Samples.Team1, new Destinations([At(sim, charger.Models[0].Id, 26.44, 24)]));           // drawn into contact from 0.3" short
        Assert.InRange(Distance.EdgeToEdge(Battlefield.Placed(sim.State, charger.Models[0].Id)!.Value, Battlefield.Placed(sim.State, target.Models[0].Id)!.Value), 0, 1e-6);
        Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.Melee);
    }
}
