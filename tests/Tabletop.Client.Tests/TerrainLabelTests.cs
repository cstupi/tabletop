using Tabletop.Client.Core; using Tabletop.Content; using Tabletop.Geometry;
using static Tabletop.Geometry.TerrainDeclarations;
namespace Tabletop.Client.Tests;

public class TerrainLabelTests
{
    static TerrainVolume V(double bottom, double top, TerrainDeclarations d) =>
        new(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), bottom, top, d, null));

    [Fact]
    public void A_builtin_piece_reads_its_label_then_each_volume_s_declarations_and_heights()
    {
        var ruin = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruin"), new Point3(0, 0, 0), new Yaw(0), 1, [V(0, 4, BlocksLineOfSight | SeeInAndOut | Difficult | TerrainDeclarations.Cover)]);
        Assert.Equal("Ruin · Cover, Difficult, Blocks LOS, See in/out · 0–4\"", TerrainLabel.Of(ruin));
    }

    [Fact]
    public void Book_ruins_read_cover_and_dangerous_on_rush_charge()
    {
        var ruins = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(0, 0, 0), new Yaw(0), 1, [V(0, 4, TerrainDeclarations.Cover | DangerousOnRushCharge)]);
        Assert.Equal("Ruins or Debris · Cover, Dangerous on Rush/Charge · 0–4\"", TerrainLabel.Of(ruins));
    }

    [Fact]
    public void The_label_uses_the_words_the_server_gave_and_its_own_for_a_name_not_given()
    {
        var words = new Dictionary<string, string> { ["see_in_and_out"] = "Dense", ["steep"] = "Steep", ["dangerous_on_rush_charge"] = "Unstable" };
        var ruin = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruin"), new Point3(0, 0, 0), new Yaw(0), 1, [V(0, 4, BlocksLineOfSight | SeeInAndOut | DangerousOnRushCharge | Steep)]);
        Assert.Equal("Ruin · Unstable, Steep, Blocks LOS, Dense · 0–4\"", TerrainLabel.Of(ruin, words));
        Assert.Equal("Ruin · Dangerous on Rush/Charge, Steep, Blocks LOS, See in/out · 0–4\"", TerrainLabel.Of(ruin));
        Assert.Equal(["Unstable", "Steep", "Blocks LOS", "Dense"], TerrainLabel.FlagsOf(ruin.Volumes[0].Volume, words));
        Assert.Contains((Steep, "Steep"), TerrainLabel.DeclarationsIn(words));
    }

    [Fact]
    public void The_setup_list_reads_the_words_given()
    {
        var words = new Dictionary<string, string> { ["see_in_and_out"] = "Dense", ["blocks_line_of_sight"] = "Sight blocking" };
        var forest = BuiltinContent.Packed.Terrain["large-forest"];
        Assert.Equal("Cover, Difficult, Sight blocking, Dense", TerrainGroups.Summary(forest, words));
        Assert.True(TerrainGroups.Matches(forest, "dense", words));
        Assert.False(TerrainGroups.Matches(forest, "dense"));
    }

    [Fact]
    public void Scale_and_elevation_raise_the_heights_and_each_volume_gets_its_own_line()
    {
        var piece = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(Guid.CreateVersion7()), new Point3(0, 0, 1), new Yaw(0), 0.5, [V(0, 3, Impassable), V(3, 5, None)]);
        Assert.Equal("Terrain · Impassable · 1–2.5\"\nTerrain · Open · 2.5–3.5\"", TerrainLabel.Of(piece));
    }
}
