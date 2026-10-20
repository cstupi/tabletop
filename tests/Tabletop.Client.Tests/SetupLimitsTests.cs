using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class SetupLimitsTests
{
    static TableState Draft(int terrain, int markers) => TableStateTests.Sample() with
    {
        Terrain = Enumerable.Range(0, terrain).Select(_ => BuiltinContent.Place(BuiltinContent.Terrain.Keys.First(), new Point3(1, 1, 0), new Yaw(0))).ToList(),
        Markers = Enumerable.Range(0, markers).Select(_ => new Marker(Guid.NewGuid(), MarkerKind.Objective, new Point3(1, 1, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"))).ToList(),
    };

    [Fact]
    public void Section_headings_count_against_the_server_s_caps()
    {
        var d = Draft(12, 3);
        Assert.Equal(("Terrain (12/64)", "Markers (3/32)"), (SetupLimits.TerrainHeading(d), SetupLimits.MarkersHeading(d)));
        Assert.Equal(("Terrain", "Markers"), (SetupLimits.TerrainHeading(null), SetupLimits.MarkersHeading(null)));
    }

    [Fact]
    public void Clearing_terrain_needs_a_piece_to_clear()
    {
        Assert.Null(SetupLimits.CannotClearTerrain(Draft(1, 0)));
        Assert.Equal("There is no terrain to clear.", SetupLimits.CannotClearTerrain(Draft(0, 3)));
        Assert.Equal("The table has not arrived yet.", SetupLimits.CannotClearTerrain(null));
    }

    [Fact]
    public void Adding_past_a_cap_says_so_before_the_server_refuses()
    {
        Assert.Null(SetupLimits.CannotAddTerrain(Draft(63, 0)));
        Assert.Equal("The table holds at most 64 terrain pieces: remove some first.", SetupLimits.CannotAddTerrain(Draft(64, 0)));
        Assert.Equal("These 21 pieces would take the table past 64 terrain pieces (it has 50): remove some first.", SetupLimits.CannotAddTerrain(Draft(50, 0), 21));
        Assert.Null(SetupLimits.CannotAddMarker(Draft(0, 31)));
        Assert.Equal("The table holds at most 32 markers: remove some first.", SetupLimits.CannotAddMarker(Draft(0, 32)));
        Assert.Equal("The table has not arrived yet.", SetupLimits.CannotAddTerrain(null));
    }

    [Fact]
    public void The_table_size_hint_names_the_range() => Assert.Equal("Width × length, 12–144\" a side", SetupLimits.SizeHint);

    [Theory]
    [InlineData("", "A name is required.")]
    [InlineData("   ", "A name is required.")]
    [InlineData("Clay", null)]
    [InlineData("a‮b", "A name contains a control or bidirectional formatting character.")]
    [InlineData("a\tb", "A name contains a control or bidirectional formatting character.")]
    public void A_name_is_checked_as_management_checks_it(string name, string? problem) => Assert.Equal(problem, TextLimits.NameProblem(name));

    [Fact]
    public void A_long_name_names_the_limit()
    {
        Assert.Null(TextLimits.NameProblem(new string('x', 64)));
        Assert.Equal("A name is at most 64 characters.", TextLimits.NameProblem(new string('x', 65)));
        Assert.Equal("A layout name is at most 64 characters.", TextLimits.NameProblem(new string('x', 65), "A layout name"));
    }

    [Theory]
    [InlineData("Player-1a2b", true)]
    [InlineData("Player-ffff", true)]
    [InlineData("Player-1a2", false)]
    [InlineData("Clay", false)]
    public void The_default_account_name_is_recognised(string name, bool isDefault) => Assert.Equal(isDefault, TextLimits.IsDefaultName(name));
}
