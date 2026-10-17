using System.Text.Json.Nodes;

namespace Tabletop.ArmyBooks.Tests;

public class ReserveUnitsTests
{
    const string Book = """
        {"units":[
          {"name":"Hive Lord","size":1,"quality":3,"defense":3,"rules":[{"name":"Hero"}],"weapons":[],
           "items":[{"name":"Spore Carrier","type":"ArmyBookItem","count":1,"content":[{"name":"Spawn","type":"ArmyBookRule","rating":"Spores [5]"}]}]},
          {"name":"Spores","size":5,"quality":5,"defense":6,"bases":{"round":"32","square":"30"},"rules":[{"id":"x","name":"Tough","rating":3,"label":"Tough(3)"}],
           "weapons":[{"name":" Claws","count":5,"range":0,"attacks":1,"specialRules":[{"name":"AP","rating":1,"label":"AP(1)"}]}],"items":[]}],
         "upgradePackages":[{"sections":[{"options":[{"gains":[{"name":"Spawn","type":"ArmyBookRule","rating":"Spores [5]"}]}]}]}]}
        """;

    [Fact]
    public void A_rating_reads_as_a_unit_name_and_size()
    {
        Assert.Equal(("Bot Swarms", 3), ReserveUnits.Parse("Bot Swarms [3]"));
        Assert.Equal(("Lesser Change Horrors", 5), ReserveUnits.Parse("Lesser Change Horrors [5]"));
        Assert.Null(ReserveUnits.Parse("3"));
    }

    [Fact]
    public void Each_named_unit_is_written_once_in_the_export_shape()
    {
        var (json, problems) = ReserveUnits.Build([("b1", "Hives", Book)]);
        Assert.Empty(problems);
        var spores = JsonNode.Parse(json)!["b1"]!["Spores"]!;
        Assert.Equal(5, (int)spores["size"]!);
        Assert.Equal("32", (string?)spores["bases"]!["round"]);
        Assert.Equal("""[{"name":"Tough","rating":3}]""", spores["rules"]!.ToJsonString());
        Assert.Equal("""[{"name":"Claws","type":"ArmyBookWeapon","count":5,"range":0,"attacks":1,"specialRules":[{"name":"AP","rating":1}]}]""", spores["loadout"]!.ToJsonString());
        Assert.Single(JsonNode.Parse(json)!["b1"]!.AsObject());
    }

    [Fact]
    public void A_rating_naming_no_unit_of_the_book_is_a_problem()
    {
        var (_, problems) = ReserveUnits.Build([("b1", "Hives", Book.Replace("\"Spores [5]\"}]}]", "\"Ghosts [5]\"}]}]"))]);
        Assert.Contains(problems, p => p.Contains("Ghosts [5]"));
    }
}
