using System.Text.RegularExpressions;

namespace Tabletop.Client.Core;

/// <summary>A spell's hover text: its catalog entry's Army Forge text (kind spell entries ride the same `/rules` rows as rules, so
/// <see cref="RuleTexts"/> resolves them by name) under its name and casting value. The pick-spell Ask offers
/// `{spell}|{caster model}` ids labelled `{spell} ({cost})`.</summary>
public static partial class SpellText
{
    [GeneratedRegex(@"^(?<name>.*?)(?: \((?<cost>\d+)\))?$", RegexOptions.Singleline)]
    private static partial Regex Labelled();

    /// <summary>The spell name of a pick-spell option id: everything before the last '|' (a spell name may hold one). An id with no '|' is the name.</summary>
    public static string NameOfOption(string optionId) { var cut = optionId.LastIndexOf('|'); return cut > 0 ? optionId[..cut] : optionId; }

    /// <summary>A label `Hex (3)` as its name and casting value; a label with no trailing number has no value.</summary>
    public static (string Name, int? Cost) Split(string label)
    {
        var m = Labelled().Match(label);
        return (m.Groups["name"].Value, m.Groups["cost"].Success ? int.Parse(m.Groups["cost"].Value) : null);
    }

    /// <summary>`Name`, `Casting value N` when known, then the catalog text. Null when the name is empty.</summary>
    public static string? Tooltip(RuleTexts rules, string name, int? cost = null)
    {
        if (name.Length == 0) return null;
        var lines = new List<string> { name };
        if (cost is { } c) lines.Add($"Casting value {c}");
        if (rules.TextOf(name) is { Length: > 0 } text) lines.Add(text);
        return string.Join("\n", lines);
    }

    /// <summary>The tooltip of a pick-spell option, from its id and label.</summary>
    public static string? OfOption(RuleTexts rules, string optionId, string label) => Tooltip(rules, NameOfOption(optionId), Split(label).Cost);
}
