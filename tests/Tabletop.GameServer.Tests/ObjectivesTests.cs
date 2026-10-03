using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>GF p.6 seizing, with p.10's Shaken and p.13's Aircraft and Ambush exclusions.</summary>
public class ObjectivesTests
{
    static readonly Tabletop.Content.Unit Red = Unit("Red", "4+", "4+", [], 1, []), Blue = Unit("Blue", "4+", "4+", [], 1, []);
    static readonly Tabletop.Content.Unit Flyer = Unit("Flyer", "4+", "4+", [P(GfNames.Aircraft)], 1, []);

    static GameState Game(params Tabletop.Content.Unit[] blue) => State([Red, Flyer], blue.Length == 0 ? [Blue] : blue);
    static Guid Objective(GameState s) => s.Board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;
    static void Apply(GameState s, params EventPayload[] events) { foreach (var e in events) s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, e, null)); }
    static void Put(GameState s, Tabletop.Content.Unit unit, double x, double y) => Apply(s, new ModelsMoved(unit.Models.Select(m =>
        new ModelRelocation(m.Id, s.Projection.Models[m.Id].Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))).ToList()));
    static IReadOnlyList<StateChange> Seize(GameState s) => GfObjectives.Seize(s, TestProperties.Core);
    static void Seized(GameState s) => Apply(s, Seize(s).ToArray());

    [Fact]
    public void One_team_alone_within_three_inches_seizes_and_keeps_it_when_it_leaves()
    {
        var s = Game();
        Put(s, Red, 36, 28);
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team1)], Seize(s));
        Seized(s);
        Assert.Empty(Seize(s));                                          // already held: no change to write
        Put(s, Red, 36, 31);
        Assert.Empty(Seize(s));                                          // "stay under the player's control even if the unit moves away"
        Assert.Equal(Samples.Team1, s.Projection.Control[Objective(s)]);
    }

    [Fact]
    public void A_unit_just_out_of_range_seizes_nothing()
    {
        var s = Game();
        Put(s, Red, 36, 31);
        Assert.Empty(Seize(s));
    }

    [Fact]
    public void Both_sides_within_range_make_a_held_marker_neutral_and_leave_a_neutral_one_neutral()
    {
        var s = Game();
        Put(s, Blue, 36, 20); Put(s, Red, 36, 28);
        Assert.Empty(Seize(s));                                          // neutral and contested: still neutral
        Apply(s, new ObjectiveControlSet(Objective(s), Samples.Team1));
        Assert.Equal([new ObjectiveControlSet(Objective(s), null)], Seize(s));
    }

    [Fact]
    public void Shaken_and_Aircraft_neither_seize_nor_contest()
    {
        var s = Game();
        Put(s, Red, 36, 28); Put(s, Flyer, 37, 28); Put(s, Blue, 36, 20);
        Apply(s, new ConditionSet(new Subject(SubjectKind.Unit, Red.Id), GfNames.Shaken, true));
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team2)], Seize(s));   // Red is Shaken and Flyer is an Aircraft: Blue is alone
    }

    [Fact]
    public void An_Ambusher_neither_seizes_nor_contests_in_the_round_it_arrives_but_does_the_next()
    {
        var s = Game();
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [Samples.Team1],
            new PieceSelection([Red.Id], 1, 1), true, false, null, null);
        Apply(s, new RoundStarted(2, [Samples.Team1, Samples.Team2], []), new AskOpened(ask), new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new SelectedPieces([Red.Id]))]));
        Put(s, Red, 36, 28); Put(s, Blue, 36, 20);
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team2)], Seize(s));   // Red arrived this round
        Apply(s, new RoundStarted(3, [Samples.Team1, Samples.Team2], []));
        Assert.Empty(Seize(s));                                          // round 3: Red counts again, both teams contest a marker nobody holds
    }

    [Fact]
    public void A_model_inside_a_transport_is_nowhere_near_a_marker()
    {
        var s = Game();
        Put(s, Blue, 36, 28);
        Apply(s, new ModelsMoved([new ModelRelocation(Red.Models[0].Id, s.Projection.Models[Red.Models[0].Id].Location, new Inside(Blue.Models[0].Id))]));
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team2)], Seize(s));
    }

    [Fact]
    public void An_objective_placed_in_play_is_seized_like_one_from_setup_and_a_token_is_not()
    {
        var s = Game();
        var placed = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(10, 10, 0), new Yaw(0), 2, "B", VisualRef.OfBuiltin("objective-ring"));
        Apply(s, new MarkerPlaced(placed), new MarkerPlaced(Samples.Token with { Position = new Point3(10, 14, 0) }));
        Put(s, Red, 10, 13);
        Assert.Equal([new ObjectiveControlSet(placed.Id, Samples.Team1)], Seize(s));
        Assert.Equal(2, GfObjectives.Markers(s).Count);
    }

    [Fact]
    public void Most_markers_wins_and_an_equal_count_is_a_tie()
    {
        var s = Game();
        var placed = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(10, 10, 0), new Yaw(0), 2, "B", VisualRef.OfBuiltin("objective-ring"));
        Apply(s, new MarkerPlaced(placed));
        Assert.Equal([Samples.Team1, Samples.Team2], GfObjectives.Winners(s));               // nobody holds anything: every team at none
        Assert.Equal("Markers held: team 1: 0, team 2: 0.", GfObjectives.Standing(s));
        Apply(s, new ObjectiveControlSet(Objective(s), Samples.Team2));
        Assert.Equal([Samples.Team2], GfObjectives.Winners(s));
        Apply(s, new ObjectiveControlSet(placed.Id, Samples.Team1));
        Assert.Equal([Samples.Team1, Samples.Team2], GfObjectives.Winners(s));
        Assert.Equal("Markers held: team 1: 1, team 2: 1.", GfObjectives.Standing(s));
    }
}
