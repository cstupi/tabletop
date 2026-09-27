namespace Tabletop.GameServer.Properties;

/// <summary>Authored content reaches the tables here: every embedded *.properties.json is seeded once, by hash, when the server starts.</summary>
public sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log)
{
    public static IReadOnlyList<PropertySetDocument> EmbeddedDocuments()
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        return asm.GetManifestResourceNames().Where(n => n.EndsWith(".properties.json")).OrderBy(n => n)
            .Select(n => { using var s = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(s); return PropertiesJson.Parse(r.ReadToEnd()); }).ToList();
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
