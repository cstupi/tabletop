using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A combat log entry. Undone: struck through, an undone event or a block whose start was undone.</summary>
public abstract record LogEntry(long Seq, GameEvent Event, bool Undone);
public sealed record LineEntry(long Seq, GameEvent Event, bool Undone, string Text) : LogEntry(Seq, Event, Undone);

/// <summary>A die as the log shows it. RawJudged: the roll judged the raw face alone (RequiredRaw). Judged: the roll has a target
/// number or a required face; a roll with neither shows its faces with no pass or fail.</summary>
public sealed record DieView(int Raw, int Modified, bool Success, bool RawJudged, bool Judged = true, IReadOnlyList<string>? Triggered = null)
{
    public string Text => $"{Raw}{(Modified == Raw ? "" : $" → {Modified}")}{(Judged ? $" {(Success ? "pass" : "fail")}" : "")}{(RawJudged ? " (raw face)" : "")}"
        + (Triggered is { Count: > 0 } t ? $" ({string.Join(", ", t)})" : "");
}

/// <summary>A reroll or hand edit of one roll: the faces it replaced and the faces it put in their place.</summary>
public sealed record RollAmendment(long Seq, string Kind, IReadOnlyList<int> Replaced, bool Undone, IReadOnlyList<int>? With = null)
{
    public string Text => Line(f => f.ToString());
    /// <summary>`rerolled ⚀ → ⚄`, one pair per die.</summary>
    public string Line(Func<int, string> face) =>
        $"{Kind} {string.Join(", ", Replaced.Select((old, i) => With is { } w && i < w.Count ? $"{face(old)} → {face(w[i])}" : face(old)))}";
}

/// <summary>
/// One RollRecord of a step_completed as one line (combat-log spec → The log): what was rolled (Label), what it was compared against
/// (Target: the printed value, each change with the rule behind it, `needs N`), the raw faces, and the result in p.8's words. Dice
/// stand after every live amendment. The words come from the record's names and numbers; no rule is named here.
/// </summary>
public sealed record RollEntry(long Seq, GameEvent Event, bool Undone, Guid FrameId, RollRecord Record, SequenceContext Context, string Label,
    string Target, IReadOnlyList<DieView> Dice, IReadOnlyList<RollAmendment> Amendments, IReadOnlyList<string> Applied, IReadOnlyList<string> Details)
    : LogEntry(Seq, Event, Undone)
{
    /// <summary>The record has a target number or a required face (a roll-off or a property's own unjudged roll has neither).</summary>
    public bool Judged => Record.Threshold is not null || Record.RequiredRaw is not null;
    public int Successes => Dice.Count(d => d.Success);
    /// <summary>The label and the target: the line before its dice, and what the dice tray titles the roll with.</summary>
    public string Title => Target.Length == 0 ? Label : $"{Label} · {Target}";

    public string Result => !Judged ? "" : RollWords.Result(Record.Roll, Dice);
    public string Chain => !Judged ? Label : $"{Label} {(Dice.Count == 1 ? Result : Successes.ToString())}";

    /// <summary>The whole line: title, faces, result, and each live amendment (`rerolled ⚀ → ⚄`).</summary>
    public string Collapsed(Func<int, string> face) => string.Join(" · ", new[]
    {
        Title, Faces(face), Result,
    }.Concat(Amendments.Where(a => !a.Undone).Select(a => a.Line(face))).Where(s => s.Length > 0));

    public string Faces(Func<int, string> face) => (Dice.Count > 1 ? $"{Dice.Count} dice: " : "") + string.Join(" ", Dice.Select(d => face(d.Raw)));

    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static RollEntry Of(GameEvent e, bool undone, StepCompleted step, RollRecord r, SequenceContext c, IEnumerable<(GameEvent Event, bool Undone)> amendments, Names n)
    {
        var dice = r.Dice.ToList(); var amended = new List<RollAmendment>();
        foreach (var (a, dead) in amendments)
        {
            var (kind, replaced) = a.Payload is DiceRerolled x ? ("rerolled", x.Replaced) : ("set by hand", ((DiceEdited)a.Payload).Replaced);
            amended.Add(new RollAmendment(a.Seq, kind, replaced.Select(d => d.Old.Raw).ToList(), dead, replaced.Select(d => d.New.Raw).ToList()));
            if (!dead) foreach (var d in replaced.Where(d => d.Index >= 0 && d.Index < dice.Count)) dice[d.Index] = d.New;
        }
        var judged = r.Threshold is not null || r.RequiredRaw is not null;
        var views = dice.Select(d => new DieView(d.Raw, d.Modified, d.Success, r.RequiredRaw is not null, judged, d.Triggered)).ToList();
        var details = new List<string>();
        if (c.WeaponIds.Count > 0) details.Add(string.Join(", ", c.WeaponIds.Select(n.Weapon).Distinct()));
        details.AddRange(RollWords.Details(r));
        return new RollEntry(e.Seq, e, undone, step.FrameId, r with { Dice = dice }, c, RollWords.Label(r.Roll), RollWords.Target(r), views, amended,
            step.Applied ?? [], details);
    }
}

/// <summary>The log's words for a roll, composed from its record. The roll names are the wire's; the words are p.8's (hits, blocked,
/// wounds) and p.5's (`needs N`, never past 6 or below 2).</summary>
public static class RollWords
{
    public static string Label(string roll) => roll switch { "hit" => "Hit", "defense" => "Block", _ => RollEntry.Capital(roll.Replace('-', ' ')) };

    /// <summary>The printed value, the changes in parentheses (an unapplied cover verdict first), `→ needs N` when anything changed it.
    /// A record with no basis (an old log, a property's own roll) starts from its threshold.</summary>
    public static string Target(RollRecord r)
    {
        if (r.RequiredRaw is { } raw) return $"{raw}+ unmodified";
        if (r.Threshold is not { } t) return "";
        var printed = r.Basis is { } b ? $"{b.Stat} {b.Printed}+" : $"{t}+";
        var parts = new List<string>();
        if (r.Cover is { } c && !(c.InCover && c.IgnoredBy is null))
            parts.Add(c.IgnoredBy is { } by ? $"cover ignored ({by})" : $"no cover: {c.Models}/{c.Of} models");
        if (r.Basis is { } basis) parts.AddRange(basis.Changes.Select(x => x.SetTo is { } to ? $"{x.Source} {to}+" : $"{x.Source} {Signed(x.Amount)}"));
        parts.AddRange(r.Modifiers.Select(Modifier));
        var changed = r.Modifiers.Count > 0 || r.Basis is { Changes.Count: > 0 };
        var net = r.Modifiers.Sum(m => m.Amount);
        return printed + (parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})") + (changed ? $" → needs {Math.Clamp(t - net, 2, 6)}" : "");
    }

    public static string Modifier(RollModifier m) => $"{(m.Via is { } via ? via + " " : "")}{m.Source} {Signed(m.Amount)}";

    /// <summary>The expanded lines: the printed value and each change, each modifier, the cover verdict's count.</summary>
    public static IReadOnlyList<string> Details(RollRecord r)
    {
        var lines = new List<string>();
        if (r.Basis is { } b) lines.AddRange([$"{b.Stat} {b.Printed}+", .. b.Changes.Select(c => c.SetTo is { } to ? $"{c.Source}: {to}+" : $"{c.Source}: {Signed(c.Amount)}")]);
        lines.AddRange(r.Modifiers.Select(Modifier));
        if (r.Cover is { } v)
            lines.Add(v.IgnoredBy is { } by ? $"In cover ({v.Models} of {v.Of} models), ignored by {by}"
                : $"{(v.InCover ? "In cover" : "Not in cover")}: {v.Models} of {v.Of} models");
        if (r.Threshold is { } t) lines.Add($"Target {t}+");
        return lines;
    }

    /// <summary>`4 hits (3 sixes: Rending)`, `1 blocked, 2 wounds`, `passed`, `2 of 3 passed`. A hit's total adds the extra hits its dice set off.</summary>
    public static string Result(string roll, IReadOnlyList<DieView> dice)
    {
        var successes = dice.Count(d => d.Success);
        var triggers = Triggers(dice);
        var text = roll switch
        {
            "hit" => Plural(successes + triggers.Where(g => g.Word.StartsWith("hit")).Sum(g => g.Extra), "hit"),
            "defense" => $"{successes} blocked, {Plural(dice.Count - successes, "wound")}",
            _ => dice.Count == 1 ? (successes == 1 ? "passed" : "failed") : $"{successes} of {dice.Count} passed",
        };
        return triggers.Count == 0 ? text : $"{text} ({string.Join("; ", triggers.Select(g => g.Text))})";
    }

    sealed record TriggerGroup(string Text, int Extra, string Word);

    /// <summary>The dice that set something off, by what they set off: `3 sixes: Rending`, `2 sixes: Furious +2 hits`.</summary>
    static List<TriggerGroup> Triggers(IReadOnlyList<DieView> dice)
    {
        var parsed = dice.SelectMany(d => (d.Triggered ?? []).Select(t => (Die: d, Trigger: Parse(t)))).ToList();
        return parsed.GroupBy(x => (x.Trigger.Rule, x.Trigger.Word)).Select(g =>
        {
            var faces = g.Select(x => x.Die.Raw).Distinct().ToList();
            var count = g.Count();
            var who = faces.Count == 1 ? FaceWord(faces[0], count) : Dice(count);
            var extra = g.Sum(x => x.Trigger.Amount);
            var what = g.Key.Word.Length == 0 ? g.Key.Rule : $"{g.Key.Rule} {Signed(extra)} {g.Key.Word}{(extra == 1 ? "" : "s")}";
            return new TriggerGroup($"{who}: {what}", extra, g.Key.Word);
        }).ToList();
    }

    /// <summary>`Rule +N things` or `Rule`.</summary>
    static (string Rule, int Amount, string Word) Parse(string trigger)
    {
        var plus = trigger.LastIndexOf(" +", StringComparison.Ordinal);
        if (plus < 0) return (trigger, 0, "");
        var rest = trigger[(plus + 2)..].Split(' ', 2);
        return int.TryParse(rest[0], out var n) && rest.Length == 2 ? (trigger[..plus], n, rest[1].TrimEnd('s')) : (trigger, 0, "");
    }

    static readonly string[] Faces = ["", "one", "two", "three", "four", "five", "six"];
    static string FaceWord(int face, int count) => face is >= 1 and <= 6 ? $"{count} {Faces[face]}{(count == 1 ? "" : face == 6 ? "es" : "s")}" : Dice(count);
    static string Dice(int n) => n == 1 ? "1 die" : $"{n} dice";

    static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    static string Signed(int n) => n.ToString("+#;-#;0");
}
