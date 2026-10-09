using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The remove-casualties pick (in-game UX spec → Casualties). A click adds one wound to an offered model up to its wounds left; a
/// right-click takes one away; Auto fills the rest in the Ask's order. Required is the lesser of the amount and what the offered
/// models can take, so a pick whose wounds outnumber the models still completes. The answer is Allocated,
/// summing to the amount, any surplus on the last target for the server to carry over (p.8).
/// </summary>
public sealed class CasualtyPick
{
    readonly Allocation _legal;
    readonly IReadOnlyDictionary<Guid, int> _woundsLeft;
    readonly Dictionary<Guid, int> _counts = new();

    public CasualtyPick(Allocation legal, IReadOnlyDictionary<Guid, int> woundsLeft)
    {
        _legal = legal; _woundsLeft = woundsLeft;
        Required = Math.Min(legal.Amount, legal.Targets.Distinct().Sum(Capacity));
    }

    public IReadOnlyDictionary<Guid, int> Counts => _counts;
    public int Chosen => _counts.Values.Sum();
    public int Required { get; }
    public bool Complete => Chosen == Required;

    /// <summary>Wounds left; a model the facts do not name takes one.</summary>
    int Capacity(Guid modelId) => Math.Max(0, _woundsLeft.GetValueOrDefault(modelId, 1));

    public void Add(Guid modelId)
    {
        if (!_legal.Targets.Contains(modelId) || Chosen >= Required || _counts.GetValueOrDefault(modelId) >= Capacity(modelId)) return;
        _counts[modelId] = _counts.GetValueOrDefault(modelId) + 1;
    }

    public void Remove(Guid modelId)
    {
        if (!_counts.TryGetValue(modelId, out var n)) return;
        if (n <= 1) _counts.Remove(modelId); else _counts[modelId] = n - 1;
    }

    public void Auto()
    {
        foreach (var m in _legal.Targets)
            while (Chosen < Required && _counts.GetValueOrDefault(m) < Capacity(m)) Add(m);
    }

    public AskAnswer? Answer()
    {
        if (!Complete) return null;
        var counts = new Dictionary<Guid, int>(_counts);
        if (_legal.Amount > Chosen && _legal.Targets.Count > 0)
            counts[_legal.Targets[^1]] = counts.GetValueOrDefault(_legal.Targets[^1]) + _legal.Amount - Chosen;
        return new Allocated(counts);
    }
}
