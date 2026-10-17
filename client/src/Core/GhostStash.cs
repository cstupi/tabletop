using Tabletop.Geometry;
namespace Tabletop.Client.Core;

/// <summary>A ghost as it stood: where, and the facing the player turned it to (null when never turned).</summary>
public sealed record KeptGhost(Point3 Position, Yaw? Yaw);

/// <summary>A move's or a deploy's ghosts across a reconnect, which drops the projection and the table's nodes. Keyed by the Ask id (a move)
/// or the unit id (a deploy placement); the first render after the reconnect takes it, and only the same key gets the ghosts back.</summary>
public sealed class GhostStash
{
    Guid? _key; IReadOnlyDictionary<Guid, KeptGhost>? _ghosts;

    public void Keep(Guid key, IReadOnlyDictionary<Guid, Point3> positions, IReadOnlyDictionary<Guid, Yaw> yaws)
    {
        _key = key;
        _ghosts = positions.ToDictionary(kv => kv.Key, kv => new KeptGhost(kv.Value, yaws.TryGetValue(kv.Key, out var y) ? y : null));
    }

    public IReadOnlyDictionary<Guid, KeptGhost>? Take(Guid key)
    {
        var ghosts = _key == key ? _ghosts : null;
        _key = null; _ghosts = null;
        return ghosts;
    }
}
