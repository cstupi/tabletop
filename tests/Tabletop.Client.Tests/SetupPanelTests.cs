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
    public void The_terrain_cards_folds_start_closed_and_keep_what_the_player_chose()
    {
        var folds = new SetupFolds();
        Assert.False(folds.IsOpen(SetupFold.PieceMesh, GameStatus.Setup));
        Assert.False(folds.IsOpen(SetupFold.PieceVolumes, GameStatus.Setup));
        folds.Set(SetupFold.PieceVolumes, true, GameStatus.Setup);
        Assert.True(folds.IsOpen(SetupFold.PieceVolumes, GameStatus.Setup));
        Assert.False(folds.IsOpen(SetupFold.PieceVolumes, GameStatus.Playing));
        Assert.Equal("Mesh", SetupFolds.Title(SetupFold.PieceMesh));
        Assert.Equal("Volume", SetupFolds.PieceVolumesTitle(1));
        Assert.Equal("Volumes 3", SetupFolds.PieceVolumesTitle(3));
    }
}
