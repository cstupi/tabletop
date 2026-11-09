using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>What a unit's rings are built from: its models on the table with their slots and placements (<see cref="RangeRings.Key"/>), and its
/// threat (null for V's printed ranges). Equal stamps give equal rings.</summary>
public sealed class RingStamp(Guid unit, IReadOnlyList<(Guid Model, ModelSlot Slot, Placement At)> at, UnitThreat? threat) : IEquatable<RingStamp>
{
    public static RingStamp Of(Projection p, Guid unitId, UnitThreat? threat) => new(unitId, RangeRings.Key(p, unitId), threat);

    public bool Equals(RingStamp? other) => other is not null && unit == other.UnitId && at.SequenceEqual(other.At)
        && (threat is null ? other.Threat is null : other.Threat is not null && ThreatRings.Same(threat, other.Threat));
    public override bool Equals(object? obj) => Equals(obj as RingStamp);
    public override int GetHashCode() => HashCode.Combine(unit, at.Count);

    Guid UnitId => unit;
    IReadOnlyList<(Guid Model, ModelSlot Slot, Placement At)> At => at;
    UnitThreat? Threat => threat;
}

/// <summary>
/// Ring outlines built off the main thread (a 16-model unit's threat or T's union takes tens of milliseconds): one build per key, kept while
/// its stamps stand. <see cref="Get"/> answers the built result, or null while the build runs (or after it failed) and starts one when the stamps
/// changed; <see cref="Built"/> fires on the builder's thread when one lands, so the caller draws again. The build must read only values the
/// caller took on the main thread. The oldest key goes past <c>capacity</c>.
/// </summary>
public sealed class RingBuilds<TKey, TResult>(Func<Func<TResult>, Task<TResult>>? run = null, int capacity = 64) where TKey : notnull where TResult : class
{
    readonly Func<Func<TResult>, Task<TResult>> _run = run ?? (build => Task.Run(build));
    readonly Dictionary<TKey, (IReadOnlyList<RingStamp> Stamps, Task<TResult> Job)> _builds = new();
    readonly LinkedList<TKey> _order = new();
    public event Action? Built;

    public TResult? Get(TKey key, IReadOnlyList<RingStamp> stamps, Func<TResult> build)
    {
        if (!_builds.TryGetValue(key, out var held) || !held.Stamps.SequenceEqual(stamps))
        {
            var job = _run(build);
            _builds[key] = held = (stamps, job);
            _order.Remove(key); _order.AddLast(key);
            while (_order.Count > capacity) { _builds.Remove(_order.First!.Value); _order.RemoveFirst(); }
            if (!job.IsCompleted) job.ContinueWith(_ => Built?.Invoke(), TaskScheduler.Default);
        }
        return held.Job.IsCompletedSuccessfully ? held.Job.Result : null;
    }
}
