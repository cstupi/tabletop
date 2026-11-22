namespace Tabletop.Protocol;

/// <summary>
/// A ruleset's optional rules, as data for the lobby (optional-rules spec → Wire). Keys, values and labels are the ruleset's;
/// nothing outside it reads their meaning.
/// </summary>
public sealed record RulesOptionCatalog(IReadOnlyList<RulesOption> Options, IReadOnlyList<RulesPreset> Presets)
{
    public static RulesOptionCatalog Empty { get; } = new([], []);
}

/// <summary>
/// One option: its map key, the book's name, its values in display order, the value an absent key reads as, and what the option decides in
/// table terms (the lobby's tooltip). The UI carries no page citations. A catalog without descriptions reads them as empty.
/// </summary>
public sealed record RulesOption(string Key, string Label, IReadOnlyList<RulesOptionValue> Values, string Default, string Description = "");

/// <summary>One value of an option: its map value, its name, and what playing with it means in table terms (the lobby's tooltip).</summary>
public sealed record RulesOptionValue(string Value, string Label, string Description = "");

/// <summary>A named option map; Enforcement, when set, is the setting the preset also picks.</summary>
public sealed record RulesPreset(string Name, IReadOnlyDictionary<string, string> OptionalRules, Enforcement? Enforcement);
