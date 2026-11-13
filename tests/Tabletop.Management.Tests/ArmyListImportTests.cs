using Tabletop.Content;
using Tabletop.Management.Content.Armies;

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
}
