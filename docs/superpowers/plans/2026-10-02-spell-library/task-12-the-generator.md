# Task 12: The generator

**Files:**
- Create: `scripts/army-books` (shell wrapper)
- Create: `scripts/ArmyBooks/Tabletop.ArmyBooks.csproj`, `Program.cs`, `ArmyForgeBook.cs`, `SpellTranslator.cs`, `TextCheck.cs`, `BookWriter.cs`
- Create: `tests/Tabletop.ArmyBooks.Tests/Tabletop.ArmyBooks.Tests.csproj`, `SpellTranslatorTests.cs`, `BookWriterTests.cs`, `Fixtures/index.json`, `Fixtures/books/*.json`
- Modify: `Tabletop.sln` (both projects)

**Interfaces:**
- Consumes: `EntryDocument`, `EffectDocument`, `ConditionDocument`, `BookDocument`, `PropertiesJson.Options/Parse/Validate`, `PropertiesSeeder.Compose` (Tasks 2, 3).
- Produces (namespace `Tabletop.ArmyBooks`): `AfIndexEntry`, `AfBook`, `AfSpell`, `AfGeneration`, `AfGenPart`, `AfParam` (the dump's shape); `static class SpellTranslator` with `EntryDocument Translate(AfBook book, AfSpell spell)` and `IReadOnlyDictionary<string, EntryDocument> Overrides`; `static class TextCheck` with `IReadOnlyList<string> Problems(AfSpell spell, EntryDocument entry)`; `sealed class GenerationException(IReadOnlyList<string> problems)`; `static class BookWriter` with `IReadOnlyList<BookDocument> Build(IReadOnlyList<AfBook> books)`, `IReadOnlyList<string> Check(IReadOnlyList<BookDocument> books, PropertySetDocument core, PropertySetDocument part)`, `string Serialize(BookDocument)`, `void Write(IReadOnlyList<BookDocument> books, string outDir)`.

Spec → *The generator*, *Translation*, *Text and generation*. `generate` writes nothing if any spell fails to translate, disagrees with its text, names a rule neither the core document nor the part holds, or composes invalid. Its output is deterministic. `fetch` is the only path that calls Army Forge.

- [ ] **Step 1: Check in the fixtures**

From the repo root, with the dump at `.superpowers/armyforge/` (run `./scripts/army-books fetch` after Step 6 if it is missing), trim twenty books to the fields the generator reads:

```bash
python3 - <<'PY'
import json, os
src = '.superpowers/armyforge'; dst = 'tests/Tabletop.ArmyBooks.Tests/Fixtures'
uids = ['z65fgu0l29i4lnlu','7oi8zeiqfamiur21','k5T0f2XIfpmPO28h','w70ha3o85pa7nigq','jpc0kyil0juwy602','dnthspt7c0klhmt8','q11la9v8h1heu9ja',
        'fk1mkbp8apvltu0z','vux1Y5vvULmaxZ8P','7o6om21wxlvvy3hq','jlray7cwf8mvw5sn','bf20fNmJEyUS-PIX','fmuqw5lr5l6dq0mq','iV_3U33NE1_ZTXBB',
        'zz3kp5ry7ks6mxcx','78qp9l5alslt6yj8','7el7k3hgy5pb9o9i','i4ZEAmqeFSfAcw3f','xnnqhh1775kvmz2r','7ex2x15bpkmy1alv']
os.makedirs(f'{dst}/books', exist_ok=True)
index = [{k: e[k] for k in ('uid', 'name', 'versionString')} for e in json.load(open(f'{src}/index.json')) if e['uid'] in uids]
json.dump(index, open(f'{dst}/index.json', 'w'), indent=1)
for u in uids:
    b = json.load(open(f'{src}/books/{u}.json'))
    spells = [{k: s[k] for k in ('id', 'name', 'threshold', 'effect', 'generation')} for s in b['spells']]
    json.dump({'uid': b['uid'], 'name': b['name'], 'versionString': b.get('versionString'), 'spells': spells}, open(f'{dst}/books/{u}.json', 'w'), indent=1)
PY
```

Between them the twenty cover all sixteen rows of the translation table, the model target, *Zap!* (no roll sentence), the six Dwarf Guilds names that end in a space, the reprints of the disciple books, the *Shield Breaker*, *Psy-Injected Courage*, *Electric Tempest* and *Rapid Putrefaction* collisions, and the book with no spells.

- [ ] **Step 2: Write the failing tests**

Create `tests/Tabletop.ArmyBooks.Tests/Tabletop.ArmyBooks.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.ArmyBooks.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../scripts/ArmyBooks/Tabletop.ArmyBooks.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <None Include="Fixtures/**/*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Create `tests/Tabletop.ArmyBooks.Tests/SpellTranslatorTests.cs`:

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class SpellTranslatorTests
{
    public static AfBook Book(string uid) => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books", uid + ".json")));
    static EntryDocument Spell(string uid, string name) { var b = Book(uid); return SpellTranslator.Translate(b, b.Spells.Single(s => s.Name.Trim() == name)); }

    [Fact]
    public void Damage_is_number_digit_times_the_spells_value_with_its_weapon_rules_as_children()
    {
        var terror = Spell("78qp9l5alslt6yj8", "Psychic Terror");                  // value 3, numberDigit 3: "takes 9 hits with Bane"
        Assert.Equal((3, EffectKind.AddHit, 9), (terror.Cost, terror.Effects.Single().Kind, terror.Effects.Single().Amount));
        Assert.Equal([("Bane", (int?)null)], terror.Effects.Single().Children.Select(c => (c.TargetEntry!, c.Amount)));
        Assert.Equal(new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 6), terror.Targeting);
        Assert.Null(terror.Faction);
    }

    [Fact]
    public void A_spell_rolls_its_hits_only_when_it_prints_the_roll_sentence()
    {
        Assert.Null(Spell("k5T0f2XIfpmPO28h", "Zap!").Effects.Single().TargetRoll);
        Assert.Equal("hit", Spell("jpc0kyil0juwy602", "Electric Tempest").Effects.Single().TargetRoll);
    }

    [Fact]
    public void A_model_target_and_its_unit_of_one()
    {
        var expel = Spell("z65fgu0l29i4lnlu", "Expel Threat");
        Assert.Equal((TargetGrain.Model, 6), (expel.Targeting!.Grain, expel.Effects.Single().Amount));
    }

    [Theory]
    [InlineData("z65fgu0l29i4lnlu", "Psy-Injected Courage", EffectKind.GrantProperty, EffectSubject.Self, "Hold the Line Boost")]
    [InlineData("z65fgu0l29i4lnlu", "Calculated Foresight", EffectKind.GrantProperty, EffectSubject.Opponent, "Relentless")]
    [InlineData("78qp9l5alslt6yj8", "Advanced Sight", EffectKind.GrantProperty, EffectSubject.Opponent, "Unstoppable")]
    [InlineData("78qp9l5alslt6yj8", "Blessed Ammo", EffectKind.GrantProperty, EffectSubject.Self, "Shred")]
    [InlineData("7el7k3hgy5pb9o9i", "Corrode Weapons", EffectKind.ModifyParameter, EffectSubject.Self, "AP")]
    public void Grants_are_once_rows_on_the_side_the_text_names(string uid, string name, EffectKind kind, EffectSubject subject, string entry)
    {
        var row = Spell(uid, name).Effects.Single();
        Assert.Equal((kind, subject, entry, EffectDuration.Once), (row.Kind, row.Subject, row.TargetEntry, row.Duration));
    }

    [Fact]
    public void Attack_types_become_conditions_and_loses_is_minus_the_rating()
    {
        Assert.Equal(["shooting"], Spell("78qp9l5alslt6yj8", "Blessed Ammo").Effects.Single().Conditions.Single().Values);
        var corrode = Spell("7el7k3hgy5pb9o9i", "Corrode Weapons").Effects.Single();
        Assert.Equal(-1, corrode.Amount);
    }

    [Theory]
    [InlineData("7oi8zeiqfamiur21", "Burn the Heretic", "casting", -3, EffectSubject.Self)]
    [InlineData("k5T0f2XIfpmPO28h", "Shroud Field", "defense", 1, EffectSubject.Self)]
    [InlineData("jpc0kyil0juwy602", "Psy-Injected Courage", "morale", 1, EffectSubject.Self)]
    [InlineData("dnthspt7c0klhmt8", "Brain Infestation", "hit", -1, EffectSubject.Self)]
    [InlineData("q11la9v8h1heu9ja", "Raiding Drugs", "hit", 1, EffectSubject.Opponent)]
    public void Roll_modifiers(string uid, string name, string roll, int amount, EffectSubject subject)
    {
        var row = Spell(uid, name).Effects.Single();
        Assert.Equal((EffectKind.ModifyRoll, roll, amount, subject, EffectDuration.Once), (row.Kind, row.TargetRoll!, row.Amount!.Value, row.Subject, row.Duration));
    }

    [Fact]
    public void Speed_range_terrain_and_the_two_tests()
    {
        Assert.Equal([("Advance", 2), ("Rush", 4), ("Charge", 4)], Spell("z65fgu0l29i4lnlu", "Shock Speed").Effects.Select(e => (e.TargetAction!, e.Amount!.Value)));
        Assert.Equal([("Advance", -2), ("Rush", -4), ("Charge", -4)], Spell("fk1mkbp8apvltu0z", "Deceleration Rune").Effects.Select(e => (e.TargetAction!, e.Amount!.Value)));
        Assert.Equal((EffectSubject.Self, 6), (Spell("fk1mkbp8apvltu0z", "Battle Rune").Effects.Single().Subject, Spell("fk1mkbp8apvltu0z", "Battle Rune").Effects.Single().Amount!.Value));
        Assert.Equal(EffectSubject.Opponent, Spell("vux1Y5vvULmaxZ8P", "Eternal Guidance").Effects.Single().Subject);
        Assert.Equal(TargetTerrain.Dangerous, Spell("7o6om21wxlvvy3hq", "Cursed Stride").Effects.Single().TargetTerrain);
        var fury = Spell("iV_3U33NE1_ZTXBB", "Terrifying Fury").Effects.Single();
        Assert.Equal((EffectKind.TakeTest, "morale", "fatigued"), (fury.Kind, fury.TargetRoll!, fury.Children.Single().TargetCondition!));
        var hypnosis = Spell("zz3kp5ry7ks6mxcx", "Deep Hypnosis").Effects.Single();
        Assert.Equal((EffectKind.Move, 6), (hypnosis.Children.Single().Kind, hypnosis.Children.Single().Amount!.Value));
    }

    [Fact]
    public void Every_fixture_spell_translates_and_agrees_with_its_text()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books")))
        {
            var book = ArmyForgeJson.ReadBook(File.ReadAllText(file));
            foreach (var s in book.Spells) Assert.Empty(TextCheck.Problems(s, SpellTranslator.Translate(book, s)));
        }
    }

    [Fact]
    public void A_text_that_disagrees_with_its_generation_is_named()
    {
        var b = Book("78qp9l5alslt6yj8");
        var s = b.Spells.Single(x => x.Name == "Psychic Terror");
        var bad = s with { Effect = s.Effect.Replace("9 hits", "8 hits") };
        Assert.Contains(TextCheck.Problems(bad, SpellTranslator.Translate(b, bad)), p => p.Contains("9 hits"));
    }
}
```

Create `tests/Tabletop.ArmyBooks.Tests/BookWriterTests.cs`:

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class BookWriterTests
{
    static IReadOnlyList<AfBook> All() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books")).Order(StringComparer.Ordinal)
        .Select(f => ArmyForgeJson.ReadBook(File.ReadAllText(f))).ToList();

    [Fact]
    public void Reprints_merge_and_colliding_names_are_qualified_by_the_first_book_that_holds_each_variant()
    {
        var books = BookWriter.Build(All());
        var names = books.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().ToList();
        Assert.Contains("Shield Breaker (Blood Brothers)", names);
        Assert.Contains("Shield Breaker (Blood Prime Brothers)", names);
        Assert.Contains("Psy-Injected Courage (Human Defense Force)", names);
        Assert.Contains("Psy-Injected Courage (Human Inquisition)", names);
        Assert.Contains("Rapid Putrefaction (Plague Disciples)", names);
        Assert.Contains("Rapid Putrefaction (Titan Lords Plague Disciples)", names);
        Assert.DoesNotContain("Shield Breaker", names);
        Assert.Contains("Battle Rune", names);                                             // trimmed
        var plague = books.Single(b => b.Uid == "fmuqw5lr5l6dq0mq").Spells.Single(s => s.Name.StartsWith("Rapid Putrefaction"));
        Assert.Equal("Rapid Putrefaction (Plague Disciples)", plague.Name);                 // the Wormhole printing equals the Plague Disciples one
        Assert.Empty(books.Single(b => b.Uid == "i4ZEAmqeFSfAcw3f").Spells);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var a = BookWriter.Build(All()).Select(BookWriter.Serialize).ToList();
        var b = BookWriter.Build(All().Reverse().ToList()).Select(BookWriter.Serialize).ToList();
        Assert.Equal(a.Order(StringComparer.Ordinal), b.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_rule_no_document_holds_is_refused_and_so_is_an_invalid_composition()
    {
        var core = new PropertySetDocument("core", "grimdark-future", [], [], [], []);
        var problems = BookWriter.Check(BookWriter.Build(All()), core, new PropertySetDocument("part", "grimdark-future", [], [], [], []));
        Assert.Contains(problems, p => p.Contains("names no rule 'Bane'"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.ArmyBooks.Tests`
Expected: build error (the project `scripts/ArmyBooks` does not exist).

- [ ] **Step 4: The project and the dump's shape**

Create `scripts/ArmyBooks/Tabletop.ArmyBooks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <RootNamespace>Tabletop.ArmyBooks</RootNamespace>
    <AssemblyName>Tabletop.ArmyBooks</AssemblyName>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../server/game/Tabletop.GameServer.csproj" />
  </ItemGroup>
</Project>
```

Create `scripts/ArmyBooks/ArmyForgeBook.cs`:

```csharp
using System.Text.Json;

namespace Tabletop.ArmyBooks;

// The subset of Army Forge's army-book API this tool reads. Field names are the API's. A parameter's value is a string or an object
// ({id, name, rating?}), so it stays a JsonElement until the translator reads it.
public sealed record AfIndexEntry(string Uid, string Name, string? VersionString);
public sealed record AfBook(string Uid, string Name, string? VersionString, List<AfSpell> Spells);
public sealed record AfSpell(string Id, string Name, int Threshold, string Effect, AfGeneration Generation);
public sealed record AfGeneration(AfGenPart Effect, AfGenPart Assignment);
public sealed record AfGenPart(string Id, List<AfParam> Parameters)
{
    public string? Text(string name) => Parameters.FirstOrDefault(p => p.Name == name)?.Value is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    public (string Name, int? Rating)? Rule(string name) =>
        Parameters.FirstOrDefault(p => p.Name == name)?.Value is { ValueKind: JsonValueKind.Object } v
            ? (v.GetProperty("name").GetString()!, v.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null) : null;
}
public sealed record AfParam(string Name, JsonElement Value);

public static class ArmyForgeJson
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static AfBook ReadBook(string json) => JsonSerializer.Deserialize<AfBook>(json, Options) ?? throw new JsonException("empty book");
    public static List<AfIndexEntry> ReadIndex(string json) => JsonSerializer.Deserialize<List<AfIndexEntry>>(json, Options) ?? [];
}

/// <summary>Every problem one generate run found, each naming its book and spell. The run writes nothing.</summary>
public sealed class GenerationException(IReadOnlyList<string> problems) : Exception(string.Join("\n", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
```

- [ ] **Step 5: The translation**

Create `scripts/ArmyBooks/SpellTranslator.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>Army Forge's `generation` → a spell entry, row by row of the spell library spec's translation table.</summary>
public static partial class SpellTranslator
{
    /// <summary>Hand-written entries keyed by Army Forge spell id, used in place of a translation the printed text disagrees with. Empty.</summary>
    public static IReadOnlyDictionary<string, EntryDocument> Overrides { get; } = new Dictionary<string, EntryDocument>();

    [GeneratedRegex(@"(-?\+?\d+)")] private static partial Regex Number();

    public static EntryDocument Translate(AfBook book, AfSpell spell)
    {
        if (Overrides.TryGetValue(spell.Id, out var given)) return given;
        var a = spell.Generation.Assignment; var e = spell.Generation.Effect;
        var where = $"{book.Name}: {spell.Name.Trim()}";
        var targeting = new SpellTargeting(
            a.Text("friendly-enemy") switch { "friendly" => TargetSide.Friendly, "enemy" => TargetSide.Enemy, var x => throw Bad(where, $"side '{x}'") },
            a.Text("target") switch { "unit" => TargetGrain.Unit, "model" => TargetGrain.Model, var x => throw Bad(where, $"target '{x}'") },
            a.Text("numberText") switch { "one" => 1, "up to two" => 2, "up to three" => 3, var x => throw Bad(where, $"count '{x}'") },
            Int(a.Text("inRange"), where));
        IReadOnlyList<EffectDocument> rows = e.Id switch
        {
            "QFjxWsOHTCSi" => [Damage(spell, e, where)],
            "RWnJrrMKAUbg" => [Grant(e.Rule("unitRule"), EffectSubject.Self, [], where)],
            "1yTPcanTyckX" => [Grant(e.Rule("unitRule"), EffectSubject.Opponent, [], where)],
            "54xO9EKTj-ix" => [Grant(e.Rule("weaponRule"), EffectSubject.Opponent, Attack(e.Text("attackType"), where), where)],
            "i1BVqztsr1V6" => [e.Text("gets/loses/ignores") == "loses" ? Loses(e.Rule("weaponRule"), Attack(e.Text("attackType"), where), where)
                                : Grant(e.Rule("weaponRule"), EffectSubject.Self, Attack(e.Text("attackType"), where), where)],
            "4xqVFHJrq78d" => [RollMod("casting", Int(e.Text("rollModifier"), where), EffectSubject.Self, [])],
            "txtrnRX2fPJJ" => [RollMod("defense", Int(e.Text("rollModifier"), where), EffectSubject.Self, [])],
            "nxIQS1UnUBL_" => [RollMod("morale", Int(e.Text("rollModifier"), where), EffectSubject.Self, [])],
            "gqflE2vy38aQ" => [RollMod("hit", Int(e.Text("rollModifier"), where), EffectSubject.Self, Attack(e.Text("attackType"), where))],
            "YbOvC9oIIli9" => [RollMod("hit", Int(e.Text("rollModifier"), where), EffectSubject.Opponent, Attack(e.Text("attackType"), where))],
            "rz0b1dShgnvg" => [Speed("Advance", Int(e.Text("advanceSpeed"), where)), Speed("Rush", Int(e.Text("rushSpeed"), where)), Speed("Charge", Int(e.Text("rushSpeed"), where))],
            "7w-FrWUjKqqG" => [Range(Int(e.Text("rangeModifier"), where), EffectSubject.Self)],
            "LixCCuyRq4-_" => [Range(Int(e.Text("rangeModifier"), where), EffectSubject.Opponent)],
            "_m0wja4blbg8" => [Row(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once) with { TargetTerrain = e.Text("terrainType") switch
                { "Difficult Terrain" => TargetTerrain.Difficult, "Dangerous Terrain" => TargetTerrain.Dangerous, var x => throw Bad(where, $"terrain '{x}'") } }],
            "ZcZbsyKjR5yN" => [Row(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction) with { TargetRoll = "morale",
                Children = [Row(EffectKind.SetCondition, EffectSubject.Self, EffectDuration.Interaction, 20) with { TargetCondition = "fatigued" }] }],
            "q9OJ_WVwJw5w" => [Row(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction) with { TargetRoll = "morale",
                Children = [Row(EffectKind.Move, EffectSubject.Self, EffectDuration.Interaction, 20) with { Amount = Int(e.Text("moraleMove"), where) }] }],
            var id => throw Bad(where, $"effect id '{id}' is not in the translation table"),
        };
        return new EntryDocument(spell.Name.Trim(), CatalogKind.Spell, false, null, spell.Threshold, null, spell.Effect, targeting, rows.ToList());
    }

    static EffectDocument Row(EffectKind kind, EffectSubject subject, EffectDuration duration, int ordinal = 10) =>
        new(subject, ordinal, kind, null, null, null, null, null, null, null, null, false, null, null, null, duration, [], []);

    /// <summary>Row 1 and 2: hits are numberDigit × the spell's value (all 141 damage spells); weapon rules are grant children; the roll sentence sets target_roll.</summary>
    static EffectDocument Damage(AfSpell spell, AfGenPart e, string where)
    {
        if (e.Text("hits-wounds") != "hits") throw Bad(where, $"hits-wounds '{e.Text("hits-wounds")}'");
        var children = new[] { e.Rule("weaponRule1"), e.Rule("weaponRule2") }.OfType<(string Name, int? Rating)>()
            .Select(r => Row(EffectKind.GrantProperty, EffectSubject.Self, EffectDuration.Interaction, 20) with { TargetEntry = r.Name, Amount = r.Rating }).ToList();
        return Row(EffectKind.AddHit, EffectSubject.Self, EffectDuration.Interaction) with
        {
            Amount = Int(e.Text("numberDigit"), where) * spell.Threshold,
            TargetRoll = string.IsNullOrEmpty(e.Text("spellHitRoll")) ? null : "hit",
            Children = children,
        };
    }

    static EffectDocument Grant((string Name, int? Rating)? rule, EffectSubject subject, IReadOnlyList<ConditionDocument> when, string where) =>
        rule is { } r ? Row(EffectKind.GrantProperty, subject, EffectDuration.Once) with { TargetEntry = r.Name, Amount = r.Rating, Conditions = when } : throw Bad(where, "no rule named");

    static EffectDocument Loses((string Name, int? Rating)? rule, IReadOnlyList<ConditionDocument> when, string where) =>
        rule is { } r ? Row(EffectKind.ModifyParameter, EffectSubject.Self, EffectDuration.Once) with { TargetEntry = r.Name, Amount = -(r.Rating ?? 1), Conditions = when } : throw Bad(where, "no rule named");

    static EffectDocument RollMod(string roll, int amount, EffectSubject subject, IReadOnlyList<ConditionDocument> when) =>
        Row(EffectKind.ModifyRoll, subject, EffectDuration.Once) with { TargetRoll = roll, Amount = amount, Conditions = when };

    static EffectDocument Speed(string action, int amount) => Row(EffectKind.ModifyStat, EffectSubject.Self, EffectDuration.Once) with { TargetAction = action, Amount = amount };

    static EffectDocument Range(int amount, EffectSubject subject) => Row(EffectKind.ModifyStat, subject, EffectDuration.Once) with
        { TargetStat = new StatRef(StatLevel.Weapon, "Range"), Amount = amount, Conditions = [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"])] };

    static IReadOnlyList<ConditionDocument> Attack(string? phrase, string where) => phrase switch
    {
        "when shooting" => [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"])],
        "in melee" => [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["melee"])],
        "when attacking" => [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting", "melee"])],
        _ => throw Bad(where, $"attack type '{phrase}'"),
    };

    static int Int(string? text, string where) =>
        text is not null && Number().Match(text) is { Success: true } m ? int.Parse(m.Value.Replace("+", ""), CultureInfo.InvariantCulture) : throw Bad(where, $"no number in '{text}'");

    static GenerationException Bad(string where, string what) => new([$"{where}: {what}"]);
}
```

- [ ] **Step 6: The text check, the writer and the console**

Create `scripts/ArmyBooks/TextCheck.cs`:

```csharp
using System.Text.RegularExpressions;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>The printed text wins (spell library → Text and generation): every fact the translation took from `generation` must be in `effect`.</summary>
public static partial class TextCheck
{
    [GeneratedRegex(@"takes? (\d+) hits?")] private static partial Regex Hits();

    public static IReadOnlyList<string> Problems(AfSpell spell, EntryDocument entry)
    {
        var problems = new List<string>(); var text = spell.Effect; var where = spell.Name.Trim();
        var a = spell.Generation.Assignment; var e = spell.Generation.Effect;
        var plural = a.Text("numberText") == "one" ? "" : "s";
        var clause = $"Pick {a.Text("numberText")} {a.Text("friendly-enemy")} {a.Text("target")}{plural} {a.Text("inRange")}";
        if (!text.StartsWith(clause, StringComparison.Ordinal)) problems.Add($"{where}: generation says '{clause}'; the text does not");
        if (entry.Effects.FirstOrDefault(f => f.Kind == EffectKind.AddHit) is { } hits && !(Hits().Match(text) is { Success: true } m && m.Groups[1].Value == hits.Amount.ToString()))
            problems.Add($"{where}: generation says {hits.Amount} hits; the text does not");
        foreach (var key in new[] { "weaponRule1", "weaponRule2", "weaponRule", "unitRule" })
            if (e.Rule(key) is { } r && !text.Contains(r.Rating is { } n ? $"{r.Name}({n})" : r.Name, StringComparison.Ordinal))
                problems.Add($"{where}: generation names {r.Name}{(r.Rating is { } k ? $"({k})" : "")}; the text does not");
        foreach (var key in new[] { "attackType", "rollModifier", "terrainType" })
            if (e.Text(key) is { Length: > 0 } phrase && !text.Contains(phrase, StringComparison.Ordinal))
                problems.Add($"{where}: generation says '{phrase}'; the text does not");
        return problems;
    }
}
```

Create `scripts/ArmyBooks/BookWriter.cs`:

```csharp
using System.Text.Json;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>Translated books, with each spell name made unique across the corpus, checked against the core document and the part, and written.</summary>
public static class BookWriter
{
    static readonly JsonSerializerOptions Indented = new(PropertiesJson.Options) { WriteIndented = true };

    public static IReadOnlyList<BookDocument> Build(IReadOnlyList<AfBook> books)
    {
        var ordered = books.OrderBy(b => b.Uid, StringComparer.Ordinal).ToList();
        var translated = ordered.Select(b => (Book: b, Spells: b.Spells.Select(s => SpellTranslator.Translate(b, s)).ToList())).ToList();
        // A name every holder uses for one spell stays bare; a name used for two or more spells names each variant after the
        // alphabetically first book (by name) that holds it.
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);   // canonical form → name
        foreach (var group in translated.SelectMany(t => t.Spells.Select(s => (t.Book, Spell: s))).GroupBy(x => x.Spell.Name))
        {
            var variants = group.GroupBy(x => PropertiesJson.EntryCanonical(x.Spell)).ToList();
            foreach (var v in variants)
                rename[v.Key] = variants.Count == 1 ? group.Key : $"{group.Key} ({v.Select(x => x.Book.Name).Order(StringComparer.Ordinal).First()})";
        }
        return translated.Select(t => new BookDocument(t.Book.Uid, t.Book.Name, t.Book.VersionString, "grimdark-future",
            t.Spells.Select(s => s with { Name = rename[PropertiesJson.EntryCanonical(s)] }).ToList())).ToList();
    }

    /// <summary>Every rule a spell names exists in the core document or the part, and the composition validates.</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<BookDocument> books, PropertySetDocument core, PropertySetDocument part)
    {
        var rules = core.Entries.Concat(part.Entries).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        static IEnumerable<EffectDocument> All(EffectDocument f) => new[] { f }.Concat(f.Children.SelectMany(All));
        var problems = books.SelectMany(b => b.Spells.SelectMany(s => s.Effects.SelectMany(All)
                .Where(f => f.TargetEntry is not null && !rules.Contains(f.TargetEntry))
                .Select(f => $"{b.Name}: {s.Name} names no rule '{f.TargetEntry}'"))).Distinct().ToList();
        if (problems.Count > 0) return problems;
        try { return PropertiesJson.Validate(PropertiesSeeder.Compose(core, [part], books)).ToList(); }
        catch (InvalidDataException ex) { return [ex.Message]; }
    }

    public static string Serialize(BookDocument book) => JsonSerializer.Serialize(book, Indented) + "\n";

    public static void Write(IReadOnlyList<BookDocument> books, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var b in books) File.WriteAllText(Path.Combine(outDir, b.Uid + ".book.json"), Serialize(b));
    }
}
```

Create `scripts/ArmyBooks/Program.cs`:

```csharp
using Tabletop.ArmyBooks;
using Tabletop.GameServer.Properties;

var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Tabletop.sln"))) repo = repo.Parent;
if (repo is null) { Console.Error.WriteLine("run from inside the repository"); return 2; }
string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
var dump = Path.GetFullPath(Arg(args.FirstOrDefault() == "fetch" ? "--to" : "--from", Path.Combine(repo.FullName, ".superpowers", "armyforge")));

switch (args.FirstOrDefault())
{
    case "fetch":
    {
        // The only code path that calls Army Forge. Never run by a test.
        using var http = new HttpClient { BaseAddress = new Uri("https://army-forge.onepagerules.com") };
        var index = await http.GetStringAsync("/api/army-books?filters=official&gameSystemSlug=grimdark-future");
        Directory.CreateDirectory(Path.Combine(dump, "books"));
        await File.WriteAllTextAsync(Path.Combine(dump, "index.json"), index);
        foreach (var e in ArmyForgeJson.ReadIndex(index))
            await File.WriteAllTextAsync(Path.Combine(dump, "books", e.Uid + ".json"), await http.GetStringAsync($"/api/army-books/{e.Uid}?gameSystem=2"));
        Console.WriteLine($"fetched {ArmyForgeJson.ReadIndex(index).Count} books into {dump}");
        return 0;
    }
    case "generate":
    {
        var index = ArmyForgeJson.ReadIndex(File.ReadAllText(Path.Combine(dump, "index.json")));
        var books = index.Select(e => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(dump, "books", e.Uid + ".json"))) with { Name = e.Name, VersionString = e.VersionString }).ToList();
        var problems = books.SelectMany(b => b.Spells.SelectMany(s =>
        {
            try { return TextCheck.Problems(s, SpellTranslator.Translate(b, s)).Select(p => $"{b.Name}: {p}"); }
            catch (GenerationException ex) { return ex.Problems; }
        })).ToList();
        IReadOnlyList<BookDocument> built = [];
        if (problems.Count == 0)
        {
            built = BookWriter.Build(books);
            var content = Path.Combine(repo.FullName, "server", "game", "Content");
            problems.AddRange(BookWriter.Check(built,
                PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-core.properties.json"))),
                PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-army-rules.part.json")))));
        }
        if (problems.Count > 0) { foreach (var p in problems) Console.Error.WriteLine(p); return 1; }
        BookWriter.Write(built, Arg("--out", Path.Combine(repo.FullName, "server", "game", "Content", "army-books")));
        Console.WriteLine($"wrote {built.Count} books, {built.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().Count()} spells");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: army-books fetch [--to DIR] | generate [--from DIR] [--out DIR]");
        return 2;
}
```

Create `scripts/army-books` (and `chmod +x scripts/army-books`):

```bash
#!/usr/bin/env bash
# Fetch the official Grimdark Future army books from Army Forge, or generate server/game/Content/army-books/ from a fetched copy.
# Spell library spec → The generator. `fetch` is the only command that calls Army Forge.
set -euo pipefail
cd "$(dirname "$0")/.."
exec dotnet run --project scripts/ArmyBooks -- "$@"
```

Add both projects to the solution:

```bash
dotnet sln Tabletop.sln add --solution-folder scripts scripts/ArmyBooks/Tabletop.ArmyBooks.csproj
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.ArmyBooks.Tests/Tabletop.ArmyBooks.Tests.csproj
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.ArmyBooks.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add scripts/army-books scripts/ArmyBooks tests/Tabletop.ArmyBooks.Tests Tabletop.sln
git commit -m "Add the army-books generator: Army Forge generation to spell entries, checked against the printed text"
```
