using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The detailed log's lines (detailed-log spec → What it shows): tables of faces, each roll's target and modifiers, the totals.</summary>
public static partial class LogSheet
{
    /// <summary>A header row of the faces 1–6 over a row of how many dice show each, columns two wide (wider for a count of 100), a space apart.</summary>
    public static IReadOnlyList<string> Table(IEnumerable<int> faces)
    {
        var counts = new int[7];
        foreach (var f in faces) if (f is >= 1 and <= 6) counts[f]++;
        var width = Math.Max(2, counts.Max().ToString().Length);
        string Row(Func<int, int> cell) => string.Join(" ", Enumerable.Range(1, 6).Select(f => cell(f).ToString().PadLeft(width)));
        return [Row(f => f), Row(f => counts[f])];
    }

    /// <summary>
    /// What a roll is judged against, ` · ` between the parts: the printed stat (`Defense 5+`, a bare `5+` with no stat), each change to it
    /// (`Rule 3+`, `Rule -1`), each row taken away (`Rule -1 ignored (By)`), cover a rule took away (`Cover ignored (Indirect)`; nothing
    /// without cover), each modifier with the sign the roll applies (`AP(1) -1`, `Rending AP(4) -4`, `Cover +1`), each
    /// modifier taken away, and `needs N+` (2..6) when anything changed it. A roll on the raw face reads `unmodified N+`; dangerous terrain `1 fails`.
    /// </summary>
    public static string Threshold(RollRecord r)
    {
        if (r.RequiredRaw is { } raw) return raw <= 1 ? "" : $"unmodified {raw}+";
        if (r.Threshold is not { } t) return "";
        if (r is { Roll: "dangerous-terrain", Threshold: 2, Basis: null, Modifiers.Count: 0 }) return "1 fails";
        var parts = new List<string> { r.Basis is { } b ? $"{b.Stat} {b.Printed}+" : $"{t}+" };
        if (r.Basis is { } basis)
        {
            parts.AddRange(basis.Changes.Select(c => c.SetTo is { } to ? $"{c.Source} {to}+" : $"{c.Source} {RollWords.Signed(c.Amount)}"));
            parts.AddRange((basis.Ignored ?? []).Select(x => $"{x.Source} {(x.SetTo is { } to ? $"{to}+" : RollWords.Signed(x.Amount))} ignored ({x.By})"));
        }
        if (r.Cover is { IgnoredBy: { } by }) parts.Add($"Cover ignored ({by})");
        parts.AddRange(r.Modifiers.Select(Modifier));
        parts.AddRange((r.Ignored ?? []).Select(x => $"{x.Source} {RollWords.Signed(x.Amount)} ignored ({x.By})"));
        if (r.Modifiers.Count > 0 || r.Basis is { Changes.Count: > 0 })
            parts.Add($"needs {Math.Clamp(t - r.Modifiers.Sum(m => m.Amount), 2, 6)}+");
        return string.Join(" · ", parts);
    }

    /// <summary>`AP(1) -1`, `Rending AP(4) -4` (granted), `Cover +1`, `Stealth -1`: the modifier's name and the amount the roll adds.</summary>
    public static string Modifier(RollModifier m)
    {
        var via = m.Via is { } v ? v + " " : "";
        var name = m.Source == "AP" ? $"AP({-m.Amount})" : RollEntry.Capital(m.Source);
        return $"{via}{name} {RollWords.Signed(m.Amount)}";
    }

    /// <summary>The faces as rolled, before any live reroll or edit.</summary>
    static IReadOnlyList<int> Rolled(RollEntry r)
    {
        var faces = r.Dice.Select(d => d.Raw).ToArray();
        foreach (var a in r.Amendments.Where(a => !a.Undone).Reverse())
            for (var i = 0; i < a.Replaced.Count; i++)
                if (a.Indexes is { } ix && i < ix.Count && ix[i] >= 0 && ix[i] < faces.Length) faces[ix[i]] = a.Replaced[i];
        return faces;
    }

    /// <summary>The roll's table of faces as rolled, then each live reroll or edit (one die: `Rerolled (Bane): 6 → 2`; more: a table of the
    /// new faces), then each rule its faces set off, as a table of the dice that set it off (`Relentless (6s: +1 hit)`).</summary>
    static IEnumerable<string> Dice(RollEntry r)
    {
        foreach (var l in Table(Rolled(r))) yield return l;
        foreach (var l in Amended(r)) yield return l;
        foreach (var g in r.Dice.SelectMany(d => (d.Triggered ?? []).Select(t => (Face: d.Raw, Trigger: RollWords.Parse(t)))).GroupBy(x => x.Trigger))
        {
            var (rule, amount, word) = g.Key;
            var faces = string.Join(", ", g.Select(x => x.Face).Distinct().Order().Select(f => $"{f}s"));
            yield return $"{rule} ({faces}{(word.Length == 0 ? "" : $": +{amount} {word}{(amount == 1 ? "" : "s")}")})";
            foreach (var l in Table(g.Select(x => x.Face))) yield return l;
        }
        if (Always(r) is { } note) yield return note;
    }

    /// <summary>The p.5 note, when it decided a die: a 6 passed short of the target, or a 1 failed past it.</summary>
    static string? Always(RollEntry r) =>
        r.Record is { Threshold: { } t, RequiredRaw: null } && r.Dice.Any(d => d.Raw == 6 && d.Success && d.Modified < t || d.Raw == 1 && !d.Success && d.Modified >= t)
            ? "a 6 always succeeds, a 1 always fails" : null;

    static IEnumerable<string> Amended(RollEntry r)
    {
        foreach (var a in r.Amendments.Where(a => !a.Undone))
        {
            var by = string.Join(", ", (a.By ?? []).OfType<string>().Distinct());
            var label = RollEntry.Capital(a.Kind) + (by.Length > 0 ? $" ({by})" : "");
            var with = a.With ?? [];
            if (a.Replaced.Count == 1) { yield return $"{label}: {a.Replaced[0]}{(with.Count > 0 ? $" → {with[0]}" : "")}"; continue; }
            yield return $"{label} ({RollWords.Plural(with.Count, "die")})";
            foreach (var l in Table(with)) yield return l;
        }
    }

    /// <summary>The rules that acted in the attack's steps, and the rules a suppression took away that no roll names.</summary>
    static IEnumerable<string> Rules(IReadOnlyList<RollEntry> rolls)
    {
        var applied = rolls.SelectMany(r => r.Applied).Distinct().ToList();
        if (applied.Count > 0) yield return $"Rules: {string.Join(", ", applied)}";
        foreach (var x in rolls.SelectMany(r => r.StepIgnored ?? [])) yield return $"{x.Source} ignored ({x.By})";
    }

    static string Count(RollEntry r) => $"({RollWords.Plural(r.Dice.Count, PerWound(r) ? "wound" : "die")})";

    static IReadOnlyList<string> AttackDetail(IReadOnlyList<RollEntry> group)
    {
        var tag = group[0].InAttack!; var t = Sum(group);
        var lines = new List<string> { Who(tag) + (tag.Weapons.Length > 0 && tag.Source is null ? $" · {tag.Weapons}" : "") };
        if (group.FirstOrDefault(r => r.Attack is { Count: > 1 })?.Attack is { } attack) lines.AddRange(attack.Skip(1));
        var hitRolls = group.Where(r => !Defense(r) && !PerWound(r)).ToList();
        foreach (var r in hitRolls)
        {
            var th = Threshold(r.Record);
            lines.Add($"{r.Label} roll{(th.Length > 0 ? $" · {th}" : "")} {Count(r)}");
            lines.AddRange(Dice(r));
        }
        var multiplier = hitRolls.Select(r => r.HitMultiplier).DefaultIfEmpty(1).Max();
        if (multiplier > 1) lines.Add($"Hits ×{multiplier}");
        lines.Add(hitRolls.Count == 0 ? $"{(tag.Source is { } s ? s + ": " : "")}{RollWords.Plural(t.Hits, "hit")}"
            : RollWords.Plural(t.Rolled, "hit") + (t.Bonus > 0 ? $" · {RollWords.Plural(t.Bonus, "bonus hit")}" : ""));
        var after = group.Where(r => Defense(r) || PerWound(r)).ToList();
        if (after.Count > 0) lines.Add("");
        foreach (var r in after)
        {
            var th = Threshold(r.Record);
            lines.Add(Defense(r) ? $"{(r.Record.Basis is null ? "Defense " : "")}{th} {Count(r)}" : $"{r.Label}{(th.Length > 0 ? $" · {th}" : "")} {Count(r)}");
            lines.AddRange(Dice(r));
            if (PerWound(r)) lines.Add($"{r.Successes} ignored");
        }
        if (tag.WoundMultiplier > 1) lines.Add($"Wounds ×{tag.WoundMultiplier}");
        lines.Add($"H: {t.Hits}  W: {t.Wounds}");
        lines.AddRange(Rules(group));
        return lines;
    }

    /// <summary>A roll outside an attack: its label and target, its faces (`rolled 3` for one die, `Team 1 rolled 3` for a roll-off, else a
    /// table), each reroll and rule set off, its result, the rules that acted.</summary>
    static IReadOnlyList<string> RollDetail(RollEntry r, Guid? actor)
    {
        var th = Threshold(r.Record);
        var lines = new List<string> { $"{Prefix(r, actor)}{r.Label}{(th.Length > 0 ? $" · {th}" : "")}{(r.Dice.Count > 1 ? " " + Count(r) : "")}" };
        if (Labelled(r) is { } labels) lines.AddRange([.. r.Dice.Select((d, i) => $"{labels[i]} rolled {d.Raw}"), .. Amended(r)]);
        else if (r.Dice.Count == 1) lines.AddRange([$"rolled {Rolled(r)[0]}", .. Amended(r), .. Always(r) is { } note ? [note] : Array.Empty<string>()]);
        else lines.AddRange(Dice(r));
        if (r.Outcome.Length > 0) lines.Add(r.Outcome);
        lines.AddRange(Rules([r]));
        return lines;
    }
}
