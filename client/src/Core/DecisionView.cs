using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What the decision box shows (in-game UX spec → Decision box): the first open Ask addressed to my team, its numbered entries
/// (keys 1–9, then 0 for entries past nine), and the hint line; with nothing addressed to me, no Ask. The waiting line is
/// HeaderView.Waiting's, which the box shows then.
/// </summary>
public sealed record DecisionModel(Ask? Ask, AskMode? Mode, string Title, IReadOnlyList<(int Key, string Label, string OptionId)> Numbered,
    string Hint, bool AutoPass = false);

/// <summary>One line of a choice list: an option's button, or (Option null) the note under a unit's buttons.</summary>
public sealed record ChoiceRow(AskOption? Option, string? Note);

public static class DecisionView
{
    const int Keyed = 9;
    static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher];

    public static DecisionModel Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyCollection<string> muted)
    {
        var mine = AskViewModel.Mine(p, game, myTeam, muted as IReadOnlySet<string> ?? muted.ToHashSet(StringComparer.Ordinal));
        if (mine is null) return new DecisionModel(null, null, "", [], "");
        var ask = mine.Ask;
        var numbered = Numbered(ask, n);
        var title = ask.Legal is Allocation a ? $"Remove {a.Amount}" : ask.Prompt;
        return new DecisionModel(ask, mine.Mode, title, numbered, Hint(p, ask, numbered), mine.AutoPass);
    }

    static IReadOnlyList<(int Key, string Label, string OptionId)> Numbered(Ask ask, Names n)
    {
        IEnumerable<(string Label, string Id)> entries = ask.Legal switch
        {
            Options o => o.Choices.Select(c => (c.Label, c.Id)),
            PieceSelection s when DeployQuestions.Contains(ask.Question) => s.Eligible.Select(id => (n.Unit(id), id.ToString())),
            _ => [],
        };
        return Keys(entries);
    }

    static IReadOnlyList<(int Key, string Label, string OptionId)> Keys(IEnumerable<(string Label, string Id)> entries) =>
        entries.Select((e, i) => (i < Keyed ? i + 1 : 0, e.Label, e.Id)).ToList();

    /// <summary>Choices numbered from 1 in their order: the box narrowed to a clicked unit's options.</summary>
    public static IReadOnlyList<(int Key, string Label, string OptionId)> Number(IEnumerable<AskOption> choices) => Keys(choices.Select(c => (c.Label, c.Id)));

    /// <summary>The pieces a table click answers with: those exactly one choice names. A piece several choices name (activate: one
    /// option per unit and action) cannot answer; a click on it selects the unit and narrows the box (<see cref="ChoicesOf"/>).</summary>
    public static IReadOnlySet<Guid> ClickAnswers(Options options) =>
        options.Choices.SelectMany(c => c.Pieces.Distinct()).GroupBy(id => id).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();

    /// <summary>The choices that name a unit, by its id or one of its models' ids, in their order.</summary>
    public static IReadOnlyList<AskOption> ChoicesOf(Options options, Projection p, Guid unitId) =>
        options.Choices.Where(c => c.Pieces.Any(id => id == unitId || p.Models.GetValueOrDefault(id)?.UnitId == unitId)).ToList();

    /// <summary>The choices as the box lists them: each option, and after a run of options naming the same pieces with the same
    /// AskOption.Note (activate: one unit's actions), that note once. The text is the server's, shown as sent.</summary>
    public static IReadOnlyList<ChoiceRow> Rows(IReadOnlyList<AskOption> shown)
    {
        var rows = new List<ChoiceRow>();
        for (var i = 0; i < shown.Count; i++)
        {
            rows.Add(new ChoiceRow(shown[i], null));
            var last = i + 1 == shown.Count || shown[i + 1].Note != shown[i].Note || !shown[i + 1].Pieces.SequenceEqual(shown[i].Pieces);
            if (last && shown[i].Note is { } note) rows.Add(new ChoiceRow(null, note));
        }
        return rows;
    }

    /// <summary>The note on a unit's choices in my open Options Ask (activate: why its actions are limited), for the unit card; null
    /// when no Ask is open for my team or none of the unit's choices carries one.</summary>
    public static string? NoteOf(Projection p, GameInfo game, Guid? myTeam, IReadOnlyCollection<string> muted, Guid unitId) =>
        AskViewModel.Mine(p, game, myTeam, muted as IReadOnlySet<string> ?? muted.ToHashSet(StringComparer.Ordinal))?.Ask.Legal is Options o
            ? ChoicesOf(o, p, unitId).Select(c => c.Note).FirstOrDefault(n => n is not null)
            : null;

    /// <summary>The stop button of a move that may stop (MoveRegion.Stoppable, p.13 Caster "at any point before attacking"): the window after the
    /// stop offers a cast when a moving model holds spell tokens, else whatever else it offers. Null when the move cannot stop.</summary>
    public static string? StopLabel(Projection p, MoveRegion m) =>
        !m.Stoppable ? null : m.ModelIds.Any(id => p.Models.GetValueOrDefault(id)?.SpellTokens > 0) ? "Cast a spell" : "Stop here to use a rule";

    static string Hint(Projection p, Ask ask, IReadOnlyList<(int Key, string Label, string OptionId)> numbered)
    {
        var keyed = numbered.Count(x => x.Key > 0);
        var keys = keyed == 1 ? "1" : $"1–{keyed}";
        return ask.Legal switch
        {
            Options when keyed == 0 => "",
            Options o => $"{keys}: pick" + (o.Choices.Any(c => c.Pieces.Count > 0) ? " · click a glowing unit" : ""),
            PieceSelection when DeployQuestions.Contains(ask.Question) => $"{keys}: pick a unit to deploy",
            PieceSelection s => $"Click a glowing {(s.Eligible.Count > 0 && p.Models.ContainsKey(s.Eligible[0]) ? "model" : "unit")}"
                                + (s.Max > 1 ? " · Enter: done" : ""),
            MoveRegion m => "Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · " + (StopLabel(p, m) is { } stop ? $"C: {char.ToLowerInvariant(stop[0])}{stop[1..]} · " : "") + "Esc: reset",
            Assignment => "Click a target · Click a chip: that group alone · Enter: fire · Esc: clear",
            Allocation => "Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm",
            TokenSpend => "Set the tokens per caster · Enter: spend",
            Confirmation => $"Enter: {AskViewModel.ConfirmLabel(ask)}",
            _ => "",
        };
    }
}
