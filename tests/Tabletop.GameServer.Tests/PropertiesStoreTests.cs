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

    [Fact]
    public async Task Played_round_trips_through_the_catalog_entry_table()
    {
        var name = "Played " + Guid.CreateVersion7().ToString("N")[24..32];
        var doc = new PropertySetDocument(name, "played-" + name, [], [], ["hit"],
            [new EntryDocument("Text Only", CatalogKind.Model, false, Aggregation.Any, null, null, "Text.", null, [], Played: false),
             new EntryDocument("Played", CatalogKind.Model, false, Aggregation.All, null, null, "Played.", null, [])]);
        var hash = PropertiesJson.Hash(doc);
        try
        {
            Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
            var set = (await _store.LoadAsync(hash, CancellationToken.None))!;
            Assert.False(set.Entry("Text Only")!.Played);
            Assert.True(set.Entry("Played")!.Played);
        }
        finally { await pg.DeletePropertiesAsync(hash); }
    }

    /// <summary>
    /// The set with every id blanked, as canonical JSON, so two loads compare on content. Stats, rolls and entries are sorted (the
    /// hash sorts them: their order carries no meaning); actions, effects, conditions and condition values keep their order, because
    /// the engine reads them in order (a turn's options, ties between equal ordinals, a roll_result's roll-then-modifier pair).
    /// </summary>
    static string Shape(PropertySet s)
    {
        Effect Blank(Effect e) => e with
        {
            Id = Guid.Empty,
            Conditions = e.Conditions.Select(c => c with { Id = Guid.Empty }).ToList(),
            Children = e.Children.Select(Blank).ToList(),
        };
        var entries = s.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => e with { Effects = e.Effects.Select(Blank).ToList() });
        return ContentJson.Serialize(new { s.ContentHash, s.Name, s.GameRules, Stats = s.Stats.OrderBy(x => x.Level).ThenBy(x => x.Name, StringComparer.Ordinal), s.Actions,
            Rolls = s.Rolls.OrderBy(r => r.Name, StringComparer.Ordinal), Entries = entries, Books = s.Books.Values.OrderBy(b => b.Uid, StringComparer.Ordinal) });
    }

    /// <summary>Ids that sort in Postgres the reverse of the order they are minted in: a random high half, then a falling counter.</summary>
    static Func<Guid> Descending()
    {
        var high = Random.Shared.NextInt64(1, long.MaxValue);
        var n = long.MaxValue;
        return () => Guid.Parse($"{high:x16}{n--:x16}");
    }

    /// <summary>Several conditions per effect, values out of alphabetical order, siblings sharing an ordinal, actions out of name order.</summary>
    static PropertySetDocument Ordered(string name)
    {
        ConditionDocument C(ConditionKind k, EffectSubject? subject, Comparison cmp, int? n, params string[] values) => new(k, subject, cmp, n, values);
        EffectDocument E(int ordinal, int amount, params ConditionDocument[] conditions) =>
            new(EffectSubject.Self, ordinal, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, conditions, []);
        var effects = new[]
        {
            E(10, 1, C(ConditionKind.MostModels, EffectSubject.Opponent, Comparison.Gte, 3, "Tough"), C(ConditionKind.ActionTaken, EffectSubject.Self, Comparison.Eq, null, "Rush", "Charge", "Advance"),
                C(ConditionKind.AttackKind, null, Comparison.Eq, null, "shooting", "melee"), C(ConditionKind.Range, null, Comparison.Gt, 9)),
            E(10, 2, C(ConditionKind.RollResult, null, Comparison.Eq, 1, "hit", "raw"), C(ConditionKind.AttackKind, null, Comparison.Eq, null, "melee"), C(ConditionKind.ActionTaken, EffectSubject.Opponent, Comparison.Eq, null, "Charge")),
            E(10, 3, C(ConditionKind.UsageCount, null, Comparison.Lt, 1, "round"), C(ConditionKind.AtPoint, null, Comparison.Eq, null, "attack-start", "activation-start")),
            E(5, 4, C(ConditionKind.HoldsProperty, EffectSubject.Self, Comparison.Eq, null, "Ordered"), C(ConditionKind.RoundNumber, null, Comparison.Gte, 2)),
        };
        return new PropertySetDocument(name, "order-" + name, [], [new ActionDefinition("Hold", 0), new ActionDefinition("Advance", 6), new ActionDefinition("Rush", 12), new ActionDefinition("Charge", 12)],
            ["hit"], [new EntryDocument("Ordered", CatalogKind.Model, false, Aggregation.All, null, null, "Ordered.", null, effects)]);
    }

    [Fact]
    public async Task A_set_reads_back_in_document_order_whatever_order_its_row_ids_sort_in()
    {
        for (var run = 0; run < 20; run++)
        {
            var doc = Ordered("Ordered " + Guid.CreateVersion7().ToString("N")[24..32]);
            var hash = PropertiesJson.Hash(doc);
            try
            {
                Assert.True(await _store.SeedAsync(doc, run % 2 == 0 ? Descending() : Guid.NewGuid, CancellationToken.None));
                Assert.Equal(Shape(PropertiesJson.ToSet(doc, Guid.CreateVersion7)), Shape((await _store.LoadAsync(hash, CancellationToken.None))!));
            }
            finally { await pg.DeletePropertiesAsync(hash); }
        }
    }

    [Fact]
    public async Task The_composed_embedded_document_reads_back_as_the_document_evaluates_with_ids_minted_in_reverse()
    {
        var doc = TestProperties.CoreDocument with { Name = "Composed " + Guid.CreateVersion7().ToString("N")[24..32] };
        var hash = PropertiesJson.Hash(doc);
        try
        {
            Assert.True(await _store.SeedAsync(doc, Descending(), CancellationToken.None));
            Assert.Equal(Shape(PropertiesJson.ToSet(doc, Guid.CreateVersion7)), Shape((await _store.LoadAsync(hash, CancellationToken.None))!));
        }
        finally { await pg.DeletePropertiesAsync(hash); }
    }

    [Fact]
    public async Task Seeding_a_present_set_without_positions_fills_them_from_the_document_and_keeps_its_row_ids()
    {
        var doc = Ordered("Unpositioned " + Guid.CreateVersion7().ToString("N")[24..32]);
        var hash = PropertiesJson.Hash(doc);
        try
        {
            Assert.True(await _store.SeedAsync(doc, Descending(), CancellationToken.None));
            var before = (await _store.LoadAsync(hash, CancellationToken.None))!;
            // A set seeded before 007_properties_positions.sql: every position null.
            await pg.CountAsync("update management.action set position = null where properties_hash = $1", hash);
            await pg.CountAsync("update management.condition_value v set position = null from management.effect_condition c, management.effect e where v.condition_id = c.id and c.effect_id = e.id and e.properties_hash = $1", hash);
            await pg.CountAsync("update management.effect_condition c set position = null from management.effect e where c.effect_id = e.id and e.properties_hash = $1", hash);
            await pg.CountAsync("update management.effect set position = null where properties_hash = $1", hash);
            Assert.False(await _store.SeedAsync(doc, CancellationToken.None));
            var after = (await _store.LoadAsync(hash, CancellationToken.None))!;
            Assert.Equal(Shape(PropertiesJson.ToSet(doc, Guid.CreateVersion7)), Shape(after));
            Assert.Equal(Ids(before).Order(), Ids(after).Order());   // a game's grants name effect ids
        }
        finally { await pg.DeletePropertiesAsync(hash); }
        static IEnumerable<Guid> Ids(PropertySet s) => s.AllEffects().SelectMany(e => e.Conditions.Select(c => c.Id).Prepend(e.Id));
    }

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

    [Fact]
    public async Task A_place_row_its_dice_and_its_point_round_trip()
    {
        var blink = new EntryDocument("Blink", CatalogKind.Model, false, Aggregation.None, null, null, "Blink.", null,
            [new EffectDocument(EffectSubject.Self, 10, EffectKind.Place, null, null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction,
                [new ConditionDocument(ConditionKind.AtPoint, null, Comparison.Eq, null, ["activation-start"])], [], AmountDice: "d3")]);
        var doc = TestProperties.RulebookDocument with { Name = "Hooks " + Guid.CreateVersion7().ToString("N")[24..32], Entries = [.. TestProperties.RulebookDocument.Entries, blink] };
        var hash = PropertiesJson.Hash(doc);
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        var row = (await _store.LoadAsync(hash, CancellationToken.None))!.Entry("Blink")!.Effects.Single();
        Assert.Equal(("d3", EffectKind.Place, ConditionKind.AtPoint), (row.AmountDice, row.Kind, row.Conditions.Single().Kind));
        await pg.DeletePropertiesAsync(hash);
    }
}
