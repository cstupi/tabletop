# Task 6b: The generator writes the rules part

**Files:**
- Modify: `scripts/ArmyBooks/RuleWriter.cs` (`Build`, `Serialize`, `RuleReport`)
- Modify: `scripts/ArmyBooks/BookWriter.cs` (`Check` takes the generated part), `scripts/ArmyBooks/Program.cs` (`generate`)
- Modify: `server/game/Properties/PropertiesSeeder.cs` (`GeneratedPart`)
- Modify: `scripts/ArmyBooks/AGENTS.md`
- Test: `tests/Tabletop.ArmyBooks.Tests/RuleWriterTests.cs` (new)

**Interfaces:**
- Consumes: Task 6a's records, `RuleTemplates`, `RuleWriter.NamesInScope`, the rule fixtures and
  `RuleTemplatesTests.Read`; `EntryDocument.Played` (Task 1); pick-entry validation (Task 3).
- Produces: `PropertiesSeeder.GeneratedPart = "grimdark-future-army-book-rules.part.json"`; `RuleWriter.Build/Serialize`,
  `RuleReport` (README → Fixed interfaces); `BookWriter.Check(books, core, hand, PropertySetDocument? generated = null)`;
  `generate` writes `server/game/Content/grimdark-future-army-book-rules.part.json` and prints the report. It does not
  run for real here (Task 8 does).

Spec *Generator*: "**Input.** Each book's `specialRules` (name, description) … Two books giving one name different
descriptions fail the run and write nothing. **Names in scope.** Every rule name on a unit or an upgrade in the official
books. A name core or the hand-authored part defines is skipped. **A wrapper is generated** when its name ends in Aura,
Buff, Mark or Debuff, its description is that pattern's template with X equal to the name less the suffix, and X is a
catalog entry. The entry's rows point at X. **Every other name is a text-only entry**: kind `model`, aggregation `any`,
the description as `ruleText`, no effects, `played: false`. **Report.** The run prints the counts of the table in *The
gap*."

Readings this task fixes:

- *On a unit or an upgrade* is a unit's `rules`, its weapons' `specialRules`, its `items[].content` rules, and every
  upgrade option's gains (an `ArmyBookRule`, an `ArmyBookWeapon`'s `specialRules`, an `ArmyBookItem`'s `content`). The
  spec's batch 1 names `Ignores Cover` and `Ignores Regeneration`, which the books print only on weapons.
- A name the books print only on weapons is text-only `kind: weapon` (no aggregation): its authored entry is a weapon
  entry, and `PostgresPropertiesStore.SeedAsync` refuses a name whose kind changes between sets of one game rules.
- X is a catalog entry when core or the hand-authored part defines it; a generated text-only name is no base.
- A description is compared and written trimmed (`Versatile Defense` ends in a newline in the dump).
- A name with no description anywhere (Banner, Musician) is text-only with an empty `ruleText`, listed under "no
  description".

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.ArmyBooks.Tests/RuleWriterTests.cs`:

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class RuleWriterTests
{
    static AfBook Read(string path) => RuleTemplatesTests.Read(path);
    static IReadOnlyList<AfBook> Books() => [Read("books/rules-a.json"), Read("books/rules-b.json")];

    static readonly PropertySetDocument Core = PropertiesJson.Parse("""
    { "name": "core", "gameRules": "grimdark-future", "rolls": [ "hit", "defense" ],
      "entries": [
        { "name": "Stealth", "kind": "model", "aggregation": "all", "ruleText": "S.", "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1 } ] },
        { "name": "Rending", "kind": "weapon", "ruleText": "R." },
        { "name": "AP", "kind": "weapon", "takesParameter": true, "ruleText": "AP." } ] }
    """);
    static readonly PropertySetDocument Hand = PropertiesJson.Parse("""
    { "name": "hand", "gameRules": "grimdark-future", "entries": [ { "name": "Bane when Shooting", "kind": "model", "aggregation": "all", "ruleText": "This model gets Bane when shooting." } ] }
    """);

    static PropertySetDocument Part() => RuleWriter.Build(Books(), Core, Hand).Part!;
    static EntryDocument Entry(string name) => Part().Entries.Single(e => e.Name == name);

    [Fact]
    public void A_template_wrapper_is_generated_when_its_base_is_in_the_catalog()
    {
        var aura = Entry("Stealth Aura");
        Assert.Equal((CatalogKind.Model, Aggregation.Any, (bool?)null, "This model and its unit get Stealth."), (aura.Kind, aura.Aggregation, aura.Played, aura.RuleText));
        Assert.Equal((EffectSubject.Self, EffectKind.GrantProperty, "Stealth", EffectDuration.Interaction), (aura.Effects.Single().Subject, aura.Effects.Single().Kind, aura.Effects.Single().TargetEntry, aura.Effects.Single().Duration));
        var buff = Entry("Stealth Buff");
        Assert.Equal(new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, false), buff.Targeting);
        Assert.Equal((EffectSubject.Self, "Stealth", EffectDuration.Once, "before-attacking"),
            (buff.Effects.Single().Subject, buff.Effects.Single().TargetEntry, buff.Effects.Single().Duration, buff.Effects.Single().Conditions.Single(c => c.Kind == ConditionKind.AtPoint).Values.Single()));
        var mark = Entry("Rending Mark");
        Assert.Equal(new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), mark.Targeting);
        Assert.Equal((EffectSubject.Opponent, "Rending", EffectDuration.Once), (mark.Effects.Single().Subject, mark.Effects.Single().TargetEntry, mark.Effects.Single().Duration));
    }

    [Fact]
    public void Every_other_name_is_text_only_and_a_name_core_or_the_hand_part_defines_is_skipped()
    {
        foreach (var name in new[] { "Fortified Aura", "Precision Shooter Aura", "Defense Debuff", "Precise", "Swift Buff" })
        {
            var e = Entry(name);
            Assert.Equal((CatalogKind.Model, (Aggregation?)Aggregation.Any, (bool?)false, 0), (e.Kind, e.Aggregation, e.Played, e.Effects.Count));
        }
        Assert.Equal("Gets +1 to hit when attacking.", Entry("Precise").RuleText);      // trimmed
        Assert.DoesNotContain(Part().Entries, e => e.Name is "AP" or "Bane when Shooting");
    }

    [Fact]
    public void A_name_printed_only_on_weapons_is_a_weapon_entry()
    {
        var e = Entry("Ignores Cover");
        Assert.Equal((CatalogKind.Weapon, (Aggregation?)null, (bool?)false, "This weapon ignores cover."), (e.Kind, e.Aggregation, e.Played, e.RuleText));
    }

    [Fact]
    public void The_report_counts_the_groups_and_names_what_has_no_description()
    {
        var report = RuleWriter.Build(Books(), Core, Hand).Report!;
        Assert.Equal(3, report.Generated);
        var counts = report.Groups.ToDictionary(g => g.Group, g => g.Count);
        Assert.Equal(1, counts["Aura, template text, base in catalog"]);
        Assert.Equal(1, counts["Aura, template text, base missing"]);
        Assert.Equal(1, counts["Aura, effect written out"]);
        Assert.Equal(1, counts["Buff, template text, base in catalog"]);
        Assert.Equal(1, counts["Buff, template text, base missing"]);
        Assert.Equal(1, counts["Mark, template text, base in catalog"]);
        Assert.Equal(1, counts["Debuff, effect written out"]);
        Assert.Equal(3, counts["Standalone, own text"]);                            // Banner, Ignores Cover, Precise
        Assert.Equal(["Banner"], report.NoDescription);
        Assert.Equal("", Entry("Banner").RuleText);
    }

    [Fact]
    public void Two_descriptions_of_one_name_fail_and_nothing_is_built()
    {
        var (part, report, problems) = RuleWriter.Build([.. Books(), Read("conflict/rules-c.json")], Core, Hand);
        Assert.Null(part);
        Assert.Null(report);
        Assert.Equal(["'Precise' has two descriptions: Rules A and Rules C"], problems);
    }

    [Fact]
    public void The_part_composes_validates_and_serializes_the_same_whatever_the_book_order()
    {
        Assert.Empty(BookWriter.Check([], Core, Hand, Part()));
        var reversed = RuleWriter.Build(Books().Reverse().ToList(), Core, Hand).Part!;
        Assert.Equal(RuleWriter.Serialize(Part()), RuleWriter.Serialize(reversed));
        Assert.EndsWith("\n", RuleWriter.Serialize(Part()));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.ArmyBooks.Tests --filter "FullyQualifiedName~RuleWriterTests"`
Expected: build fails with `CS0117: 'RuleWriter' does not contain a definition for 'Build'`.

- [ ] **Step 3: The generated part's name**

In `server/game/Properties/PropertiesSeeder.cs`, add as the class's first member:

```csharp
    /// <summary>The part scripts/ArmyBooks generates (army-book rules spec → Generator): template wrappers and text-only entries. Never edited by hand.</summary>
    public const string GeneratedPart = "grimdark-future-army-book-rules.part.json";
```

- [ ] **Step 4: The writer**

Replace `scripts/ArmyBooks/RuleWriter.cs` (Task 6a's `NamesInScope` kept as it is):

```csharp
using System.Text.Json;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>Generated: the count of wrappers written. Groups: the spec's table (The gap), in its order. NoDescription: names no book describes.</summary>
public sealed record RuleReport(int Generated, IReadOnlyList<(string Group, int Count)> Groups, IReadOnlyList<string> NoDescription);

/// <summary>The army-book rules part (army-book rules spec → Generator): a wrapper per template name whose base the catalog holds, a text-only
/// entry per other name in scope that core and the hand-authored part leave undefined.</summary>
public static class RuleWriter
{
    static readonly JsonSerializerOptions Indented = new(PropertiesJson.Options) { WriteIndented = true, NewLine = "\n" };
    static readonly string[] GroupOrder =
    [
        "Aura, template text, base in catalog", "Aura, template text, base missing", "Aura, effect written out",
        "Buff, template text, base in catalog", "Buff, template text, base missing", "Buff, effect written out",
        "Mark, template text, base in catalog", "Mark, template text, base missing", "Mark, effect written out",
        "Debuff, effect written out", "Standalone, own text",
    ];

    /// <summary>Every rule name a unit or an upgrade carries, true when any carries it as a rule (a model's), false when only weapons do.</summary>
    public static IReadOnlyDictionary<string, bool> NamesInScope(AfBook book)
    {
        var names = new Dictionary<string, bool>(StringComparer.Ordinal);
        void Rule(string n) => names[n] = true;
        void WeaponRule(string n) => names.TryAdd(n, false);
        void Gain(AfGain g)
        {
            switch (g.Type)
            {
                case "ArmyBookRule" when g.Name is { } n: Rule(n); break;
                case "ArmyBookWeapon": foreach (var r in g.SpecialRules ?? []) WeaponRule(r.Name); break;
                case "ArmyBookItem": foreach (var c in g.Content ?? []) Gain(c); break;
            }
        }
        foreach (var u in book.Units ?? [])
        {
            foreach (var r in u.Rules ?? []) Rule(r.Name);
            foreach (var r in (u.Weapons ?? []).SelectMany(w => w.SpecialRules ?? [])) WeaponRule(r.Name);
            foreach (var i in u.Items ?? []) Gain(i);
        }
        foreach (var g in (book.UpgradePackages ?? []).SelectMany(p => p.Sections ?? []).SelectMany(s => s.Options ?? []).SelectMany(o => o.Gains ?? [])) Gain(g);
        return names;
    }

    public static (PropertySetDocument? Part, RuleReport? Report, IReadOnlyList<string> Problems) Build(IReadOnlyList<AfBook> books, PropertySetDocument core, PropertySetDocument hand)
    {
        var ordered = books.OrderBy(b => b.Name, StringComparer.Ordinal).ToList();
        var descriptions = new Dictionary<string, (string Text, string Book)>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var b in ordered)
            foreach (var r in b.SpecialRules ?? [])
            {
                var text = (r.Description ?? "").Trim();
                if (!descriptions.TryGetValue(r.Name, out var had)) descriptions[r.Name] = (text, b.Name);
                else if (had.Text != text) problems.Add($"'{r.Name}' has two descriptions: {had.Book} and {b.Name}");
            }
        if (problems.Count > 0) return (null, null, problems.Distinct().ToList());

        var defined = core.Entries.Concat(hand.Entries).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var scope = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (name, isRule) in ordered.SelectMany(NamesInScope)) scope[name] = scope.GetValueOrDefault(name) || isRule;
        var entries = new List<EntryDocument>();
        var counts = GroupOrder.ToDictionary(g => g, _ => 0);
        var noDescription = new List<string>();
        foreach (var name in scope.Keys.Where(n => !defined.Contains(n)).Order(StringComparer.Ordinal))
        {
            if (!descriptions.TryGetValue(name, out var d)) noDescription.Add(name);
            var text = d.Text ?? "";
            var pattern = RuleTemplates.PatternOf(name);
            var x = pattern is { } p && p != RulePattern.Debuff ? RuleTemplates.Base(p, name, text) : null;
            if (x is not null && defined.Contains(x))
            {
                entries.Add(RuleTemplates.Wrapper(pattern!.Value, name, x, text));
                counts[$"{pattern}, template text, base in catalog"]++;
                continue;
            }
            counts[pattern switch
            {
                null => "Standalone, own text",
                RulePattern.Debuff => "Debuff, effect written out",
                { } q when x is not null => $"{q}, template text, base missing",
                { } q => $"{q}, effect written out",
            }]++;
            var weaponOnly = !scope[name];
            entries.Add(new EntryDocument(name, weaponOnly ? CatalogKind.Weapon : CatalogKind.Model, false, weaponOnly ? null : Aggregation.Any, null, null, text, null, [], Played: false));
        }
        var part = new PropertySetDocument("Grimdark Future army-book rules (generated)", "grimdark-future", [], [], [], entries);
        var generated = GroupOrder.Where(g => g.EndsWith("base in catalog")).Sum(g => counts[g]);
        return (part, new RuleReport(generated, GroupOrder.Select(g => (g, counts[g])).ToList(), noDescription), []);
    }

    public static string Serialize(PropertySetDocument part) =>
        JsonSerializer.Serialize(part with { Entries = part.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList() }, Indented) + "\n";
}
```

- [ ] **Step 5: `BookWriter.Check` takes the generated part**

In `scripts/ArmyBooks/BookWriter.cs`, replace `Check`:

```csharp
    /// <summary>Every rule a spell names exists in the core document or the hand-authored part (a generated text-only entry plays nothing, so it
    /// is no target), and the composition with the generated part, when given, validates.</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<BookDocument> books, PropertySetDocument core, PropertySetDocument part, PropertySetDocument? generated = null)
    {
        var rules = core.Entries.Concat(part.Entries).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        static IEnumerable<EffectDocument> All(EffectDocument f) => new[] { f }.Concat(f.Children.SelectMany(All));
        var problems = books.SelectMany(b => b.Spells.SelectMany(s => s.Effects.SelectMany(All)
                .Where(f => f.TargetEntry is not null && !rules.Contains(f.TargetEntry))
                .Select(f => $"{b.Name}: {s.Name} names no rule '{f.TargetEntry}'"))).Distinct().ToList();
        if (problems.Count > 0) return problems;
        try { return PropertiesJson.Validate(PropertiesSeeder.Compose(core, generated is null ? [part] : [part, generated], books)).ToList(); }
        catch (InvalidDataException ex) { return [ex.Message]; }
    }
```

- [ ] **Step 6: `generate` writes the part**

In `scripts/ArmyBooks/Program.cs`, replace everything from `IReadOnlyList<BookDocument> built = [];` to the end of the
`generate` case's `return 0;` with:

```csharp
        IReadOnlyList<BookDocument> built = [];
        var content = Path.Combine(repo.FullName, "server", "game", "Content");
        var core = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-core.properties.json")));
        var hand = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-army-rules.part.json")));
        var (rules, report, ruleProblems) = RuleWriter.Build(books, core, hand);
        problems.AddRange(ruleProblems);
        if (problems.Count == 0)
        {
            built = BookWriter.Build(books);
            problems.AddRange(BookWriter.Check(built, core, hand, rules));
        }
        if (problems.Count > 0) { foreach (var p in problems) Console.Error.WriteLine(p); return 1; }
        BookWriter.Write(built, Arg("--out", Path.Combine(content, "army-books")));
        var rulesPath = Arg("--rules", Path.Combine(content, PropertiesSeeder.GeneratedPart));
        File.WriteAllText(rulesPath, RuleWriter.Serialize(rules!));
        Console.WriteLine($"wrote {built.Count} books, {built.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().Count()} spells");
        Console.WriteLine($"wrote {rulesPath}: {report!.Generated} generated, {rules!.Entries.Count - report.Generated} text-only");
        foreach (var (group, count) in report.Groups) Console.WriteLine($"  {group}: {count}");
        if (report.NoDescription.Count > 0) Console.WriteLine($"  no description: {string.Join(", ", report.NoDescription)}");
        return 0;
```

and the usage line:

```csharp
        Console.Error.WriteLine("usage: army-books fetch [--to DIR] | generate [--from DIR] [--out DIR] [--rules FILE]");
```

- [ ] **Step 7: Run the generator tests**

Run: `dotnet test tests/Tabletop.ArmyBooks.Tests`
Expected: PASS, the existing `SpellTranslatorTests` and `BookWriterTests` included.

- [ ] **Step 8: The module notes**

In `scripts/ArmyBooks/AGENTS.md`:
- In the first paragraph, after "Writes `server/game/Content/army-books/<uid>.book.json`", add "and
  `server/game/Content/grimdark-future-army-book-rules.part.json`".
- In *Layout*, replace the `ArmyForgeBook.cs` line with "`ArmyForgeBook.cs`: `AfIndexEntry`, `AfBook` (spells, rule
  texts, units, upgrades), `AfSpell` and `ArmyForgeJson`." and add:
  "- `RuleTemplates.cs`: the Aura, Buff and Mark template texts and the entry each becomes.
  - `RuleWriter.cs`: names in scope, a wrapper or a text-only entry per name core and the hand part leave undefined, the report."
- In *How it works*, add: "- `generate` also writes the army-book rules part: every rule name on a unit, a weapon, an
  item or an upgrade that core and `grimdark-future-army-rules.part.json` do not define becomes a template wrapper (an
  Aura, Buff or Mark whose text is its template and whose base the catalog holds) or a `played: false` text-only entry
  (`kind: weapon` when only weapons carry it). Two books describing one name differently fail the run. It prints the
  count per group and the names with no description."
- In *Commands*, change the generate line to `./scripts/army-books generate [--from DIR] [--out DIR] [--rules FILE]`.

- [ ] **Step 9: Commit**

```bash
git add scripts/ArmyBooks/RuleWriter.cs scripts/ArmyBooks/BookWriter.cs scripts/ArmyBooks/Program.cs scripts/ArmyBooks/AGENTS.md \
  server/game/Properties/PropertiesSeeder.cs tests/Tabletop.ArmyBooks.Tests/RuleWriterTests.cs
git commit -m "Generate the army-book rules part: template wrappers and text-only entries"
```
