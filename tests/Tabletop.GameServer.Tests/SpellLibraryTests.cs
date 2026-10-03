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
