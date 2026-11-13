using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

/// <summary>Whole Army Forge lists (Fixtures/army-forge-&lt;id&gt;.json): each model ends with the weapons Army Forge shows.</summary>
public class ArmyListImportTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ArmyListImportTests(ManagementFactory f) => _f = f;

    static bool Ranged(Weapon w) => w.Stats.ContainsKey("Range");

    /// <summary>HDF Artillery Blob: two combined Infantry Squads (each 7 Battle Rifles, a Weapon Team's Autocannon, 2 Laser Rifles) with a
    /// Medical Training and a Company Standard upgrade, which arm nobody: their carriers keep a rifle, and no model holds two guns.</summary>
    [Fact]
    public async Task An_upgrade_that_only_gives_a_rule_takes_no_weapon_slot()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("https://army-forge.onepagerules.com/share?id=ipY2KtxH4tJG&name=HDF_Artillery%20Blob"));
        var squad = army.Units.Single(u => u.Name == "Infantry Squad");
        Assert.Equal(21, squad.Models.Count);                                              // 20 and the joined Company Leader
        var troops = squad.Models.Take(20).ToList();
        Assert.All(troops, m => Assert.Single(m.Weapons, Ranged));
        Assert.All(troops, m => Assert.Single(m.Weapons, w => w.Name == "Strike"));
        Assert.Equal((14, 2, 4), (troops.Count(m => m.Weapons.Any(w => w.Name == "Battle Rifle")), troops.Count(m => m.Weapons.Any(w => w.Name == "Autocannon")),
            troops.Count(m => m.Weapons.Any(w => w.Name == "Laser Rifle"))));
        Assert.All(troops.Where(m => m.Weapons.Any(w => w.Name == "Autocannon")), m => Assert.Contains(new PropertyInstance("Tough", 3), m.Properties));
        Assert.Single(troops, m => m.Properties.Any(p => p.Name == "Regeneration Aura"));
        Assert.Single(troops, m => m.Properties.Any(p => p.Name == "Enhanced Hold the Line Aura"));
        // The carrier of an upgrade the rest of the squad lacks is named for it.
        Assert.Matches(@"^Infantry Squad \(Company Standard\) \d+$", troops.Single(m => m.Properties.Any(p => p.Name == "Enhanced Hold the Line Aura")).Name);
        Assert.Matches(@"^Infantry Squad \(Medical Training\) \d+$", troops.Single(m => m.Properties.Any(p => p.Name == "Regeneration Aura")).Name);
        Assert.All(troops.Where(m => m.Weapons.Any(w => w.Name == "Autocannon")), m => Assert.Matches(@"^Infantry Squad \(Weapon Team\) \d+$", m.Name));
        Assert.Equal(16, troops.Count(m => System.Text.RegularExpressions.Regex.IsMatch(m.Name, @"^Infantry Squad \d+$")));
        var leader = squad.Models[20];
        Assert.Equal(["Pistol", "Hand Weapon"], leader.Weapons.Select(w => w.Name));
        Assert.Contains(leader.Properties, p => p.Name == "Precision Shooter Buff");

        var veterans = army.Units.Single(u => u.Name == "Veterans");
        Assert.All(veterans.Models, m => Assert.Equal(["Strike", "Shotgun"], m.Weapons.Select(w => w.Name)));
    }

    /// <summary>Ratmen Clans: three units carry Reinforcement on every model (two combined Operators, Elite Operators, Saboteurs). Each gets a
    /// reserve copy without the rule, which the army's counts leave out: the copy stands on its original's miniatures.</summary>
    [Fact]
    public async Task A_reinforcement_unit_gets_a_copy_that_the_armys_counts_leave_out()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("https://army-forge.onepagerules.com/share?id=Wo0xOxxgRADS&name=Ratmen_Clans"));
        var copies = army.Units.Where(u => u.Reserve is not null).ToList();
        Assert.Equal(["Operators", "Elite Operators", "Saboteurs"], copies.Select(u => u.Name));
        Assert.All(copies, u => Assert.Equal("Reinforcement", u.Reserve!.Property));
        Assert.All(copies, u => Assert.NotNull(army.CopiedBy(u)));
        Assert.All(copies, u => Assert.DoesNotContain(u.Properties.Concat(u.Models.SelectMany(m => m.Properties)), p => p.Name == "Reinforcement"));
        Assert.Equal(20, copies[0].Models.Count);
        // Elite Operators: the Flamer model and the four Shotgun models each hold one gun and one Strike.
        var elite = army.Units.First(u => u.Name == "Elite Operators");
        Assert.All(elite.Models, m => Assert.Equal(2, m.Weapons.Count));
        Assert.Equal((1, 4, 5), (elite.Models.Count(m => m.Weapons.Any(w => w.Name == "Flamer")), elite.Models.Count(m => m.Weapons.Any(w => w.Name == "Shotgun")),
            elite.Models.Count(m => m.Weapons.Any(w => w.Name == "Strike"))));
        var summary = (await c.GetJson<Page<ArmySummary>>("/v1/armies")).Items.Single(a => a.Id == army.Id);
        Assert.Equal((4, 34, 0), (summary.Units, summary.Models, summary.Meshes));
    }

    /// <summary>Army Forge lists a loadout as flat lines with counts. A model holds one line of the guns and one of the melee weapons, so
    /// the two are dealt out apart: the sergeant's Pistol and Sword land on one model, and nobody holds two guns or no melee weapon.</summary>
    [Fact]
    public async Task Guns_and_melee_weapons_are_dealt_to_the_models_apart()
    {
        var id = "t" + Guid.NewGuid().ToString("N");
        static string W(string name, int count, int range) => $$"""{"type":"ArmyBookWeapon","name":"{{name}}","count":{{count}},"range":{{range}},"attacks":1}""";
        var loadout = string.Join(",", W("Rifle", 4, 24), W("Strike", 4, 0), W("Pistol", 1, 12), W("Sword", 1, 0));
        FakeArmyForge.Lists[id] = """{"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"a","name":"Squad","size":5,"quality":4,"defense":4,"combined":false,"joinToUnit":null,"loadout":[""" + loadout + "]}]}";
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var squad = (await c.PostJson<Army>("/v1/armies/import", new ImportRequest(id))).Units.Single();
        Assert.Equal(4, squad.Models.Count(m => m.Weapons.Select(w => w.Name).SequenceEqual(["Rifle", "Strike"])));
        Assert.Single(squad.Models, m => m.Weapons.Select(w => w.Name).SequenceEqual(["Pistol", "Sword"]));
    }
}
