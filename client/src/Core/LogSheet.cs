namespace Tabletop.Client.Core;

/// <summary>One piece of the drawn log: an entry, or an attack's rolls (Entries), as lines of plain text at a depth (two spaces each).
/// Entry is what a click on it picks: the block, the line, the roll, an attack's first roll.</summary>
public sealed record LogParagraph(LogEntry Entry, IReadOnlyList<LogEntry> Entries, int Depth, IReadOnlyList<string> Lines)
{
    public bool Undone => Entry.Undone;

    /// <summary>The lines indented two spaces per depth; an empty line stays empty.</summary>
    public IEnumerable<string> Indented => Lines.Select(l => l.Length == 0 ? l : new string(' ', Depth * 2) + l);
}

/// <summary>
/// The log as plain text (combat-log spec → The log's text; detailed-log spec): what both panels draw and what a copy holds, one entry per
/// paragraph. The summary gives each entry one line: a block's header, a change line as CombatLog words it, an attack as
/// `{unit} → {target}  H: {hits}  W: {wounds}`, then `  with cover` when cover applied (with the attack's rolls per wound under it), any other roll as
/// `{Label} {target}: rolled {faces}, {result}`. The detailed log gives an attack its weapons, a 1–6 table of the faces per roll, a table per
/// reroll and per rule a face set off, the hits, each block's modifiers over its table, and `H: X  W: Y`; any other roll its target, its
/// faces and its result; a change line each model. Words come from CombatLog's entries; no rule is named here.
/// </summary>
public static partial class LogSheet
{
    public static IReadOnlyList<LogParagraph> Of(IReadOnlyList<LogEntry> entries, bool detailed)
    {
        var list = new List<LogParagraph>();
        Add(list, entries, 0, null, detailed);
        return list;
    }

    /// <summary>The paragraphs as one text, a line each.</summary>
    public static string Text(IEnumerable<LogParagraph> paragraphs) => string.Join("\n", paragraphs.SelectMany(p => p.Indented));

    /// <summary>The whole log as plain text: what the panels' Copy puts on the clipboard.</summary>
    public static string Text(IReadOnlyList<LogEntry> entries, bool detailed) => Text(Of(entries, detailed));

    static void Add(List<LogParagraph> into, IReadOnlyList<LogEntry> entries, int depth, Guid? actor, bool detailed)
    {
        // A detailed paragraph of several lines ends with an empty one, so blocks of lines stand apart.
        void Para(LogEntry e, IReadOnlyList<LogEntry> group, IReadOnlyList<string> lines) =>
            into.Add(new LogParagraph(e, group, depth, detailed && lines.Count > 1 ? [.. lines, ""] : lines));
        for (var i = 0; i < entries.Count; i++)
            switch (entries[i])
            {
                case ActivationBlock b:
                    Para(b, [b], [b.Header]);
                    Add(into, b.Entries, depth + 1, b.Actor ?? actor, detailed);
                    break;
                case RollEntry r:
                    var group = new List<RollEntry> { r };
                    while (r.InAttack is { } tag && i + 1 < entries.Count && entries[i + 1] is RollEntry next && next.InAttack?.Frame == tag.Frame && next.Undone == r.Undone)
                    {
                        group.Add(next); i++;
                    }
                    if (group.Any(x => x.Record.Roll is "hit" or "defense")) Para(r, group, detailed ? AttackDetail(group) : AttackSummary(group));
                    else foreach (var x in group) Para(x, [x], detailed ? RollDetail(x, actor) : [RollLine(x, actor)]);
                    break;
                case LineEntry l:
                    Para(l, [l], detailed && l.Worked is { } worked ? worked : [l.Text]);
                    break;
            }
    }

    static bool PerWound(RollEntry r) => RollWords.PerWound(r.Record.Roll, r.Sequence);
    static bool Defense(RollEntry r) => r.Record.Roll == "defense";

    /// <summary>`{unit} → {targets}`, with ` (Impact)` for hits a rule dealt and ` (melee)` for a melee weapon's.</summary>
    static string Who(AttackTag t) =>
        string.Join(" → ", new[] { t.Actor, t.Targets }.Where(s => s.Length > 0)) + (t.Source is { } s ? $" ({s})" : t.Melee ? " (melee)" : "");

    /// <summary>An attack's totals. Hits: the dice its blocks rolled (every hit that reached them), else the hits its rolls scored with the
    /// extra hits their faces set off, multiplied. Bonus: the hits past the ones rolled. Wounds: the blocks' failures and the extra wounds
    /// their faces set off, times the server's wound multiplier (Deadly; past what a model has left too), less the wounds a roll per wound
    /// ignored. Cover: a block's cover verdict applied.</summary>
    sealed record Totals(int Rolled, int Bonus, int Hits, int Wounds, bool Cover);

    static Totals Sum(IReadOnlyList<RollEntry> group)
    {
        var hitRolls = group.Where(r => !Defense(r) && !PerWound(r)).ToList();
        var blocks = group.Where(Defense).ToList();
        var rolled = hitRolls.Sum(r => r.Successes);
        var multiplier = hitRolls.Select(r => r.HitMultiplier).DefaultIfEmpty(1).Max();
        var hits = blocks.Count > 0 ? blocks.Sum(b => b.Dice.Count) : (rolled + hitRolls.Sum(r => Extra(r.Dice, "hit"))) * multiplier;
        var wounds = blocks.Sum(b => b.Dice.Count(d => !d.Success) + Extra(b.Dice, "wound")) * group[0].InAttack!.WoundMultiplier - group.Where(PerWound).Sum(r => r.Successes);
        return new Totals(rolled, Math.Max(0, hits - rolled), hits, Math.Max(0, wounds), blocks.Any(b => b.Record.Cover is { InCover: true, IgnoredBy: null }));
    }

    /// <summary>The extra things (`hit`, `wound`) the faces set off: `Rule +N things` on each die.</summary>
    static int Extra(IReadOnlyList<DieView> dice, string word) =>
        dice.SelectMany(d => d.Triggered ?? []).Select(RollWords.Parse).Where(t => t.Word == word).Sum(t => t.Amount);

    static IReadOnlyList<string> AttackSummary(IReadOnlyList<RollEntry> group)
    {
        var t = Sum(group);
        return [$"{Who(group[0].InAttack!)}  H: {t.Hits}  W: {t.Wounds}{(t.Cover ? "  with cover" : "")}", .. group.Where(PerWound).Select(r => RollLine(r, null))];
    }

    /// <summary>The unit a test is for, ahead of its line, when it is not the block's actor: `Blue unit 1 · `.</summary>
    static string Prefix(RollEntry r, Guid? actor) =>
        RollEntry.IsTest(r.Record, r.Sequence) && r.Unit is { } unit && r.Context.ActorUnitId != actor ? $"{unit} · " : "";

    /// <summary>`{Label} {target}: rolled {faces}, {result}` (a roll its modifiers add to: `Cast Hex: rolled 1 +1 token = 2, needs 4+, failed`) and each live reroll or edit; a roll-off `Roll-off: Team 1 rolled 3 · Team 2 rolled 2, Team 1 wins`.</summary>
    public static string RollLine(RollEntry r, Guid? actor)
    {
        if (r.Sum is { } sum && r.Record.Threshold is { } t)
            return $"{Prefix(r, actor)}{r.Label}: {sum}, needs {t}+{(r.Outcome.Length == 0 ? "" : $", {r.Outcome}")}" + string.Concat(r.Amendments.Where(a => !a.Undone).Select(a => " · " + a.Text));
        var head = r.Target.Length == 0 ? r.Label : $"{r.Label} {r.Target}";
        var faces = Labelled(r) is { } labels
            ? string.Join(" · ", r.Dice.Select((d, i) => $"{labels[i]} rolled {d.Raw}"))
            : $"rolled {string.Join(" ", r.Dice.Select(d => d.Raw))}";
        var outcome = r.Outcome.Length == 0 ? "" : $", {r.Outcome}";
        return $"{Prefix(r, actor)}{head}: {faces}{outcome}" + string.Concat(r.Amendments.Where(a => !a.Undone).Select(a => " · " + a.Text));
    }

    static IReadOnlyList<string>? Labelled(RollEntry r) => r.DieLabels is { Count: > 0 } labels && labels.Count == r.Dice.Count ? labels : null;
}
