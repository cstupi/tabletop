using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Units with named properties, placed on a 72x48 table. Team1's participant is P1; Team2's is P2.</summary>
public static class Armies
{
    public static readonly Guid P2 = Guid.CreateVersion7();
    public static PropertyInstance P(string name, int? parameter = null) => new(name, parameter);
    public static Weapon Gun(string name, string range, int attacks, params PropertyInstance[] props) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Range"] = range, ["Attacks"] = attacks.ToString() }, props);
    public static Weapon Melee(string name, int attacks, params PropertyInstance[] props) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Attacks"] = attacks.ToString() }, props);

    public static Unit Unit(string name, string quality, string defense, PropertyInstance[] unitProps, int models, PropertyInstance[] modelProps, params Weapon[] weapons) =>
        new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string> { ["Quality"] = quality, ["Defense"] = defense }, unitProps,
            Enumerable.Range(1, models).Select(i => new ModelSlot(Guid.CreateVersion7(), i, $"{name} {i}", new Dictionary<string, string>(), modelProps, Base.Circle(1.26), 1.5, null,
                weapons.Select(w => w with { Id = Guid.CreateVersion7() }).ToList())).ToList());

    /// <summary>A playing game: two armies, every model deployed in a row at the given x, one inch apart along y.</summary>
    public static GameState State(IReadOnlyList<Unit> team1, IReadOnlyList<Unit> team2, double x1 = -12, double x2 = 12, Enforcement enforcement = Enforcement.Warn, bool waits = false)
    {
        var info = Tabletop.Protocol.Tests.Samples.Game with { Enforcement = enforcement, WaitsBetweenSteps = waits,
            Participants = [new Participant(Tabletop.Protocol.Tests.Samples.P1, Guid.CreateVersion7(), "Clay", Tabletop.Protocol.Tests.Samples.Team1, null), new Participant(P2, Guid.CreateVersion7(), "Opponent", Tabletop.Protocol.Tests.Samples.Team2, null)] };
        var board = new Board(Tabletop.Content.Tests.TableStateTests.Sample() with { Terrain = [] },
            [new ArmyInPlay(Tabletop.Protocol.Tests.Samples.P1, new ArmySnapshot(Guid.CreateVersion7(), "One", "grimdark-future", team1)), new ArmyInPlay(P2, new ArmySnapshot(Guid.CreateVersion7(), "Two", "grimdark-future", team2))]);
        var applier = new EventApplier(board, info);
        var moves = new List<ModelRelocation>();
        foreach (var (units, x) in new[] { (team1, x1), (team2, x2) })
            foreach (var (u, ui) in units.Select((u, i) => (u, i)))
                foreach (var m in u.Models) moves.Add(new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, ui * 12 + m.Ordinal * 1.5 - 20, 0), new Yaw(0)))));
        applier.Apply(new GameEvent(1, null, new ModelsMoved(moves), null));
        return new GameState(info, board, applier, GrimdarkFutureRules.Instance);
    }
}
