using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a sight query between two units depends on, as the client knows it: the pair, where each model of the two stands, and the terrain. Equal keys give the same server answer, so the earlier answer stands.</summary>
public sealed class SightKey : IEquatable<SightKey>
{
    readonly Guid _from, _to;
    readonly (Guid Model, Location Location)[] _models;
    readonly IReadOnlyList<TerrainPlacement> _terrain;

    SightKey(Guid from, Guid to, (Guid, Location)[] models, IReadOnlyList<TerrainPlacement> terrain) { _from = from; _to = to; _models = models; _terrain = terrain; }

    public static SightKey Of(Projection p, Guid from, Guid to) =>
        new(from, to, p.Models.Values.Where(m => m.UnitId == from || m.UnitId == to).OrderBy(m => m.Id).Select(m => (m.Id, m.Location)).ToArray(), p.Board.Table.Terrain);

    public bool Equals(SightKey? o) =>
        o is not null && _from == o._from && _to == o._to && _models.SequenceEqual(o._models)
        && (ReferenceEquals(_terrain, o._terrain) || _terrain.SequenceEqual(o._terrain));
    public override bool Equals(object? o) => Equals(o as SightKey);
    public override int GetHashCode() => HashCode.Combine(_from, _to, _models.Length);
}

/// <summary>The key each assigned target was last asked about: a target is asked again only when its key changed or it is new, and a target no longer assigned is forgotten.</summary>
public sealed class SightCache
{
    readonly Dictionary<Guid, SightKey> _asked = new();

    public bool NeedsAsk(Guid target, SightKey key) => !_asked.TryGetValue(target, out var old) || !old.Equals(key);
    public void Asked(Guid target, SightKey key) => _asked[target] = key;
    public void Forget(Guid target) => _asked.Remove(target);
    /// <summary>Drops every target not in <paramref name="targets"/>.</summary>
    public void Keep(IReadOnlyCollection<Guid> targets) { foreach (var gone in _asked.Keys.Where(t => !targets.Contains(t)).ToList()) _asked.Remove(gone); }
}
