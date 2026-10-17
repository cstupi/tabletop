using Tabletop.Content;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

/// <summary>What the import adds for a rule whose profile no weapon carries (army-book rules spec → Batch 3 and 5 readings).</summary>
public class ArmyBookAdditionsTests
{
    static readonly Dictionary<string, PropertyLevel> Catalog = new() { ["Takedown Shot"] = PropertyLevel.Model, ["Takedown Strike"] = PropertyLevel.Model };

    static Army Import(string rules, string loadout, int size = 1) => ArmyForgeImport.ToArmy(ArmyForgeClient.Parse($$"""
        {"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"a","name":"Agents","size":{{size}},"quality":4,"defense":4,"rules":[{{rules}}],"combined":false,"joinToUnit":null,
        "bases":{"round":"32","square":"25"},"loadout":[{{loadout}}]}]}
        """), "x", Catalog);

    const string Rifle = """{"name":"Sniper Rifle","type":"ArmyBookWeapon","count":1,"range":30,"attacks":1,"specialRules":[]},{"name":"Pistol","type":"ArmyBookWeapon","count":1,"range":12,"attacks":1,"specialRules":[]}""";
    const string Ccw = """{"name":"CCW","type":"ArmyBookWeapon","count":1,"range":0,"attacks":2,"specialRules":[]}""";

    [Fact]
    public void Takedown_Shot_adds_a_once_per_game_shot_at_the_models_longest_range()
    {
        var model = Import("""{"name":"Takedown Shot"}""", $"{Rifle},{Ccw}").Units[0].Models[0];
        var shot = model.Weapons.Single(w => w.Name == "Takedown Shot");
        Assert.Equal(new Dictionary<string, string> { ["Attacks"] = "1", ["Range"] = "30\"" }, shot.Stats);
        // "one extra attack against the target at Quality 2+ with AP(2), Deadly(3), and Takedown", "Once per game"
        Assert.Equal([new PropertyInstance("AP", 2), new PropertyInstance("Deadly", 3), new PropertyInstance("Reliable", null), new PropertyInstance("Takedown", null), new PropertyInstance("Limited", null)], shot.Properties);
        Assert.Equal(["Sniper Rifle", "Pistol", "CCW", "Takedown Shot"], model.Weapons.Select(w => w.Name));
    }

    [Fact]
    public void Takedown_Strike_adds_a_once_per_game_melee_attack()
    {
        var model = Import("""{"name":"Takedown Strike"}""", Ccw).Units[0].Models[0];
        var strike = model.Weapons.Single(w => w.Name == "Takedown Strike");
        Assert.Equal(new Dictionary<string, string> { ["Attacks"] = "1" }, strike.Stats);
        Assert.Equal(["AP", "Deadly", "Reliable", "Takedown", "Limited"], strike.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Only_the_carrying_model_gets_the_weapon_and_a_model_with_no_gun_gets_no_shot()
    {
        var item = """{"name":"Spy-Assassin Sniper","type":"ArmyBookItem","count":1,"content":[{"name":"Takedown Shot","type":"ArmyBookRule"}]}""";
        var models = Import("", $$"""{"name":"Rifle","type":"ArmyBookWeapon","count":3,"range":24,"attacks":1,"specialRules":[]},{{item}}""", size: 3).Units[0].Models;
        Assert.Equal([1, 0, 0], models.Select(m => m.Weapons.Count(w => w.Name == "Takedown Shot")));
        Assert.DoesNotContain(Import("""{"name":"Takedown Shot"}""", Ccw).Units[0].Models[0].Weapons, w => w.Name == "Takedown Shot");
    }
}
