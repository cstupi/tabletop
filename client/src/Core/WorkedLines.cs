using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The detailed log's words (detailed-log spec → What it shows): a roll as a worked resolution, top to bottom, in monospace columns:
/// the stat's printed value, each change to it, each row a suppression took away, the cover verdict, each roll modifier and each one
/// taken away, the target, every die (rolled, final, pass), rerolls and edits, the step's removed rules and the rules that acted, the
/// result. Composed from the record's names and numbers; no rule is named here.
/// </summary>
public static class WorkedLines
{
    const int LabelWidth = 16, DicePerRow = 10;
    const string Pass = "✓", Miss = "·";

    /// <summary>`  {label}{value}   {note}`: the label padded to 16, the value right-aligned in 3.</summary>
    public static string Row(string label, string value, string note = "")
    {
        var head = $"  {(label.Length >= LabelWidth ? label + " " : label.PadRight(LabelWidth))}{value.PadLeft(3)}";
        return note.Length == 0 ? head.TrimEnd() : $"{head}   {note}";
    }

    static string Signed(int n) => RollWords.Signed(n);

    /// <summary>A modifier's label as the normal log writes it, less its amount: `AP(1)`, `Rending AP(+4)`, `Stealth`.</summary>
    static string ModifierLabel(RollModifier m) => m.Source == "AP"
        ? m.Via is { } granted ? $"{granted} AP({Signed(-m.Amount)})" : $"AP({-m.Amount})"
        : $"{(m.Via is { } via ? via + " " : "")}{m.Source}";

    public static IReadOnlyList<string> Of(RollEntry e)
    {
        var r = e.Record; var lines = new List<string>();
        if (e.Attack is { } attack) lines.AddRange(attack);
        var perWound = RollWords.PerWound(r.Roll, e.Sequence);
        lines.Add(RollWords.Label(r.Roll) + (perWound ? " · one die per wound" : ""));
        var t = r.Threshold;
        if (r.Basis is { } b)
        {
            lines.Add(Row(b.Stat, $"{b.Printed}+", "printed"));
            var running = b.Printed;
            foreach (var c in b.Changes)
                if (c.SetTo is { } to) { running = to; lines.Add(Row(c.Source, $"{to}+", "set")); }
                else { running += c.Amount; lines.Add(Row(c.Source, Signed(c.Amount), $"→ {running}+")); }
            foreach (var x in b.Ignored ?? []) lines.Add(Row(x.Source, x.SetTo is { } to ? $"{to}+" : Signed(x.Amount), $"ignored: {x.By}"));
        }
        if (r.Cover is { } v)
        {
            var count = $"{v.Models}/{v.Of} models in cover";
            if (v.IgnoredBy is { } by) lines.Add(Row("cover", "+1", $"ignored: {by} ({count})"));
            else if (!v.InCover) lines.Add(Row("cover", "--", $"no cover: {count}"));
        }
        foreach (var m in r.Modifiers)
            lines.Add(Row(ModifierLabel(m), Signed(m.Amount), m.Source == "cover" && r.Cover is { } verdict ? $"{verdict.Models}/{verdict.Of} models in cover" : ""));
        foreach (var x in r.Ignored ?? []) lines.Add(Row(x.Source, Signed(x.Amount), $"ignored: {x.By}"));
        var net = r.Modifiers.Sum(m => m.Amount);
        if (r.RequiredRaw is { } raw) lines.Add(raw <= 1 ? "  target: every face" : $"  target unmodified {raw}+");
        else if (r is { Roll: "dangerous-terrain", Threshold: 2, Basis: null, Modifiers.Count: 0 }) lines.Add("  target: a 1 fails");
        else if (t is { } target) lines.Add($"  target {target}+" + (net != 0 ? $", roll {Signed(net)}" : ""));
        lines.AddRange(Dice(e, net));
        if (t is { } th && r.RequiredRaw is null && e.Dice.Any(d => d.Raw == 6 && d.Success && d.Modified < th || d.Raw == 1 && !d.Success && d.Modified >= th))
            lines.Add("  a 6 always succeeds, a 1 always fails (p.5)");
        foreach (var a in e.Amendments.Where(a => !a.Undone))
            for (var i = 0; i < a.Replaced.Count; i++)
            {
                var which = a.Indexes is { } ix && i < ix.Count ? $"die {ix[i] + 1}: " : "";
                var to = a.With is { } w && i < w.Count ? $" → {w[i]}" : "";
                var by = a.By is { } bs && i < bs.Count && bs[i] is { } rule ? $" ({rule})" : "";
                lines.Add($"  {a.Kind} {which}{a.Replaced[i]}{to}{by}");
            }
        foreach (var x in e.StepIgnored ?? []) lines.Add(Row(x.Source, "", $"ignored: {x.By}"));
        if (e.Applied.Count > 0) lines.Add($"  rules: {string.Join(", ", e.Applied)}");
        if (e.Outcome.Length > 0) lines.Add($"  = {e.Outcome}");
        return lines;
    }

    /// <summary>Ten dice a row: `rolled` (raw faces), `final` (raw plus the net modifier, when the roll judges it and it is not zero), and the
    /// pass row named for the roll. A roll-off with team labels reads one line per die; a roll with no target, `rolled` alone.</summary>
    static IEnumerable<string> Dice(RollEntry e, int net)
    {
        if (e.DieLabels is { Count: > 0 } labels && labels.Count == e.Dice.Count)
        {
            for (var i = 0; i < e.Dice.Count; i++) yield return Row(labels[i], e.Dice[i].Raw.ToString());
            yield break;
        }
        var word = e.Record.Roll switch
        {
            "hit" => "hits", "defense" => "blocks", _ when RollWords.PerWound(e.Record.Roll, e.Sequence) => "ignored", _ => "passes",
        };
        var final = e.Record.RequiredRaw is null && e.Record.Threshold is not null && net != 0;
        for (var start = 0; start < e.Dice.Count; start += DicePerRow)
        {
            var chunk = e.Dice.Skip(start).Take(DicePerRow).ToList();
            string Line(string label, Func<DieView, string> cell) => $"  {label.PadRight(7)}{string.Concat(chunk.Select(d => cell(d).PadLeft(3)))}";
            yield return Line("rolled", d => d.Raw.ToString());
            if (final) yield return Line("final", d => d.Modified.ToString());
            if (e.Judged) yield return Line(word, d => d.Success ? Pass : Miss);
        }
    }

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
