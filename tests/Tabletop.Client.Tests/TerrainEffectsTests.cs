using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using static Tabletop.Geometry.TerrainDeclarations;
namespace Tabletop.Client.Tests;

public class TerrainEffectsTests
{
    [Fact]
    public void Every_declaration_has_one_effect_with_its_own_icon_and_colour()
    {
        Assert.Equal(BuiltinTerrain.DeclarationNames.Values.Order(), TerrainEffects.All.Select(e => e.Flag).Order());
        Assert.Equal(TerrainEffects.All.Count, TerrainEffects.All.Select(e => e.Colour).Distinct().Count());
        Assert.Equal(TerrainEffects.All.Count, TerrainEffects.All.Select(e => e.Icon).Distinct().Count());
        Assert.All(TerrainEffects.All, e => Assert.Equal(e.Flag, BuiltinTerrain.DeclarationNames[e.Name]));
        Assert.DoesNotContain(TerrainEffects.Open.Colour, TerrainEffects.All.Select(e => e.Colour));
    }

    [Fact]
    public void Rows_list_a_volume_s_effects_in_the_label_s_order_with_the_server_s_word_and_rule()
    {
        var words = new Dictionary<string, string> { ["see_in_and_out"] = "Dense" };
        var rules = new Dictionary<string, string> { ["cover"] = "+1 to Defense rolls against shooting." };
        var rows = TerrainEffects.Rows(SeeInAndOut | TerrainDeclarations.Cover | Difficult, words, rules);
        Assert.Equal(["Cover", "Difficult", "Dense"], rows.Select(r => r.Word));
        Assert.Equal(["cover", "difficult", "see-in-out"], rows.Select(r => r.Icon));
        Assert.Equal("+1 to Defense rolls against shooting.", rows[0].Rule);
        Assert.Null(rows[1].Rule);
        Assert.Equal(TerrainEffects.Of(Difficult).Colour, rows[1].Colour);
    }

    [Fact]
    public void A_volume_with_no_declaration_reads_open_ground()
    {
        var open = Assert.Single(TerrainEffects.Rows(None, null, null));
        Assert.Equal(("Open", "open-ground"), (open.Word, open.Icon));
    }

    [Fact]
    public void The_table_tints_a_volume_by_its_strongest_effect()
    {
        Assert.Equal(TerrainEffects.Of(Impassable), TerrainEffects.Primary(Impassable | BlocksLineOfSight));
        Assert.Equal(TerrainEffects.Of(TerrainDeclarations.Cover), TerrainEffects.Primary(TerrainDeclarations.Cover | Difficult | SeeInAndOut));
        Assert.Equal(TerrainEffects.Of(Dangerous), TerrainEffects.Primary(Dangerous | TerrainDeclarations.Cover));
        Assert.Equal(TerrainEffects.Open, TerrainEffects.Primary(None));
    }
}
