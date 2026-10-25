using Tabletop.Content;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class MenusTests
{
    static readonly Guid M0 = TableUx.ModelOf(TableUx.Squad), M1 = TableUx.ModelOf(TableUx.Squad, 1);
    static readonly IReadOnlyList<GameEvent> Log = TableUx.Events(TableUx.Deploy);
    static readonly Projection P = TableUx.After(TableUx.Deploy);
    static readonly IReadOnlyList<string> Known = MenuModel.KnownActionNames(P, Log);

    static MenuContext Ctx(bool watching = false, AskView? mine = null, MarkerLocks? locks = null) =>
        new(TableUx.Game, P, Log, watching, TableUx.T1, mine, [], new Dictionary<Guid, IReadOnlyList<Guid>>(), locks ?? new MarkerLocks());
    static IEnumerable<string> Labels(IEnumerable<MenuItem> items) => items.Select(i => i.Label);

    [Fact]
    public void A_watcher_gets_no_menu()
    {
        var ctx = Ctx(watching: true);
        var marker = TableUx.Board.Table.Markers[0].Id;
        Assert.Empty(Menus.Piece(ctx, M0, false)); Assert.Empty(Menus.Piece(ctx, M0, true)); Assert.Empty(Menus.Piece(ctx, marker, false));
        Assert.Empty(Menus.Team(ctx, TableUx.T1)); Assert.Empty(Menus.Roster(ctx, TableUx.Squad.Id)); Assert.Empty(Menus.Table(ctx, new Point3(5, 6, 0)));
    }

    [Fact]
    public void A_model_menu_is_the_models_items_then_the_frames()
        => Assert.Equal(Labels(MenuModel.ForModel(P, M0, Known).Concat(MenuModel.ForFrame(P))), Labels(Menus.Piece(Ctx(), M0, shift: false)));

    [Fact]
    public void Shift_gives_the_units_items()
        => Assert.Equal(Labels(MenuModel.ForUnit(P, TableUx.Squad.Id, Known, [])), Labels(Menus.Piece(Ctx(), M0, shift: true)));

    [Fact]
    public void Boarding_leads_a_model_menu_during_its_move()
    {
        var legal = new MoveRegion([M1], 6, [], [], null, new MoveContainment([M0], null, null));
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Deploy, "Move", [TableUx.T1], legal, false, false, null, null);
        var mine = new AskView(ask, AskMode.Move, [], [], false);
        var items = Menus.Piece(Ctx(mine: mine), M1, shift: false);
        Assert.StartsWith("Board transport: ", items[0].Label);
        Assert.Equal(Labels(MenuModel.ForBoarding(P, ask, M1).Concat(MenuModel.ForModel(P, M1, Known)).Concat(MenuModel.ForFrame(P))), Labels(items));
    }

    [Fact]
    public void A_marker_menu_is_the_markers_items()
    {
        var locks = new MarkerLocks(); var id = TableUx.Board.Table.Markers[0].Id;
        var items = Menus.Piece(Ctx(locks: locks), id, shift: false);
        Assert.NotEmpty(items);
        Assert.Equal(Labels(MenuModel.ForMarker(TableUx.Game, P, id, locks)), Labels(items));
    }

    [Fact]
    public void The_team_menu_is_the_teams_items_then_its_models_off_the_table()
        => Assert.Equal(Labels(MenuModel.ForTeam(TableUx.Game, P, TableUx.T1).Concat(MenuModel.OffTable(P, TableUx.T1, new Names(TableUx.Game, P)))), Labels(Menus.Team(Ctx(), TableUx.T1)));

    [Fact]
    public void The_roster_menu_is_the_rosters_items_for_my_team()
        => Assert.Equal(Labels(MenuModel.ForRosterUnit(TableUx.Game, P, TableUx.Reserve.Id, TableUx.T1, new Dictionary<Guid, IReadOnlyList<Guid>>())), Labels(Menus.Roster(Ctx(), TableUx.Reserve.Id)));

    [Fact]
    public void The_table_menu_drops_a_token_there()
    {
        var item = Assert.Single(Menus.Table(Ctx(), new Point3(5, 6, 0)));
        var place = Assert.IsType<PlaceMarker>(item.Build("smoke"));
        Assert.Equal((MarkerKind.Token, new Point3(5, 6, 0), "smoke", "chip", 1.0), (place.Marker.Kind, place.Marker.Position, place.Marker.Label, place.Marker.Visual.Builtin, place.Marker.Diameter));
        Assert.Equal(new Yaw(0), place.Marker.Yaw);
    }
}
