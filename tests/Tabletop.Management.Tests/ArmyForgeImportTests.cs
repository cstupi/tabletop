using System.Text.Json;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

public class ArmyForgeImportTests
{
    static AfList Fixture() => ArmyForgeClient.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json")));
    static readonly Dictionary<string, PropertyLevel> Catalog = new() { ["Tough"] = PropertyLevel.Model, ["Fast"] = PropertyLevel.Unit, ["Impact"] = PropertyLevel.Unit };

    [Fact]
    public void Parses_the_real_export()
    {
        var list = Fixture();
        Assert.Equal(("Wormhole Daemons of War", "gf", 7), (list.Name, list.GameSystem, list.Units.Count));
        Assert.Equal("60x35", list.Units.Single(u => u.Name == "Beast Riders").Bases!.Round);
    }

    [Fact]
    public void Combined_pairs_merge_and_everything_else_is_its_own_unit()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py", Catalog);
        Assert.Equal(("grimdark-future", "Wormhole Daemons of War", 5), (army.GameSystem, army.Name, army.Units.Count));
        var warriors = army.Units.Where(u => u.Name == "Blood Warriors").ToList();
        Assert.Equal(2, warriors.Count);
        Assert.All(warriors, u => { Assert.Equal(10, u.Models.Count); Assert.All(u.Models, m => Assert.Equal(["Great Weapon"], m.Weapons.Select(w => w.Name))); });
        Assert.Equal(Enumerable.Range(1, 5), army.Units.Select(u => u.Ordinal));
    }

    [Fact]
    public void Stats_properties_and_bases_land_where_the_catalog_says()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "x", Catalog);
        var riders = army.Units.Single(u => u.Name == "Beast Riders");
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "3+" }, riders.Stats);
        Assert.Equal(["Fast", "Impact", "Warbound"], riders.Properties.Select(p => p.Name));
        Assert.Equal(2, riders.Properties.Single(p => p.Name == "Impact").Parameter);
        Assert.All(riders.Models, m =>
        {
            Assert.Equal([new Tabletop.Content.PropertyInstance("Tough", 3)], m.Properties);
            Assert.Equal(Base.Oval(60 / 25.4, 35 / 25.4), m.Base);
            Assert.Equal(1.5 * 60 / 25.4, m.Height);                                            // scaled off the base's long axis, not a constant
            Assert.Null(m.AssetId);
        });
        var claws = riders.Models[0].Weapons.Single(w => w.Name == "Heavy Claws");
        Assert.Equal(new Dictionary<string, string> { ["Attacks"] = "1" }, claws.Stats);        // melee: no Range
        Assert.Equal([new Tabletop.Content.PropertyInstance("AP", 1)], claws.Properties);
        var hounds = army.Units.First(u => u.Name == "Blood Hounds");
        Assert.Contains(hounds.Properties, p => p.Name == "Fast");                            // item content, count == size → unit level
        Assert.Contains(hounds.Properties, p => p is { Name: "Ravage", Parameter: 1 });
    }

    [Fact]
    public void Partial_weapons_fill_from_a_cursor_and_ranged_weapons_carry_range()
    {
        var json = """
          {"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"a","name":"Squad","size":5,"quality":"4+","defense":"5+","rules":[],"combined":false,"joinToUnit":null,
          "bases":{"round":"25","square":"20"},"loadout":[
          {"name":"Rifle","type":"ArmyBookWeapon","count":4,"range":24,"attacks":1,"specialRules":[]},
          {"name":"Flamer","type":"ArmyBookWeapon","count":1,"range":12,"attacks":1,"specialRules":[{"name":"Blast","rating":3}]},
          {"name":"CCW","type":"ArmyBookWeapon","count":5,"range":0,"attacks":1,"specialRules":[]},
          {"name":"Shield","type":"ArmyBookItem","count":2,"content":[{"name":"Shielded","type":"ArmyBookRule"}]}]}]}
          """;
        var army = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", new Dictionary<string, PropertyLevel>());
        var m = army.Units[0].Models;
        Assert.Equal(["Rifle", "CCW"], m[0].Weapons.Select(w => w.Name));
        Assert.Equal(["Flamer", "CCW"], m[4].Weapons.Select(w => w.Name));
        Assert.Equal("24\"", m[0].Weapons[0].Stats["Range"]);
        Assert.Equal([new Tabletop.Content.PropertyInstance("Blast", 3)], m[4].Weapons[0].Properties);
        Assert.Equal(2, m.Count(x => x.Properties.Any(p => p.Name == "Shielded")));
        Assert.Equal("4+", army.Units[0].Stats["Quality"]);
        Assert.Equal(Base.Circle(25 / 25.4), m[0].Base);
    }

    [Fact]
    public void A_joined_hero_becomes_a_model_of_the_unit_it_joined()
    {
        var json = """
          {"id":"t","name":"T","gameSystem":"gf","units":[
          {"selectionId":"sq","name":"Squad","size":5,"quality":4,"defense":5,"rules":[{"name":"Fearless"}],"combined":false,"joinToUnit":null,
           "bases":{"round":"25","square":"20"},"loadout":[{"name":"Rifle","type":"ArmyBookWeapon","count":5,"range":24,"attacks":1,"specialRules":[]}]},
          {"selectionId":"champ","name":"Champion","size":1,"quality":3,"defense":4,"combined":false,"joinToUnit":"sq",
           "rules":[{"name":"Hero"},{"name":"Tough","rating":3},{"name":"Fast"}],"bases":{"round":"40","square":"40"},
           "loadout":[{"name":"Great Weapon","type":"ArmyBookWeapon","count":1,"range":0,"attacks":3,"specialRules":[{"name":"AP","rating":2}]}]}]}
          """;
        var unit = Assert.Single(ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", Catalog).Units);
        Assert.Equal(("Squad", 6), (unit.Name, unit.Models.Count));
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" }, unit.Stats);         // the squad's, not the hero's
        Assert.Equal(["Fearless"], unit.Properties.Select(p => p.Name));
        Assert.Equal(Enumerable.Range(1, 6), unit.Models.Select(m => m.Ordinal));
        Assert.Equal(["Squad 1", "Squad 2", "Squad 3", "Squad 4", "Squad 5", "Champion 1"], unit.Models.Select(m => m.Name));
        var hero = unit.Models[5];
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "4+" }, hero.Stats);        // its own, on its slot
        Assert.Empty(unit.Models[0].Stats);
        Assert.Equal(["Hero", "Tough", "Fast"], hero.Properties.Select(p => p.Name));                               // Fast is `unit` in Catalog and still lands here
        Assert.Equal(3, hero.Properties.Single(p => p.Name == "Tough").Parameter);
        Assert.Empty(unit.Models[0].Properties);
        Assert.Equal((Base.Circle(40 / 25.4), 1.5 * 40 / 25.4), (hero.Base, hero.Height));                           // base and height from the hero's own entry
        Assert.Equal((Base.Circle(25 / 25.4), 1.5 * 25 / 25.4), (unit.Models[0].Base, unit.Models[0].Height));
        Assert.Equal(["Great Weapon"], hero.Weapons.Select(w => w.Name));                                           // a cursor per section
        Assert.Equal(["Rifle"], unit.Models[0].Weapons.Select(w => w.Name));
        Assert.False(hero.Weapons[0].Stats.ContainsKey("Range"));
    }

    [Fact]
    public void A_join_resolves_through_a_combined_pair_and_an_unresolvable_join_stays_its_own_unit()
    {
        var json = """
          {"id":"t","name":"T","gameSystem":"gf","units":[
          {"selectionId":"a","name":"A","size":2,"quality":4,"defense":4,"rules":[],"combined":true,"joinToUnit":null,"bases":{"round":"25","square":"20"},"loadout":[]},
          {"selectionId":"b","name":"A","size":2,"quality":4,"defense":4,"rules":[],"combined":true,"joinToUnit":"a","bases":{"round":"25","square":"20"},"loadout":[]},
          {"selectionId":"h1","name":"H1","size":1,"quality":3,"defense":3,"rules":[],"combined":false,"joinToUnit":"b","bases":{"round":"40","square":"40"},"loadout":[]},
          {"selectionId":"h2","name":"H2","size":1,"quality":3,"defense":3,"rules":[],"combined":false,"joinToUnit":"gone","bases":{"round":"40","square":"40"},"loadout":[]},
          {"selectionId":"h3","name":"H3","size":1,"quality":3,"defense":3,"rules":[],"combined":false,"joinToUnit":"h2","bases":{"round":"40","square":"40"},"loadout":[]}]}
          """;
        var army = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", Catalog);
        Assert.Equal(["A", "H2", "H3"], army.Units.Select(u => u.Name));                        // nothing answers "gone", and h3 names an entry that itself joins
        Assert.Equal(Enumerable.Range(1, 3), army.Units.Select(u => u.Ordinal));
        Assert.Equal(["A 1", "A 2", "A 3", "A 4", "H1 1"], army.Units[0].Models.Select(m => m.Name));   // h1 named the absorbed partner
        Assert.Equal("4+", army.Units[0].Stats["Quality"]);
        Assert.Equal("3+", army.Units[0].Models[4].Stats["Quality"]);
        Assert.All(army.Units.Skip(1), u => Assert.Single(u.Models));
    }

    [Fact]
    public void Other_game_systems_are_refused_and_ids_are_read_from_share_links()
    {
        Assert.Equal("grimdark-future", ArmyForgeImport.ToArmy(Fixture() with { GameSystem = "aof" }, "x", Catalog).GameSystem);   // one rules implementation, two list kinds
        Assert.Throws<UnsupportedGameSystemException>(() => ArmyForgeImport.ToArmy(Fixture() with { GameSystem = "aofs" }, "x", Catalog));
        Assert.Equal("c2-6YsWRH3py", ArmyForgeClient.ListId("https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py&name=Wormhole"));
        Assert.Equal("c2-6YsWRH3py", ArmyForgeClient.ListId("c2-6YsWRH3py"));
        Assert.Null(ArmyForgeClient.ListId("https://example.com/nothing"));
    }

    [Fact]
    public void Every_slot_carries_the_army_book_its_entry_came_from()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "c2-6YsWRH3py", Catalog);
        Assert.All(army.Units.SelectMany(u => u.Models), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
    }

    [Fact]
    public void A_joined_entry_keeps_its_own_army_book()
    {
        var list = Fixture();
        var host = list.Units.First(u => !u.Combined && u.JoinToUnit is null);
        var hero = host with { SelectionId = "hero", Name = "Visiting Hero", Size = 1, JoinToUnit = host.SelectionId, ArmyId = "another-book" };
        var army = ArmyForgeImport.ToArmy(list with { Units = [.. list.Units, hero] }, "c2-6YsWRH3py", Catalog);
        var unit = army.Units.Single(u => u.Models.Any(m => m.Name == "Visiting Hero 1"));
        Assert.Equal("another-book", unit.Models.Single(m => m.Name == "Visiting Hero 1").ArmyBook);
        Assert.All(unit.Models.Where(m => m.Name != "Visiting Hero 1"), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
    }
}
