using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestProperties
{
    /// <summary>The rulebook's pp. 13–15 alone, the hand-written core resource, for the tests that pin its thirty-three rules.</summary>
    public static PropertySetDocument RulebookDocument { get; } = PropertiesJson.Parse(ReadResource("Tabletop.GameServer.Content.grimdark-future-core.properties.json"));
    /// <summary>The rulebook with its army-rules parts and no books: what WithSpells builds on, so a test spell never collides with a shipped book's.</summary>
    static PropertySetDocument SpellBaseDocument { get; } = PropertiesSeeder.Compose(RulebookDocument,
        typeof(PropertySet).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".part.json")).Select(n => PropertiesJson.Parse(ReadResource(n))), []);
    /// <summary>The shipped set: the composed document the server seeds (core, the army-rules part, every book).</summary>
    public static PropertySetDocument CoreDocument { get; } = PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == "grimdark-future");
    public static PropertySet Core { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    /// <summary>The book every test unit's slots name (Armies, TestArmies) and WithSpells lists its spells under. No shipped set lists it.
    /// WithSpells takes rule entries too; only the spell-kind ones go in the book.</summary>
    public const string TestBook = "test-book";

    public static PropertySet WithSpells(params EntryDocument[] spells) =>
        PropertiesJson.ToSet(SpellBaseDocument with
        {
            Name = "Core + test spells", Entries = [.. SpellBaseDocument.Entries, .. spells],
            Books = [.. SpellBaseDocument.Books ?? [], new BookRecord(TestBook, "Test Book", null, spells.Where(s => s.Kind == CatalogKind.Spell).Select(s => s.Name).ToList())],
        }, Guid.CreateVersion7);

    static string ReadResource(string name)
    {
        using var s = typeof(PropertySet).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
