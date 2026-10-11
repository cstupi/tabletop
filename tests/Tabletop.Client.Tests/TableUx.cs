using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>Team 1 (P1): Squad of three on the table (two rifles 24", one pistol 12", a knife with no Range) and Reserve on the shelf. Team 2 (P2): Orcs, one model with a melee weapon.</summary>
public static class TableUx
{
    public static readonly Guid T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7(), P1 = Guid.CreateVersion7(), P2 = Guid.CreateVersion7();
    static Weapon W(string name, string? range) => new(Guid.CreateVersion7(), name, range is null ? new Dictionary<string, string> { ["Attacks"] = "1" } : new Dictionary<string, string> { ["Range"] = range, ["Attacks"] = "1" }, []);
    static ModelSlot M(int ordinal, params Weapon[] w) => new(Guid.CreateVersion7(), ordinal, $"M{ordinal}", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, w);
    static Unit U(string name, params ModelSlot[] m) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" }, [], m);
    public static readonly Unit Squad = U("Squad", M(1, W("Rifle", "24\""), W("Knife", null)), M(2, W("Rifle", "24\"")), M(3, W("Pistol", "12\"")));
    public static readonly Unit Reserve = U("Reserve", M(1, W("Rifle", "24\"")));
    public static readonly Unit Orcs = U("Orcs", M(1, W("Choppa", null)));
    public static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, P1, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(T1, 1), new Team(T2, 2)],
        [new Participant(P1, Guid.CreateVersion7(), "Clay", T1, null), new Participant(P2, Guid.CreateVersion7(), "Ann", T2, null)]);
    static ArmyInPlay Of(Guid participant, params Unit[] units) => new(participant, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", units), Guid.CreateVersion7()));
    public static readonly Board Board = new(TableStateTests.Sample(), [Of(P1, Squad, Reserve), Of(P2, Orcs)]);
    public static Guid ModelOf(Unit u, int i = 0) => u.Models[i].Id;
    static ModelRelocation Place(ModelSlot m, double x, double y) => new(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
    /// <summary>Squad at (10,10), (12,10), (14,10); Orcs at (40,30).</summary>
    public static EventPayload Deploy => new ModelsMoved([Place(Squad.Models[0], 10, 10), Place(Squad.Models[1], 12, 10), Place(Squad.Models[2], 14, 10), Place(Orcs.Models[0], 40, 30)]);
    public static IReadOnlyList<GameEvent> Events(params EventPayload[] payloads) => payloads.Select((x, i) => new GameEvent(i + 1, P1, x, null)).ToList();
    public static EventApplier Applier(params EventPayload[] history) { var a = new EventApplier(Board, Game); a.ApplyAll(Events(history)); return a; }
    public static Projection After(params EventPayload[] history) => Applier(history).Projection;
}
