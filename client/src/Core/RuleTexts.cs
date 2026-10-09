namespace Tabletop.Client.Core;

/// <summary>A pinned set's catalog by rule name: the hover text of every rule, and whether the rule is in the catalog at all.
/// A rule with no entry is not automated (in-game UX spec → Stat sheet).</summary>
public sealed class RuleTexts
{
    readonly IReadOnlyDictionary<string, string> _byName;
    RuleTexts(IReadOnlyDictionary<string, string> byName) => _byName = byName;

    public static RuleTexts Empty { get; } = new(new Dictionary<string, string>());

    public static RuleTexts From(IReadOnlyList<RuleTextDto> rows)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) byName.TryAdd(row.Name, row.RuleText);
        return new RuleTexts(byName);
    }

    public string? TextOf(string name) => _byName.TryGetValue(name, out var text) ? text : null;
    public bool IsAutomated(string name) => _byName.ContainsKey(name);
}
