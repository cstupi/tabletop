using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// The sight options (optional-rules spec): Unit sight blocking per_model or perimeter (p.5 "the perimeter of other units"), and the line of
/// sight method core or hybrid_true (p.56 Hybrid True Line of Sight). The observer looks up the y axis at x 3.88 to a target 20" away; what
/// stands between is at y 10.
/// </summary>
public class GfSightOptionsTests
{
    const double X = 3.88;

    static Dictionary<string, string> Options(string? sight = null, string? blocking = null)
    {
        var map = new Dictionary<string, string>();
        if (sight is not null) map["line_of_sight"] = sight;
        if (blocking is not null) map["unit_sight_blocking"] = blocking;
        return map;
    }

    static Unit Tough(string name, int models, int? tough, double height = 1.5) =>
        Tall(Unit(name, "4+", "4+", [], models, tough is { } t ? [P("Tough", t)] : []), height);

    static Unit Tall(Unit u, double height) => u with { Models = u.Models.Select(m => m with { Height = height }).ToList() };

    /// <summary>A playing game with the units placed where given (each model's x, y, z), under the given options.</summary>
    static GameState Table(IReadOnlyDictionary<string, string> options, params (Unit Unit, bool Team1, (double X, double Y, double Z)[] At)[] units)
    {
        var state = State(units.Where(u => u.Team1).Select(u => u.Unit).ToList(), units.Where(u => !u.Team1).Select(u => u.Unit).ToList());
        state.Info = state.Info with { OptionalRules = options };
        var moves = units.SelectMany(u => u.Unit.Models.Zip(u.At, (m, at) => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location,
            new OnTable(new Placement(new Point3(at.X, at.Y, at.Z), new Yaw(0)))))).ToList();
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved(moves), null));
        return state;
    }

    static (double, double, double) At(double x, double y, double z = 0) => (x, y, z);

    // ---- unit sight blocking ----------------------------------------------------------------------------------------------------------

    /// <summary>A third unit of two models 1.5" apart either side of the line, its bases spanning x 1.87–3.13 and 4.63–5.89: every line from the
    /// observer to the target passes the gap between them.</summary>
    static (GameState State, Unit Observer, Unit Screen, Unit Target) Gap(string blocking)
    {
        var (observer, screen, target) = (Tough("Observer", 1, null), Tough("Screen", 2, null), Tough("Target", 1, null));
        return (Table(Options(blocking: blocking), (observer, true, [At(X, 0)]), (screen, true, [At(X - 1.38, 10), At(X + 1.38, 10)]), (target, false, [At(X, 20)])),
            observer, screen, target);
    }

    [Fact]
    public void A_line_through_the_gap_in_a_third_unit_is_clear_per_model_and_blocked_by_its_perimeter()
    {
        var (state, observer, screen, target) = Gap("per_model");
        Assert.True(Sight.From(state, observer.Id).CanSee(observer.Models[0].Id, target.Models[0].Id));
        (state, observer, screen, target) = Gap("perimeter");
        var line = Sight.From(state, observer.Id).Line(observer.Models[0].Id, target.Models[0].Id)!;
        Assert.False(line.Clear);
        Assert.Equal([screen.Id], line.BlockedBy);
    }

    [Fact]
    public void A_target_behind_the_gap_has_cover_only_behind_a_perimeter()
    {
        var (state, observer, _, target) = Gap("per_model");
        Assert.Empty(Battlefield.ModelsBehindSightBlockers(state, target.Id, [observer.Models[0].Id]));
        (state, observer, _, target) = Gap("perimeter");
        Assert.Equal([target.Models[0].Id], Battlefield.ModelsBehindSightBlockers(state, target.Id, [observer.Models[0].Id]));
    }

    [Theory]
    [InlineData("per_model")]
    [InlineData("perimeter")]
    public void The_targets_own_unit_blocks_per_model_and_the_observers_own_unit_never_blocks(string blocking)
    {
        var (observer, target) = (Tough("Observer", 2, null), Tough("Target", 2, null));
        var state = Table(Options(blocking: blocking), (observer, true, [At(X, 0), At(X, 5)]), (target, false, [At(X, 10), At(X, 20)]));
        var sight = Sight.From(state, observer.Id);
        var (rear, front) = (observer.Models[0].Id, observer.Models[1].Id);
        Assert.True(sight.CanSee(rear, target.Models[0].Id));                          // p.5: "they can always see through friendly models from their own unit"
        var line = sight.Line(front, target.Models[1].Id)!;                            // the target's front model stands in the line to its rear one
        Assert.False(line.Clear);
        Assert.Equal([target.Models[0].Id], line.BlockedBy);
    }

    // ---- hybrid true line of sight ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, false, GfSizeClass.Small)]
    [InlineData(1, false, GfSizeClass.Small)]
    [InlineData(2, false, GfSizeClass.Small)]
    [InlineData(3, false, GfSizeClass.Medium)]
    [InlineData(5, false, GfSizeClass.Medium)]
    [InlineData(6, false, GfSizeClass.Large)]
    [InlineData(12, false, GfSizeClass.Large)]
    [InlineData(14, false, GfSizeClass.Large)]
    [InlineData(15, false, GfSizeClass.Huge)]
    [InlineData(23, false, GfSizeClass.Huge)]
    [InlineData(24, false, GfSizeClass.Titanic)]
    [InlineData(null, true, GfSizeClass.Small)]
    [InlineData(3, true, GfSizeClass.Small)]
    [InlineData(5, true, GfSizeClass.Small)]
    [InlineData(6, true, GfSizeClass.Medium)]
    [InlineData(9, true, GfSizeClass.Large)]
    [InlineData(15, true, GfSizeClass.Huge)]
    public void Size_class_follows_p56s_bands_with_a_value_between_two_in_the_lower(int? tough, bool hero, GfSizeClass expected) =>
        Assert.Equal(expected, GfSight.SizeClass(tough, hero));

    /// <summary>One model of the unit in the way stands on the line at y 10, so under core it blocks every line.</summary>
    static (GameState State, Guid From, Guid To) Behind(string sight, int? observer, int? inTheWay, int? target, double observerZ = 0, double wayHeight = 1.5)
    {
        var (o, w, t) = (Tough("Observer", 1, observer), Tough("InTheWay", 1, inTheWay, wayHeight), Tough("Target", 1, target));
        var state = Table(Options(sight: sight), (o, true, [At(X, 0, observerZ)]), (w, true, [At(X, 10)]), (t, false, [At(X, 20)]));
        return (state, o.Models[0].Id, t.Models[0].Id);
    }

    static bool Sees((GameState State, Guid From, Guid To) g) =>
        Sight.From(g.State, g.State.Projection.Models[g.From].UnitId).CanSee(g.From, g.To);

    [Fact]
    public void A_larger_model_sees_through_a_unit_of_smaller_size_class()
    {
        Assert.True(Sees(Behind("hybrid_true", observer: 12, inTheWay: null, target: null)));
        Assert.False(Sees(Behind("hybrid_true", observer: null, inTheWay: null, target: null)));
        Assert.False(Sees(Behind("core", observer: 12, inTheWay: null, target: null)));
    }

    [Fact]
    public void An_equal_unit_in_the_way_blocks_unless_the_target_is_larger_than_it()
    {
        Assert.False(Sees(Behind("hybrid_true", observer: 6, inTheWay: 6, target: null)));
        Assert.True(Sees(Behind("hybrid_true", observer: 6, inTheWay: 6, target: 15)));
        Assert.False(Sees(Behind("core", observer: 6, inTheWay: 6, target: 15)));
    }

    [Fact]
    public void A_model_on_elevation_sees_through_a_unit_below_it_which_then_gives_no_cover()
    {
        var hybrid = Behind("hybrid_true", observer: null, inTheWay: null, target: null, observerZ: 2, wayHeight: 4);
        Assert.True(Sees(hybrid));
        var targetUnit = hybrid.State.Projection.Models[hybrid.To].UnitId;
        Assert.Empty(Battlefield.ModelsBehindSightBlockers(hybrid.State, targetUnit, [hybrid.From]));
        var core = Behind("core", observer: null, inTheWay: null, target: null, observerZ: 2, wayHeight: 4);
        Assert.False(Sees(core));
        Assert.Single(Battlefield.ModelsBehindSightBlockers(core.State, core.State.Projection.Models[core.To].UnitId, [core.From]));
    }

    // ---- a tall target behind a unit of shorter models ---------------------------------------------------------------------------------

    /// <summary>An infantry shooter (32mm base, 1.89" tall: the import's 1.5 × base) looks 24" up the table at a walker on a 60mm base
    /// (2.36", 3.54" tall); five more infantry stand bases touching in two rows across the line at y 12, the walker's own side's unit.</summary>
    static (GameState State, Guid From, Guid To, Unit Screen) WalkerBehindInfantry(string blocking, double walkerHeight = 3.54)
    {
        var shooter = Tall(Unit("Shooter", "4+", "4+", [], 1, []), 1.89);
        var screen = Tall(Unit("Infantry", "4+", "4+", [], 5, []), 1.89);
        var walker = Unit("Light Walker", "4+", "4+", [], 1, [P("Tough", 6)]);
        walker = walker with { Models = walker.Models.Select(m => m with { Base = Base.Circle(2.36), Height = walkerHeight }).ToList() };
        var state = Table(Options(blocking: blocking), (shooter, true, [At(X, 0)]),
            (screen, false, [At(X - 1.26, 12), At(X, 12), At(X + 1.26, 12), At(X - 0.63, 13.1), At(X + 0.63, 13.1)]),
            (walker, false, [At(X, 24)]));
        return (state, shooter.Models[0].Id, walker.Models[0].Id, screen);
    }

    [Theory]
    [InlineData("per_model")]
    [InlineData("perimeter")]
    public void A_walker_taller_than_the_infantry_in_front_is_seen_over_them_and_the_line_is_screened(string blocking)
    {
        var (state, from, to, screen) = WalkerBehindInfantry(blocking);
        var line = Sight.From(state, state.Projection.Models[from].UnitId).Line(from, to)!;
        Assert.True(line.Clear);                                            // the walker's head shows over the infantry: p.5 "check if it can see the target"
        Assert.True(line.Screened);                                         // its lower half is behind them: p.11 sight blockers
        Assert.Equal(3.54, line.Seen!.Value.Z, 6);                          // the clients draw the line to the head it saw, not through the infantry
        (state, from, to, screen) = WalkerBehindInfantry(blocking, walkerHeight: 1.89);
        line = Sight.From(state, state.Projection.Models[from].UnitId).Line(from, to)!;
        Assert.False(line.Clear);                                           // no taller than the infantry: hidden behind them
        Assert.Null(line.Seen);
        Assert.All(line.BlockedBy, id => Assert.True(id == screen.Id || screen.Models.Any(m => m.Id == id)));
    }
}
