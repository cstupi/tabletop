using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tabletop.Content;

/// <summary>The closed set a stat declaration carries (identity and enumerations → StatDefinition.kind).</summary>
public enum StatKind { TargetNumber, Distance, Count, DiceExpression }

/// <summary>A stat's text as written, read under the kind its declaration gives it.</summary>
public readonly partial record struct StatValue(StatKind Kind, string Text)
{
    // ASCII digits only (\d also matches other scripts' digits, which int.Parse rejects) and at most six, so no read overflows.
    [GeneratedRegex(@"^\s*([0-9]{1,6})\s*\+?\s*$")] private static partial Regex TargetNumberPattern();
    [GeneratedRegex(@"^\s*([0-9]{1,6}(?:\.[0-9]{1,6})?)\s*(?:""|”|in)?\s*$")] private static partial Regex DistancePattern();
    [GeneratedRegex(@"^\s*([0-9]{1,6})\s*$")] private static partial Regex CountPattern();

    /// <summary>Validates <paramref name="text"/> against <paramref name="kind"/>. Dice expressions are kept as text; their grammar is undecided.</summary>
    public static StatValue Read(StatKind kind, string text)
    {
        var ok = kind switch
        {
            StatKind.TargetNumber => TargetNumberPattern().IsMatch(text),
            StatKind.Distance => DistancePattern().IsMatch(text),
            StatKind.Count => CountPattern().IsMatch(text),
            StatKind.DiceExpression => !string.IsNullOrWhiteSpace(text),
            _ => false,
        };
        return ok ? new StatValue(kind, text) : throw new FormatException($"'{text}' is not a {kind}.");
    }

    // All three are [JsonIgnore] because two of them throw for any given Kind, and System.Text.Json writes every get-only property.
    [JsonIgnore] public int TargetNumber => Kind == StatKind.TargetNumber
        ? int.Parse(TargetNumberPattern().Match(Text).Groups[1].Value, CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"{Kind} is not a target number.");

    [JsonIgnore] public double Inches => Kind == StatKind.Distance
        ? double.Parse(DistancePattern().Match(Text).Groups[1].Value, CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"{Kind} is not a distance.");

    [JsonIgnore] public int Count => Kind == StatKind.Count
        ? int.Parse(CountPattern().Match(Text).Groups[1].Value, CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"{Kind} is not a count.");
}
