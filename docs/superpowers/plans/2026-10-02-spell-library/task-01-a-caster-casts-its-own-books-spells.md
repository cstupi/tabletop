# Task 1: A caster casts its own book's spells

**Files:**
- Modify: `libs/Tabletop.Content/Army.cs` (`ModelSlot.ArmyBook`)
- Modify: `server/game/Properties/PropertySetDocument.cs` (`BookRecord`, `PropertySetDocument.Books`, parse default, canonical order, validation)
- Modify: `server/game/Properties/PropertySet.cs` (`Books`, `SpellsOf`)
- Modify: `server/game/Rules/GrimdarkFuture/GfCasting.cs` (`SpellOptions`)
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs`, `Infrastructure/Armies.cs`, `Infrastructure/TestArmies.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfCastingTests.cs`, `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`

**Interfaces:**
- Consumes: `PropertySetDocument`, `EntryDocument`, `PropertiesJson.Parse/Canonical/Validate/ToSet`, `PropertySet`, `CatalogEntry`, `GfCasting.Casters`, `TriedPicks` (all on `main`).
- Produces: `ModelSlot(..., IReadOnlyList<Weapon> Weapons, string? ArmyBook = null)`; `sealed record BookRecord(string Uid, string Name, string? Version, IReadOnlyList<string> Spells)`; `PropertySetDocument(..., IReadOnlyList<EntryDocument> Entries, IReadOnlyList<BookRecord>? Books = null)`; `PropertySet(..., IReadOnlyList<CatalogEntry> entries, IReadOnlyList<BookRecord>? books = null)` with `IReadOnlyDictionary<string, BookRecord> Books` and `IReadOnlyList<CatalogEntry> SpellsOf(string? bookUid)`; `TestProperties.TestBook`; `Armies.InBook(Unit, string)`.

p.13: *"the Caster's spells must be picked from their own faction"*. The faction is the army book, and the book rides on the slot (spec → *A caster casts its own book's spells*). A slot with no book, or a book the set does not list, offers no spell.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Tabletop.GameServer.Tests/GfCastingTests.cs`, inside the class (it already has `Fireball`, `Ward`, `Book` and `Casting`):

```csharp
    [Fact]
    public void A_caster_is_offered_only_its_own_books_spells()
    {
        var other = Fireball with { Name = "Other Fireball" };
        var props = PropertiesJson.ToSet(TestProperties.CoreDocument with
        {
            Name = "two books",
            Entries = [.. TestProperties.CoreDocument.Entries, Fireball, other],
            Books = [.. TestProperties.CoreDocument.Books ?? [], new BookRecord("book-a", "Book A", "1", ["Fireball"]), new BookRecord("book-b", "Book B", "1", ["Other Fireball"])],
        }, Guid.CreateVersion7);
        var wizard = Armies.InBook(Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]), "book-a");
        var state = State([wizard], [Unit("T", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(2, null, new SpellTokensSet(wizard.Models[0].Id, 3), null));
        Assert.Equal([$"Fireball|{wizard.Models[0].Id}"], GfCasting.SpellOptions(state, props, wizard.Id).Select(o => o.Id));
    }

    [Fact]
    public void A_caster_with_no_book_or_an_unknown_book_is_offered_nothing()
    {
        foreach (var book in new string?[] { null, "a-book-this-set-does-not-list" })
        {
            var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
            wizard = wizard with { Models = wizard.Models.Select(m => m with { ArmyBook = book }).ToList() };
            var state = State([wizard], [Unit("T", "4+", "4+", [], 1, [])]);
            state.Applier.Apply(new GameEvent(2, null, new SpellTokensSet(wizard.Models[0].Id, 3), null));
            Assert.Empty(GfCasting.SpellOptions(state, Book, wizard.Id));
        }
    }
```

Add to `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`:

```csharp
    [Fact]
    public void A_book_must_list_spell_entries_and_book_uids_are_unique()
    {
        var spell = new EntryDocument("Zap", CatalogKind.Spell, false, null, 1, null, "Zap.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
            [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], [])]);
        var doc = TestProperties.CoreDocument with { Entries = [.. TestProperties.CoreDocument.Entries, spell] };
        Assert.Contains(PropertiesJson.Validate(doc with { Books = [new BookRecord("b", "B", null, ["Nope"])] }), p => p.Contains("book b lists 'Nope', which is no spell entry"));
        Assert.Contains(PropertiesJson.Validate(doc with { Books = [new BookRecord("b", "B", null, ["AP"])] }), p => p.Contains("book b lists 'AP', which is no spell entry"));
        Assert.Contains(PropertiesJson.Validate(doc with { Books = [new BookRecord("b", "B", null, ["Zap"]), new BookRecord("b", "B2", null, [])] }), p => p.Contains("book uids must be unique"));
        Assert.Empty(PropertiesJson.Validate(doc with { Books = [new BookRecord("b", "B", null, ["Zap"])] }));
    }

    [Fact]
    public void Books_sort_by_uid_in_the_canonical_form_and_keep_their_spell_order()
    {
        var doc = TestProperties.CoreDocument with { Books = [new BookRecord("z", "Z", null, []), new BookRecord("a", "A", null, [])] };
        var swapped = doc with { Books = [doc.Books![1], doc.Books[0]] };
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(swapped));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfCastingTests|FullyQualifiedName~PropertySetDocumentTests"`
Expected: compile errors (`BookRecord`, `ArmyBook`, `Books`, `Armies.InBook` do not exist).

- [ ] **Step 3: Add the slot's book**

In `libs/Tabletop.Content/Army.cs`, replace the `ModelSlot` record:

```csharp
/// <summary>One model. Base and Height are the hitbox; AssetId is appearance and a soft reference. ArmyBook is the Army Forge book uid
/// of the entry the slot was imported from (`armyId` in the TTS export); p.13 picks a caster's spells from it. Null when unknown.</summary>
public sealed record ModelSlot(
    Guid Id, int Ordinal, string Name,
    IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties,
    Base Base, double Height, Guid? AssetId,
    IReadOnlyList<Weapon> Weapons, string? ArmyBook = null);
```

- [ ] **Step 4: Add books to the authored form**

In `server/game/Properties/PropertySetDocument.cs`, change the document record and add `BookRecord` beside it:

```csharp
public sealed record PropertySetDocument(string Name, string GameRules, IReadOnlyList<StatDefinition> Stats, IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<string> Rolls, IReadOnlyList<EntryDocument> Entries, IReadOnlyList<BookRecord>? Books = null);
/// <summary>An army book: its Army Forge uid, name and version, and the spell entries it lists, in its own order (spell library spec).</summary>
public sealed record BookRecord(string Uid, string Name, string? Version, IReadOnlyList<string> Spells);
```

In `PropertiesJson.Parse`, add `Books` to the `with` block so an omitted list is empty:

```csharp
            Books = (doc.Books ?? []).Select(b => b with { Spells = b.Spells ?? [] }).ToList(),
```

In `PropertiesJson.Canonical`, add to the `with` block:

```csharp
        Books = (d.Books ?? []).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList(),
```

In `PropertiesJson.Validate`, after the per-entry loop and before `return problems;`:

```csharp
        var books = d.Books ?? [];
        if (books.Select(b => b.Uid).Distinct().Count() != books.Count) problems.Add("book uids must be unique");
        var spells = d.Entries.Where(e => e.Kind == CatalogKind.Spell).Select(e => e.Name).ToHashSet();
        foreach (var b in books)
            foreach (var s in b.Spells.Where(s => !spells.Contains(s)))
                problems.Add($"book {b.Uid} lists '{s}', which is no spell entry");
```

In `PropertiesJson.ToSet`, pass the books through as the last constructor argument:

```csharp
        return new PropertySet(Hash(d), d.Name, d.GameRules, d.Stats, d.Actions, d.Rolls.Select(r => new RollDefinition(r)).ToList(),
            d.Entries.Select(e => new CatalogEntry(e.Name, e.Kind, e.TakesParameter, e.Aggregation, e.Cost, e.Faction, e.RuleText, e.Targeting, e.Effects.Select(Build).ToList())).ToList(),
            d.Books ?? []);
```

- [ ] **Step 5: Add books to the set**

In `server/game/Properties/PropertySet.cs`, replace the class header and add two members:

```csharp
public sealed class PropertySet(string contentHash, string name, string gameRules, IReadOnlyList<StatDefinition> stats,
    IReadOnlyList<ActionDefinition> actions, IReadOnlyList<RollDefinition> rolls, IReadOnlyList<CatalogEntry> entries, IReadOnlyList<BookRecord>? books = null)
{
    // ...existing members unchanged...
    public IReadOnlyDictionary<string, BookRecord> Books { get; } = (books ?? []).ToDictionary(b => b.Uid);

    /// <summary>p.13: the spells a caster of this book may try, in the book's order. Empty for no book or a book the set does not list.</summary>
    public IReadOnlyList<CatalogEntry> SpellsOf(string? bookUid) =>
        bookUid is not null && Books.TryGetValue(bookUid, out var b) ? b.Spells.Select(Entry).OfType<CatalogEntry>().Where(e => e.Kind == CatalogKind.Spell).ToList() : [];
```

`PostgresPropertiesStore.LoadAsync` still calls the seven-argument constructor until Task 3 adds the tables; a set loaded from Postgres has no books until then, which no Postgres test reads yet.

- [ ] **Step 6: Filter the cast options by the slot's book**

In `server/game/Rules/GrimdarkFuture/GfCasting.cs`, replace `SpellOptions`:

```csharp
    /// <summary>
    /// p.13: a caster may "spend as many tokens as the spell's value to try casting one or more spells (only one try per spell)", and
    /// "the Caster's spells must be picked from their own faction": one option per caster model and spell of that model's own army book
    /// (ModelSlot.ArmyBook) it can afford and has not tried this activation. Empty means nothing to cast, so GfHooks.ReactionsAt offers no window.
    /// </summary>
    public static IReadOnlyList<AskOption> SpellOptions(GameState state, PropertySet props, Guid unit)
    {
        var tried = TriedPicks(state, unit);
        return Casters(state, unit).SelectMany(m => props.SpellsOf(state.Projection.Models[m].Slot.ArmyBook)
                .Where(e => !tried.Contains((e.Name, m)) && e.Cost <= state.Projection.Models[m].SpellTokens)
                .Select(e => new AskOption($"{e.Name}|{m}", $"{e.Name} ({e.Cost})", [m]))).ToList();
    }
```

- [ ] **Step 7: Give test units the test book**

In `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs`:

```csharp
    /// <summary>The book every test unit's slots name (Armies, TestArmies) and WithSpells lists its spells under. No shipped set lists it.
    /// WithSpells takes rule entries too; only the spell-kind ones go in the book.</summary>
    public const string TestBook = "test-book";

    public static PropertySet WithSpells(params EntryDocument[] spells) =>
        PropertiesJson.ToSet(CoreDocument with
        {
            Name = "Core + test spells", Entries = [.. CoreDocument.Entries, .. spells],
            Books = [.. CoreDocument.Books ?? [], new BookRecord(TestBook, "Test Book", null, spells.Where(s => s.Kind == CatalogKind.Spell).Select(s => s.Name).ToList())],
        }, Guid.CreateVersion7);
```

In `Infrastructure/Armies.cs`, give every slot `Unit` builds the test book (append `, TestProperties.TestBook` as the `ModelSlot` constructor's last argument, after the weapons list) and add:

```csharp
    /// <summary>The unit with every slot naming the given army book, for a test against a shipped book's spells.</summary>
    public static Unit InBook(Unit unit, string bookUid) => unit with { Models = unit.Models.Select(m => m with { ArmyBook = bookUid }).ToList() };
```

In `Infrastructure/TestArmies.cs`, append `, TestProperties.TestBook` as the last argument of the `new ModelSlot(...)` call.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfCastingTests|FullyQualifiedName~PropertySetDocumentTests|FullyQualifiedName~GfRollsTests|FullyQualifiedName~OutOfSetAnswerTests|FullyQualifiedName~RoundBoundaryTests|FullyQualifiedName~CommandDispatcherTests|FullyQualifiedName~GrimdarkFutureRulesTests"`
Expected: PASS. The casting tests that build their set with `WithSpells` cast as before, because their units name `test-book`.

- [ ] **Step 9: Run every suite that constructs a slot**

Run: `dotnet build && dotnet test tests/Tabletop.Content.Tests tests/Tabletop.Protocol.Tests tests/Tabletop.Client.Tests tests/Tabletop.Sim.Tests`
Expected: PASS. The trailing optional `ArmyBook` leaves every other constructor compiling, and `WhenWritingNull` keeps it out of the JSON of every existing snapshot.

- [ ] **Step 10: Commit**

```bash
git add libs/Tabletop.Content/Army.cs server/game/Properties server/game/Rules/GrimdarkFuture/GfCasting.cs tests/Tabletop.GameServer.Tests
git commit -m "Offer a caster only its own army book's spells"
```
