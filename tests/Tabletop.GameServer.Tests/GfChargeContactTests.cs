using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>p.9 CHARGE MOVES: "Charging models must move by up to 12” to get into base contact with an enemy model from the target unit, or as close as possible".</summary>
public class GfChargeContactTests
{
    // Bases are 1.26" circles: centres 1.26" apart touch.
    static Sim Charging(Enforcement enforcement, out Unit charger, out Unit target)
    {
        charger = Unit("Berserker Lord", "4+", "4+", [], 2, [], Melee("Fist", 1));
        target = Unit("Berserkers", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], enforcement: enforcement), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, charger.Models[1].Id, 20, 25.5);
        Place(sim, target.Models[0].Id, 28, 24); Place(sim, target.Models[1].Id, 28, 28);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        return sim;
    }

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    static DispatchOutcome Answer(Sim sim, AskAnswer answer) =>
        sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, answer), Samples.P1, Samples.Team1);

    [Fact]
    public void The_charge_move_Ask_names_the_target_s_models_to_reach()
    {
        var sim = Charging(Enforcement.Strict, out _, out var target);
        Assert.Equal(target.Models.Select(m => m.Id), Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).ContactWith!);
    }

    [Fact]
    public void A_charge_move_that_reaches_base_contact_is_accepted()
    {
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        Assert.IsType<Applied>(Answer(sim, new Destinations([At(sim, charger.Models[0].Id, 26.74, 24), At(sim, charger.Models[1].Id, 25.5, 25.5)])));
    }

    static double Gap(Sim sim, Guid a, Guid b) => Distance.EdgeToEdge(Battlefield.Placed(sim.State, a)!.Value, Battlefield.Placed(sim.State, b)!.Value);

    [Fact]
    public void The_charge_move_Ask_carries_the_grace()
    {
        var sim = Charging(Enforcement.Strict, out _, out _);
        Assert.Equal(GfNames.ChargeContactGrace, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).ContactGrace);
    }

    [Theory]
    [InlineData(0.14)] [InlineData(0.3)] [InlineData(0.5)]
    public void A_model_ending_within_the_grace_is_drawn_into_base_contact(double shortBy)
    {
        // Owner ruling: "It should have a small grace window". The drop lands where it was dropped; the charge ends it touching.
        var sim = Charging(Enforcement.Strict, out var charger, out var target);
        sim.Answer(Samples.Team1, new Destinations([At(sim, charger.Models[0].Id, 26.74 - shortBy, 24), At(sim, charger.Models[1].Id, 25.4, 25.2)]));
        Assert.InRange(Gap(sim, charger.Models[0].Id, target.Models[0].Id), 0, 1e-6);
        Assert.Equal(new Point2(25.4, 25.2), ((OnTable)sim.Model(charger.Models[1].Id).Location).Placement.Position.Horizontal);   // 1.6" off: left where it was put
    }

    [Fact]
    public void A_charge_short_of_contact_with_the_movement_to_reach_it_is_accepted_and_its_nearest_model_drawn_in()
    {
        // Owner ruling: "if it had the movement to make the charge then it should be accepted". The nearer model ends 1" short with 6" used.
        var sim = Charging(Enforcement.Strict, out var charger, out var target);
        sim.Answer(Samples.Team1, new Destinations([At(sim, charger.Models[0].Id, 24.74, 24), At(sim, charger.Models[1].Id, 25.9, 24.9)]));
        Assert.InRange(target.Models.Min(t => Gap(sim, charger.Models[1].Id, t.Id)), 0, 1e-6);
        Assert.InRange(Gap(sim, charger.Models[0].Id, charger.Models[1].Id), 0, GfNames.CoherencyChain);
        Assert.Equal(new Point2(24.74, 24), ((OnTable)sim.Model(charger.Models[0].Id).Location).Placement.Position.Horizontal);   // only the nearest is drawn in
    }

    [Fact]
    public void A_model_is_not_drawn_in_when_that_would_break_coherency_so_the_short_charge_is_refused()
    {
        // Side by side 3" short: either one drawn 3" in leaves the other about 2" behind it.
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        var answer = new Destinations([At(sim, charger.Models[0].Id, 23.74, 24), At(sim, charger.Models[1].Id, 23.6, 25.4)]);
        Assert.Equal(GfNames.ChargeContactRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), answer)!.Rule);
        Assert.IsType<Refused>(Answer(sim, answer));
    }

    [Fact]
    public void Drawn_into_contact_the_defender_s_touching_model_does_not_close_in()
    {
        // p.9: "all models from the target unit that are not in base contact with a charging model must move by up to 3”".
        var sim = Charging(Enforcement.Strict, out var charger, out var target);
        sim.Answer(Samples.Team1, new Destinations([At(sim, charger.Models[0].Id, 26.4, 24), At(sim, charger.Models[1].Id, 25.4, 25.2)]));
        var close = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal([target.Models[1].Id], close.ModelIds);
    }

    [Fact]
    public void A_stop_part_way_is_not_drawn_in()
    {
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        var stop = new Destinations([At(sim, charger.Models[0].Id, 26.4, 24)], Continues: true);
        Assert.Equal(26.4, ((OnTable)GfAnswers.Moves(sim.State, region, stop).Single().To).Placement.Position.X, 9);
    }

    [Fact]
    public void Under_strict_a_charge_move_that_stops_short_of_reachable_contact_is_refused()
    {
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        var none = new Destinations([At(sim, charger.Models[0].Id, 20, 24), At(sim, charger.Models[1].Id, 20, 25.5)]);
        var breach = GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), none)!;
        Assert.Equal(GfNames.ChargeContactRule, breach.Rule);
        var refused = Assert.IsType<Refused>(Answer(sim, none));
        Assert.Equal(RefusalReason.Illegal, refused.Reason);
        Assert.Equal("no charging model reached base contact with Berserkers: charging models must move to get into base contact with an enemy model from the target unit", refused.Detail);
        Assert.IsType<Refused>(Answer(sim, new Destinations([At(sim, charger.Models[0].Id, 24, 24)])));
    }

    [Fact]
    public void A_charge_that_can_reach_contact_offers_no_pass()
    {
        // p.9: "Charging models must move by up to 12” to get into base contact with an enemy model from the target unit".
        var sim = Charging(Enforcement.Strict, out _, out _);
        Assert.False(sim.AskFor(Samples.Team1).AllowPass);
        Assert.IsType<Refused>(Answer(sim, new Passed()));
        var warned = Charging(Enforcement.Warn, out _, out _);
        Assert.False(warned.AskFor(Samples.Team1).AllowPass);
        Assert.NotNull(Assert.IsType<Applied>(Answer(warned, new Passed())).Warning);
    }

    [Fact]
    public void A_charge_that_can_no_longer_reach_contact_may_pass()
    {
        // p.7: "if being within charge distance becomes impossible ... the unit may complete its move in any direction and ends its activation".
        // A model standing in the only path that fits: the way round is longer than the 12" left.
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var blocker = Unit("Wall of Shields", "4+", "4+", [], 3, []);
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger, blocker], [target], enforcement: Enforcement.Strict), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 33.1, 24);
        Place(sim, blocker.Models[0].Id, 27, 22.74); Place(sim, blocker.Models[1].Id, 27, 24); Place(sim, blocker.Models[2].Id, 27, 25.26);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.True(sim.AskFor(Samples.Team1).AllowPass);
        Assert.IsType<Applied>(Answer(sim, new Passed()));
    }

    [Fact]
    public void Under_warn_a_charge_move_short_of_contact_is_applied_with_the_breach_named()
    {
        var sim = Charging(Enforcement.Warn, out var charger, out _);
        var applied = Assert.IsType<Applied>(Answer(sim, new Destinations([At(sim, charger.Models[0].Id, 24, 24), At(sim, charger.Models[1].Id, 23, 25.5)])));
        Assert.NotNull(applied.Warning);
        Assert.Equal(GfNames.ChargeContactRule, applied.Events.OfType<AskAnswered>().Single().Breach);
    }

    [Fact]
    public void When_no_path_reaches_contact_within_the_allowance_stopping_short_is_not_a_breach()
    {
        // p.7: "if being within charge distance becomes impossible ... the unit may complete its move in any direction".
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        var ask = sim.AskFor(Samples.Team1);
        var shortened = Assert.IsType<MoveRegion>(ask.Legal) with { Allowance = 3 };
        Assert.Null(GfMoves.ChargeContactBreach(sim.State, shortened, new Destinations([At(sim, charger.Models[0].Id, 23, 24)])));
        Assert.Null(GfMoves.ChargeContactBreach(sim.State, shortened, null));
    }

    [Fact]
    public void A_model_standing_in_the_only_path_that_fits_makes_contact_impossible()
    {
        // p.7: models "may never move through other models"; the way round the blocker is longer than the 12" left.
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var blocker = Unit("Wall of Shields", "4+", "4+", [], 3, []);
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger, blocker], [target], enforcement: Enforcement.Strict), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 33.1, 24);
        Place(sim, blocker.Models[0].Id, 27, 22.74); Place(sim, blocker.Models[1].Id, 27, 24); Place(sim, blocker.Models[2].Id, 27, 25.26);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.IsType<Applied>(Answer(sim, new Destinations([At(sim, charger.Models[0].Id, 24, 24)])));
    }

    [Fact]
    public void A_wall_with_no_way_round_within_the_allowance_makes_contact_impossible()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(24, 24, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);
        var state = State([charger], [target], enforcement: Enforcement.Strict, terrain: [wall]);
        var sim = Sim.Gf(state, 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24);
        var region = new MoveRegion([charger.Models[0].Id], 12, wall.WorldVolumes().Select(v => v.Id).ToList(), [], 6, ContactWith: [target.Models[0].Id]);
        var stopped = new Destinations([At(sim, charger.Models[0].Id, 22, 24)]);
        Assert.Null(GfMoves.ChargeContactBreach(sim.State, region, stopped));
        Assert.Equal(22, ((OnTable)GfAnswers.Moves(sim.State, region, stopped).Single().To).Placement.Position.X, 9);
        // With no wall the movement reaches contact, so the model is drawn in from where it stopped.
        Assert.Equal(26.74, ((OnTable)GfAnswers.Moves(sim.State, region with { ImpassableVolumeIds = [] }, stopped).Single().To).Placement.Position.X, 6);
    }
}
