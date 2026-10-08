using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Models the player dropped whose MoveModels the server has not answered. The table leaves them where they were dropped; the move's event, or a refusal, ends the hold.</summary>
public sealed class PendingDrops
{
    readonly Dictionary<Guid, (Guid Command, Point3 To)> _held = new();
    public bool Holds(Guid modelId) => _held.ContainsKey(modelId);

    public void Add(Guid command, IEnumerable<ModelRelocation> moves)
    { foreach (var m in moves) if (m.To is OnTable on) _held[m.ModelId] = (command, on.Placement.Position); }

    /// <summary>A move arrived for the model: the hold ends. True when it landed where this client dropped it, so the node is already there and nothing needs animating.</summary>
    public bool Consume(ModelRelocation moved) => _held.Remove(moved.ModelId, out var h) && moved.To is OnTable on && on.Placement.Position == h.To;

    /// <summary>The command was refused or never sent: the models it held, now back to the projection's truth.</summary>
    public IReadOnlyList<Guid> Refuse(Guid command)
    {
        var ids = _held.Where(kv => kv.Value.Command == command).Select(kv => kv.Key).ToList();
        foreach (var id in ids) _held.Remove(id);
        return ids;
    }
}
