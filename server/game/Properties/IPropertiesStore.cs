namespace Tabletop.GameServer.Properties;

/// <summary>The seeded sets. Load by hash at hydration, with aliases for the effect rows of every other set of the same game rules
/// (EffectAliases); seed once from an authored document.</summary>
public interface IPropertiesStore
{
    Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct);
    /// <summary>Writes the ten tables for the document's hash. False, and nothing written, when the hash is already present.</summary>
    Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct);
}

public sealed class InMemoryPropertiesStore : IPropertiesStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PropertySet> _sets = new();
    public InMemoryPropertiesStore Add(PropertySet set) { _sets[set.ContentHash] = set; return this; }
    public Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct)
    {
        if (_sets.GetValueOrDefault(contentHash) is not { } set) return Task.FromResult<PropertySet?>(null);
        var foreign = _sets.Values.Where(o => o.ContentHash != contentHash && o.GameRules == set.GameRules).SelectMany(EffectAliases.Of).ToList();
        return Task.FromResult<PropertySet?>(foreign.Count == 0 ? set : set.WithAliases(foreign));
    }
    public Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct)
    {
        var hash = PropertiesJson.Hash(document);
        if (_sets.ContainsKey(hash)) return Task.FromResult(false);
        return Task.FromResult(_sets.TryAdd(hash, PropertiesJson.ToSet(document, Guid.CreateVersion7)));
    }
}
