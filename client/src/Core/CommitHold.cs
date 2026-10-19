using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A refused commit's ghosts: where each stood and faced, keyed as GhostStash keys them (a move's Ask id, a deployment's unit id),
/// and the models it moved (every model when none did), which carry the server's reason.</summary>
public sealed record HeldCommit(Guid Key, IReadOnlyDictionary<Guid, Point3> At, IReadOnlyDictionary<Guid, Yaw> Yaws, IReadOnlyList<Guid> Moved);

/// <summary>
/// The last move or placement commit, held until the server answers it: a refusal of that command hands back its ghosts so the player finds
/// them where they were dropped, marked with the server's reason, rather than snapped back. Any other send in between drops it.
/// </summary>
public sealed class CommitHold
{
    HeldCommit? _held; Guid? _command;

    /// <summary>The commit about to go: its relocations, before its command id is known.</summary>
    public void Remember(Guid key, IReadOnlyList<ModelRelocation> moves)
    {
        var landed = moves.Where(m => m.To is OnTable).ToDictionary(m => m.ModelId, m => ((OnTable)m.To).Placement);
        var moved = moves.Where(m => m.To is OnTable to && (m.From is not OnTable from
            || from.Placement.Position.Horizontal.DistanceTo(to.Placement.Position.Horizontal) > 1e-6)).Select(m => m.ModelId).ToList();
        _held = new HeldCommit(key, landed.ToDictionary(kv => kv.Key, kv => kv.Value.Position), landed.ToDictionary(kv => kv.Key, kv => kv.Value.Yaw),
            moved.Count > 0 ? moved : landed.Keys.ToList());
        _command = null;
    }

    /// <summary>A command went out: the remembered commit if it is still waiting for one, else nothing is held.</summary>
    public void Sent(Guid command)
    {
        if (_held is not null && _command is null) _command = command;
        else { _held = null; _command = null; }
    }

    /// <summary>A command was refused: the commit it carried, once; null for any other command.</summary>
    public HeldCommit? Refused(Guid command)
    {
        if (_held is null || _command != command) return null;
        var held = _held; _held = null; _command = null;
        return held;
    }
}
