using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The models of a drag or a placement in breach and why: the client's own measure (ContactCheck, by whoever holds the models: the decision
/// box for a move or a deployment's ghosts, the table for a hand move), and the server's reason for a commit it refused, which stands on the
/// models it sent until one of them moves again. The table tints every marked model and labels each reason; under strict a mark holds the commit.
/// </summary>
public sealed class Violations
{
    readonly Dictionary<Guid, string> _measured = new();
    readonly HashSet<Guid> _refused = new(); string? _refusal;
    Dictionary<Guid, string> _marked = new();

    public event Action? Changed;

    /// <summary>Each marked model's reason: the client's own, else the server's refusal.</summary>
    public IReadOnlyDictionary<Guid, string> Marked => _marked;

    /// <summary>A measure over these models: their marks are replaced by what it found; other models' are kept.</summary>
    public void Measure(IEnumerable<Guid> over, IReadOnlyDictionary<Guid, string> found)
    {
        foreach (var id in over) _measured.Remove(id);
        foreach (var (id, reason) in found) _measured[id] = reason;
        Update();
    }

    /// <summary>The server refused the commit that sent these models: they stay marked with its reason.</summary>
    public void Refused(IEnumerable<Guid> models, string reason)
    {
        _refused.Clear(); _refused.UnionWith(models); _refusal = reason;
        Update();
    }

    /// <summary>The player moved these models: a refusal naming one of them lapses.</summary>
    public void Moved(IEnumerable<Guid> models)
    {
        if (!models.Any(_refused.Contains)) return;
        _refused.Clear(); _refusal = null;
        Update();
    }

    public void Clear() { _measured.Clear(); _refused.Clear(); _refusal = null; Update(); }

    /// <summary>Under strict a marked model holds the commit; under warn and silent it goes and the server judges.</summary>
    public bool Blocks(Enforcement enforcement) => enforcement == Enforcement.Strict && _marked.Count > 0;

    /// <summary>One label per reason, over the first model in this order that has it.</summary>
    public IReadOnlyList<(Guid ModelId, string Reason)> Labels(IEnumerable<Guid> order) =>
        order.Where(_marked.ContainsKey).Select(id => (id, _marked[id])).DistinctBy(x => x.Item2).ToList();

    void Update()
    {
        var next = new Dictionary<Guid, string>(_measured);
        if (_refusal is { } r) foreach (var id in _refused) next.TryAdd(id, r);
        if (next.Count == _marked.Count && next.All(kv => _marked.TryGetValue(kv.Key, out var v) && v == kv.Value)) return;
        _marked = next;
        Changed?.Invoke();
    }
}
