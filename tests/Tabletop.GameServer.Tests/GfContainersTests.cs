using Tabletop.GameServer.Dice;
using Tabletop.Geometry;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfContainersTests
{
    /// <summary>Starts deal-wounds under a parent frame that recorded the wounds, the way resolve-hits does (Task 12's harness, R-F3). Riders, when given, are inside the container first.</summary>
    static Sim Wound(Tabletop.Content.Unit attacker, Tabletop.Content.Unit[] team2, Tabletop.Content.Unit target, int wounds, Tabletop.Content.Unit? riders, Guid container, params int[] dice)
    {
        var sim = new Sim(State([attacker], team2), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds")), TestProperties.Core, new ScriptedDice(dice));
        if (riders is not null) sim.Append([new ModelsMoved(riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [target.Id], [], GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], [], GfNames.Hold));
        return sim;
    }

    [Fact]
    public void A_destroyed_transport_tests_shakes_and_places_its_occupants_and_is_removed_last()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 3, []);
        // Three wounds kill the Tough(3) bus; the riders then roll three dangerous terrain dice (1, 4, 4) for one wound.
        var container = bus.Models[0].Id;
        var sim = Wound(gunners, [bus, riders], bus, 3, riders, container, 1, 4, 4);
        Assert.IsType<OnTable>(sim.Model(container).Location);                                        // p.15: not removed until the occupants are out
        Assert.Equal(3, sim.Steps("test").Single().Rolls.Single().Dice.Count);                        // one die per rider (p.11)
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [riders.Models[2].Id] = 1 }));
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (ask.Containment!.ExitingId!.Value, ask.Containment.ExitWithin!.Value));
        Assert.Equal(2, ask.ModelIds.Count);                                                          // the survivors
        sim.Answer(Samples.Team2, new Destinations([
            new ModelRelocation(riders.Models[0].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 4 }, new Yaw(0)))),
            new ModelRelocation(riders.Models[1].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 12 }, new Yaw(0)))),
        ]));
        Assert.Equal(seat.X + 4, ((OnTable)sim.Model(riders.Models[0].Id).Location).Placement.Position.X, 6);
        Assert.IsType<OnShelf>(sim.Model(riders.Models[1].Id).Location);                              // 12" out is not "fully within 6": nowhere legal to stand
        Assert.Equal(1, sim.Model(riders.Models[1].Id).Wounds);                                       // and so destroyed, not parked
        Assert.Equal(1, sim.Steps("place").Single().Values!["destroyed"]);
        Assert.Equal(2, sim.Unit(riders.Id).WoundsThisActivation);                                    // one from the test, one from the removal
        Assert.IsType<OnShelf>(sim.Model(riders.Models[2].Id).Location);                              // killed by the test
        Assert.IsType<OnShelf>(sim.Model(container).Location);                                        // removed after them
        Assert.Contains(GfNames.Shaken, sim.Unit(riders.Id).Conditions);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_container_destroyed_with_nobody_inside_is_an_ordinary_casualty()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var sim = Wound(gunners, [bus], bus, 1, null, Guid.Empty, 1);                              // one eligible model: no allocation is asked (R-N11)
        Assert.IsType<OnShelf>(sim.Model(bus.Models[0].Id).Location);                                 // assign shelved it; Wrecks never sees it
        Assert.Empty(sim.Steps("test"));
        Assert.Empty(GfContainers.Wrecks(sim.State));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
