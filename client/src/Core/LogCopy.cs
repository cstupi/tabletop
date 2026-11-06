namespace Tabletop.Client.Core;

/// <summary>
/// The log panel's Copy: the rows the panel draws as plain text, one line per row, two spaces per level. A block is its header (folded:
/// with its chain) and, open, its entries; a roll its collapsed line and, open, its details, dice and amendments; in the detailed log a
/// roll and a line with worked lines are those lines. An undone row ends `(undone)`.
/// </summary>
public static class LogCopy
{
    public static string Text(IReadOnlyList<LogEntry> entries, bool detailed, Func<LogEntry, bool> open)
    {
        var lines = new List<string>();
        foreach (var e in entries) Add(lines, e, 0, detailed, open);
        return string.Join("\n", lines);
    }

    /// <summary>One activation's Copy: its header and every entry with every roll open, whatever the panel shows folded.</summary>
    public static string Activation(ActivationBlock block, bool detailed) => Text([block], detailed, _ => true);

    static void Add(List<string> lines, LogEntry e, int depth, bool detailed, Func<LogEntry, bool> open)
    {
        var pad = new string(' ', depth * 2);
        void Row(string text, int extra = 0) => lines.Add(new string(' ', extra * 2) + pad + text + (e.Undone ? " (undone)" : ""));
        var isOpen = open(e);
        switch (e)
        {
            case RollEntry r when detailed: foreach (var l in r.Worked) Row(l); return;
            case LineEntry { Worked: { } worked } when detailed: foreach (var l in worked) Row(l); return;
            case LineEntry l: Row(l.Text); return;
            case ActivationBlock b:
                Row(isOpen || b.Chain.Length == 0 ? b.Header : $"{b.Header}  ·  {b.Chain}");
                if (isOpen) foreach (var x in b.Entries) Add(lines, x, depth + 1, detailed, open);
                return;
            case RollEntry r:
                Row(r.Collapsed(CombatLog.Pip));
                if (!isOpen) return;
                foreach (var l in r.Details.Concat(r.Dice.Select(d => d.Text)).Concat(r.Amendments.Select(a => a.Text))) Row(l, 1);
                return;
        }
    }
}
