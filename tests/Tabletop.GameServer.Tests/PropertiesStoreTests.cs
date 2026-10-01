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
        Assert.False(await _store.SeedAsync(doc, CancellationToken.None));
        var set = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.NotNull(set);
        var expected = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(Shape(expected), Shape(set));
        Assert.Equal(33, set.Entries.Count);
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
}
