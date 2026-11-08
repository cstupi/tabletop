using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class HoverViewTests
{
    static readonly Guid TeamA = Guid.CreateVersion7(), TeamB = Guid.CreateVersion7(), PA = Guid.CreateVersion7(), PB = Guid.CreateVersion7(), PWatch = Guid.CreateVersion7();
    static ModelSlot Slot(int ordinal) => new(Guid.CreateVersion7(), ordinal, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, []);
    static Unit UnitOf(string name, int models) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [], Enumerable.Range(1, models).Select(Slot).ToList());
    static readonly Unit Blue = UnitOf("Blue Warriors", 3), Red = UnitOf("Red Warriors", 3), Loose = UnitOf("Loose Warriors", 1);
    static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, PA, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(TeamA, 1), new Team(TeamB, 2)],
        [new Participant(PA, Guid.CreateVersion7(), "Clay", TeamA, null), new Participant(PB, Guid.CreateVersion7(), "Sam", TeamB, null),
         new Participant(PWatch, Guid.CreateVersion7(), "Watcher", null, null)]);
    static Army ArmyOf(Unit unit) => new(Guid.CreateVersion7(), unit.Name, "grimdark-future", "test", [unit]);
    static readonly Board Board = new(TableStateTests.Sample(),
        [new ArmyInPlay(PA, ArmySnapshot.Of(ArmyOf(Blue), Guid.CreateVersion7())), new ArmyInPlay(PB, ArmySnapshot.Of(ArmyOf(Red), Guid.CreateVersion7())),
         new ArmyInPlay(PWatch, ArmySnapshot.Of(ArmyOf(Loose), Guid.CreateVersion7()))]);

    static ModelRelocation Put(ModelSlot slot, double x, double y) => new(slot.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
    /// <summary>Blue in a row at y = 10, Red at y = 20 with its first model straight across from Blue's first: 10" centre to centre, 9" edge to edge.</summary>
    static Projection Table(params EventPayload[] more)
    {
        var a = new EventApplier(Board, Game);
        EventPayload[] history = [new ModelsMoved([Put(Blue.Models[0], 10, 10), Put(Blue.Models[1], 11.5, 10), Put(Blue.Models[2], 13, 10),
            Put(Red.Models[0], 10, 20), Put(Red.Models[1], 20, 20), Put(Red.Models[2], 21.5, 20)]), .. more];
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, PA, p, null)));
        return a.Projection;
    }
    static readonly IReadOnlyDictionary<Guid, int> NoFacts = new Dictionary<Guid, int>();

    [Fact]
    public void Hover_names_the_unit_in_its_teams_ordinal()
    {
        var p = Table();
        var info = HoverView.Of(p, Game, new Names(Game, p), Red.Id, null, NoFacts)!;
        Assert.Equal((Red.Id, "Red Warriors", 2), (info.UnitId, info.Name, info.TeamOrdinal));
        Assert.Null(info.Distance); Assert.Null(info.From); Assert.Null(info.To);
        Assert.Empty(info.Wounds);
    }

    // p.5: "When measuring the distance between two units you always measure from/to the closest model in each unit", and between two
    // models "from/to the closest point of their bases".
    [Fact]
    public void With_a_unit_selected_the_distance_is_closest_models_base_edge_to_base_edge()
    {
        var p = Table();
        var info = HoverView.Of(p, Game, new Names(Game, p), Red.Id, Blue.Id, NoFacts)!;
        Assert.Equal(9.0, info.Distance!.Value, 6);
        Assert.Equal(new Point3(10, 10.5, 0), info.From);                 // on the selected unit's closest base
        Assert.Equal(new Point3(10, 19.5, 0), info.To);                   // on the hovered unit's
    }

    [Fact]
    public void No_distance_to_itself_or_from_a_unit_off_the_table()
    {
        var p = Table();
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Red.Id, Red.Id, NoFacts)!.Distance);
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Red.Id, Loose.Id, NoFacts)!.Distance);   // Loose is on the shelf
    }

    [Fact]
    public void A_unit_with_no_model_on_the_table_or_unknown_gives_nothing()
    {
        var p = Table();
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Loose.Id, Blue.Id, NoFacts));
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Guid.CreateVersion7(), Blue.Id, NoFacts));
    }

    [Fact]
    public void Wounds_show_for_models_that_take_more_than_one_by_what_is_left()
    {
        var p = Table(new WoundsSet(Red.Models[0].Id, 1), new WoundsSet(Red.Models[1].Id, 1));
        var facts = new Dictionary<Guid, int> { [Red.Models[0].Id] = 3, [Red.Models[1].Id] = 1 };   // Red.Models[2] absent: 1
        var info = HoverView.Of(p, Game, new Names(Game, p), Red.Id, null, facts)!;
        Assert.Equal([(Red.Models[0].Id, 2, 3)], info.Wounds);
    }

    [Fact]
    public void A_unit_whose_participant_has_no_team_is_ordinal_zero()
    {
        var p = Table(new ModelsMoved([Put(Loose.Models[0], 40, 40)]));
        Assert.Equal(0, HoverView.Of(p, Game, new Names(Game, p), Loose.Id, null, NoFacts)!.TeamOrdinal);
    }

    [Fact]
    public void The_wounds_are_one_line_under_the_name_like_ones_counted()
    {
        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7(), c = Guid.CreateVersion7(), d = Guid.CreateVersion7();
        Assert.Equal("3/3 ×2 · 1/3 · 2/3", HoverView.WoundsLine([(a, 3, 3), (b, 1, 3), (c, 3, 3), (d, 2, 3)]));
        Assert.Equal("2/2", HoverView.WoundsLine([(a, 2, 2)]));
        Assert.Null(HoverView.WoundsLine([]));
    }
}
