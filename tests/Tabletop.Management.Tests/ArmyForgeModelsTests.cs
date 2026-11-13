using System.Text.Json.Nodes;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

/// <summary>Which model holds which loadout line (`ArmyForgeModels`), on small army-book units exported as Army Forge writes them
/// (`ArmyForgeExportBuilder`). Each case is one the dump's invariant test (`ArmyBookLoadoutInvariantTests`) found.</summary>
public class ArmyForgeModelsTests
{
    static readonly IReadOnlyDictionary<string, PropertyLevel> Catalog = new Dictionary<string, PropertyLevel>();

    static string W(string name, int count, int range = 0) => $$"""{"name":"{{name}}","type":"ArmyBookWeapon","count":{{count}},"range":{{range}},"attacks":1,"specialRules":[]}""";
    static string Item(string name, int count, string content) => $$"""{"name":"{{name}}","type":"ArmyBookItem","count":{{count}},"content":[{{content}}]}""";
    static JsonNode Book(string name, int size, params string[] weapons) =>
        JsonNode.Parse($$"""{"name":"{{name}}","size":{{size}},"quality":4,"defense":4,"bases":{"round":"32"},"rules":[],"weapons":[{{string.Join(",", weapons)}}],"items":[]}""")!;
    static JsonNode Section(string variant, string? affects, string[] targets, params string[] gains) => JsonNode.Parse($$"""
        {"uid":"{{Guid.NewGuid():N}}","variant":"{{variant}}","affects":{{affects ?? "null"}},"targets":[{{string.Join(",", targets.Select(t => $"\"{t}\""))}}],
         "options":[{"gains":[{{string.Join(",", gains)}}]}]}
        """)!;
    static JsonNode Option(JsonNode section) => section["options"]![0]!;

    static ArmyForgeExportBuilder Apply(ArmyForgeExportBuilder b, params JsonNode[] sections)
    {
        foreach (var s in sections) Assert.True(b.Apply(s, Option(s)));
        return b;
    }

    static Unit Import(params JsonObject[] entries) =>
        ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(ArmyForgeExportBuilder.List(entries).ToJsonString()), "x", Catalog).Units[0];

    static List<string> Loadouts(Unit u) => u.Models.Select(m => string.Join(", ", m.Weapons.Select(w => w.Name).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal).ToList();

    /// <summary>Avatar of Change: "2x Heavy Daemon Hammer" is two hammers on its one model.</summary>
    [Fact]
    public void A_count_above_the_size_is_that_many_copies_per_model()
    {
        var u = Import(new ArmyForgeExportBuilder(Book("Avatar", 1, W("Heavy Daemon Hammer", 2), W("Stomp", 1))).Entry("a", null));
        Assert.Equal(["Heavy Daemon Hammer", "Heavy Daemon Hammer", "Stomp"], u.Models[0].Weapons.Select(w => w.Name));
    }

    /// <summary>Operators: the sergeant (one Battle Rifle and Strike for a Sgt. Pistol and Sgt. Hand Weapon), then two special rifles. The
    /// export appends the specials after the sergeant's lines, and the sergeant's pistol and sword stay on one model.</summary>
    [Fact]
    public void A_sergeants_pistol_and_sword_are_one_model_whatever_comes_after()
    {
        var b = Apply(new ArmyForgeExportBuilder(Book("Operators", 10, W("Battle Rifle", 10, 24), W("Strike", 10))),
            Section("replace", """{"type":"exactly","value":1}""", ["Battle Rifle", "Strike"], W("Sgt. Pistol", 1, 12), W("Sgt. Hand Weapon", 1)),
            Section("replace", """{"type":"up to","value":2}""", ["Battle Rifles"], W("Plasma Rifle", 1, 24)),
            Section("replace", """{"type":"up to","value":2}""", ["Battle Rifles"], W("Plasma Rifle", 1, 24)));
        var u = Import(b.Entry("a", null));
        Assert.Single(u.Models, m => m.Weapons.Select(w => w.Name).Order().SequenceEqual(["Sgt. Hand Weapon", "Sgt. Pistol"]));
        Assert.Equal(2, u.Models.Count(m => m.Weapons.Select(w => w.Name).Order().SequenceEqual(["Plasma Rifle", "Strike"])));
        Assert.Equal(7, u.Models.Count(m => m.Weapons.Select(w => w.Name).Order().SequenceEqual(["Battle Rifle", "Strike"])));
    }

    /// <summary>Novice Sisters: a Flamer for one Energy Club leaves that model the Flamer only, and no model empty-handed.</summary>
    [Fact]
    public void A_gun_for_a_melee_weapon_takes_the_melee_weapons_place()
    {
        var b = Apply(new ArmyForgeExportBuilder(Book("Novices", 10, W("Energy Club", 10))),
            Section("replace", """{"type":"up to","value":2}""", ["Energy Clubs"], W("Flamer", 1, 12)));
        Assert.Equal([.. Enumerable.Repeat("Energy Club", 9), "Flamer"], Loadouts(Import(b.Entry("a", null))));
    }

    /// <summary>Specialists: a Bomb-Drone added to one model, then a Rocket Launcher for one Flamer. The model holding the Rocket Launcher no
    /// longer holds a Flamer.</summary>
    [Fact]
    public void A_replaced_weapon_leaves_its_model()
    {
        var b = Apply(new ArmyForgeExportBuilder(Book("Specialists", 3, W("Flamer", 3, 12), W("Strike", 3))),
            Section("upgrade", """{"type":"exactly","value":1}""", [], W("Bomb-Drone", 1, 18)),
            Section("replace", """{"type":"any"}""", ["Flamer"], W("Rocket Launcher", 1, 18)));
        var u = Import(b.Entry("a", null));
        Assert.DoesNotContain(u.Models, m => m.Weapons.Any(w => w.Name == "Rocket Launcher") && m.Weapons.Any(w => w.Name == "Flamer"));
        Assert.Equal(["Bomb-Drone", "Flamer", "Strike"], u.Models.Single(m => m.Weapons.Any(w => w.Name == "Bomb-Drone")).Weapons.Select(w => w.Name).Order());
    }

    /// <summary>Destroyers: a Missile Rack and a Burst Cyclic Rifle for one Burst Cyclic Rifle are one model's.</summary>
    [Fact]
    public void A_replacement_that_gives_the_target_back_is_one_models()
    {
        var b = Apply(new ArmyForgeExportBuilder(Book("Destroyers", 3, W("Burst Cyclic Rifle", 3, 18), W("Hand Weapon", 3))),
            Section("replace", """{"type":"exactly","value":1}""", ["Burst Cyclic Rifle"], W("Missile Rack", 1, 30), W("Burst Cyclic Rifle", 1, 18)));
        Assert.Equal(["Burst Cyclic Rifle, Hand Weapon", "Burst Cyclic Rifle, Hand Weapon", "Burst Cyclic Rifle, Hand Weapon, Missile Rack"],
            Loadouts(Import(b.Entry("a", null))));
    }

    /// <summary>Two combined Specialists entries, one whose Flamers and Strikes all became Bomb Spears: each entry's loadout stays on its own
    /// models.</summary>
    [Fact]
    public void Combined_entries_keep_their_own_loadouts()
    {
        var spears = Apply(new ArmyForgeExportBuilder(Book("Specialists", 3, W("Flamer", 3, 12), W("Strike", 3))),
            Section("replace", """{"type":"all"}""", ["Flamers", "Strikes"], W("Bomb Spear", 1)));
        var plain = new ArmyForgeExportBuilder(Book("Specialists", 3, W("Flamer", 3, 12), W("Strike", 3)));
        var u = Import(spears.Entry("a", null, combined: true), plain.Entry("b", null, combined: true, joinTo: "a"));
        Assert.Equal(["Bomb Spear", "Bomb Spear", "Bomb Spear", "Flamer, Strike", "Flamer, Strike", "Flamer, Strike"], Loadouts(u));
    }

    /// <summary>Veteran Assault Brothers: a Combat Shield on every model, two from the book and one with a Heavy CCW, names no one.</summary>
    [Fact]
    public void An_upgrade_every_model_carries_over_several_lines_names_no_one()
    {
        var b = Apply(new ArmyForgeExportBuilder(JsonNode.Parse($$"""
            {"name":"Veterans","size":3,"quality":4,"defense":4,"rules":[],"weapons":[{{W("Heavy Pistol", 3, 12)}}],
             "items":[{{Item("Combat Shield", 3, """{"name":"Bash","type":"ArmyBookWeapon","count":1,"range":0,"attacks":1},{"name":"Shielded","type":"ArmyBookRule"}""")}}]}
            """)!),
            Section("replace", """{"type":"any"}""", ["Heavy Pistol", "Combat Shield"], W("Heavy CCW", 1), Item("Combat Shield", 1, """{"name":"Shielded","type":"ArmyBookRule"}""")));
        var u = Import(b.Entry("a", null));
        Assert.All(u.Models, m => Assert.Matches(@"^Veterans \d$", m.Name));
        Assert.Equal(["Bash, Heavy Pistol", "Bash, Heavy Pistol", "Heavy CCW"], Loadouts(u));
    }
}
