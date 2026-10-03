using System.Reflection;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestProperties
{
    public const string ResourceName = "Tabletop.GameServer.Content.grimdark-future-core.properties.json";
    public static PropertySetDocument CoreDocument { get; } = PropertiesJson.Parse(ReadResource());
    public static PropertySet Core { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    /// <summary>The book every test unit's slots name (Armies, TestArmies) and WithSpells lists its spells under. No shipped set lists it.
    /// WithSpells takes rule entries too; only the spell-kind ones go in the book.</summary>
    public const string TestBook = "test-book";

    public static PropertySet WithSpells(params EntryDocument[] spells) =>
        PropertiesJson.ToSet(CoreDocument with
        {
            Name = "Core + test spells", Entries = [.. CoreDocument.Entries, .. spells],
            Books = [.. CoreDocument.Books ?? [], new BookRecord(TestBook, "Test Book", null, spells.Where(s => s.Kind == CatalogKind.Spell).Select(s => s.Name).ToList())],
        }, Guid.CreateVersion7);

    static string ReadResource()
    {
        using var s = typeof(PropertySet).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
