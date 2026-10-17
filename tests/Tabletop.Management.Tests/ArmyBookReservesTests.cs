using Tabletop.Content;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

/// <summary>Spawn, Split and Reinforcement place a unit the list does not hold: the import adds it as a reserve unit (army-book rules spec → Readings, batch 7).</summary>
public class ArmyBookReservesTests
{
    const string Robots = "4k5amkxoybdiqotm", ChangeDaemons = "04z57ua0bwth37zh";

    static Army Import(string units) => ArmyForgeImport.ToArmy(ArmyForgeClient.Parse($$"""{"id":"t","name":"T","gameSystem":"gf","units":[{{units}}]}"""), "x",
        new Dictionary<string, PropertyLevel> { ["Tough"] = PropertyLevel.Model, ["Spawn"] = PropertyLevel.Model });

    static string Entry(string id, string name, int size, string rules, string book, string loadout = "") =>
        $$"""{"selectionId":"{{id}}","name":"{{name}}","size":{{size}},"quality":4,"defense":4,"rules":[{{rules}}],"combined":false,"joinToUnit":null,"armyId":"{{book}}","bases":{"round":"32","square":"25"},"loadout":[{{loadout}}]}""";

    [Fact]
    public void Spawn_adds_the_unit_its_rating_names_from_the_book_held_by_the_carrying_model()
    {
        var item = """{"name":"Swarm Beacon","type":"ArmyBookItem","count":1,"content":[{"name":"Spawn","type":"ArmyBookRule","rating":"Bot Swarms [3]"}]}""";
        var army = Import(Entry("a", "Lord", 2, "", Robots, item));
        Assert.Equal(["Lord", "Bot Swarms"], army.Units.Select(u => u.Name));
        var swarms = army.Units[1];
        Assert.Equal(new UnitReserve("Spawn", army.Units[0].Models[0].Id), swarms.Reserve);       // the model the item went to
        Assert.Equal(2, swarms.Ordinal);
        Assert.Equal(3, swarms.Models.Count);
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "5+", ["Defense"] = "6+" }, swarms.Stats);
        Assert.All(swarms.Models, m =>
        {
            Assert.Contains(new PropertyInstance("Tough", 3), m.Properties);
            Assert.Equal(["Swarm Attacks"], m.Weapons.Select(w => w.Name));
            Assert.Equal(Robots, m.ArmyBook);
        });
        Assert.Null(army.Units[0].Reserve);
    }

    [Fact]
    public void Split_adds_its_unit_and_that_unit_its_own()
    {
        var army = Import(Entry("a", "Change Horrors", 5, """{"name":"Split","rating":"Lesser Change Horrors [5]"}""", ChangeDaemons));
        Assert.Equal(["Change Horrors", "Lesser Change Horrors", "Changelings"], army.Units.Select(u => u.Name));
        Assert.Equal(new UnitReserve("Split", army.Units[0].Id), army.Units[1].Reserve);
        Assert.Equal(new UnitReserve("Split", army.Units[1].Id), army.Units[2].Reserve);           // Lesser Change Horrors: "Split(Changelings [10])"
        Assert.Equal([5, 10], army.Units.Skip(1).Select(u => u.Models.Count));
    }

    [Fact]
    public void Reinforcement_adds_a_copy_without_the_rule_when_every_model_has_it()
    {
        var gun = """{"name":"Rifle","type":"ArmyBookWeapon","count":3,"range":24,"attacks":1,"specialRules":[]}""";
        var army = Import(Entry("a", "Rats", 3, """{"name":"Reinforcement"},{"name":"Tough","rating":1}""", Robots, gun));
        var (rats, copy) = (army.Units[0], army.Units[1]);
        Assert.Equal(new UnitReserve("Reinforcement", rats.Id), copy.Reserve);
        Assert.Equal(rats.Name, copy.Name);
        Assert.DoesNotContain(copy.Properties.Concat(copy.Models.SelectMany(m => m.Properties)), p => p.Name == "Reinforcement");   // "this rule doesn't apply to the new copy"
        Assert.Equal(rats.Models.Select(m => m.Weapons.Count), copy.Models.Select(m => m.Weapons.Count));
        Assert.Empty(copy.Models.Select(m => m.Id).Intersect(rats.Models.Select(m => m.Id)));
        Assert.Empty(copy.Models.SelectMany(m => m.Weapons).Select(w => w.Id).Intersect(rats.Models.SelectMany(m => m.Weapons).Select(w => w.Id)));
    }

    [Fact]
    public void A_rating_naming_no_book_unit_adds_nothing_and_reserves_stop_at_the_army_limits()
    {
        Assert.Single(Import(Entry("a", "Lord", 1, """{"name":"Spawn","rating":"Ghosts [3]"}""", Robots)).Units);
        Assert.Single(Import(Entry("a", "Lord", 1, """{"name":"Spawn","rating":"Bot Swarms [3]"}""", "no-such-book")).Units);
        var many = string.Join(",", Enumerable.Range(1, ArmyBookReserves.MaxUnits).Select(i => Entry($"u{i}", $"Lord {i}", 1, """{"name":"Spawn","rating":"Bot Swarms [3]"}""", Robots)));
        Assert.Equal(ArmyBookReserves.MaxUnits, Import(many).Units.Count);
    }
}
