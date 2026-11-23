using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A combat log entry. Undone: struck through, an undone event or a block whose start was undone.</summary>
public abstract record LogEntry(long Seq, GameEvent Event, bool Undone);
/// <summary>Worked: what the detailed log draws in place of Text (each model a change named, a step's removed rules); null draws Text.</summary>
public sealed record LineEntry(long Seq, GameEvent Event, bool Undone, string Text, IReadOnlyList<string>? Worked = null) : LogEntry(Seq, Event, Undone);

/// <summary>A die as the log shows it. RawJudged: the roll judged the raw face alone (RequiredRaw). Judged: the roll has a target
/// number or a required face; a roll with neither shows its faces with no pass or fail.</summary>
public sealed record DieView(int Raw, int Modified, bool Success, bool RawJudged, bool Judged = true, IReadOnlyList<string>? Triggered = null)
{
    public string Text => $"{Raw}{(Modified == Raw ? "" : $" → {Modified}")}{(Judged ? $" {(Success ? "pass" : "fail")}" : "")}{(RawJudged ? " (raw face)" : "")}"
        + (Triggered is { Count: > 0 } t ? $" ({string.Join(", ", t)})" : "");
}

/// <summary>A reroll or hand edit of one roll: the faces it replaced and the faces it put in their place. Indexes: each replaced die's place
/// in the roll; By: the rule that forced each reroll (DieReplacement.By), null on an edit and in logs written before it.</summary>
public sealed record RollAmendment(long Seq, string Kind, IReadOnlyList<int> Replaced, bool Undone, IReadOnlyList<int>? With = null,
    IReadOnlyList<int>? Indexes = null, IReadOnlyList<string?>? By = null)
{
    public string Text => Line(f => f.ToString());
    /// <summary>`rerolled ⚀ → ⚄`, one pair per die.</summary>
    public string Line(Func<int, string> face) =>
        $"{Kind} {string.Join(", ", Replaced.Select((old, i) => With is { } w && i < w.Count ? $"{face(old)} → {face(w[i])}" : face(old)))}";
}

/// <summary>
/// One RollRecord of a step_completed as one line (combat-log spec → The log): what was rolled (Label), what it was compared against
/// (Target: the printed value, each change with the rule behind it, `needs N`), the raw faces, and the result in p.8's words. Dice
/// stand after every live amendment. Sequence: the sequence its frame runs. The words come from the record's names and numbers;
/// no rule is named here.
/// </summary>
public sealed record RollEntry(long Seq, GameEvent Event, bool Undone, Guid FrameId, RollRecord Record, SequenceContext Context, string Label,
    string Target, IReadOnlyList<DieView> Dice, IReadOnlyList<RollAmendment> Amendments, IReadOnlyList<string> Applied, IReadOnlyList<string> Details,
    string? Sequence = null)
    : LogEntry(Seq, Event, Undone)
{
    /// <summary>The record has a target number or a required face (a roll-off or a property's own unjudged roll has neither).</summary>
    public bool Judged => Record.Threshold is not null || Record.RequiredRaw is not null;
    public int Successes => Dice.Count(d => d.Success);
    /// <summary>The label and the target: the line before its dice, and what the dice tray titles the roll with.</summary>
    public string Title => Target.Length == 0 ? Label : $"{Label} · {Target}";

    /// <summary>What the roll led to, set by CombatLog: a single-die test's changes (`failed → Shaken`); a roll-off's winner, read
    /// after its faces (`Team 1 wins`).</summary>
    public string? Consequence { get; init; }

    /// <summary>Set by CombatLog on a roll-off: one word per die (`Team 1`) read before its face, the teams the record's dice belong
    /// to; empty when the dice and those teams differ in number, and the faces read with their count.</summary>
    public IReadOnlyList<string>? DieLabels { get; init; }

    /// <summary>Set by CombatLog on the first hit roll of a step: the attack's lines (detailed-log spec → What it shows, Attack).</summary>
    public IReadOnlyList<string>? Attack { get; init; }

    /// <summary>Set by CombatLog on the first roll of a step: the rules a suppression removed in the step that no record of it names.</summary>
    public IReadOnlyList<IgnoredEffect>? StepIgnored { get; init; }

    /// <summary>Set by CombatLog: the attack the roll belongs to (LogSheet draws an attack's rolls together).</summary>
    public AttackTag? InAttack { get; init; }

    /// <summary>Set by CombatLog on a hit roll: what its step multiplied the hits by (`hit_multiplier`, Blast); 1 otherwise.</summary>
    public int HitMultiplier { get; init; } = 1;

    /// <summary>Set by CombatLog: the unit the roll's frame acts for (the unit a morale test tests), else null.</summary>
    public string? Unit { get; init; }

    /// <summary>One judged die read passed or failed (not a hit, a block or a roll per wound): what a Consequence follows.</summary>
    public static bool IsTest(RollRecord r, string? sequence) =>
        (r.Threshold is not null || r.RequiredRaw is not null) && r.Dice.Count == 1 && r.Roll is not ("hit" or "defense") && !RollWords.PerWound(r.Roll, sequence);

    public string Result => !Judged ? "" : RollWords.Result(Record.Roll, Dice, Sequence) + (Consequence is { } c && Dice.Count == 1 ? $" → {c}" : "");
    public string Chain => !Judged ? Label : $"{Label} {(Dice.Count == 1 ? Result : Successes.ToString())}";

    /// <summary>The value a die is drawn at, so the modifiers show once: its total on a roll its modifiers add to (RollWords.AddsToRoll, whose
    /// title keeps the printed target), else its face as rolled, the title's `→ needs N` already holding them (p.5: the same verdict).</summary>
    public int Face(DieView d) => RollWords.AddsToRoll(Record) ? d.Modified : d.Raw;

    /// <summary>A roll its modifiers add to (RollWords.AddsToRoll) of one die: `rolled 1 +1 token = 2`, `rolled 5` with none; else null.</summary>
    public string? Sum => RollWords.AddsToRoll(Record) && Dice.Count == 1
        ? $"rolled {Dice[0].Raw}{string.Concat(Record.Modifiers.Select(m => " " + RollWords.RollPart(m)))}{(Record.Modifiers.Count > 0 ? $" = {Dice[0].Modified}" : "")}"
        : null;

    /// <summary>What follows the faces: the result, or an unjudged roll's consequence (a roll-off's winner).</summary>
    public string Outcome => Judged ? Result : Consequence ?? "";

    /// <summary>The whole line: title, faces, outcome, and each live amendment (`rerolled ⚀ → ⚄`).</summary>
    public string Collapsed(Func<int, string> face) => string.Join(" · ", new[]
    {
        Title, Faces(face), Outcome,
    }.Concat(Amendments.Where(a => !a.Undone).Select(a => a.Line(face))).Where(s => s.Length > 0));

    /// <summary>`3 dice: ⚅ ⚁ ⚀` (`3 wounds:` for a roll per wound), one die bare; a labelled roll-off `Team 1 ⚃ · Team 2 ⚀`.</summary>
    public string Faces(Func<int, string> face) => DieLabels is { Count: > 0 } labels && labels.Count == Dice.Count
        ? string.Join(" · ", Dice.Select((d, i) => $"{labels[i]} {face(d.Raw)}"))
        : (Dice.Count > 1 ? $"{RollWords.Plural(Dice.Count, RollWords.PerWound(Record.Roll, Sequence) ? "wound" : "die")}: " : "")
            + string.Join(" ", Dice.Select(d => face(d.Raw)));

    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static RollEntry Of(GameEvent e, bool undone, StepCompleted step, RollRecord r, SequenceContext c, string? sequence,
        IEnumerable<(GameEvent Event, bool Undone)> amendments, Names n)
    {
        var dice = r.Dice.ToList(); var amended = new List<RollAmendment>();
        foreach (var (a, dead) in amendments)
        {
            var (kind, replaced) = a.Payload is DiceRerolled x ? ("rerolled", x.Replaced) : ("set by hand", ((DiceEdited)a.Payload).Replaced);
            amended.Add(new RollAmendment(a.Seq, kind, replaced.Select(d => d.Old.Raw).ToList(), dead, replaced.Select(d => d.New.Raw).ToList(),
                replaced.Select(d => d.Index).ToList(), replaced.Select(d => d.By).ToList()));
            if (!dead) foreach (var d in replaced.Where(d => d.Index >= 0 && d.Index < dice.Count)) dice[d.Index] = d.New;
        }
        var judged = r.Threshold is not null || r.RequiredRaw is not null;
        var views = dice.Select(d => new DieView(d.Raw, d.Modified, d.Success, r.RequiredRaw is not null, judged, d.Triggered)).ToList();
        var details = new List<string>();
        if (c.WeaponIds.Count > 0) details.Add(string.Join(", ", c.WeaponIds.Select(n.Weapon).Distinct()));
        details.AddRange(RollWords.Details(r));
        return new RollEntry(e.Seq, e, undone, step.FrameId, r with { Dice = dice }, c, RollWords.Label(r.Roll), RollWords.Target(r), views, amended,
            step.Applied ?? [], details, sequence);
    }
}

/// <summary>The log's words for a roll, composed from its record. The roll names are the wire's; the words are p.8's (hits, blocked,
/// wounds) and p.5's (`needs N`, never past 6 or below 2).</summary>
public static class RollWords
{
    public static string Label(string roll) => roll switch
    {
        "hit" => "Hit", "defense" => "Block", "roll-off" => "Roll-off", _ => RollEntry.Capital(roll.Replace('-', ' ')),
    };

    /// <summary>A roll made once per wound being dealt (p.14 Regeneration: "roll one die for each"): one in a deal-wounds frame.</summary>
    public static bool PerWound(string roll, string? sequence) => sequence == "deal-wounds" && roll is not ("hit" or "defense");

    /// <summary>A roll whose modifiers change the die and leave the target as printed: casting (3.6.0 p.13, p.13: "roll one die, and on 4+
    /// resolve the spell's effect", tokens "give the Caster +1/-1 to the roll per token").</summary>
    public static bool AddsToRoll(RollRecord r) => r.Roll == "casting" && r.Threshold is not null && r.RequiredRaw is null;

    /// <summary>A modifier as an addition to the die: `+1 token`, `-2 tokens`, `+1 Spell Conduit`.</summary>
    public static string RollPart(RollModifier m) => m.Source == "tokens"
        ? $"{Signed(m.Amount)} token{(Math.Abs(m.Amount) == 1 ? "" : "s")}"
        : $"{Signed(m.Amount)} {(m.Via is { } via ? via + " " : "")}{m.Source}";

    /// <summary>The printed value, the changes in parentheses (cover a rule took away first), `→ needs N` when anything changed it.
    /// A record with no basis (an old log, a property's own roll) starts from its threshold. A roll judged on the raw face alone reads
    /// `unmodified N+`, nothing when every face passes; dangerous terrain (threshold 2, p.11) reads the face that fails.</summary>
    public static string Target(RollRecord r)
    {
        if (r.RequiredRaw is { } raw) return raw <= 1 ? "" : $"unmodified {raw}+";
        if (r.Threshold is not { } t) return "";
        if (r is { Roll: "dangerous-terrain", Threshold: 2, Basis: null, Modifiers.Count: 0 }) return "1 fails";
        if (AddsToRoll(r)) return $"needs {t}+" + (r.Modifiers.Count == 0 ? "" : $" ({string.Join(", ", r.Modifiers.Select(RollPart))})");
        var printed = r.Basis is { } b ? $"{b.Stat} {b.Printed}+" : $"{t}+";
        var parts = new List<string>();
        if (r.Cover is { IgnoredBy: { } by }) parts.Add($"cover ignored ({by})");
        if (r.Basis is { } basis) parts.AddRange(basis.Changes.Select(x => x.SetTo is { } to ? $"{x.Source} {to}+" : $"{x.Source} {Signed(x.Amount)}"));
        parts.AddRange(r.Modifiers.Select(Modifier));
        var changed = r.Modifiers.Count > 0 || r.Basis is { Changes.Count: > 0 };
        var net = r.Modifiers.Sum(m => m.Amount);
        return printed + (parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})") + (changed ? $" → needs {Math.Clamp(t - net, 2, 6)}" : "");
    }

    /// <summary>`cover +1`, a spell's `-1`; AP as p.13 writes it, `AP(1)` for the weapon's own and `Rending AP(+4)` for a granted one (p.14).</summary>
    public static string Modifier(RollModifier m) => m.Source == "AP"
        ? m.Via is { } granted ? $"{granted} AP({Signed(-m.Amount)})" : $"AP({-m.Amount})"
        : $"{(m.Via is { } via ? via + " " : "")}{m.Source} {Signed(m.Amount)}";

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

    /// <summary>`4 hits (3 sixes: Rending)`, `1 blocked, 2 wounds`, `1 ignored, 2 wounds stand`, `passed`, `2 of 3 passed`.
    /// A hit's total adds the extra hits its dice set off, a block's wounds the extra wounds.</summary>
    public static string Result(string roll, IReadOnlyList<DieView> dice, string? sequence = null)
    {
        var successes = dice.Count(d => d.Success); var failures = dice.Count - successes;
        var triggers = Triggers(dice);
        int Extra(string word) => triggers.Where(g => g.Word == word).Sum(g => g.Extra);
        var text = roll switch
        {
            "hit" => Plural(successes + Extra("hit"), "hit"),
            "defense" => $"{successes} blocked, {Plural(failures + Extra("wound"), "wound")}",
            _ when PerWound(roll, sequence) => $"{successes} ignored, {Plural(failures, "wound")} {(failures == 1 ? "stands" : "stand")}",
            _ when dice.Count == 1 => successes == 1 ? "passed" : "failed",
            "dangerous-terrain" => Plural(failures, "wound"),
            _ => $"{successes} of {dice.Count} passed",
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
            var who = faces.Count == 1 ? FaceWord(faces[0], count) : Plural(count, "die");
            var extra = g.Sum(x => x.Trigger.Amount);
            var what = g.Key.Word.Length == 0 ? g.Key.Rule : $"{g.Key.Rule} {Signed(extra)} {g.Key.Word}{(extra == 1 ? "" : "s")}";
            return new TriggerGroup($"{who}: {what}", extra, g.Key.Word);
        }).ToList();
    }

    /// <summary>`Rule +N things` or `Rule`; the thing singular.</summary>
    public static (string Rule, int Amount, string Word) Parse(string trigger)
    {
        var plus = trigger.LastIndexOf(" +", StringComparison.Ordinal);
        if (plus < 0) return (trigger, 0, "");
        var rest = trigger[(plus + 2)..].Split(' ', 2);
        return int.TryParse(rest[0], out var n) && rest.Length == 2 ? (trigger[..plus], n, rest[1].TrimEnd('s')) : (trigger, 0, "");
    }

    static readonly string[] Faces = ["", "one", "two", "three", "four", "five", "six"];
    static string FaceWord(int face, int count) => face is >= 1 and <= 6 ? $"{count} {Faces[face]}{(count == 1 ? "" : face == 6 ? "es" : "s")}" : Plural(count, "die");

    /// <summary>`1 hit`, `2 hits`, `1 die`, `3 dice`.</summary>
    public static string Plural(int n, string word) => word == "die" ? (n == 1 ? "1 die" : $"{n} dice") : $"{n} {word}{(n == 1 ? "" : "s")}";
    public static string Signed(int n) => n.ToString("+#;-#;0");
}
