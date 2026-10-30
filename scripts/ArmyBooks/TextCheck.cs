using System.Text.RegularExpressions;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>The printed text wins (spell library → Text and generation): every fact the translation took from `generation` must be in `effect`.</summary>
public static partial class TextCheck
{
    [GeneratedRegex(@"takes? (\d+) hits?")] private static partial Regex Hits();

    public static IReadOnlyList<string> Problems(AfSpell spell, EntryDocument entry)
    {
        var problems = new List<string>(); var text = spell.Effect; var where = spell.Name.Trim();
        var a = spell.Generation.Assignment; var e = spell.Generation.Effect;
        var plural = a.Text("numberText") == "one" ? "" : "s";
        var clause = $"Pick {a.Text("numberText")} {a.Text("friendly-enemy")} {a.Text("target")}{plural} {(SpellTranslator.RangeCorrection(spell) is { } c ? a.Text("inRange")!.Replace(c.Generation.ToString(), c.Printed.ToString()) : a.Text("inRange"))}";
        if (!text.StartsWith(clause, StringComparison.Ordinal)) problems.Add($"{where}: generation says '{clause}'; the text does not");
        if (a.Text("inLoS") is { Length: > 0 } sight && !text.Contains(sight, StringComparison.Ordinal)) problems.Add($"{where}: generation says '{sight}'; the text does not");
        if (entry.Effects.FirstOrDefault(f => f.Kind == EffectKind.AddHit) is { } hits && !(Hits().Match(text) is { Success: true } m && m.Groups[1].Value == hits.Amount.ToString()))
            problems.Add($"{where}: generation says {hits.Amount} hits; the text does not");
        foreach (var key in new[] { "weaponRule1", "weaponRule2", "weaponRule", "unitRule" })
            if (e.Rule(key) is { } r && !text.Contains(r.Rating is { } n ? $"{r.Name}({n})" : r.Name, StringComparison.Ordinal))
                problems.Add($"{where}: generation names {r.Name}{(r.Rating is { } k ? $"({k})" : "")}; the text does not");
        foreach (var key in new[] { "attackType", "rollModifier", "terrainType" })
            if (e.Text(key) is { Length: > 0 } phrase && !text.Contains(phrase, StringComparison.Ordinal))
                problems.Add($"{where}: generation says '{phrase}'; the text does not");
        return problems;
    }
}
