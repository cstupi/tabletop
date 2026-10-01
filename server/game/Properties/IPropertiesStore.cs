namespace Tabletop.GameServer.Properties;

/// <summary>The pinned sets. Load by hash at hydration; seed once from an authored document.</summary>
public interface IPropertiesStore
{
    Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct);
    /// <summary>Writes the eight tables for the document's hash. False, and nothing written, when the hash is already present.</summary>
    Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct);
}

public sealed class InMemoryPropertiesStore : IPropertiesStore
{
    private readonly Dictionary<string, PropertySet> _sets = new();
    public InMemoryPropertiesStore Add(PropertySet set) { _sets[set.ContentHash] = set; return this; }
    public Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct) => Task.FromResult(_sets.GetValueOrDefault(contentHash));
    public Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct)
    {
        var hash = PropertiesJson.Hash(document);
        if (_sets.ContainsKey(hash)) return Task.FromResult(false);
        _sets[hash] = PropertiesJson.ToSet(document, Guid.CreateVersion7);
        return Task.FromResult(true);
    }
}
