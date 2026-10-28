using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-10-27-godot-setup-screen-design.md: the terrain list, the sections and the head.</summary>
public class SetupPanelTests
{
    static readonly IReadOnlyCollection<BuiltinTerrain> Catalog = BuiltinContent.Packed.Terrain.Values.ToList();

    [Fact]
    public void The_terrain_list_groups_every_piece_once_in_the_order_a_table_is_balanced()
    {
        var groups = TerrainGroups.Of(Catalog);
        Assert.Equal(["Blocks line of sight", "Impassable", "Dangerous", "Cover", "Difficult", TerrainGroups.Open], groups.Select(g => g.Title));
        Assert.Equal(Catalog.Count, groups.Sum(g => g.Entries.Count));
        string TitleOf(string name) => groups.First(g => g.Entries.Any(e => e.Name == name)).Title;
        Assert.Equal("Blocks line of sight", TitleOf("large-forest"));
        Assert.Equal("Impassable", TitleOf("large-lake"));
        Assert.Equal("Dangerous", TitleOf("mine-field"));
        Assert.Equal("Dangerous", TitleOf("quicksand"));
        Assert.Equal("Cover", TitleOf("hill"));
        Assert.Equal("Difficult", TitleOf("shallow-river"));
        Assert.Equal(TerrainGroups.Open, TitleOf("hill-small"));
        foreach (var g in groups) Assert.Equal(g.Entries.Select(e => e.Label).Order(StringComparer.CurrentCulture), g.Entries.Select(e => e.Label));
    }

    [Fact]
    public void The_search_matches_word_starts_of_the_name_and_what_the_piece_does()
    {
        Assert.Equal(["Forest", "Large Forest"], TerrainGroups.Of(Catalog, "forest").SelectMany(g => g.Entries).Select(e => e.Label));
        Assert.Equal(["large-forest"], TerrainGroups.Of(Catalog, "LARGE for").SelectMany(g => g.Entries).Select(e => e.Name));
        Assert.Empty(TerrainGroups.Of(Catalog, "zzz"));
        var barricade = BuiltinContent.Packed.Terrain["barricade"];
        Assert.True(TerrainGroups.Matches(barricade, "diff"));
        Assert.False(TerrainGroups.Matches(barricade, "arricade"));
        Assert.Equal(Catalog.Count, TerrainGroups.Of(Catalog, "   ").Sum(g => g.Entries.Count));
    }

    [Fact]
    public void A_row_says_what_the_piece_does_in_the_browsers_words()
    {
        Assert.Equal("Cover, Difficult, Blocks line of sight, See in and out", TerrainGroups.Summary(BuiltinContent.Packed.Terrain["large-forest"]));
        Assert.Equal("Open", TerrainGroups.Summary(BuiltinContent.Packed.Terrain["hill-small"]));
    }

    [Fact]
    public void Setup_opens_only_terrain_and_the_lobby_opens_players_and_game_settings()
    {
        var folds = new SetupFolds();
        Assert.Equal([SetupFold.Terrain, SetupFold.Objectives, SetupFold.Table, SetupFold.Zones, SetupFold.Layouts, SetupFold.Game, SetupFold.Players],
            SetupFolds.Order(GameStatus.Setup));
        Assert.Equal([SetupFold.Players, SetupFold.Game], SetupFolds.Order(GameStatus.Lobby));
        Assert.Equal([SetupFold.Terrain], SetupFolds.Order(GameStatus.Setup).Where(f => folds.IsOpen(f, GameStatus.Setup)));
        Assert.Equal([SetupFold.Players, SetupFold.Game], SetupFolds.Order(GameStatus.Lobby).Where(f => folds.IsOpen(f, GameStatus.Lobby)));
        Assert.False(folds.IsOpen(SetupFold.PieceMesh, GameStatus.Setup));
        Assert.False(folds.IsOpen(SetupFold.PieceVolumes, GameStatus.Setup));
    }

    [Fact]
    public void A_section_keeps_what_the_player_chose_until_the_screen_goes()
    {
        var folds = new SetupFolds();
        folds.Set(SetupFold.Objectives, true, GameStatus.Setup);
        folds.Set(SetupFold.Terrain, false, GameStatus.Setup);
        folds.Set(SetupFold.PieceVolumes, true, GameStatus.Setup);
        Assert.True(folds.IsOpen(SetupFold.Objectives, GameStatus.Setup));
        Assert.False(folds.IsOpen(SetupFold.Terrain, GameStatus.Setup));
        Assert.True(folds.IsOpen(SetupFold.PieceVolumes, GameStatus.Setup));
        // The lobby's choices are its own: Game settings closed in the lobby stays closed by default in setup and open in the lobby again only if chosen.
        folds.Set(SetupFold.Game, false, GameStatus.Lobby);
        Assert.False(folds.IsOpen(SetupFold.Game, GameStatus.Lobby));
        Assert.False(folds.IsOpen(SetupFold.Game, GameStatus.Setup));
        folds.Set(SetupFold.Game, true, GameStatus.Setup);
        Assert.False(folds.IsOpen(SetupFold.Game, GameStatus.Lobby));
    }

    [Fact]
    public void Headings_carry_their_counts()
    {
        var table = Tabletop.Content.Tests.TableStateTests.Sample() with { Texture = VisualRef.OfBuiltin("grass") };
        var game = Samples.Game with { Participants = [new Participant(Guid.NewGuid(), Guid.NewGuid(), "Clay", null, null), new Participant(Guid.NewGuid(), Guid.NewGuid(), "Ana", null, null)] };
        Assert.Equal($"Terrain {table.Terrain.Count}/64", SetupFolds.Title(SetupFold.Terrain, table, game));
        Assert.Equal($"Objectives {table.Markers.Count}/32", SetupFolds.Title(SetupFold.Objectives, table, game));
        Assert.Equal($"Table {table.Size.Width} × {table.Size.Length}\" · Grass", SetupFolds.Title(SetupFold.Table, table, game));
        Assert.Equal("Deployment zones", SetupFolds.Title(SetupFold.Zones, table, game));
        Assert.Equal("Layouts", SetupFolds.Title(SetupFold.Layouts, table, game));
        Assert.Equal("Game settings", SetupFolds.Title(SetupFold.Game, table, game));
        Assert.Equal("Players 2", SetupFolds.Title(SetupFold.Players, table, game));
        Assert.Equal("Terrain", SetupFolds.Title(SetupFold.Terrain, null, null));
        Assert.Equal("Table", SetupFolds.Title(SetupFold.Table, null, null));
        Assert.Equal("Mesh", SetupFolds.Title(SetupFold.PieceMesh, table, game));
        Assert.Equal("Volume", SetupFolds.PieceVolumesTitle(1));
        Assert.Equal("Volumes 3", SetupFolds.PieceVolumesTitle(3));
    }

    [Fact]
    public void The_head_holds_the_one_main_action_or_what_the_player_waits_for()
    {
        var lobby = Samples.Game with { Status = GameStatus.Lobby };
        Assert.Equal(new SetupHeadView("Lobby", "Start setup", new StartSetup(), null), SetupHead.Of(lobby, isCreator: true, null));
        Assert.Equal(new SetupHeadView("Lobby", null, null, "Waiting for the creator to start setup."), SetupHead.Of(lobby, isCreator: false, null));
        var setup = lobby with { Status = GameStatus.Setup };
        Assert.Equal(new SetupHeadView("Setup", "End setup", new EndSetup(), null), SetupHead.Of(setup, isCreator: true, null));
        Assert.Equal(new SetupHeadView("Setup", null, null, "Anyone may edit the table. Waiting for the creator to end setup."), SetupHead.Of(setup, isCreator: false, null));
        Assert.Null(SetupHead.Of(lobby with { Status = GameStatus.Playing }, isCreator: true, null));
    }

    [Theory]
    [InlineData(720, 160)]
    [InlineData(1080, 520)]
    [InlineData(400, 160)]
    [InlineData(2160, 640)]
    public void The_terrain_list_fills_what_the_window_leaves_the_headings(double window, double list) =>
        Assert.Equal(list, SetupLayout.TerrainListHeight(window));
}
