using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class SheetFocusTests
{
    internal static readonly Guid TeamA = Guid.CreateVersion7(), TeamB = Guid.CreateVersion7(), PA = Guid.CreateVersion7(), PB = Guid.CreateVersion7();
    static Unit UnitOf(string name) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [],
        [new ModelSlot(Guid.CreateVersion7(), 1, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, [])]);
    internal static readonly Unit Blue = UnitOf("Blue"), Blue2 = UnitOf("Blue Two"), Red = UnitOf("Red");
    internal static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, PA, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(TeamA, 1), new Team(TeamB, 2)],
        [new Participant(PA, Guid.CreateVersion7(), "Clay", TeamA, null), new Participant(PB, Guid.CreateVersion7(), "Sam", TeamB, null)]);
    static readonly Board Board = new(TableStateTests.Sample(),
        [new ArmyInPlay(PA, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Blue", "grimdark-future", "test", [Blue, Blue2]), Guid.CreateVersion7())),
         new ArmyInPlay(PB, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Red", "grimdark-future", "test", [Red]), Guid.CreateVersion7()))]);

    internal static Projection Activating(Guid? actor)
    {
        var a = new EventApplier(Board, Game);
        if (actor is { } u)
            a.ApplyAll([new GameEvent(1, null, new SequenceStarted(Guid.CreateVersion7(), "advance", new SequenceContext(u, [], [], "Advance")), null)]);
        return a.Projection;
    }

    [Fact]
    public void The_selection_shows_else_my_activating_unit_else_none()
    {
        Assert.Equal(Red.Id, SheetFocus.Of(Activating(null), Game, TeamA, Red.Id));   // a selected unit of any team
        Assert.Equal(Blue.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, null));
        Assert.Null(SheetFocus.Of(Activating(null), Game, TeamA, null));
    }

    /// <summary>The owner, 2026-11-14: "it doesn't show your current unit's weapon range". The weapon range rings are the card's unit when it is
    /// my team's: the selected one, else my team's activating unit with nothing selected; never another team's.</summary>
    [Fact]
    public void Range_rings_are_for_my_selected_unit_else_my_activating_one()
    {
        Assert.Equal(Blue.Id, RangeRings.UnitFor(Activating(null), Game, TeamA, Blue.Id));
        Assert.Equal(Blue.Id, RangeRings.UnitFor(Activating(Blue.Id), Game, TeamA, null));        // activated from the decision box, never clicked
        Assert.Equal(Blue2.Id, RangeRings.UnitFor(Activating(Blue.Id), Game, TeamA, Blue2.Id));
        Assert.Null(RangeRings.UnitFor(Activating(Blue.Id), Game, TeamA, Red.Id));                // an enemy selected: its threat rings draw instead
        Assert.Null(RangeRings.UnitFor(Activating(Red.Id), Game, TeamA, null));
        Assert.Null(RangeRings.UnitFor(Activating(Blue.Id), Game, null, null));
    }

    [Fact]
    public void An_activation_never_changes_a_selection()
    {
        Assert.Equal(Blue2.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, Blue2.Id));
        Assert.Equal(Red.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, Red.Id));
    }

    [Fact]
    public void An_enemy_activation_never_takes_the_card()
    {
        Assert.Null(SheetFocus.Of(Activating(Red.Id), Game, TeamA, null));
        Assert.Null(SheetFocus.Of(Activating(Blue.Id), Game, null, null));   // a watcher has no team: no activation is theirs
    }

    [Fact]
    public void A_selected_marker_falls_back() =>
        Assert.Equal(Blue.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, Activating(null).Board.Table.Markers[0].Id));

    [Fact]
    public void A_selected_terrain_piece_takes_the_card_s_slot()
    {
        var p = Activating(Blue.Id);
        Assert.Null(SheetFocus.Of(p, Game, TeamA, p.Board.Table.Terrain[0].Id));   // the terrain card shows it instead
    }

    [Fact]
    public void Team_of_a_unit_comes_from_its_participant() => Assert.Equal(2, SheetFocus.TeamOf(Activating(null), Game, Red.Id)?.Ordinal);
}
