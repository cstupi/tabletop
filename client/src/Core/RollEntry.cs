using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A combat log entry. Undone: struck through, an undone event or a block whose start was undone.</summary>
public abstract record LogEntry(long Seq, GameEvent Event, bool Undone);
public sealed record LineEntry(long Seq, GameEvent Event, bool Undone, string Text) : LogEntry(Seq, Event, Undone);

/// <summary>A die as the log shows it. RawJudged: the roll judged the raw face alone (RequiredRaw).</summary>
public sealed record DieView(int Raw, int Modified, bool Success, bool RawJudged)
{
    public string Text => $"{Raw}{(Modified == Raw ? "" : $" → {Modified}")} {(Success ? "pass" : "fail")}{(RawJudged ? " (raw face)" : "")}";
}

/// <summary>A reroll or hand edit of one roll, with the faces it replaced.</summary>
public sealed record RollAmendment(long Seq, string Kind, IReadOnlyList<int> Replaced, bool Undone)
{
    public string Text => $"{Kind}: replaced {string.Join(" ", Replaced)}";
}

/// <summary>One RollRecord of a step_completed in its frame's words (client UX spec → Combat log). Dice stand after every live amendment.</summary>
public sealed record RollEntry(long Seq, GameEvent Event, bool Undone, Guid FrameId, RollRecord Record, SequenceContext Context, string Title,
    IReadOnlyList<string> Basis, IReadOnlyList<string> Modifiers, IReadOnlyList<DieView> Dice, IReadOnlyList<RollAmendment> Amendments,
    IReadOnlyList<string> Applied) : LogEntry(Seq, Event, Undone)
{
    public int Successes => Dice.Count(d => d.Success);
    public string Result => Dice.Count == 1 ? (Dice[0].Success ? "passed" : "failed") : $"{Successes} {Record.Roll}{(Successes == 1 ? "" : "s")}";
    public string Chain => $"{Capital(Record.Roll)} {(Dice.Count == 1 ? Result : Successes.ToString())}";
    public string Collapsed(Func<int, string> face) => $"{Title} · {string.Join(" ", Dice.Select(d => face(d.Raw)))} · {Result}";
    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static RollEntry Of(GameEvent e, bool undone, StepCompleted step, RollRecord r, SequenceContext c, IEnumerable<(GameEvent Event, bool Undone)> amendments, Names n)
    {
        var dice = r.Dice.ToList(); var amended = new List<RollAmendment>();
        foreach (var (a, dead) in amendments)
        {
            var (kind, replaced) = a.Payload is DiceRerolled x ? ("Rerolled", x.Replaced) : ("Set by hand", ((DiceEdited)a.Payload).Replaced);
            amended.Add(new RollAmendment(a.Seq, kind, replaced.Select(d => d.Old.Raw).ToList(), dead));
            if (!dead) foreach (var d in replaced.Where(d => d.Index >= 0 && d.Index < dice.Count)) dice[d.Index] = d.New;
        }
        var title = new List<string>();
        if (c.ActorUnitId is { } actor) title.Add(c.TargetUnitIds.Count == 0 ? n.Unit(actor) : $"{n.Unit(actor)} → {string.Join(", ", c.TargetUnitIds.Select(n.Unit))}");
        if (c.WeaponIds.Count > 0) title.Add(string.Join(", ", c.WeaponIds.Select(n.Weapon).Distinct()));
        title.Add($"{Capital(r.Roll)}{(r.Threshold is { } t ? $" {t}+" : "")}");
        return new RollEntry(e.Seq, e, undone, step.FrameId, r with { Dice = dice }, c, string.Join(" · ", title), BasisLines(r),
            r.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}").ToList(), dice.Select(d => new DieView(d.Raw, d.Modified, d.Success, r.RequiredRaw is not null)).ToList(),
            amended, step.Applied ?? []);
    }

    /// <summary>The printed value, each change with its source, the result; the threshold alone when the record has no basis.</summary>
    static IReadOnlyList<string> BasisLines(RollRecord r) => r.Threshold is not { } t ? [] : r.Basis is not { } b ? [$"Target {t}+"]
        : [$"{b.Stat} {b.Printed}+", .. b.Changes.Select(c => c.SetTo is { } to ? $"{c.Source}: {to}+" : $"{c.Source}: {c.Amount:+#;-#;0}"), $"Target {t}+"];
}
