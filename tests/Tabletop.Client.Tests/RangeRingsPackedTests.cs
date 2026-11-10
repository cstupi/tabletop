using System.Diagnostics;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Xunit.Abstractions;

namespace Tabletop.Client.Tests;

/// <summary>20 carriers of a 24" rifle packed in a 5 × 4 grid, the case the performance review measured; one squad on 1" rounds, one on 2.5" × 1.5" ovals turned 30°.</summary>
[Collection(TimingCollection.Name)]
public class RangeRingsPackedTests(ITestOutputHelper output)
{
    static readonly Guid T1 = Guid.CreateVersion7(), P1 = Guid.CreateVersion7();

    static (Projection P, Unit U) Packed(Base b, double spacing, double yaw)
    {
        var slots = Enumerable.Range(1, 20).Select(i => new ModelSlot(Guid.CreateVersion7(), i, $"M{i}", new Dictionary<string, string>(), [], b, 1.5, null,
            [new Weapon(Guid.CreateVersion7(), "Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [])])).ToList();
        var unit = new Unit(Guid.CreateVersion7(), 1, "Packed", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" }, [], slots);
        var game = new GameInfo(Guid.CreateVersion7(), "Friday", GameStatus.Playing, P1, "grimdark-future", "sha256:abc", SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1,
            [new Team(T1, 1)], [new Participant(P1, Guid.CreateVersion7(), "Clay", T1, null)]);
        var board = new Board(TableStateTests.Sample(), [new ArmyInPlay(P1, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", [unit]), Guid.CreateVersion7()))]);
        var moved = new ModelsMoved(slots.Select((m, i) => new ModelRelocation(m.Id, new OnShelf(),
            new OnTable(new Placement(new Point3(30 + i % 5 * spacing, 20 + i / 5 * spacing, 0), new Yaw(yaw))))).ToList());
        var a = new EventApplier(board, game); a.ApplyAll([new GameEvent(1, P1, moved, null)]);
        return (a.Projection, unit);
    }

    static IReadOnlyList<PlacedModel> Carriers(Projection p, Guid unit) => p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnTable)
        .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();

    void Holds(Projection p, Unit u, string name)
    {
        RangeRings.Of(p, u.Id);                                                                                              // warm up
        var times = Enumerable.Range(0, 5).Select(_ => { var sw = Stopwatch.StartNew(); RangeRings.Of(p, u.Id); return sw.Elapsed.TotalMilliseconds; }).Order().ToList();
        output.WriteLine($"{name}: median {times[2]:F2} ms, min {times[0]:F2} ms");
        var ring = Assert.Single(RangeRings.Of(p, u.Id));
        var carriers = Carriers(p, u.Id);
        var ends = ring.Segments.SelectMany(s => new[] { s.A, s.B }).ToList();
        Assert.NotEmpty(ends);
        Assert.All(ends, q => Assert.Equal(24, carriers.Min(c => Distance.EdgeToEdge(c, q)), 3));                       // on the outline: 24" from the nearest base edge
        Assert.All(ends, q => Assert.Equal(2, ring.Segments.Sum(s => (s.A.DistanceTo(q) < 1e-3 ? 1 : 0) + (s.B.DistanceTo(q) < 1e-3 ? 1 : 0)))); // one closed outline
        var mids = ring.Segments.Select(s => new Point2((s.A.X + s.B.X) / 2, (s.A.Y + s.B.Y) / 2));
        Assert.All(mids, q => Assert.True(carriers.Min(c => Distance.EdgeToEdge(c, q)) > 23.9));                          // no chord cuts inside the union
    }

    [Fact]
    public void Twenty_packed_round_bases_draw_one_outline() { var (p, u) = Packed(Base.Circle(1), 1.1, 0); Holds(p, u, "20 × 1\" rounds"); }

    [Fact]
    public void Twenty_packed_ovals_draw_one_outline() { var (p, u) = Packed(Base.Oval(2.5, 1.5), 2.7, 30); Holds(p, u, "20 × ovals"); }
}
