namespace Tabletop.Protocol;

/// <summary>
/// A ruleset's optional rules, as data for the lobby (optional-rules spec → Wire). Keys, values, labels and pages are the ruleset's;
/// nothing outside it reads their meaning.
/// </summary>
public sealed record RulesOptionCatalog(IReadOnlyList<RulesOption> Options, IReadOnlyList<RulesPreset> Presets)
{
    public static RulesOptionCatalog Empty { get; } = new([], []);
}

/// <summary>One option: its map key, the book's name and page, its values in display order, and the value an absent key reads as.</summary>
public sealed record RulesOption(string Key, string Label, string Page, IReadOnlyList<RulesOptionValue> Values, string Default);

public sealed record RulesOptionValue(string Value, string Label);

/// <summary>A named option map; Enforcement, when set, is the setting the preset also picks.</summary>
public sealed record RulesPreset(string Name, IReadOnlyDictionary<string, string> OptionalRules, Enforcement? Enforcement);
