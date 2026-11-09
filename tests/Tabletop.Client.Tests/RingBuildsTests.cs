using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>Rings built off the main thread: one build per key and stamp, kept while the stamp holds, null until it lands.</summary>
public class RingBuildsTests
{
    static UnitThreat SquadThreat(double? charge = 12) => new(charge, 6, new Dictionary<Guid, double> { [TableUx.Squad.Models[0].Id] = 24 });

    [Fact]
    public void A_build_runs_once_per_key_and_stamp_and_its_result_stands_until_the_stamp_changes()
    {
        var p = TableUx.After(TableUx.Deploy); var builds = 0;
        var cache = new RingBuilds<Guid, string>(Inline);
        var stamp = RingStamp.Of(p, TableUx.Squad.Id, SquadThreat());
        Assert.Equal("1", cache.Get(TableUx.Squad.Id, [stamp], () => (++builds).ToString()));
        Assert.Equal("1", cache.Get(TableUx.Squad.Id, [RingStamp.Of(p, TableUx.Squad.Id, SquadThreat())], () => (++builds).ToString()));   // an equal threat from a later ModelFacts
        Assert.Equal("2", cache.Get(TableUx.Squad.Id, [RingStamp.Of(p, TableUx.Squad.Id, SquadThreat(8))], () => (++builds).ToString()));
        Assert.Equal("3", cache.Get(TableUx.Orcs.Id, [RingStamp.Of(p, TableUx.Orcs.Id, null)], () => (++builds).ToString()));
        Assert.Equal("2", cache.Get(TableUx.Squad.Id, [RingStamp.Of(p, TableUx.Squad.Id, SquadThreat(8))], () => (++builds).ToString()));   // the squad's build is kept beside the orcs'
    }

    [Fact]
    public void A_moved_model_changes_the_stamp()
    {
        var a = TableUx.Applier(TableUx.Deploy);
        var before = RingStamp.Of(a.Projection, TableUx.Squad.Id, SquadThreat());
        a.ApplyAll([new GameEvent(2, TableUx.P1, new ModelsMoved([new ModelRelocation(TableUx.Squad.Models[2].Id, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))),
            new OnTable(new Placement(new Point3(20, 10, 0), new Yaw(0))))]), null)]);
        Assert.NotEqual(before, RingStamp.Of(a.Projection, TableUx.Squad.Id, SquadThreat()));
        Assert.Equal(RingStamp.Of(a.Projection, TableUx.Squad.Id, SquadThreat()), RingStamp.Of(a.Projection, TableUx.Squad.Id, SquadThreat()));
    }

    [Fact]
    public void Until_a_build_lands_the_answer_is_null_and_Built_says_when_it_has()
    {
        var p = TableUx.After(TableUx.Deploy);
        var gate = new TaskCompletionSource();
        var cache = new RingBuilds<Guid, string>(build => Task.Run(async () => { await gate.Task; return build(); }));
        var built = new TaskCompletionSource();
        cache.Built += () => built.TrySetResult();
        IReadOnlyList<RingStamp> stamp = [RingStamp.Of(p, TableUx.Squad.Id, SquadThreat())];
        Assert.Null(cache.Get(TableUx.Squad.Id, stamp, () => "rings"));
        Assert.Null(cache.Get(TableUx.Squad.Id, stamp, () => throw new InvalidOperationException("built twice")));
        gate.SetResult();
        Assert.True(built.Task.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("rings", cache.Get(TableUx.Squad.Id, stamp, () => throw new InvalidOperationException("built twice")));
    }

    [Fact]
    public void A_failed_build_answers_null_and_the_oldest_keys_go_past_the_capacity()
    {
        var p = TableUx.After(TableUx.Deploy);
        var cache = new RingBuilds<int, string>(Inline, capacity: 2);
        IReadOnlyList<RingStamp> stamp = [RingStamp.Of(p, TableUx.Squad.Id, null)];
        Assert.Null(cache.Get(0, stamp, () => throw new InvalidOperationException("no outline")));
        Assert.Equal("one", cache.Get(1, stamp, () => "one"));
        Assert.Equal("two", cache.Get(2, stamp, () => "two"));
        Assert.Equal("one", cache.Get(1, stamp, () => "rebuilt"));                                             // 1 is still held: 0 went first
        Assert.Equal("zero", cache.Get(0, stamp, () => "zero"));                                             // 0 was dropped: built again
    }

    static Task<T> Inline<T>(Func<T> build)
    {
        try { return Task.FromResult(build()); } catch (Exception e) { return Task.FromException<T>(e); }
    }
}
