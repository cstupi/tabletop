namespace Tabletop.Client.Core;

/// <summary>A pinned set's catalog by rule name: the hover text of every rule, and whether the engine plays it.
/// A rule with no entry, or a text-only entry (Played false), is not automated (army-book rules spec → Played flag). Empty is the
/// catalog not loaded yet, or a failed fetch: it knows no text and tags nothing, since an unknown catalog says nothing about what is automated.</summary>
public sealed class RuleTexts
{
    readonly IReadOnlyDictionary<string, (string Text, bool Played)> _byName;
    RuleTexts(IReadOnlyDictionary<string, (string Text, bool Played)> byName, bool loaded) { _byName = byName; Loaded = loaded; }

    public static RuleTexts Empty { get; } = new(new Dictionary<string, (string, bool)>(), loaded: false);

    public static RuleTexts From(IReadOnlyList<RuleTextDto> rows)
    {
        var byName = new Dictionary<string, (string Text, bool Played)>(StringComparer.Ordinal);
        foreach (var row in rows) byName.TryAdd(row.Name, (row.RuleText, row.Played));
        return new RuleTexts(byName, loaded: true);
    }

    public bool Loaded { get; }
    public string? TextOf(string name) => _byName.TryGetValue(name, out var e) ? e.Text : null;
    public bool IsAutomated(string name) => _byName.TryGetValue(name, out var e) && e.Played;
    /// <summary>The *not automated* tag: the catalog is loaded, and has no entry for the name or a text-only one.</summary>
    public bool NotAutomated(string name) => Loaded && !IsAutomated(name);
}
