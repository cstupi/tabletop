using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clauses, 3.6.0 p.15 Transport(X): "[T1] May transport up to X friendly models or [T2] Heroes with up to Tough(6), and non-Heroes with up to Tough(3) which
/// occupy 3 spaces each. [T3] Transports may deploy with units inside, and [T4] units may enter/exit by using any move action, [T5] getting in contact to enter,
/// and [T6] staying fully within 6" when exiting. [T7] Units inside/outside transports may not target other units that are outside/inside, [T8] but may target
/// the transport itself. [T9] When a transport is destroyed, units inside must take a Dangerous Terrain Test, are Shaken, and must be placed fully within 6"
/// of the transport before it's removed." Note: "[T10] units can't both embark/disembark as part of the same activation. [T11] Units may use Charge actions
/// to disembark, and [T12] they ignore the 1" move restriction when embarking (only one model needs to reach the Transport for everyone to embark). [T13] If
/// the Transport is unreachable (for example because it's flying), then units can't embark in it."
/// Aircraft p.13: "[C1] May only use Advance actions, [C2] moving in a straight line, and [C3] adding 30" to its total move (even if Shaken). [C4] Aircraft
/// ignore all units and terrain when moving and stopping, [C5] can't seize or contest objectives, [C6] can't be moved in contact with or charged, and [C7] units
/// targeting them get -12" range and -1 to hit rolls." Note: "[C8] only the actual model counts (base doesn't block movement or line of sight, and units may
/// move and shoot under it). [C9] Aircraft can't move into the table edges to move less than 30", and [C10] their mandatory move doesn't break the staying idle
/// requirement to stop being Shaken."
/// </summary>
public class ClausesDeployTransportTests
{
    static void Stand(Sim sim, params (Guid Model, double X, double Y)[] at) =>
        sim.Append([new ModelsMoved(at.Select(a => new ModelRelocation(a.Model, sim.Model(a.Model).Location, new OnTable(new Placement(new Point3(a.X, a.Y, 0), new Yaw(0))))).ToList())]);

    static void Board(Sim sim, Tabletop.Content.Unit unit, Guid container) =>
        sim.Append([new ModelsMoved([.. unit.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container)))])]);

    /// <summary>[T1] "up to X": a Transport(2) has no room for a unit of three, so an Advance next to it offers no embarking.</summary>
    [Fact]
    public void A_unit_that_does_not_fit_is_not_offered_the_Transport()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 2), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 3, []);
        var sim = Sim.Gf36(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: 30, x2: 70), 1);
        var near = ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position;
        Stand(sim, (bus.Models[0].Id, near.X + 3, near.Y));
        sim.Start(AdvanceSeq, new SequenceContext(troops.Id, [], [], Advance));
        Assert.Empty(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Containment?.Enterable ?? []);
    }

    /// <summary>[T4] "any move action": a Rush reaches the Transport as an Advance does.</summary>
    [Fact]
    public void A_Rush_may_embark()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, []);
        var sim = Sim.Gf36(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: 30, x2: 70), 1);
        var near = ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position;
        Stand(sim, (bus.Models[0].Id, near.X + 10, near.Y));                                                // 8.74" away: beyond an Advance, within a Rush
        sim.Start(RushSeq, new SequenceContext(troops.Id, [], [], Rush));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([bus.Models[0].Id], region.Containment!.Enterable);
        sim.Answer(Samples.Team1, new Destinations([.. troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(bus.Models[0].Id)))]));
        Assert.All(troops.Models, m => Assert.Equal(bus.Models[0].Id, Assert.IsType<Inside>(sim.Model(m.Id).Location).ContainerId));
    }

    static Assignment? Declared(Sim sim)
    {
        var ask = sim.AskForOrNull(Samples.Team1);
        return ask?.Legal as Assignment;
    }

    /// <summary>[T7][T8] a shooter outside is offered the enemy Transport and not the unit inside it.</summary>
    [Fact]
    public void A_unit_outside_may_target_the_Transport_but_not_the_unit_inside()
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, []);
        var sim = Sim.Gf36(State([shooter], [bus, troops], x1: 30, x2: 40), 1, 1, 1, 1);
        Board(sim, troops, bus.Models[0].Id);
        sim.Start(Shooting, new SequenceContext(shooter.Id, [], [], Hold));
        Assert.Equal([bus.Id], Assert.IsType<Assignment>(Declared(sim)).Targets);
    }

    /// <summary>[T7] a unit inside is offered no unit outside, though one stands in range.</summary>
    [Fact]
    public void A_unit_inside_may_not_target_a_unit_outside()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var enemy = Unit("E", "4+", "4+", [], 1, []);
        var sim = Sim.Gf36(State([bus, troops], [enemy], x1: 30, x2: 40), 1, 1, 1, 1);
        Board(sim, troops, bus.Models[0].Id);
        sim.Start(Shooting, new SequenceContext(troops.Id, [], [], Hold));
        Assert.True(Declared(sim) is null || !Declared(sim)!.Targets.Contains(enemy.Id));
    }

    /// <summary>[C8] "base doesn't block movement … units may move … under it": a ground model's Rush may pass under an Aircraft's base to stand clear beyond it.</summary>
    [Fact]
    public void A_ground_unit_may_move_under_an_Aircraft()
    {
        var walker = Unit("Walker", "4+", "4+", [], 1, []);
        var flyer = Unit("Flyer", "4+", "4+", [P(Aircraft)], 1, []);
        var sim = Sim.Gf36(State([walker], [flyer], x1: 36, x2: 40), 1);
        var at = ((OnTable)sim.Model(walker.Models[0].Id).Location).Placement;
        sim.Start(RushSeq, new SequenceContext(walker.Id, [], [], Rush));
        var ask = sim.AskFor(Samples.Team1);
        var answer = new Destinations([new ModelRelocation(walker.Models[0].Id, sim.Model(walker.Models[0].Id).Location, new OnTable(at with { Position = at.Position with { X = 43 } }))]);
        Assert.Null(sim.State.Rules.AnswerBreach(sim.State, ask, answer));                                  // 7" straight through the Aircraft's base, 1.74" clear of it beyond
    }
}
