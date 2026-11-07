using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The remove-casualties pick (in-game UX spec → Casualties). A click adds one wound to an offered model up to its wounds left, and on a
/// model that can take no more puts it back; a right-click takes one away; Clear empties the pick; Auto fills the rest in the Ask's order. Required is the lesser of the amount and what the models it may
/// use can take, so a pick whose wounds outnumber the models still completes. Under the Ask's MaxTargets of one, a click on another model
/// moves the pick there. The answer is Allocated, summing to the amount, any surplus on the last model counted for the server to carry over (p.8).
/// </summary>
public sealed class CasualtyPick
{
    readonly Allocation _legal;
    readonly IReadOnlyDictionary<Guid, int> _woundsLeft;
    readonly Dictionary<Guid, int> _counts = new();

    public CasualtyPick(Allocation legal, IReadOnlyDictionary<Guid, int> woundsLeft) { _legal = legal; _woundsLeft = woundsLeft; }

    public IReadOnlyDictionary<Guid, int> Counts => _counts;
    public int Chosen => _counts.Values.Sum();
    public bool Complete => Chosen == Required;

    /// <summary>The models it may still use: all of them, or under MaxTargets the ones counted once that many are, else the roomiest.</summary>
    public int Required
    {
        get
        {
            var targets = _legal.Targets.Distinct().ToList();
            if (_legal.MaxTargets is { } most && most < targets.Count)
                targets = _counts.Count >= most ? [.. _counts.Keys] : [.. targets.OrderByDescending(Capacity).Take(most)];
            return Math.Min(_legal.Amount, targets.Sum(Capacity));
        }
    }

    /// <summary>Every offered model takes one wound: the pick is which models are removed.</summary>
    public bool OneEach => _legal.Targets.All(m => Capacity(m) <= 1);

    public string Title => OneEach ? $"Remove {Required} {(Required == 1 ? "model" : "models")}"
        : $"Assign {_legal.Amount} {(_legal.Amount == 1 ? "wound" : "wounds")}" + (_legal.MaxTargets == 1 ? " to one model" : "");

    public string Status => $"{Chosen} of {Required} chosen";

    /// <summary>Wounds left; a model the facts do not name takes one.</summary>
    int Capacity(Guid modelId) => Math.Max(0, _woundsLeft.GetValueOrDefault(modelId, 1));

    public void Add(Guid modelId)
    {
        if (!_legal.Targets.Contains(modelId)) return;
        if (!_counts.ContainsKey(modelId) && _legal.MaxTargets is { } most && _counts.Count >= most)
        {
            if (most != 1) return;
            _counts.Clear();                                                // one model at a time: the click moves the pick
        }
        if (Chosen >= Required || _counts.GetValueOrDefault(modelId) >= Capacity(modelId)) return;
        _counts[modelId] = _counts.GetValueOrDefault(modelId) + 1;
    }

    /// <summary>A left click: a picked model that can take no more (its wounds left, or the pick full) is put back, anything else is Add.</summary>
    public void Click(Guid modelId)
    {
        if (_counts.ContainsKey(modelId) && (Chosen >= Required || _counts[modelId] >= Capacity(modelId))) _counts.Remove(modelId);
        else Add(modelId);
    }

    public void Remove(Guid modelId)
    {
        if (!_counts.TryGetValue(modelId, out var n)) return;
        if (n <= 1) _counts.Remove(modelId); else _counts[modelId] = n - 1;
    }

    public void Clear() => _counts.Clear();

    public void Auto()
    {
        foreach (var m in _legal.Targets)
        {
            if (!_counts.ContainsKey(m) && _legal.MaxTargets is { } most && _counts.Count >= most) continue;
            while (Chosen < Required && _counts.GetValueOrDefault(m) < Capacity(m)) Add(m);
        }
    }

    public AskAnswer? Answer()
    {
        if (!Complete) return null;
        var counts = new Dictionary<Guid, int>(_counts);
        var last = _counts.Count > 0 ? _counts.Keys.Last() : _legal.Targets.LastOrDefault();
        if (_legal.Amount > Chosen && last != Guid.Empty) counts[last] = counts.GetValueOrDefault(last) + _legal.Amount - Chosen;
        return new Allocated(counts);
    }
}
