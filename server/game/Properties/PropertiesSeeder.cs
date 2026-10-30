namespace Tabletop.GameServer.Properties;

/// <summary>Authored content reaches the tables here: every embedded *.properties.json is seeded once, by hash, when the server starts, and
/// its hash becomes its game rules' current set (CurrentProperties).</summary>
public sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log, CurrentProperties? current = null)
{
    /// <summary>The part scripts/ArmyBooks generates into each edition's folder (army-book rules spec → Generator): template wrappers and
    /// text-only entries. Never edited by hand.</summary>
    public const string GeneratedPartName = "army-book-rules.part.json";

    /// <summary>One embedded `*.part.json` of one edition's content folder (GfEdition.ContentFolder) by file name, parsed; null when the
    /// assembly embeds none of that name there.</summary>
    public static PropertySetDocument? EmbeddedPart(string contentFolder, string fileName)
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        var suffix = $".Content.{contentFolder}.{fileName}";
        if (asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal)) is not { } name) return null;
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return PropertiesJson.Parse(r.ReadToEnd());
    }

    /// <summary>One composed document per embedded `*.properties.json` (one per edition folder): that document, every `*.part.json` and
    /// every `*.book.json` of its game rules folded in (spell library spec → One set holds every book).</summary>
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
            current?.Set(doc.GameRules, hash);
            hashes.Add(hash);
        }
        return hashes;
    }
}
