# Task 9: Army Forge — the export's shape and the import mapping

**Files:**
- Create: `server/management/Content/Armies/ArmyForgeList.cs`, `server/management/Content/Armies/ArmyForgeClient.cs`, `server/management/Content/Armies/ArmyForgeImport.cs`
- Create: `tests/Tabletop.Management.Tests/Fixtures/army-forge-c2-6YsWRH3py.json`, `tests/Tabletop.Management.Tests/ArmyForgeImportTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Content.Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`; `Tabletop.Geometry.Base`.
- Produces: `AfList(string Id, string Name, string GameSystem, List<AfUnit> Units)`, `AfUnit(string SelectionId, string Name, int Size, JsonElement Quality, JsonElement Defense, List<AfRule>? Rules, List<AfLoadoutItem>? Loadout, AfBases? Bases, bool Combined, string? JoinToUnit)`, `AfRule(string Name, JsonElement? Rating)`, `AfLoadoutItem(string Name, string Type, int Count, JsonElement? Range, int? Attacks, List<AfRule>? SpecialRules, List<AfRule>? Content)`, `AfBases(string? Round, string? Square)`; `ArmyForgeClient(HttpClient)` with `HttpClientName = "army-forge"`, `static string? ListId(string source)` (a share URL's `id=` or a bare id), `Task<AfList> Fetch(string listId)` throwing `ArmyForgeException(int Status)`; `PropertyLevel { Unit, Model }`; `ArmyForgeImport.ToArmy(AfList list, string source, IReadOnlyDictionary<string, PropertyLevel> catalog) : Army` throwing `UnsupportedGameSystemException(string gameSystem)`; `ArmyForgeImport.GameRules = { ["gf"] = "grimdark-future", ["aof"] = "grimdark-future" }`; `ArmyForgeImport.DefaultBody = ModelBody(Base.Rect(221 / 25.4, 119 / 25.4), 113 / 25.4)`, `HeightScale = 1.5`, `static double HeightFromBase(Base) => HeightScale * b.A`.
- Read from a real export (`GET https://army-forge.onepagerules.com/api/tts?id=c2-6YsWRH3py` → `200`, 26 KB, `access-control-allow-origin: *`; saved as the fixture). What the export actually holds, and what import therefore reads:
  - Top level: `id`, `name`, `gameSystem` (`"gf"` on this list), `units`, plus `points`, `pointsLimit`, `listPoints`, `specialRules`, `modified` and eight more this project ignores. **`gameSystem` is the list's own field and the only signal import uses** — a list may carry content from another army book and still report `"gf"`, and nothing here resolves the book.
  - A unit carries 27 keys. Read: `selectionId` (the unique one — `id` repeats across duplicated units), `name`, `size`, `quality`, `defense` (**plain integers**, `4` not `"4+"` and never objects), `rules`, `loadout`, `bases`, `combined`, `joinToUnit`. Ignored: `id`, `cost`, `items`, `weapons`, `upgrades`, `selectedUpgrades`, `xp`, `traits`, `notes`, `armyId`, `genericName`, `valid` and the rest. **There is no `customName`** — `name` is the only name field import uses.
  - **`loadout`, not `weapons`.** `weapons` is the base profile before upgrades; `loadout` is the resolved post-upgrade list (unit 0's `weapons[0]` is "Blood Sword" where its `loadout[0]` is the "Great Weapon" that replaced it).
  - A rule is `{id, name, label}` plus `rating` **only when it takes one** — absent, not null, on most rules. `label` is the pre-rendered display string (`"AP(3)"`) and is not stored.
  - A loadout entry has only `name`, `type` and `count` universally. `ArmyBookWeapon` adds `range`, `attacks`, `specialRules`, `weaponId`, `attacksMultiplier`; `ArmyBookItem` adds `content[]` of `ArmyBookRule` entries (`{name, rating, count}`). **`range` is `0` on some melee weapons and `null` on others in the same payload**, so the melee test is `(range ?? 0) == 0`; ranged is an integer of inches. `attacksMultiplier` is `1` on every entry observed and is **not** read — the `Attacks` stat is `attacks` alone; if a list ever carries a multiplier other than 1 the stat is wrong and the fix is one term in `Unit`.
  - `bases` is `{round, square}`, both **strings** of millimetres, both always present: a bare number is a circle (`"32"`), `WxH` an oval (`"60x35"`, `"120x92"`). `bases` also appears on loadout entries — a different field, not read.
  - Two entries with `combined: true` where one's `joinToUnit` names the other's `selectionId` are one unit of double size. `joinToUnit` is also how a hero joins, so it does not imply `combined`: an entry that carries `joinToUnit` and is not a `combined` partner is a hero joining, and its models become models of the unit it names (ruling 2). This list has no joined hero — the four `combined` entries are the two Blood Warriors pairs, and the other three carry `joinToUnit: null` — so the join path is tested against hand-written lists.
- Mapping decisions (management API → Army import; rulings 2–5): `gf` and `aof` → `game_system = "grimdark-future"`, any other system → `422` (ruling 5: one rules implementation serves both, so both list kinds carry the same `GameRules` identifier); combined pairs merge, and then a joining entry becomes a **section** of the entry it names, its slots appended to that unit; `Quality`/`Defense` become unit stats `"4+"` from the host entry, and a joined entry's own `Quality`/`Defense` become slot stats on the slots it contributes; every unit rule and every item rule with `count >= size` attaches at unit level unless the catalog says the name is a `model` property, in which case it goes onto every slot; on a joined entry every such rule goes onto that entry's own slots whatever level the catalog gives it, because the unit's properties stay the host's; a rule with `count < size` goes onto `count` slots of its own section; weapons with `count >= size` go on every slot of their section, otherwise onto the next `count` slots from a cursor that advances within that section (so `9× Rifle, 1× Flamer` puts the flamer on slot 10, not slot 1, and a joined hero's weapon can never land on a host slot); weapon stats are `Attacks` and, when `range > 0`, `Range = "{range}\""`; a melee weapon has no `Range` (content values → A weapon lives on the model slot); base from each section's own `bases.round` — `"32"` → `Base.Circle(32/25.4)`, `"60x35"` → `Base.Oval(60/25.4, 35/25.4)`; absent → `DefaultBody`; height `HeightFromBase(base)` = `1.5 ×` the base's longest dimension (ruling 3), so a 25.4 mm circle is 1.5", a 32 mm circle 1.89", a 60×35 mm oval 3.54" and a 120×92 mm oval 7.09" — no ruleset supplies a height and Army Forge publishes none, so this is a starting guess scaled off the one physical dimension the export does give, and the player corrects it per slot with `SlotPatch.Height`; slot name `"{entry name} {index within that entry}"`, and `Ordinal` counts over the merged unit, so the two coincide unless something joined; `AssetId` null.
- Join resolution runs as one pass over what the combined pass left. An absorbed `combined` partner's `selectionId` resolves to the entry that absorbed it, so a hero naming either half of a combined pair joins the merged unit of ten. **A `joinToUnit` no surviving entry answers, and one whose entry itself carries `joinToUnit`, both leave the joining entry as its own unit** — and no `422`, because an unresolvable reference still describes a model the user paid for. Joins are therefore never chained and a cycle needs no walk: each side of a cycle names an entry that itself joins.
- Stat names `Quality`, `Defense`, `Range`, `Attacks` must match the OPR `stat_definition` names the game-server plan seeds. A joined entry's slots carry `Quality` and `Defense` as **slot** stat keys, a level the OPR properties document does not declare them at, and every rule a joined hero carries is a slot property whatever its catalog kind — both recorded under *Interfaces needed from other modules*.

Spec: management API → Army import ("Management fetches the list … The parser lives in one place"); the army → Where the values come from, Binding, Base shape and height ("defaulting to whatever the ruleset says" — no ruleset default exists yet, hence `DefaultBody` for an entry with none and a height scaled off a published base); content values → Stats are typed, A weapon lives on the model slot, `upgrades` is dropped, `list_entry` is dropped, The army's own two fields; data model → "Nothing carries a count … Import does that expansion once, at the boundary."

- [ ] **Step 1: Save the fixture and write the failing tests**

```bash
curl -s 'https://army-forge.onepagerules.com/api/tts?id=c2-6YsWRH3py' -o tests/Tabletop.Management.Tests/Fixtures/army-forge-c2-6YsWRH3py.json
```

```csharp
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
}
```

- [ ] **Step 2: Run; expect compile errors. Write `ArmyForgeList.cs` and `ArmyForgeClient.cs`:**

```csharp
using System.Text.Json;

namespace Tabletop.Management.Content.Armies;

// The subset of Army Forge's TTS export this project reads. Field names are the export's.
// Only `name`, `type` and `count` are present on every loadout entry, and `rating` is absent (not null) on a rule that takes no
// parameter, so every optional member is nullable and nothing is `required`. A required-field model throws on the real export.
public sealed record AfList(string Id, string Name, string GameSystem, List<AfUnit> Units);
public sealed record AfUnit(string SelectionId, string Name, int Size, JsonElement Quality, JsonElement Defense,
    List<AfRule>? Rules, List<AfLoadoutItem>? Loadout, AfBases? Bases, bool Combined, string? JoinToUnit);
public sealed record AfRule(string Name, JsonElement? Rating);
public sealed record AfLoadoutItem(string Name, string Type, int Count, JsonElement? Range, int? Attacks, List<AfRule>? SpecialRules, List<AfRule>? Content);
public sealed record AfBases(string? Round, string? Square);
```

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tabletop.Management.Content.Armies;

public sealed class ArmyForgeException(int status) : Exception($"Army Forge answered {status}") { public int Status { get; } = status; }

public sealed partial class ArmyForgeClient(HttpClient http)
{
    public const string HttpClientName = "army-forge";
    public const string BaseUrl = "https://army-forge.onepagerules.com";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]+)")] private static partial Regex IdParam();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{4,64}$")] private static partial Regex BareId();

    /// <summary>The list id from a share link (the id= parameter) or a bare id; null when neither.</summary>
    public static string? ListId(string source) =>
        IdParam().Match(source) is { Success: true } m ? m.Groups[1].Value : BareId().IsMatch(source) ? source : null;

    public static AfList Parse(string json) => JsonSerializer.Deserialize<AfList>(json, Json) ?? throw new JsonException("empty export");

    public async Task<AfList> Fetch(string listId)
    {
        using var response = await http.GetAsync($"{BaseUrl}/api/tts?id={Uri.EscapeDataString(listId)}");
        if (!response.IsSuccessStatusCode) throw new ArmyForgeException((int)response.StatusCode);
        return Parse(await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 3: Write `ArmyForgeImport.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Armies;

public enum PropertyLevel { Unit, Model }

public sealed class UnsupportedGameSystemException(string gameSystem) : Exception($"Only Grimdark Future and Age of Fantasy lists import; this list is '{gameSystem}'.")
{
    public string GameSystem { get; } = gameSystem;
}

/// <summary>Army Forge's list → a self-contained Army. Expansion to one slot per model happens here and nowhere else.</summary>
public static class ArmyForgeImport
{
    // Grimdark Future and Age of Fantasy are the same mechanics on different content, so both map to the one rules implementation.
    public static readonly IReadOnlyDictionary<string, string> GameRules = new Dictionary<string, string> { ["gf"] = "grimdark-future", ["aof"] = "grimdark-future" };
    /// <summary>What an entry with no published base gets: a vehicle hull, 221 x 119 x 113 mm. Army Forge publishes bases.round
    /// reliably for infantry, so a missing base means a vehicle or monster in practice, and a 32 mm circle would be wrong for
    /// exactly those models — a tank measured from a 1.26" circle at its hull centre. The player corrects it per slot, and
    /// the height comes with the base because HeightFromBase would scale 221 mm into a 13" tall tank.</summary>
    public static readonly ModelBody DefaultBody = new(Base.Rect(221 / 25.4, 119 / 25.4), 113 / 25.4);
    public const double HeightScale = 1.5;

    /// <summary>A starting height for a base that *was* published, scaled off its long axis because nothing publishes a height:
    /// 1.5" for a 25.4 mm circle, 3.54" for a 60x35 mm oval. The player corrects it per slot.</summary>
    public static double HeightFromBase(Base b) => HeightScale * b.A;

    public static Army ToArmy(AfList list, string source, IReadOnlyDictionary<string, PropertyLevel> catalog)
    {
        if (!GameRules.TryGetValue(list.GameSystem, out var gameRules)) throw new UnsupportedGameSystemException(list.GameSystem);
        var units = Merge(list.Units).Select((m, i) => Unit(m, i + 1, catalog)).ToList();
        return new Army(Guid.CreateVersion7(), list.Name, gameRules, source, units);
    }

    /// <summary>A unit as it will be built: the host entry, then the entries whose models join it.</summary>
    private sealed record Merged(AfUnit Host, List<AfUnit> Joined);

    /// <summary>Two combined entries are one unit: sizes add, loadout counts add. Then a joining entry becomes a section of the entry
    /// it names, an absorbed partner's id resolving to the entry that absorbed it. A name nothing answers, and a name whose entry
    /// itself joins, leave the joining entry as its own unit — which is also what breaks a cycle without walking one.</summary>
    private static List<Merged> Merge(List<AfUnit> units)
    {
        var survivors = new List<AfUnit>();
        var absorbed = new Dictionary<string, string>();          // a combined partner's selectionId → the id that absorbed it
        foreach (var u in units)
        {
            if (absorbed.ContainsKey(u.SelectionId)) continue;
            var partner = u.Combined ? units.FirstOrDefault(o => o.Combined && o.SelectionId != u.SelectionId && (o.JoinToUnit == u.SelectionId || u.JoinToUnit == o.SelectionId)) : null;
            if (partner is null) { survivors.Add(u); continue; }
            absorbed[partner.SelectionId] = u.SelectionId;
            var loadout = (u.Loadout ?? []).Concat(partner.Loadout ?? [])
                .GroupBy(l => (l.Type, l.Name))
                .Select(g => g.First() with { Count = g.Sum(l => l.Count) }).ToList();
            survivors.Add(u with { Size = u.Size + partner.Size, Loadout = loadout, JoinToUnit = null });
        }

        var byId = survivors.ToDictionary(u => u.SelectionId);
        AfUnit? Host(string id) => byId.GetValueOrDefault(absorbed.GetValueOrDefault(id, id));
        bool Joins(AfUnit u) => u.JoinToUnit is not null && Host(u.JoinToUnit) is { JoinToUnit: null };

        var result = survivors.Where(u => !Joins(u)).Select(u => new Merged(u, [])).ToList();
        var hosts = result.ToDictionary(m => m.Host.SelectionId);
        foreach (var j in survivors.Where(Joins)) hosts[Host(j.JoinToUnit!)!.SelectionId].Joined.Add(j);
        return result;
    }

    private static Unit Unit(Merged m, int ordinal, IReadOnlyDictionary<string, PropertyLevel> catalog)
    {
        var host = m.Host;
        var stats = new Dictionary<string, string> { ["Quality"] = TargetNumber(host.Quality), ["Defense"] = TargetNumber(host.Defense) };
        var unitProps = new List<PropertyInstance>();
        var models = new List<ModelSlot>();
        Section(host, catalog, unitProps, models);
        foreach (var joined in m.Joined) Section(joined, catalog, null, models);
        return new Unit(Guid.CreateVersion7(), ordinal, host.Name, stats, unitProps, models);
    }

    /// <summary>One entry's slots, appended to the unit being built. `unitProps` is null for a joined entry: every rule of it goes on
    /// its own slots whatever level the catalog gives the name, and its Quality and Defense become slot stats, because the unit's own
    /// stats and properties stay the host's. The cursor is per section, so a joined hero's loadout never reaches a host slot.</summary>
    private static void Section(AfUnit u, IReadOnlyDictionary<string, PropertyLevel> catalog, List<PropertyInstance>? unitProps, List<ModelSlot> into)
    {
        var slotProps = Enumerable.Range(0, u.Size).Select(_ => new List<PropertyInstance>()).ToList();
        var slotWeapons = Enumerable.Range(0, u.Size).Select(_ => new List<Weapon>()).ToList();
        var cursor = 0;

        void Place(PropertyInstance p, int count)
        {
            if (count >= u.Size)
            {
                if (unitProps is null || catalog.GetValueOrDefault(p.Name) == PropertyLevel.Model) slotProps.ForEach(s => s.Add(p));
                else unitProps.Add(p);
                return;
            }
            for (var i = 0; i < count; i++) slotProps[(cursor + i) % u.Size].Add(p);
            cursor += count;
        }

        foreach (var r in u.Rules ?? []) Place(new PropertyInstance(r.Name, Rating(r.Rating)), u.Size);
        foreach (var item in u.Loadout ?? [])
        {
            if (item.Type == "ArmyBookWeapon")
            {
                var wstats = new Dictionary<string, string> { ["Attacks"] = (item.Attacks ?? 1).ToString(CultureInfo.InvariantCulture) };
                // Melee is `"range": 0` on some entries and `"range": null` on others in the same export; `is { } range and > 0` excludes both.
                if (Range(item.Range) is { } range and > 0) wstats["Range"] = $"{range.ToString(CultureInfo.InvariantCulture)}\"";
                var props = (item.SpecialRules ?? []).Select(r => new PropertyInstance(r.Name, Rating(r.Rating))).ToList();
                Weapon W() => new(Guid.CreateVersion7(), item.Name, new Dictionary<string, string>(wstats), props);
                if (item.Count >= u.Size) slotWeapons.ForEach(s => s.Add(W()));
                else { for (var i = 0; i < item.Count; i++) slotWeapons[(cursor + i) % u.Size].Add(W()); cursor += item.Count; }
            }
            else if (item.Type == "ArmyBookItem")
                foreach (var rule in item.Content ?? []) Place(new PropertyInstance(rule.Name, Rating(rule.Rating)), item.Count);
        }

        var body = ParseBase(u.Bases?.Round) is { } published ? new ModelBody(published, HeightFromBase(published)) : DefaultBody;
        var slotStats = unitProps is null
            ? new Dictionary<string, string> { ["Quality"] = TargetNumber(u.Quality), ["Defense"] = TargetNumber(u.Defense) }
            : new Dictionary<string, string>();
        for (var i = 0; i < u.Size; i++)
            into.Add(new ModelSlot(Guid.CreateVersion7(), into.Count + 1, $"{u.Name} {i + 1}", new Dictionary<string, string>(slotStats),
                slotProps[i], body.Base, body.Height, null, slotWeapons[i]));
    }

    /// <summary>"32" → circle 32 mm; "60x35" → oval 60×35 mm; anything else → null. Inches out.</summary>
    public static Base? ParseBase(string? round)
    {
        if (string.IsNullOrWhiteSpace(round)) return null;
        var parts = round.ToLowerInvariant().Split('x');
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return Base.Circle(d / 25.4);
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) return Base.Oval(l / 25.4, w / 25.4);
        return null;
    }

    private static string TargetNumber(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => $"{e.GetInt32()}+",
        JsonValueKind.String => e.GetString()!.TrimEnd('+') + "+",
        _ => throw new JsonException("quality/defense must be a number or a string"),
    };

    private static int? Rating(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } r when r.TryGetInt32(out var n) => n,
        { ValueKind: JsonValueKind.String } r when int.TryParse(r.GetString(), out var n) => n,
        _ => null,
    };

    private static double? Range(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } r ? r.GetDouble() : null;
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Management.Tests`; expect at least 52 passed, 7 of them `ArmyForgeImportTests`. Commit: `Parse Army Forge exports and expand them into self-contained armies`.**
