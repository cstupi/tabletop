using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

public class GfMovesTests
{
    // Armies.State puts team1's first unit at x1, its models at y -18.5 and -17. The patch lies across the first model's path only.
    static TerrainPlacement Patch(TerrainDeclarations kind, double x = -17, double y = -19, double width = 2, double length = 1) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("patch"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, 1, kind, null))]);

    static Sim Rush(Enforcement enforcement, PropertyInstance[] unitRules, out Unit unit)
    {
        unit = Unit("A", "4+", "4+", unitRules, 2, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, enforcement: enforcement, terrain: [Patch(TerrainDeclarations.Difficult)]), 4);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        return sim;
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves) =>
        sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);

    [Fact]
    public void Under_strict_a_rush_whose_one_model_entered_difficult_ground_holds_every_model_to_six_inches()
    {
        // p.11: "If any model in a unit moves in or through difficult terrain ... then all models in the unit may not move more than 6"".
        var sim = Rush(Enforcement.Strict, [], out var unit);
        var outcome = Move(sim, Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -9));
        var refused = Assert.IsType<Refused>(outcome);
        Assert.Equal(RefusalReason.OutsideLegalSet, refused.Reason);
        Assert.Contains("6\"", refused.Detail);
        Assert.IsType<Applied>(Move(sim, Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -6)));
    }

    [Fact]
    public void Under_warn_the_held_rush_is_applied_with_a_warning()
    {
        var sim = Rush(Enforcement.Warn, [], out var unit);
        var applied = Assert.IsType<Applied>(Move(sim, Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -9)));
        Assert.NotNull(applied.Warning);
        Assert.True(applied.Events.OfType<AskAnswered>().Single().OutsideLegalSet);
    }

    [Fact]
    public void A_rush_on_open_ground_and_a_strider_rush_through_difficult_ground_are_not_held()
    {
        var open = Rush(Enforcement.Strict, [], out var unit);
        Assert.IsType<Applied>(Move(open, Shifted(open, unit.Models[0].Id, -9), Shifted(open, unit.Models[1].Id, -9)));
        var strider = Rush(Enforcement.Strict, [P("Strider")], out var striders);
        Assert.IsType<Applied>(Move(strider, Shifted(strider, striders.Models[0].Id, 9), Shifted(strider, striders.Models[1].Id, -9)));
    }

    // ---- Aircraft (p.13) ---------------------------------------------------------------------------------------------------------------

    static Sim Fly(TerrainPlacement? terrain, out Unit aircraft, params int[] dice)
    {
        aircraft = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var sim = Sim.Gf(State([aircraft], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 30, terrain: terrain is null ? null : [terrain]), dice.Length == 0 ? [4] : dice);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(aircraft.Id, [], [], GfNames.Advance));
        return sim;
    }

    [Fact]
    public void An_aircraft_advance_adds_thirty_inches_and_ignores_terrain()
    {
        // p.13: "adding 30" to its total move"; "Aircraft ignore all units and terrain when moving and stopping".
        var sim = Fly(Patch(TerrainDeclarations.Difficult | TerrainDeclarations.Impassable, width: 2, length: 4), out var jet);
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(36, region.Allowance);
        Assert.Empty(region.ImpassableVolumeIds);
        Assert.Empty(region.DifficultVolumeIds);
        Assert.Null(region.DifficultCap);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, jet.Models[0].Id, 30)]));
        Assert.Contains(GfNames.Aircraft, sim.Steps("move").SelectMany(s => s.Applied ?? []));
    }

    [Fact]
    public void An_aircraft_takes_no_dangerous_terrain_test_moving_over_it_or_activated_in_it()
    {
        var sim = Fly(Patch(TerrainDeclarations.Dangerous, x: -20, y: -18.5, width: 4, length: 4), out var jet, 1);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, jet.Models[0].Id, 30)]));
        Assert.Empty(sim.Steps("test"));
    }

    [Fact]
    public void An_aircraft_is_never_a_charge_target()
    {
        // p.13: Aircraft "can't be charged".
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var jet = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var walker = Unit("Walker", "4+", "4+", [], 1, []);
        var state = State([charger], [jet, walker], x1: -2, x2: 2);
        Assert.Equal([walker.Id], GfMelee.ChargeTargets(state, TestProperties.Core, charger.Id));
    }

    // ---- Leaving a transport (p.7, p.15) -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_disembark_placement_with_part_of_the_base_off_the_table_is_refused()
    {
        // p.7: "models may also never move outside of the battlefield (no part of them, at any point)".
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([bus, riders], [Unit("B", "4+", "4+", [], 1, [])]), 4);
        var container = bus.Models[0].Id;
        var seat = new Placement(new Point3(3, 24, 0), new Yaw(0));
        sim.Append([new ModelsMoved([new ModelRelocation(container, sim.Model(container).Location, new OnTable(seat)),
            .. riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container)))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(riders.Models[0].Id, new Inside(container), new OnTable(seat with { Position = seat.Position with { X = 0.5 } })),   // within 6", half off the edge
            new ModelRelocation(riders.Models[1].Id, new Inside(container), new OnTable(seat with { Position = seat.Position with { X = 6 } })),
        ]));
        Assert.IsType<Inside>(sim.Model(riders.Models[0].Id).Location);
        Assert.IsType<OnTable>(sim.Model(riders.Models[1].Id).Location);
    }
}
