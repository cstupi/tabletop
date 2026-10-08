namespace Tabletop.GameServer.Properties;

/// <summary>Authored content reaches the tables here: every embedded *.properties.json is seeded once, by hash, when the server starts.</summary>
public sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log)
{
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

    public async Task<IReadOnlyList<string>> SeedEmbeddedAsync(CancellationToken ct)
    {
        var hashes = new List<string>();
        foreach (var doc in EmbeddedDocuments())
        {
            var inserted = await store.SeedAsync(doc, ct);
            var hash = PropertiesJson.Hash(doc);
            log.LogInformation("Properties '{Name}' {Hash}: {What}", doc.Name, hash, inserted ? "seeded" : "already present");
            hashes.Add(hash);
        }
        return hashes;
    }
}
