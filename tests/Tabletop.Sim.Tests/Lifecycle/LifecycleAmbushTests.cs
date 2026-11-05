using Tabletop.Geometry;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Lifecycle;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Lifecycle;

/// <summary>The ambusher shape's arrival check over a hand-built log: a casualty goes back to the shelf, so the final shelf cannot say who arrived.</summary>
public class LifecycleAmbushTests
{
    static readonly CompiledScenario C = ScenarioCompiler.Compile(new ScenarioBuilder("ambush", 1).Army("Red", 1, 3).Army("Blue", 1, 1).Build());
    static readonly Unit Riders = C.Board.Armies[0].Snapshot.Units[0];

    static GameEvent E(long seq, EventPayload p) => new(seq, null, p, null);
    static ModelRelocation Place(int i) => new(Riders.Models[i].Id, new OnShelf(), new OnTable(new Placement(new Point3(10 + i, 10, 0), new Yaw(0))));

    static LogQueries Log(params GameEvent[] events)
    {
        var applier = new EventApplier(C.Board, C.Info);
        foreach (var e in events) applier.Apply(e);
        return new LogQueries(events, applier.Projection);
    }

    [Fact]
    public void A_rider_killed_after_it_arrived_counts_as_arrived()
    {
        var log = Log(
            E(1, new ModelsMoved([Place(0), Place(1), Place(2)])),
            E(2, new ModelsMoved([new ModelRelocation(Riders.Models[1].Id, Place(1).To, new OnShelf())])));
        Assert.Contains(log.Final.Models.Values, m => m.UnitId == Riders.Id && m.Location is OnShelf);
        Assert.Null(LifecycleGame.EveryModelLeftTheShelf(log, Riders.Id));
    }

    [Fact]
    public void A_rider_never_placed_fails_the_check()
    {
        var log = Log(E(1, new ModelsMoved([Place(0), Place(2)])));
        Assert.Equal("a Beast Rider never left the shelf", LifecycleGame.EveryModelLeftTheShelf(log, Riders.Id));
    }
}
