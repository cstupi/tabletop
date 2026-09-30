using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Other participants' latest presence. Existence is connectedness; cleared when our own socket closes.</summary>
public sealed class PresenceTable
{
    readonly Dictionary<Guid, Presence> _values = new();
    public void Set(Presence value) => _values[value.ParticipantId] = value;
    public void Remove(Guid participantId) => _values.Remove(participantId);
    public Presence? Of(Guid participantId) => _values.GetValueOrDefault(participantId);
    public IReadOnlyCollection<Presence> All => _values.Values;
    public void Clear() => _values.Clear();
}
