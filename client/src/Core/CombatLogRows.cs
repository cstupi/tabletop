namespace Tabletop.Client.Core;

/// <summary>
/// What the log panel can keep across a render: the leading top-level entries that draw the same as the rows already on screen.
/// Same compares everything a row shows (text, dice, amendments, rule chips, details, struck or not, nested entries) by value; a false
/// "differs" only costs a rebuilt row, so a roll's record compares by instance.
/// </summary>
public static class CombatLogRows
{
    /// <summary>How many leading entries of next draw as drawn does; the panel keeps their rows and rebuilds the rest.</summary>
    public static int Kept(IReadOnlyList<LogEntry> drawn, IReadOnlyList<LogEntry> next)
    {
        var k = 0;
        while (k < drawn.Count && k < next.Count && Same(drawn[k], next[k])) k++;
        return k;
    }

    public static bool Same(LogEntry a, LogEntry b) => ReferenceEquals(a, b) || a.Seq == b.Seq && a.Undone == b.Undone && (a, b) switch
    {
        (LineEntry x, LineEntry y) => x.Text == y.Text && x.Event.Payload.GetType() == y.Event.Payload.GetType(),
        (RollEntry x, RollEntry y) => x.Record.Id == y.Record.Id && x.Record.Roll == y.Record.Roll && x.Record.Threshold == y.Record.Threshold
            && x.Record.RequiredRaw == y.Record.RequiredRaw && x.Label == y.Label && x.Target == y.Target && x.Sequence == y.Sequence
            && x.Consequence == y.Consequence && Same(x.DieLabels, y.DieLabels) && Same(x.Applied, y.Applied) && Same(x.Details, y.Details)
            && x.Dice.Count == y.Dice.Count && x.Dice.Zip(y.Dice).All(d => d.First.Raw == d.Second.Raw && d.First.Modified == d.Second.Modified
                && d.First.Success == d.Second.Success && d.First.Judged == d.Second.Judged && d.First.RawJudged == d.Second.RawJudged && Same(d.First.Triggered, d.Second.Triggered))
            && x.Amendments.Count == y.Amendments.Count && x.Amendments.Zip(y.Amendments).All(m => m.First.Seq == m.Second.Seq && m.First.Kind == m.Second.Kind
                && m.First.Undone == m.Second.Undone && m.First.Replaced.SequenceEqual(m.Second.Replaced) && Same(m.First.With, m.Second.With)),
        (ActivationBlock x, ActivationBlock y) => x.Header == y.Header && x.Nested == y.Nested && x.Spell == y.Spell && x.SpellCost == y.SpellCost
            && x.Event.Payload.GetType() == y.Event.Payload.GetType() && Same(x.ChainParts, y.ChainParts)
            && x.Entries.Count == y.Entries.Count && x.Entries.Zip(y.Entries).All(p => Same(p.First, p.Second)),
        _ => false,
    };

    static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) => a is null || b is null ? a is null && b is null : a.SequenceEqual(b);
}
