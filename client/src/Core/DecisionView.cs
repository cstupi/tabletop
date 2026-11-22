using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What the decision box shows (UX redesign part 2 → Decision box): the first open Ask addressed to my team and its numbered entries
/// (keys 1–9, then 0 for entries past nine); with nothing addressed to me, no Ask, and the box draws DecisionCard.Quiet's bands.
/// The keys are caps on the buttons; there is no hint line.
/// </summary>
public sealed record DecisionModel(Ask? Ask, AskMode? Mode, string Title, IReadOnlyList<(int Key, string Label, string OptionId)> Numbered);

/// <summary>One line of a choice list: an option's button, or (Option null) the note under a unit's buttons.</summary>
public sealed record ChoiceRow(AskOption? Option, string? Note);

public static class DecisionView
{
    const int Keyed = 9;
    static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher, AskQuestions.DeployReinforcement];

    public static DecisionModel Of(Projection p, GameInfo game, Names n, Guid? myTeam)
    {
        var mine = AskViewModel.Mine(p, game, myTeam);
        if (mine is null) return new DecisionModel(null, null, "", []);
        var ask = mine.Ask;
        var numbered = Numbered(ask, n);
        var title = ask.Legal is Allocation a ? $"Remove {a.Amount}" : ask.Prompt;
        return new DecisionModel(ask, mine.Mode, title, numbered);
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

    /// <summary>The pieces a table click answers with: the other team's pieces (a target pick) that exactly one choice names. A piece several
    /// choices name cannot answer; a click on it selects the unit and narrows the box (<see cref="ChoicesOf"/>). A unit of a team the Ask
    /// addresses (my own unit, the acting unit, a Shaken unit's Idle) never answers by a click: the click selects it, and the action is the
    /// bar over the unit, the decision box or a key.</summary>
    public static IReadOnlySet<Guid> ClickAnswers(Options options, Ask ask, Projection p, GameInfo game) =>
        options.Choices.SelectMany(c => c.Pieces.Distinct()).GroupBy(id => id).Where(g => g.Count() == 1).Select(g => g.Key)
            .Where(id => OthersPiece(ask, p, game, id)).ToHashSet();

    /// <summary>A unit or model whose team the Ask does not address; false for anything else.</summary>
    public static bool OthersPiece(Ask ask, Projection p, GameInfo game, Guid id)
    {
        var unit = p.Units.ContainsKey(id) ? id : p.Models.GetValueOrDefault(id)?.UnitId;
        return unit is { } u && SheetFocus.TeamOf(p, game, u) is { } team && !ask.Teams.Contains(team.Id);
    }

    /// <summary>The choices that name a unit, by its id or one of its models' ids, in their order.</summary>
    public static IReadOnlyList<AskOption> ChoicesOf(Options options, Projection p, Guid unitId) =>
        options.Choices.Where(c => c.Pieces.Any(id => id == unitId || p.Models.GetValueOrDefault(id)?.UnitId == unitId)).ToList();

    /// <summary>The one unit an option's pieces name, by its id or its models' ids: the unit whose hover label shows while the pointer is on
    /// the option's button. Null when the pieces name no unit, several, or anything that is not a unit or model.</summary>
    public static Guid? UnitNamed(IReadOnlyList<Guid> pieces, Projection p)
    {
        Guid? unit = null;
        foreach (var id in pieces)
        {
            Guid? of = p.Units.ContainsKey(id) ? id : p.Models.GetValueOrDefault(id)?.UnitId;
            if (of is null || (unit is not null && unit != of)) return null;
            unit = of;
        }
        return unit;
    }

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
    public static string? NoteOf(Projection p, GameInfo game, Guid? myTeam, Guid unitId) =>
        AskViewModel.Mine(p, game, myTeam)?.Ask.Legal is Options o
            ? ChoicesOf(o, p, unitId).Select(c => c.Note).FirstOrDefault(n => n is not null)
            : null;

    /// <summary>The stop button of a move that may stop (MoveRegion.Stoppable, p.13 Caster "at any point before attacking"): the window after the
    /// stop offers a cast when a moving model holds spell tokens, else whatever else it offers. Null when the move cannot stop.</summary>
    public static string? StopLabel(Projection p, MoveRegion m) =>
        !m.Stoppable ? null : m.ModelIds.Any(id => p.Models.GetValueOrDefault(id)?.SpellTokens > 0) ? "Cast here" : "Stop here";
}
