using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The attack's units and weapon profiles (detailed-log spec → What it shows, Attack), which LogSheet draws under a detailed attack's head.</summary>
public static class WorkedLines
{
    /// <summary>`Attack: {unit} ({n} models) → {targets}`, then one line per weapon name: `{name} ×{count} ({stats}, {rules})`.
    /// Stats as the army lists them: Range as it reads, Attacks as `A{n}`, any other `{name} {value}`.</summary>
    public static IReadOnlyList<string> Attack(SequenceContext c, Names n)
    {
        var weapons = n.WeaponsOf(c.WeaponIds);
        var models = weapons.Select(w => w.ModelId).Distinct().Count();
        var who = c.ActorUnitId is { } a ? n.Unit(a) : "Attack";
        var head = $"Attack: {who}{(models > 0 ? $" ({RollWords.Plural(models, "model")})" : "")}"
            + (c.TargetUnitIds.Count > 0 ? $" → {string.Join(", ", c.TargetUnitIds.Select(n.Unit))}" : "");
        var lines = new List<string> { head };
        foreach (var g in weapons.GroupBy(w => w.Weapon.Name))
            lines.Add($"  {g.Key} ×{g.Count()}{Profile(g.First().Weapon)}");
        return lines;
    }

    static string Profile(Weapon w)
    {
        var parts = w.Stats.Select(s => s.Key switch { "Range" => s.Value, "Attacks" => $"A{s.Value}", _ => $"{s.Key} {s.Value}" })
            .Concat(w.Properties.Select(p => p.Parameter is { } x ? $"{p.Name}({x})" : p.Name)).ToList();
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }
}
