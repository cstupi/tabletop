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

    /// <summary>The units a click may aim at, which glow: with the Ask's maximum of distinct targets taken (p.8: up to two), only those
    /// targets; otherwise every offered unit the picked groups may move onto.</summary>
    public IReadOnlyList<Guid> Candidates
    {
        get
        {
            var taken = _assigned.Values.Distinct().ToList();
            return taken.Count >= _legal.MaxDistinctTargets ? _legal.Targets.Where(taken.Contains).ToList() : _legal.Targets.Where(CanTarget).ToList();
        }
    }

    /// <summary>A group's chip: its label and how many weapons it holds (its pieces are the weapon ids).</summary>
    public static string Chip(AskOption group) => $"{group.Label} ×{group.Pieces.Count}";

    public bool Aim(Guid targetUnitId)
    {
        if (!Candidates.Contains(targetUnitId)) return false;
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
