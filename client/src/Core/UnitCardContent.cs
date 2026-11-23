namespace Tabletop.Client.Core;

/// <summary>
/// Like models of a unit (SheetModel.Kind equal), in the order of their first model. Loadout is what sets the group apart from the
/// unit's main group (the most models, the first on a tie): its loadout items the main group lacks, empty for the main group. Wounds
/// are summed over every model of the group; Standing lists the models not removed, which a click on the row cycles through.
/// </summary>
public sealed record ModelGroup(string Label, string Loadout, bool Hero, int Alive, int Size, int WoundsLeft, int WoundsMax, IReadOnlyList<Guid> Standing)
{
    /// <summary>Some model of the group has more than one wound: the row shows the wound total, else the alive count is it.</summary>
    public bool ShowsWounds => WoundsMax > Size;

    /// <summary>`6/8 Orc Warrior — Field Doctor`: the row's text, which clips.</summary>
    public string Title => $"{Alive}/{Size} {Label}" + (Loadout.Length > 0 ? $" — {Loadout}" : "");

    /// <summary>`Wounds 9/12`, beside the title so a long loadout never clips it; null for one-wound models.</summary>
    public string? Wounds => ShowsWounds ? $"Wounds {WoundsLeft}/{WoundsMax}" : null;

    /// <summary>One pip per model of the group, filled alive; past twelve `alive/size`.</summary>
    public string Pips => UnitCardContent.PipsOf(Alive, Size);

    /// <summary>The row's words: the label, then what sets the group apart.</summary>
    public string Name => Label + (Loadout.Length > 0 ? $" · {Loadout}" : "");

    /// <summary>The group's wounds as pips when a model has more than one, else null.</summary>
    public string? WoundPips => ShowsWounds ? UnitCardContent.PipsOf(WoundsLeft, WoundsMax) : null;
    public string WoundCount => $"{WoundsLeft}/{WoundsMax}";

    /// <summary>The whole row: the title and the wounds, the row's tooltip.</summary>
    public string Line => Title + (Wounds is { } w ? $" · {w}" : "");

    /// <summary>The standing model after the selected one, wrapping; the first when the selection is not of this group; null when none stands.</summary>
    public Guid? Next(Guid? selected)
    {
        if (Standing.Count == 0) return null;
        var at = selected is { } s ? Standing.ToList().IndexOf(s) : -1;
        return Standing[(at + 1) % Standing.Count];
    }

    public bool Equals(ModelGroup? other) =>
        other is not null && Label == other.Label && Loadout == other.Loadout && Hero == other.Hero && Alive == other.Alive && Size == other.Size
        && WoundsLeft == other.WoundsLeft && WoundsMax == other.WoundsMax && Standing.SequenceEqual(other.Standing);

    public override int GetHashCode() => HashCode.Combine(Label, Loadout, Alive, WoundsLeft);
}

/// <summary>
/// What the unit card draws: the sheet, the team's ordinal for its colour bar, the limited-choice note, the model groups and the unit's
/// wound total, and the picked models (the selection's model ids), whose groups' rows are marked. InCover: the server's ModelFacts names the
/// unit in cover where it stands, and the Quality / Defense line ends in a shield. Equal by value, lists element by element, so the card
/// rebuilds only when what it shows changed and otherwise keeps its scroll and tooltip.
/// </summary>
public sealed record UnitCardContent(UnitSheetModel Sheet, int Team, string? Note, IReadOnlyList<Guid>? Picked = null, bool InCover = false)
{
    /// <summary>The cover shield's tooltip, its only words; null when the unit is not in cover and no shield shows.</summary>
    public string? CoverTip => InCover ? "In cover" : null;

    public IReadOnlyList<ModelGroup> Groups { get; } = Group(Sheet.Models);
    public int WoundsLeft => Groups.Sum(g => g.WoundsLeft);
    public int WoundsMax => Groups.Sum(g => g.WoundsMax);

    /// <summary>The unit's wounds as the roster and the decision card draw them (DecisionCard.UnitPips): one pip per wound, those left first;
    /// past twelve `left/max`.</summary>
    public string Pips => PipsOf(Sheet.Models.Where(m => !m.Removed).Sum(m => m.Left), Sheet.Models.Sum(m => m.Max));

    /// <summary>`5 models · 2 wounds left on Sergeant · 170 pts`: the count of standing models, then the wounds left when a standing model has
    /// more than one (on that model by name when it is the only tough one among others; else every standing model's, as the pips count them),
    /// then the unit's points from its army list when it gave them.</summary>
    public string CountNote
    {
        get
        {
            var points = Sheet.Points is { } pts ? $" · {pts} pts" : "";
            var count = Sheet.Alive == 1 ? "1 model" : $"{Sheet.Alive} models";
            var tough = Sheet.Models.Where(m => !m.Removed && m.Max > 1).ToList();
            if (tough.Count == 0) return count + points;
            var named = tough.Count == 1 && Sheet.Alive > 1;
            var left = named ? tough[0].Left : Sheet.Models.Where(m => !m.Removed).Sum(m => m.Left);
            var wounds = left == 1 ? "1 wound left" : $"{left} wounds left";
            return (named ? $"{count} · {wounds} on {tough[0].Label}" : $"{count} · {wounds}") + points;
        }
    }

    const int RosterPipsMax = 12;
    public static string PipsOf(int left, int max) => max <= RosterPipsMax ? RosterView.Pips(left, max) : $"{left}/{max}";

    /// <summary>A state chip's words: the activated chip names only its actions (its icon says activated).</summary>
    public static string ChipText(SheetChip c) => c.Kind == ChipKind.Activated && c.Text.StartsWith(ActivatedPrefix, StringComparison.Ordinal) ? c.Text[ActivatedPrefix.Length..] : c.Text;
    const string ActivatedPrefix = "Activated: ";

    /// <summary>The theme icon a state chip leads with: activated, the condition's own; null for the rest.</summary>
    public static string? ChipIcon(SheetChip c) => c.Kind switch { ChipKind.Activated => "activated", ChipKind.Condition => c.Condition, _ => null };

    /// <summary>The group holds a picked model: its row is marked.</summary>
    public bool Marked(ModelGroup group) => Picked is { Count: > 0 } picked && group.Standing.Any(picked.Contains);

    static IReadOnlyList<ModelGroup> Group(IReadOnlyList<SheetModel> models)
    {
        var kinds = models.GroupBy(m => m.Kind, StringComparer.Ordinal).Select(g => g.ToList()).ToList();
        if (kinds.Count == 0) return [];
        var main = kinds.OrderByDescending(k => k.Count).First();   // stable: the first on a tie
        var common = main[0].Loadout.ToHashSet(StringComparer.Ordinal);
        return kinds.Select(k => new ModelGroup(k[0].Label,
            k == main ? "" : string.Join(", ", k[0].Loadout.Where(i => !common.Contains(i))), k[0].Hero,
            k.Count(m => !m.Removed), k.Count, k.Sum(m => m.Left), k.Sum(m => m.Max),
            k.Where(m => !m.Removed).Select(m => m.Id).ToList())).ToList();
    }

    public bool Equals(UnitCardContent? other) =>
        other is not null && Team == other.Team && Note == other.Note && InCover == other.InCover && Same(Sheet, other.Sheet) && Groups.SequenceEqual(other.Groups)
        && (Picked ?? []).SequenceEqual(other.Picked ?? []);

    public override int GetHashCode() => HashCode.Combine(Sheet.UnitId, Sheet.Alive, Team, Note);

    static bool Same(UnitSheetModel a, UnitSheetModel b) =>
        a.UnitId == b.UnitId && a.Team == b.Team && a.Name == b.Name && a.Alive == b.Alive && a.Size == b.Size
        && a.Quality == b.Quality && a.Defense == b.Defense && a.Points == b.Points
        && a.Chips.SequenceEqual(b.Chips) && a.Rules.SequenceEqual(b.Rules)
        && a.Weapons.Count == b.Weapons.Count && a.Weapons.Zip(b.Weapons).All(w => Same(w.First, w.Second));

    static bool Same(SheetWeapon a, SheetWeapon b) =>
        a.Name == b.Name && a.Count == b.Count && a.Range == b.Range && a.Attacks == b.Attacks && a.Ap == b.Ap
        && a.ApRule == b.ApRule && a.Rules.SequenceEqual(b.Rules) && (a.Carriers ?? []).SequenceEqual(b.Carriers ?? []);
}
