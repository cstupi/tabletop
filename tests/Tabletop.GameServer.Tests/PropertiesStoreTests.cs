using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class PropertiesStoreTests(PostgresFixture pg)
{
    readonly PostgresPropertiesStore _store = new(pg.Db);

    [Fact]
    public async Task Seeding_the_core_document_round_trips_through_the_eight_tables_and_is_idempotent()
    {
        // [24..32], not [..6]: a v7 guid's leading hex digits are its millisecond timestamp (PostgresFixture.SeedPropertiesAsync's
        // comment), identical across calls made minutes or even hours apart, which collided with a stale row from an earlier
        // run of this very test; the tail is random.
        var doc = TestProperties.CoreDocument with { Name = "Core " + Guid.CreateVersion7().ToString("N")[24..32] };
        var hash = PropertiesJson.Hash(doc);
        Assert.Null(await _store.LoadAsync(hash, CancellationToken.None));
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        await pg.CountAsync("update management.properties set seeded_at = now() - interval '1 day' where content_hash = $1", hash);
        Assert.False(await _store.SeedAsync(doc, CancellationToken.None));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.properties where content_hash = $1 and seeded_at > now() - interval '1 hour'", hash));   // a re-seed marks the set current again
        var set = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.NotNull(set);
        var expected = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(Shape(expected), Shape(set));
        Assert.Equal(expected.Entries.Count, set.Entries.Count);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.properties where content_hash = $1", hash));
        Assert.Equal(expected.AllEffects().Count(), await pg.CountAsync("select count(*) from management.effect where properties_hash = $1", hash));
        var again = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.Equal(set.Entry("Bane")!.Effects[0].Id, again!.Entry("Bane")!.Effects[0].Id);     // ids are stable once seeded
        await pg.DeletePropertiesAsync(hash);
    }

    /// <summary>
    /// The set with every id blanked, as canonical JSON, so two loads compare on content. Row order within an entry
    /// carries no meaning (PropertiesJson.ToSet keeps the author's order; the store's loader returns `order by
    /// ordinal, id`, and `id` is not even deterministic between siblings sharing an ordinal), so effects, their
    /// children and their conditions are each sorted by their own blanked, serialised content before comparing.
    /// Only `Ordinal` and the conjunction of `Conditions` carry meaning, and both are preserved either way.
    /// </summary>
    static string Shape(PropertySet s)
    {
        Effect Blank(Effect e) => e with
        {
            Id = Guid.Empty,
            Conditions = SortByContent(e.Conditions.Select(c => c with { Id = Guid.Empty })),
            Children = SortByContent(e.Children.Select(Blank)),
        };
        var entries = s.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => e with { Effects = SortByContent(e.Effects.Select(Blank)) });
        return ContentJson.Serialize(new { s.ContentHash, s.Name, s.GameRules, Stats = s.Stats.OrderBy(x => x.Level).ThenBy(x => x.Name), Actions = s.Actions.OrderBy(a => a.Name), Rolls = s.Rolls.OrderBy(r => r.Name), Entries = entries });
    }

    static List<T> SortByContent<T>(IEnumerable<T> items) =>
        items.Select(i => (Item: i, Key: ContentJson.Serialize(i))).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Item).ToList();

    [Fact]
    public async Task The_embedded_documents_seed_at_startup()
    {
        var docs = PropertiesSeeder.EmbeddedDocuments();
        Assert.Single(docs);
        var hashes = await new PropertiesSeeder(_store, Microsoft.Extensions.Logging.Abstractions.NullLogger<PropertiesSeeder>.Instance).SeedEmbeddedAsync(CancellationToken.None);
        Assert.Equal([PropertiesJson.Hash(docs[0])], hashes);
        Assert.NotNull(await _store.LoadAsync(hashes[0], CancellationToken.None));
    }

    static PropertySetDocument Tiny(string rules, string name, CatalogKind kind, string entry = "Alpha") =>
        new(name, rules, [], [], [], [new EntryDocument(entry, kind, false, kind == CatalogKind.Model ? Aggregation.All : null, null, null, "text", null, [])]);

    [Fact]
    public async Task A_second_set_that_changes_a_catalog_entrys_kind_for_the_same_rules_is_refused_and_a_consistent_one_seeds()
    {
        var rules = "kinds-" + Guid.CreateVersion7().ToString("N")[24..32];
        var first = Tiny(rules, "one", CatalogKind.Unit);
        var consistent = Tiny(rules, "two", CatalogKind.Unit, "Beta");
        var conflicting = Tiny(rules, "three", CatalogKind.Model);
        try
        {
            Assert.True(await _store.SeedAsync(first, CancellationToken.None));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SeedAsync(conflicting, CancellationToken.None));
            Assert.Contains("Alpha", ex.Message); Assert.Contains("Unit", ex.Message); Assert.Contains("Model", ex.Message);
            Assert.Null(await _store.LoadAsync(PropertiesJson.Hash(conflicting), CancellationToken.None));
            Assert.True(await _store.SeedAsync(consistent, CancellationToken.None));
        }
        finally { foreach (var d in new[] { first, consistent, conflicting }) await pg.DeletePropertiesAsync(PropertiesJson.Hash(d)); }
    }

    [Fact]
    public async Task Books_and_the_new_effect_columns_round_trip()
    {
        var test = new EffectDocument(EffectSubject.Opponent, 10, EffectKind.TakeTest, "morale", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [],
            [new EffectDocument(EffectSubject.Opponent, 20, EffectKind.SetCondition, null, null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], [], TargetCondition: "fatigued")]);
        var stride = new EffectDocument(EffectSubject.Self, 10, EffectKind.CountAsTerrain, null, null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [], TargetTerrain: TargetTerrain.Dangerous);
        EntryDocument Spell(string n, EffectDocument f) => new(n, CatalogKind.Spell, false, null, 1, null, $"{n}.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), [f]);
        var doc = TestProperties.RulebookDocument with
        {
            Name = "Books " + Guid.CreateVersion7().ToString("N")[24..32],
            Entries = [.. TestProperties.RulebookDocument.Entries, Spell("Terrifying Fury", test), Spell("Cursed Stride", stride)],
            Books = [new BookRecord("b-one", "Book One", "3.5.3", ["Terrifying Fury", "Cursed Stride"]), new BookRecord("b-two", "Book Two", null, ["Cursed Stride"])],
        };
        var hash = PropertiesJson.Hash(doc);
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        var set = (await _store.LoadAsync(hash, CancellationToken.None))!;
        Assert.Equal(["Terrifying Fury", "Cursed Stride"], set.Books["b-one"].Spells);
        Assert.Equal(("Book Two", (string?)null), (set.Books["b-two"].Name, set.Books["b-two"].Version));
        Assert.Equal("fatigued", set.Entry("Terrifying Fury")!.Effects[0].Children[0].TargetCondition);
        Assert.Equal(TargetTerrain.Dangerous, set.Entry("Cursed Stride")!.Effects[0].TargetTerrain);
        Assert.Equal(PropertiesJson.Hash(doc), set.ContentHash);
        await pg.DeletePropertiesAsync(hash);
    }
}
