using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
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

    [Fact]
    public void A_dropped_model_resting_a_little_short_of_touching_counts_as_in_contact()
    {
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        Assert.IsType<Applied>(Answer(sim, new Destinations([At(sim, charger.Models[0].Id, 26.6, 24), At(sim, charger.Models[1].Id, 25.3, 25.5)])));   // 0.14" apart
    }

    [Fact]
    public void Under_strict_a_charge_move_that_stops_short_of_reachable_contact_or_passes_is_refused()
    {
        var sim = Charging(Enforcement.Strict, out var charger, out _);
        var none = new Destinations([At(sim, charger.Models[0].Id, 20, 24), At(sim, charger.Models[1].Id, 20, 25.5)]);
        var breach = GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), none)!;
        Assert.Equal(GfNames.ChargeContactRule, breach.Rule);
        var refused = Assert.IsType<Refused>(Answer(sim, none));
        Assert.Equal(RefusalReason.Illegal, refused.Reason);
        Assert.Equal("no charging model reached base contact with Berserkers: charging models must move to get into base contact with an enemy model from the target unit (p.9)", refused.Detail);
        Assert.IsType<Refused>(Answer(sim, new Destinations([At(sim, charger.Models[0].Id, 24, 24)])));
        Assert.IsType<Refused>(Answer(sim, new Passed()));
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
        Assert.Null(GfMoves.ChargeContactBreach(sim.State, region, new Destinations([At(sim, charger.Models[0].Id, 22, 24)])));
        Assert.NotNull(GfMoves.ChargeContactBreach(sim.State, region with { ImpassableVolumeIds = [] }, new Destinations([At(sim, charger.Models[0].Id, 22, 24)])));
    }
}
