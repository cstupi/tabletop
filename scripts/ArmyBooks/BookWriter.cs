using System.Text.Json;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>Translated books, with each spell name made unique across the corpus, checked against the core document and the part, and written.</summary>
public static class BookWriter
{
    static readonly JsonSerializerOptions Indented = new(PropertiesJson.Options) { WriteIndented = true, NewLine = "\n" };

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

    public static string Serialize(BookDocument book) => JsonSerializer.Serialize(book, Indented) + "\n";

    public static void Write(IReadOnlyList<BookDocument> books, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var b in books) File.WriteAllText(Path.Combine(outDir, b.Uid + ".book.json"), Serialize(b));
    }
}
