namespace Tabletop.Client.Core;

/// <summary>A pinned set's catalog by rule name: the hover text of every rule, and whether the rule is in the catalog at all.
/// A rule with no entry is not automated (in-game UX spec → Stat sheet). Empty is the catalog not loaded yet, or a failed fetch:
/// it knows no text and tags nothing, since an unknown catalog says nothing about what is automated.</summary>
public sealed class RuleTexts
{
    readonly IReadOnlyDictionary<string, string> _byName;
    RuleTexts(IReadOnlyDictionary<string, string> byName, bool loaded) { _byName = byName; Loaded = loaded; }

    public static RuleTexts Empty { get; } = new(new Dictionary<string, string>(), loaded: false);

    public static RuleTexts From(IReadOnlyList<RuleTextDto> rows)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) byName.TryAdd(row.Name, row.RuleText);
        return new RuleTexts(byName, loaded: true);
    }

    public bool Loaded { get; }
    public string? TextOf(string name) => _byName.TryGetValue(name, out var text) ? text : null;
    public bool IsAutomated(string name) => _byName.ContainsKey(name);
    /// <summary>The *not automated* tag: the catalog is loaded and has no entry for the name.</summary>
    public bool NotAutomated(string name) => Loaded && !_byName.ContainsKey(name);
}
