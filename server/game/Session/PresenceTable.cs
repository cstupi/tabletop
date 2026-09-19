using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Latest value per connected participant; existence is connectedness. Relayed on the tick, never stored anywhere else.</summary>
public sealed class PresenceTable
{
    private readonly Dictionary<Guid, Presence> _values = new();
    private readonly HashSet<Guid> _dirty = new();

    public void Set(Presence value) { _values[value.ParticipantId] = value; _dirty.Add(value.ParticipantId); }
    public void Remove(Guid participantId) { _values.Remove(participantId); _dirty.Remove(participantId); }
    public IReadOnlyList<Presence> All => _values.Values.ToList();

    /// <summary>Values changed since the last tick, then clears the set. Latest wins: intermediate samples are never relayed.</summary>
    public IReadOnlyList<Presence> TakeDirty()
    {
        var result = _dirty.Select(id => _values[id]).ToList();
        _dirty.Clear();
        return result;
    }
}
