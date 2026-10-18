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
/// wound total. Equal by value, lists element by element, so the card rebuilds only when what it shows changed and otherwise keeps its
/// scroll and tooltip.
/// </summary>
public sealed record UnitCardContent(UnitSheetModel Sheet, int Team, string? Note)
{
    public IReadOnlyList<ModelGroup> Groups { get; } = Group(Sheet.Models);
    public int WoundsLeft => Groups.Sum(g => g.WoundsLeft);
    public int WoundsMax => Groups.Sum(g => g.WoundsMax);

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
        other is not null && Team == other.Team && Note == other.Note && Same(Sheet, other.Sheet) && Groups.SequenceEqual(other.Groups);

    public override int GetHashCode() => HashCode.Combine(Sheet.UnitId, Sheet.Alive, Team, Note);

    static bool Same(UnitSheetModel a, UnitSheetModel b) =>
        a.UnitId == b.UnitId && a.Team == b.Team && a.Name == b.Name && a.Alive == b.Alive && a.Size == b.Size
        && a.Quality == b.Quality && a.Defense == b.Defense
        && a.Chips.SequenceEqual(b.Chips) && a.Rules.SequenceEqual(b.Rules)
        && a.Weapons.Count == b.Weapons.Count && a.Weapons.Zip(b.Weapons).All(w => Same(w.First, w.Second));

    static bool Same(SheetWeapon a, SheetWeapon b) =>
        a.Name == b.Name && a.Count == b.Count && a.Range == b.Range && a.Attacks == b.Attacks && a.Ap == b.Ap
        && a.ApRule == b.ApRule && a.Rules.SequenceEqual(b.Rules);
}
