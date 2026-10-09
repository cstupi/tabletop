using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The declare-targets pick (in-game UX spec → Shooting targets). p.8: each weapon group fires at one target, at most two targets in
/// all. Every group starts picked; aiming points the picked groups at a unit; a chip picks its group alone. The answer is
/// Assigned, every group to one target.
/// </summary>
public sealed class TargetAssignment
{
    readonly Assignment _legal;
    readonly HashSet<string> _picked;
    readonly Dictionary<string, Guid> _assigned = new();

    public TargetAssignment(Assignment legal)
    {
        _legal = legal;
        _picked = legal.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<AskOption> Groups => _legal.Items;
    public IReadOnlySet<string> Picked => _picked;
    public IReadOnlyDictionary<string, Guid> Assigned => _assigned;

    public void PickOnly(string groupId)
    {
        if (_legal.Items.All(i => i.Id != groupId)) return;
        _picked.Clear(); _picked.Add(groupId);
    }

    /// <summary>An offered unit that, with the picked groups moved onto it, keeps the distinct targets within the Ask's maximum.</summary>
    public bool CanTarget(Guid unitId) =>
        _picked.Count > 0 && _legal.Targets.Contains(unitId)
        && _assigned.Where(kv => !_picked.Contains(kv.Key)).Select(kv => kv.Value).Append(unitId).Distinct().Count() <= _legal.MaxDistinctTargets;

    public bool Aim(Guid targetUnitId)
    {
        if (!CanTarget(targetUnitId)) return false;
        foreach (var g in _picked) _assigned[g] = targetUnitId;
        return true;
    }

    public void Clear()
    {
        _assigned.Clear();
        _picked.Clear(); _picked.UnionWith(_legal.Items.Select(i => i.Id));
    }

    /// <summary>Null until every group has a target: the legal set wants every item answered.</summary>
    public AskAnswer? Answer() =>
        _legal.Items.All(i => _assigned.ContainsKey(i.Id)) ? new Assigned(new Dictionary<string, Guid>(_assigned)) : null;
}
