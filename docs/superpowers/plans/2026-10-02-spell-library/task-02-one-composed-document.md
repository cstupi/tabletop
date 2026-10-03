# Task 2: One composed document

**Files:**
- Modify: `server/game/Properties/PropertySetDocument.cs` (`BookDocument`, public `PropertiesJson.Options`, `ParseBook`, `EntryCanonical`)
- Modify: `server/game/Properties/PropertiesSeeder.cs` (`Compose`, composed `EmbeddedDocuments`)
- Modify: `server/game/Tabletop.GameServer.csproj` (embed parts and books)
- Modify: `server/game/Content/grimdark-future-core.properties.json` (`name`)
- Create: `server/game/Content/grimdark-future-army-rules.part.json` (empty entries; Task 11 fills it)
- Create: `server/game/Content/army-books/.gitkeep` (Task 13 fills the directory)
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs`, `CoreContentTests.cs`, `PropertiesStoreTests.cs`
- Create: `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs`

**Interfaces:**
- Consumes: `PropertySetDocument`, `BookRecord` (Task 1), `PropertiesJson`.
- Produces: `sealed record BookDocument(string Uid, string Name, string? Version, string GameRules, IReadOnlyList<EntryDocument> Spells)`; `PropertiesJson.Options` (public); `PropertiesJson.ParseBook(string) → BookDocument`; `PropertiesJson.EntryCanonical(EntryDocument) → string`; `PropertiesSeeder.Compose(PropertySetDocument core, IEnumerable<PropertySetDocument> parts, IEnumerable<BookDocument> books) → PropertySetDocument`; `TestProperties.RulebookDocument` (the core resource alone).

Spec → *One set holds every book*: a game pins one set at creation and its armies are chosen later, so every book is in the one set. The composed document validates, hashes and seeds exactly as one authored document does today.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs`:

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class SpellLibraryTests
{
    static EntryDocument Spell(string name, int hits) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name} text.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, null, null, null, hits, false, null, null, null, EffectDuration.Interaction, [], [])]);
    static PropertySetDocument Part(params EntryDocument[] entries) => new("part", "grimdark-future", [], [], [], entries);

    [Fact]
    public void The_embedded_set_is_one_composed_document_named_for_the_ruleset()
    {
        var doc = Assert.Single(PropertiesSeeder.EmbeddedDocuments(), d => d.GameRules == "grimdark-future");
        Assert.Equal("Grimdark Future v3.5.1", doc.Name);
        Assert.Empty(PropertiesJson.Validate(doc));
        Assert.All(TestProperties.RulebookDocument.Entries, e => Assert.Contains(doc.Entries, x => x.Name == e.Name));
    }

    [Fact]
    public void A_reprinted_spell_is_written_once_and_listed_by_both_books()
    {
        var zap = Spell("Zap", 2);
        var doc = PropertiesSeeder.Compose(TestProperties.RulebookDocument, [],
            [new BookDocument("b2", "Book Two", "1", "grimdark-future", [zap]), new BookDocument("b1", "Book One", "1", "grimdark-future", [zap])]);
        Assert.Single(doc.Entries, e => e.Name == "Zap");
        Assert.Equal(["b1", "b2"], doc.Books!.Select(b => b.Uid));
        Assert.All(doc.Books!, b => Assert.Equal(["Zap"], b.Spells));
        Assert.Empty(PropertiesJson.Validate(doc));
    }

    [Fact]
    public void Two_different_entries_under_one_name_are_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PropertiesSeeder.Compose(TestProperties.RulebookDocument, [],
            [new BookDocument("b1", "One", "1", "grimdark-future", [Spell("Zap", 2)]), new BookDocument("b2", "Two", "1", "grimdark-future", [Spell("Zap", 3)])]));
        Assert.Contains("'Zap'", ex.Message);
    }

    [Fact]
    public void A_part_adds_its_entries_and_another_rulesets_parts_and_books_are_ignored()
    {
        var rule = new EntryDocument("Evasive", CatalogKind.Model, false, Aggregation.All, null, null, "Enemies get -1 to hit rolls when attacking units where all models have this rule.", null, []);
        var doc = PropertiesSeeder.Compose(TestProperties.RulebookDocument, [Part(rule), Part(rule with { Name = "Elsewhere" }) with { GameRules = "other" }],
            [new BookDocument("x", "X", "1", "other", [Spell("Zap", 1)])]);
        Assert.Contains(doc.Entries, e => e.Name == "Evasive");
        Assert.DoesNotContain(doc.Entries, e => e.Name is "Elsewhere" or "Zap");
        Assert.Empty(doc.Books!);
    }

    [Fact]
    public void Composition_is_stable_and_a_book_change_changes_the_hash()
    {
        var books = new[] { new BookDocument("b1", "One", "1", "grimdark-future", [Spell("Zap", 2)]) };
        var a = PropertiesJson.Hash(PropertiesSeeder.Compose(TestProperties.RulebookDocument, [], books));
        var b = PropertiesJson.Hash(PropertiesSeeder.Compose(TestProperties.RulebookDocument, [], books));
        var c = PropertiesJson.Hash(PropertiesSeeder.Compose(TestProperties.RulebookDocument, [], [books[0] with { Version = "2" }]));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~SpellLibraryTests`
Expected: compile errors (`BookDocument`, `Compose`, `RulebookDocument` do not exist).

- [ ] **Step 3: Expose the canonical options and parse a book**

In `server/game/Properties/PropertySetDocument.cs`, add the record beside `BookRecord`:

```csharp
/// <summary>A generated army-book file (`Content/army-books/&lt;uid&gt;.book.json`): the book's identity and its spells as entries.</summary>
public sealed record BookDocument(string Uid, string Name, string? Version, string GameRules, IReadOnlyList<EntryDocument> Spells);
```

In `PropertiesJson`, change `static readonly JsonSerializerOptions Options` to `public static readonly JsonSerializerOptions Options`, and add:

```csharp
    public static BookDocument ParseBook(string json)
    {
        var book = JsonSerializer.Deserialize<BookDocument>(json, Options) ?? throw new JsonException("empty book");
        var asDocument = Parse(JsonSerializer.Serialize(new PropertySetDocument(book.Name, book.GameRules, [], [], [], book.Spells ?? []), Options));
        return book with { Spells = asDocument.Entries };   // the same defaults Parse applies to an entry's effects and conditions
    }

    /// <summary>An entry's canonical form: what composition compares two same-named entries by.</summary>
    public static string EntryCanonical(EntryDocument e) => JsonSerializer.Serialize(e, Options);
```

- [ ] **Step 4: Compose in the seeder**

Replace `EmbeddedDocuments` in `server/game/Properties/PropertiesSeeder.cs` and add `Compose`:

```csharp
    /// <summary>One composed document per embedded `*.properties.json`: that document, every `*.part.json` and every `*.book.json` of its
    /// game rules folded in (spell library spec → One set holds every book).</summary>
    public static IReadOnlyList<PropertySetDocument> EmbeddedDocuments()
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        string Read(string n) { using var s = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(s); return r.ReadToEnd(); }
        var names = asm.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var parts = names.Where(n => n.EndsWith(".part.json")).Select(n => PropertiesJson.Parse(Read(n))).ToList();
        var books = names.Where(n => n.EndsWith(".book.json")).Select(n => PropertiesJson.ParseBook(Read(n))).ToList();
        return names.Where(n => n.EndsWith(".properties.json")).Select(n => Compose(PropertiesJson.Parse(Read(n)), parts, books)).ToList();
    }

    /// <summary>
    /// Entries are unioned: an entry two sources carry is written once when their canonical forms are equal (a reprinted spell), and a
    /// refusal otherwise. Books are listed in uid order, each with its spells' names in its own order. Parts and books of another
    /// game rules are ignored.
    /// </summary>
    public static PropertySetDocument Compose(PropertySetDocument core, IEnumerable<PropertySetDocument> parts, IEnumerable<BookDocument> books)
    {
        var mine = books.Where(b => b.GameRules == core.GameRules).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList();
        var entries = new Dictionary<string, EntryDocument>(StringComparer.Ordinal);
        void Add(EntryDocument e)
        {
            if (!entries.TryGetValue(e.Name, out var had)) { entries[e.Name] = e; return; }
            if (PropertiesJson.EntryCanonical(had) != PropertiesJson.EntryCanonical(e))
                throw new InvalidDataException($"two different entries are named '{e.Name}'");
        }
        foreach (var e in core.Entries) Add(e);
        foreach (var p in parts.Where(p => p.GameRules == core.GameRules)) foreach (var e in p.Entries) Add(e);
        foreach (var b in mine) foreach (var s in b.Spells) Add(s);
        return core with
        {
            Entries = entries.Values.ToList(),
            Books = [.. core.Books ?? [], .. mine.Select(b => new BookRecord(b.Uid, b.Name, b.Version, b.Spells.Select(s => s.Name).ToList()))],
        };
    }
```

- [ ] **Step 5: Embed the parts and the books, and rename the set**

In `server/game/Tabletop.GameServer.csproj`, beside the existing `EmbeddedResource` line:

```xml
    <EmbeddedResource Include="Content/*.part.json" LogicalName="Tabletop.GameServer.Content.%(Filename)%(Extension)" />
    <EmbeddedResource Include="Content/army-books/*.book.json" LogicalName="Tabletop.GameServer.Content.army-books.%(Filename)%(Extension)" />
```

In `server/game/Content/grimdark-future-core.properties.json`, change line 2's `"name": "Grimdark Future v3.5.1 core"` to `"name": "Grimdark Future v3.5.1"`. The set now holds the books too; the core-only set stays listed under its old name for the games that pinned it (spec → *One set holds every book*).

Create `server/game/Content/grimdark-future-army-rules.part.json`:

```json
{ "name": "Grimdark Future army-book rules", "gameRules": "grimdark-future", "entries": [] }
```

Create an empty `server/game/Content/army-books/.gitkeep`.

- [ ] **Step 6: Read the composed document in the tests**

Replace the body of `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs` above `WithSpells`:

```csharp
    /// <summary>The rulebook's pp. 13–15 alone, the hand-written core resource, for the tests that pin its thirty-three rules.</summary>
    public static PropertySetDocument RulebookDocument { get; } = PropertiesJson.Parse(ReadResource("Tabletop.GameServer.Content.grimdark-future-core.properties.json"));
    /// <summary>The shipped set: the composed document the server seeds (core, the army-rules part, every book).</summary>
    public static PropertySetDocument CoreDocument { get; } = PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == "grimdark-future");
    public static PropertySet Core { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);
```

and make the reader take the name:

```csharp
    static string ReadResource(string name)
    {
        using var s = typeof(PropertySet).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
```

Delete the `ResourceName` constant.

In `CoreContentTests.The_document_is_valid_and_names_the_rulebooks_thirty_three_rules`, read the rulebook alone: replace `Assert.Empty(PropertiesJson.Validate(TestProperties.CoreDocument));` with `Assert.Empty(PropertiesJson.Validate(TestProperties.RulebookDocument));` and `Core.Entries.Select(e => e.Name)` with `TestProperties.RulebookDocument.Entries.Select(e => e.Name)`.

In `PropertiesStoreTests.Seeding_the_core_document_round_trips_through_the_eight_tables_and_is_idempotent`, replace `Assert.Equal(33, set.Entries.Count);` with `Assert.Equal(expected.Entries.Count, set.Entries.Count);`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~SpellLibraryTests|FullyQualifiedName~CoreContentTests|FullyQualifiedName~PropertySetDocumentTests"`
Expected: PASS.

- [ ] **Step 8: Run the server and simulator suites**

Run: `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS. Every scenario and fixture now pins the composed set's hash, which today differs from the old one only by the name.

- [ ] **Step 9: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests
git commit -m "Compose the core document, the army-rules part and the army books into one set"
```
