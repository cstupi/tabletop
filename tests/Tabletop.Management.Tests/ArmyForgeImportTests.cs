using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

public class ArmyForgeImportTests
{
    static AfList Fixture() => ArmyForgeClient.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json")));
    static readonly Dictionary<string, PropertyLevel> Catalog = new() { ["Tough"] = PropertyLevel.Model, ["Fast"] = PropertyLevel.Unit, ["Impact"] = PropertyLevel.Unit };

    [Fact]
    public void Import_stamps_the_edition_army_forge_serves() => Assert.Equal(ArmyEditions.Served, ArmyForgeImport.ToArmy(Fixture(), "src", Catalog).Edition);

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

    /// <summary>p.13: instances of the same rule don't stack unless it has (X). Army Forge's Voidys list (67f1yXBTN5XA): a Reaper
    /// Champion with Tough(3) on a Flying Beast, whose upgrade gives Tough(12), is Tough(15).</summary>
    [Fact]
    public void A_repeated_rule_is_one_property_whose_ratings_add()
    {
        var hero = Entry(name: "Reaper Champion", size: 1, rules: """[{"name":"Hero"},{"name":"Tough","rating":3},{"name":"Fast"}]""",
            loadout: """[{"name":"Flying Beast","type":"ArmyBookItem","count":1,"content":[{"name":"Tough","rating":12},{"name":"Fear","rating":2},{"name":"Fast"}]},""" +
                     """{"name":"Banner","type":"ArmyBookItem","count":1,"content":[{"name":"Fear","rating":1}]},""" +
                     Weapon("Claws", count: 1, range: "0", rules: """[{"name":"AP","rating":1},{"name":"AP","rating":1}]""") + "]");
        var army = ArmyForgeImport.ToArmy(ListOf(hero), "x", Catalog);
        var unit = army.Units.Single();
        var slot = unit.Models.Single();
        Assert.Equal([new PropertyInstance("Tough", 15)], slot.Properties);
        Assert.Equal([new PropertyInstance("Hero", null), new PropertyInstance("Fast", null), new PropertyInstance("Fear", 3)], unit.Properties);
        Assert.Equal([new PropertyInstance("AP", 2)], slot.Weapons.Single(w => w.Name == "Claws").Properties);
    }

    /// <summary>A mount's or upgrade's content carries weapons beside rules (Voidys: the Flying Beast's Stomp and Toxin Claws). They
    /// are the carriers' weapons, never rule names, and an item's rules and weapons go to the same models.</summary>
    [Fact]
    public void Weapons_inside_an_item_are_weapons_of_the_models_that_carry_it()
    {
        var squad = Entry(size: 5, loadout: """[{"name":"Rifle","type":"ArmyBookWeapon","count":5,"range":24,"attacks":1,"specialRules":[]},""" +
            """{"name":"Beast","type":"ArmyBookItem","count":2,"content":[""" +
            """{"name":"Stomp","type":"ArmyBookWeapon","count":1,"range":0,"attacks":4,"specialRules":[{"name":"AP","rating":1}]},""" +
            """{"name":"Fear","type":"ArmyBookRule","rating":2},""" +
            """{"name":"Guns","type":"ArmyBookWeapon","count":1,"range":15,"attacks":3,"specialRules":[]}]}]""");
        var unit = ArmyForgeImport.ToArmy(ListOf(squad), "x", new Dictionary<string, PropertyLevel> { ["Fear"] = PropertyLevel.Model }).Units.Single();
        var m = unit.Models;
        Assert.DoesNotContain(unit.Properties, p => p.Name is "Stomp" or "Guns");
        foreach (var carrier in m.Take(2))
        {
            Assert.Equal(["Rifle", "Stomp", "Guns"], carrier.Weapons.Select(w => w.Name));
            Assert.Equal([new PropertyInstance("Fear", 2)], carrier.Properties);
        }
        Assert.All(m.Skip(2), x => { Assert.Equal(["Rifle"], x.Weapons.Select(w => w.Name)); Assert.Empty(x.Properties); });
        var stomp = m[0].Weapons[1];
        Assert.Equal(new Dictionary<string, string> { ["Attacks"] = "4" }, stomp.Stats);
        Assert.Equal([new PropertyInstance("AP", 1)], stomp.Properties);
        Assert.Equal("15\"", m[0].Weapons[2].Stats["Range"]);
    }

    /// <summary>A mount publishes its own bases (Voidys: the Reaper Champion's 32 mm, its Flying Beast's 120x92); the models that
    /// carry it stand on the mount's base, the rest on the entry's.</summary>
    [Fact]
    public void Models_carrying_an_item_with_bases_stand_on_its_base()
    {
        var squad = Entry(size: 5, bases: """{"round":"32","square":"30"}""",
            loadout: """[{"name":"Beast","type":"ArmyBookItem","count":2,"bases":{"round":"120x92","square":"100x50"},"content":[{"name":"Fast"}]},""" +
                     """{"name":"Bike","type":"ArmyBookItem","count":1,"bases":{"round":"","square":"50"},"content":[]},""" +
                     """{"name":"Banner","type":"ArmyBookItem","count":1,"content":[]}]""");
        var m = ArmyForgeImport.ToArmy(ListOf(squad), "x", Catalog).Units.Single().Models;
        var beast = Base.Oval(120 / 25.4, 92 / 25.4);
        Assert.Equal([beast, beast, Base.Rect(50 / 25.4, 50 / 25.4), Base.Circle(32 / 25.4), Base.Circle(32 / 25.4)], m.Select(x => x.Base));
        Assert.Equal(ArmyForgeImport.HeightFromBase(beast), m[0].Height, 6);
        Assert.Contains("base", Refused(ListOf(Entry(loadout: """[{"name":"Beast","type":"ArmyBookItem","count":1,"bases":{"round":"2000"},"content":[]}]"""))));
    }

    [Fact]
    public void An_item_weapon_is_bounded_like_any_weapon() =>
        Assert.Contains("attacks", Refused(ListOf(Entry(loadout: """[{"name":"Beast","type":"ArmyBookItem","count":1,"content":[{"name":"Stomp","type":"ArmyBookWeapon","count":1,"range":0,"attacks":0,"specialRules":[]}]}]"""))));

    /// <summary>The owner, 2026-11-14: "Add the amount of points to the unit details card". A unit's points are its entry's cost and each
    /// selected upgrade's cost for that entry (the option's `costs` row naming the entry's `id`, else its plain `cost`); a combined or
    /// joined entry adds its own. An export without costs has none.</summary>
    [Fact]
    public void A_unit_carries_its_points_from_the_list()
    {
        var json = """
          {"id":"t","name":"T","gameSystem":"gf","units":[
          {"selectionId":"sq","id":"SQ1","name":"Squad","size":5,"quality":4,"defense":5,"cost":100,"rules":[],"combined":false,"joinToUnit":null,
           "bases":{"round":"25","square":"20"},"loadout":[{"name":"Rifle","type":"ArmyBookWeapon","count":5,"range":24,"attacks":1,"specialRules":[]}],
           "selectedUpgrades":[{"upgrade":{"variant":"upgrade","affects":{"type":"all"},"targets":[]},
             "option":{"cost":null,"costs":[{"cost":99,"unitId":"OTHER"},{"cost":15,"unitId":"SQ1"}],"gains":[]}}]},
          {"selectionId":"champ","id":"CH1","name":"Champion","size":1,"quality":3,"defense":4,"cost":50,"combined":false,"joinToUnit":"sq",
           "rules":[{"name":"Hero"}],"bases":{"round":"40","square":"40"},
           "loadout":[{"name":"Great Weapon","type":"ArmyBookWeapon","count":1,"range":0,"attacks":3,"specialRules":[]}],
           "selectedUpgrades":[{"upgrade":{"variant":"upgrade","affects":{"type":"all"},"targets":[]},"option":{"cost":5,"gains":[]}}]},
          {"selectionId":"old","name":"Old","size":1,"quality":4,"defense":4,"combined":false,"joinToUnit":null,"rules":[],
           "bases":{"round":"25","square":"25"},"loadout":[{"name":"Rifle","type":"ArmyBookWeapon","count":1,"range":24,"attacks":1,"specialRules":[]}]}]}
          """;
        var units = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", Catalog).Units;
        Assert.Equal([("Squad", (int?)170), ("Old", null)], units.Select(u => (u.Name, u.Points)));      // 100 + 15, and the joined champion's 50 + 5
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
        var hero = host with { SelectionId = "hero", Name = "Visiting Hero", Size = 1, JoinToUnit = host.SelectionId, ArmyId = "another-book",
            Loadout = host.Loadout?.Select(l => l with { Count = 1 }).ToList() };
        var army = ArmyForgeImport.ToArmy(list with { Units = [.. list.Units, hero] }, "c2-6YsWRH3py", Catalog);
        var unit = army.Units.Single(u => u.Models.Any(m => m.Name == "Visiting Hero 1"));
        Assert.Equal("another-book", unit.Models.Single(m => m.Name == "Visiting Hero 1").ArmyBook);
        Assert.All(unit.Models.Where(m => m.Name != "Visiting Hero 1"), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
    }
    [Fact]
    public void Import_takes_the_round_base_and_heights_from_it()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "x", Catalog);
        Assert.All(army.Units.SelectMany(u => u.Models), m => Assert.Equal(ArmyForgeImport.HeightFromBase(m.Base), m.Height));
        Assert.All(army.Units.Single(u => u.Name == "Beast Riders").Models, m => Assert.Equal(Base.Oval(60 / 25.4, 35 / 25.4), m.Base));
    }

    [Fact]
    public void Height_scales_off_the_larger_dimension_of_the_base()
    {
        Assert.Equal(1.5 * 32 / 25.4, ArmyForgeImport.HeightFromBase(Base.Circle(32 / 25.4)), 9);
        Assert.Equal(1.5 * 60 / 25.4, ArmyForgeImport.HeightFromBase(Base.Oval(60 / 25.4, 35 / 25.4)), 9);
        Assert.Equal(1.5 * 100 / 25.4, ArmyForgeImport.HeightFromBase(Base.Rect(60 / 25.4, 100 / 25.4)), 9);   // wider than long: still the larger side
    }

    [Fact]
    public void A_missing_round_base_falls_back_to_square_then_the_default_hull()
    {
        var json = """
          {"id":"t","name":"T","gameSystem":"gf","units":[
          {"selectionId":"a","name":"RoundOnly","size":1,"quality":4,"defense":4,"rules":[],"combined":false,"joinToUnit":null,"bases":{"round":"32"},"loadout":[]},
          {"selectionId":"b","name":"SquareOnly","size":1,"quality":4,"defense":4,"rules":[],"combined":false,"joinToUnit":null,"bases":{"square":"100x60"},"loadout":[]},
          {"selectionId":"c","name":"Neither","size":1,"quality":4,"defense":4,"rules":[],"combined":false,"joinToUnit":null,"loadout":[]}]}
          """;
        var army = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", Catalog);
        ModelSlot M(string n) => army.Units.Single(u => u.Name == n).Models[0];
        Assert.Equal((Base.Circle(32 / 25.4), ArmyForgeImport.HeightFromBase(Base.Circle(32 / 25.4))), (M("RoundOnly").Base, M("RoundOnly").Height));
        Assert.Equal((Base.Rect(100 / 25.4, 60 / 25.4), ArmyForgeImport.HeightFromBase(Base.Rect(100 / 25.4, 60 / 25.4))), (M("SquareOnly").Base, M("SquareOnly").Height));
        Assert.Equal(ArmyForgeImport.DefaultBody, new ModelBody(M("Neither").Base, M("Neither").Height));
    }

    /// <summary>Army Forge publishes no base for a vehicle ("bases": {"round": "none", "square": "none"}: it stands on its hull); a monster on a
    /// published square base is not one, nor is a model riding a mount that publishes a base.</summary>
    [Fact]
    public void An_entry_with_no_published_base_is_a_vehicle()
    {
        var army = ArmyForgeImport.ToArmy(ListOf(
            Entry("a", "Battle Tank", 1, bases: """{"round":"none","square":"none"}"""),
            Entry("b", "Great Beast", 1, bases: """{"round":"none","square":"175x125"}"""),
            Entry("c", "Squad", 5)), "x", Catalog);
        bool Vehicle(string n) => army.Units.Single(u => u.Name == n).Models.All(m => m.Vehicle);
        Assert.True(Vehicle("Battle Tank"));
        Assert.False(army.Units.Single(u => u.Name == "Great Beast").Models.Any(m => m.Vehicle));
        Assert.False(army.Units.Single(u => u.Name == "Squad").Models.Any(m => m.Vehicle));
        var json = System.Text.Json.JsonSerializer.Serialize(army.Units.Single(u => u.Name == "Squad").Models[0]);
        Assert.DoesNotContain("ehicle", json);
        Assert.Contains("\"Vehicle\":true", System.Text.Json.JsonSerializer.Serialize(army.Units.Single(u => u.Name == "Battle Tank").Models[0]));
    }

    [Fact]
    public void ParseBase_prefers_round_and_falls_back_to_square()
    {
        Assert.Equal(Base.Circle(32 / 25.4), ArmyForgeImport.ParseBase(new AfBases("32", "30")));
        Assert.Equal(Base.Oval(120 / 25.4, 92 / 25.4), ArmyForgeImport.ParseBase(new AfBases("120x92", null)));
        Assert.Equal(Base.Rect(30 / 25.4, 30 / 25.4), ArmyForgeImport.ParseBase(new AfBases(null, "30")));
        Assert.Equal(Base.Rect(100 / 25.4, 60 / 25.4), ArmyForgeImport.ParseBase(new AfBases("", "100X60")));
        Assert.Equal(Base.Rect(30 / 25.4, 30 / 25.4), ArmyForgeImport.ParseBase(new AfBases("big", "30")));
        Assert.Null(ArmyForgeImport.ParseBase(new AfBases("", "")));
        Assert.Null(ArmyForgeImport.ParseBase(null));
    }

    static string Entry(string id = "a", string name = "Squad", int size = 5, string quality = "4", string defense = "\"4+\"", string rules = "[]", string loadout = "[]", string bases = """{"round":"25","square":"20"}""") =>
        $$"""{"selectionId":"{{id}}","name":{{JsonSerializer.Serialize(name)}},"size":{{size}},"quality":{{quality}},"defense":{{defense}},"rules":{{rules}},"combined":false,"joinToUnit":null,"bases":{{bases}},"loadout":{{loadout}}}""";
    static AfList ListOf(params string[] entries) => ListNamed("T", entries);
    static AfList ListNamed(string name, params string[] entries) => ArmyForgeClient.Parse($$"""{"id":"t","name":{{JsonSerializer.Serialize(name)}},"gameSystem":"gf","units":[{{string.Join(",", entries)}}]}""");
    static string Weapon(string name = "Rifle", int count = 5, string range = "24", string attacks = "1", string rules = "[]") =>
        $$"""{"name":{{JsonSerializer.Serialize(name)}},"type":"ArmyBookWeapon","count":{{count}},"range":{{range}},"attacks":{{attacks}},"specialRules":{{rules}}}""";
    static string Refused(AfList list, string source = "x") => Assert.Throws<InvalidArmyException>(() => ArmyForgeImport.ToArmy(list, source, Catalog)).Message;

    [Fact]
    public void Quality_and_defense_are_2_to_6_as_numbers_or_plus_strings()
    {
        var unit = ArmyForgeImport.ToArmy(ListOf(Entry(quality: "2", defense: "\"6+\"")), "x", Catalog).Units[0];
        Assert.Equal(("2+", "6+"), (unit.Stats["Quality"], unit.Stats["Defense"]));
        Assert.Equal("Unit 'Squad': quality 7 is outside 2–6.", Refused(ListOf(Entry(quality: "7"))));
        Assert.Equal("Unit 'Squad': defense 1 is outside 2–6.", Refused(ListOf(Entry(defense: "\"1+\""))));
        Assert.Equal("Unit 'Squad': quality must be a number from 2 to 6.", Refused(ListOf(Entry(quality: "\"best\""))));
        Assert.Equal("Unit 'Squad': quality must be a number from 2 to 6.", Refused(ListOf(Entry(quality: "4.5"))));
        Assert.Equal("Unit 'Squad': defense must be a number from 2 to 6.", Refused(ListOf(Entry(defense: "null"))));
    }

    [Fact]
    public void Sizes_unit_counts_and_model_totals_are_bounded()
    {
        Assert.Equal("Unit 'Squad': size 51 is outside 1–50.", Refused(ListOf(Entry(size: 51))));
        Assert.Equal("Unit 'Squad': size 0 is outside 1–50.", Refused(ListOf(Entry(size: 0))));
        Assert.Equal(50, ArmyForgeImport.ToArmy(ListOf(Entry(size: 50)), "x", Catalog).Units[0].Models.Count);
        Assert.Equal(100, ArmyForgeImport.ToArmy(ListOf(Enumerable.Range(0, 100).Select(i => Entry(id: $"u{i}", size: 1)).ToArray()), "x", Catalog).Units.Count);
        Assert.Equal("The list has 101 units; at most 100 import.", Refused(ListOf(Enumerable.Range(0, 101).Select(i => Entry(id: $"u{i}", size: 1)).ToArray())));
        Assert.Equal("The list has 510 models; at most 500 import.", Refused(ListOf(Enumerable.Range(0, 51).Select(i => Entry(id: $"u{i}", size: 10)).ToArray())));
    }

    [Fact]
    public void Weapon_attacks_counts_and_ranges_are_bounded()
    {
        var w = ArmyForgeImport.ToArmy(ListOf(Entry(loadout: $"[{Weapon(range: "120", attacks: "100", count: 20)}]")), "x", Catalog).Units[0].Models[0].Weapons;
        // A count of four per model is four copies on each model ("4x Rifle").
        Assert.Equal(4, w.Count);
        Assert.All(w, x => Assert.Equal(("120\"", "100"), (x.Stats["Range"], x.Stats["Attacks"])));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' has 101 attacks; the range is 1–100.", Refused(ListOf(Entry(loadout: $"[{Weapon(attacks: "101")}]"))));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' has 0 attacks; the range is 1–100.", Refused(ListOf(Entry(loadout: $"[{Weapon(attacks: "0")}]"))));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' count 21 is outside 0–20.", Refused(ListOf(Entry(loadout: $"[{Weapon(count: 21)}]"))));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' count -1 is outside 0–20.", Refused(ListOf(Entry(loadout: $"[{Weapon(count: -1)}]"))));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' range 121 is outside 0–120.", Refused(ListOf(Entry(loadout: $"[{Weapon(range: "121")}]"))));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' range must be a whole number of inches.", Refused(ListOf(Entry(loadout: $"[{Weapon(range: "12.5")}]"))));
        Assert.Equal("Unit 'Squad': weapon 'Rifle' range must be a whole number of inches.", Refused(ListOf(Entry(loadout: $"[{Weapon(range: "\"12\"")}]"))));
        var melee = ArmyForgeImport.ToArmy(ListOf(Entry(loadout: $"[{Weapon(name: "A", range: "null")},{Weapon(name: "B", range: "0")}]")), "x", Catalog).Units[0].Models[0].Weapons;
        Assert.All(melee, x => Assert.False(x.Stats.ContainsKey("Range")));
    }

    [Fact]
    public void Rule_ratings_are_0_to_50_wherever_a_rule_appears()
    {
        Assert.Equal(50, ArmyForgeImport.ToArmy(ListOf(Entry(rules: """[{"name":"Tough","rating":50}]""")), "x", Catalog).Units[0].Models[0].Properties[0].Parameter);
        Assert.Equal("Unit 'Squad': rule 'Tough' rating 51 is outside 0–50.", Refused(ListOf(Entry(rules: """[{"name":"Tough","rating":51}]"""))));
        Assert.Equal("Unit 'Squad': rule 'Tough' rating -1 is outside 0–50.", Refused(ListOf(Entry(rules: """[{"name":"Tough","rating":"-1"}]"""))));
        Assert.Equal("Unit 'Squad': rule 'AP' rating 99 is outside 0–50.", Refused(ListOf(Entry(loadout: $$"""[{{Weapon(rules: """[{"name":"AP","rating":99}]""")}}]"""))));
        Assert.Equal("Unit 'Squad': rule 'Shielded' rating 51 is outside 0–50.",
            Refused(ListOf(Entry(loadout: """[{"name":"Shield","type":"ArmyBookItem","count":2,"content":[{"name":"Shielded","rating":51}]}]"""))));
    }

    [Fact]
    public void Bases_and_heights_are_bounded_in_inches_after_conversion()
    {
        Assert.Equal("Unit 'Squad': base 10x10 mm is outside 0.4–12 inches.", Refused(ListOf(Entry(bases: """{"round":"10x10"}"""))));
        Assert.Equal("Unit 'Squad': base 400 mm is outside 0.4–12 inches.", Refused(ListOf(Entry(bases: """{"round":"400"}"""))));
        Assert.Equal("Unit 'Squad': base NaN mm is outside 0.4–12 inches.", Refused(ListOf(Entry(bases: """{"round":"NaN"}"""))));
        var big = ArmyForgeImport.ToArmy(ListOf(Entry(bases: """{"round":"300"}""")), "x", Catalog).Units[0].Models[0];
        Assert.Equal(Base.Circle(300 / 25.4), big.Base);
    }

    [Fact]
    public void Names_and_the_source_are_checked_text()
    {
        var name65 = new string('n', 65);
        Assert.Equal("Army name is at most 64 characters.", Refused(ListNamed(name65, Entry())));
        Assert.Equal("Army name contains a control or bidirectional formatting character.", Refused(ListNamed("A\u202eB", Entry())));
        Assert.Equal("Unit name is at most 64 characters.", Refused(ListOf(Entry(name: name65))));
        Assert.Equal("Unit name is required.", Refused(ListOf(Entry(name: " "))));
        Assert.Equal("Unit 'Squad': weapon name contains a control or bidirectional formatting character.", Refused(ListOf(Entry(loadout: $"[{Weapon(name: "Ri\u0007fle")}]"))));
        Assert.Equal("Unit 'Squad': rule name is at most 64 characters.", Refused(ListOf(Entry(rules: $$"""[{"name":"{{name65}}"}]"""))));
        Assert.Equal("Unit 'Squad': item name is at most 64 characters.",
            Refused(ListOf(Entry(loadout: $$"""[{"name":"{{name65}}","type":"ArmyBookItem","count":1,"content":[]}]"""))));
        Assert.Equal("Unit 'Squad': rule name is required.",
            Refused(ListOf(Entry(loadout: """[{"name":"Shield","type":"ArmyBookItem","count":1,"content":[{"name":null}]}]"""))));
        Assert.Equal("source is at most 512 characters.", Refused(ListOf(Entry()), new string('s', 513)));
    }

    [Fact]
    public void A_list_with_a_repeated_or_missing_selection_id_is_refused()
    {
        Assert.Equal("The list names entry 'a' twice.", Refused(ListOf(Entry(id: "a"), Entry(id: "a"))));
        Assert.Equal("Every entry needs a selectionId.", Refused(ArmyForgeClient.Parse("""{"id":"t","name":"T","gameSystem":"gf","units":[{"name":"S","size":1,"quality":4,"defense":4}]}""")));
        Assert.Equal("The list has no units.", Refused(ArmyForgeClient.Parse("""{"id":"t","name":"T","gameSystem":"gf"}""")));
    }
}
