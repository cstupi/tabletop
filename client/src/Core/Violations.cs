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
    readonly List<Guid> _order = new();
    readonly HashSet<Guid> _refused = new(); string? _refusal;
    Dictionary<Guid, string> _words = new(), _marked = new();

    public event Action? Changed;

    /// <summary>Names a model for the decision box's sentences (ContactCheck.Sentence); without it, or for a model it does not name, a mark is its words.</summary>
    public Func<Guid, string?>? Subject { get; set; }

    /// <summary>Each marked model's reason for the decision box: the client's own as a sentence naming the first model with it, else the server's refusal.</summary>
    public IReadOnlyDictionary<Guid, string> Marked => _marked;

    /// <summary>A measure over these models: their marks are replaced by what it found; other models' are kept.</summary>
    public void Measure(IEnumerable<Guid> over, IReadOnlyDictionary<Guid, string> found)
    {
        foreach (var id in over) { _measured.Remove(id); _order.Remove(id); }
        foreach (var (id, reason) in found) { if (_measured.TryAdd(id, reason)) _order.Add(id); else _measured[id] = reason; }
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

    public void Clear() { _measured.Clear(); _order.Clear(); _refused.Clear(); _refusal = null; Update(); }

    /// <summary>Under strict a marked model holds the commit; under warn and silent it goes and the server judges.</summary>
    public bool Blocks(Enforcement enforcement) => enforcement == Enforcement.Strict && _marked.Count > 0;

    /// <summary>One label per reason, over the first model in this order that has it: the words, since the label stands over the model.</summary>
    public IReadOnlyList<(Guid ModelId, string Reason)> Labels(IEnumerable<Guid> order) =>
        order.Where(_words.ContainsKey).Select(id => (id, _words[id])).DistinctBy(x => x.Item2).ToList();

    void Update()
    {
        var words = new Dictionary<Guid, string>(_measured);
        if (_refusal is { } r) foreach (var id in _refused) words.TryAdd(id, r);
        var said = new Dictionary<string, string>();                           // each measured reason's sentence, about its first model
        foreach (var id in _order)
        {
            var w = _measured[id];
            if (!said.ContainsKey(w)) said[w] = Subject?.Invoke(id) is { } name ? ContactCheck.Sentence(w, name) : w;
        }
        var marked = words.ToDictionary(kv => kv.Key, kv => _measured.ContainsKey(kv.Key) ? said[kv.Value] : kv.Value);
        if (Same(words, _words) && Same(marked, _marked)) return;
        _words = words; _marked = marked;
        Changed?.Invoke();
    }

    static bool Same(Dictionary<Guid, string> a, Dictionary<Guid, string> b) => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
}
