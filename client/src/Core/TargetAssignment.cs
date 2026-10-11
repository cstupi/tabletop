using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The declare-targets pick (in-game UX spec → Shooting targets). p.8: each weapon group fires at one target it can shoot (the
/// Ask's Assignment.TargetsOf), at most two targets in all. Every group starts picked; aiming points those picked groups that can
/// shoot a unit at it, and the others keep their aim; a chip picks its group alone. The answer is Assigned, each aimed group to its
/// target; a group left unaimed holds fire (p.8: models "may" shoot).
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

    /// <summary>The picked groups that can shoot the unit: the ones a click on it aims.</summary>
    IReadOnlyList<string> Shooters(Guid unitId) => _legal.Items.Where(i => _picked.Contains(i.Id) && _legal.TargetsOf(i.Id).Contains(unitId)).Select(i => i.Id).ToList();

    /// <summary>A unit some picked group can shoot that, with those groups moved onto it, keeps the distinct targets within the Ask's maximum.</summary>
    public bool CanTarget(Guid unitId)
    {
        var shooters = Shooters(unitId);
        return shooters.Count > 0
            && _assigned.Where(kv => !shooters.Contains(kv.Key)).Select(kv => kv.Value).Append(unitId).Distinct().Count() <= _legal.MaxDistinctTargets;
    }

    /// <summary>The units a click may aim at, which glow: with the Ask's maximum of distinct targets taken (p.8: up to two), only those
    /// targets; otherwise every offered unit a picked group can shoot and move onto.</summary>
    public IReadOnlyList<Guid> Candidates
    {
        get
        {
            var taken = _assigned.Values.Distinct().ToList();
            return _legal.Targets.Where(u => (taken.Count < _legal.MaxDistinctTargets || taken.Contains(u)) && CanTarget(u)).ToList();
        }
    }

    /// <summary>A group's chip: its label and how many weapons it holds (its pieces are the weapon ids).</summary>
    public static string Chip(AskOption group) => $"{group.Label} ×{group.Pieces.Count}";

    /// <summary>A chip with its target's name, or "holds fire" while the group has none.</summary>
    public static string Chip(AskOption group, string? target) => target is null ? $"{Chip(group)} · holds fire" : $"{Chip(group)} → {target}";

    public bool Aim(Guid targetUnitId)
    {
        if (!Candidates.Contains(targetUnitId)) return false;
        foreach (var g in Shooters(targetUnitId)) _assigned[g] = targetUnitId;
        return true;
    }

    public void Clear()
    {
        _assigned.Clear();
        _picked.Clear(); _picked.UnionWith(_legal.Items.Select(i => i.Id));
    }

    /// <summary>Null until some group has a target; the groups without one hold fire.</summary>
    public AskAnswer? Answer() => _assigned.Count > 0 ? new Assigned(new Dictionary<string, Guid>(_assigned)) : null;
}
