using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The lobby's optional-rules controls over the ruleset's catalog (optional-rules spec → Lobby). Keys and values are the catalog's data:
/// nothing here names a rule.
/// </summary>
public static class RulesOptions
{
    /// <summary>What the preset control reads when no preset's map matches the game's.</summary>
    public const string Custom = "Custom";

    /// <summary>The option's value in the map, its default when the map has none.</summary>
    public static string ValueOf(RulesOption option, IReadOnlyDictionary<string, string> map) => map.GetValueOrDefault(option.Key) ?? option.Default;

    /// <summary>The first preset whose map reads every option the same as this one, else <see cref="Custom"/>.</summary>
    public static string PresetOf(RulesOptionCatalog catalog, IReadOnlyDictionary<string, string> map) =>
        catalog.Presets.FirstOrDefault(p => catalog.Options.All(o => ValueOf(o, p.OptionalRules) == ValueOf(o, map)))?.Name ?? Custom;

    /// <summary>The map to send for these choices: every option not at its default, by key.</summary>
    public static IReadOnlyDictionary<string, string> MapOf(RulesOptionCatalog catalog, IReadOnlyDictionary<string, string> chosen) =>
        catalog.Options.Where(o => ValueOf(o, chosen) != o.Default).ToDictionary(o => o.Key, o => ValueOf(o, chosen));
}
