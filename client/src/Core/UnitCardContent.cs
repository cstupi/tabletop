namespace Tabletop.Client.Core;

/// <summary>
/// What the unit card draws: the sheet, the team's ordinal for its colour bar and the limited-choice note. Equal by value,
/// lists element by element, so the card rebuilds only when what it shows changed and otherwise keeps its scroll and tooltip.
/// </summary>
public sealed record UnitCardContent(UnitSheetModel Sheet, int Team, string? Note)
{
    public bool Equals(UnitCardContent? other) =>
        other is not null && Team == other.Team && Note == other.Note && Same(Sheet, other.Sheet);

    public override int GetHashCode() => HashCode.Combine(Sheet.UnitId, Sheet.Alive, Team, Note);

    static bool Same(UnitSheetModel a, UnitSheetModel b) =>
        a.UnitId == b.UnitId && a.Team == b.Team && a.Name == b.Name && a.Alive == b.Alive && a.Size == b.Size
        && a.Quality == b.Quality && a.Defense == b.Defense
        && a.Chips.SequenceEqual(b.Chips) && a.Rules.SequenceEqual(b.Rules) && a.Models.SequenceEqual(b.Models)
        && a.Weapons.Count == b.Weapons.Count && a.Weapons.Zip(b.Weapons).All(w => Same(w.First, w.Second));

    static bool Same(SheetWeapon a, SheetWeapon b) =>
        a.Name == b.Name && a.Count == b.Count && a.Range == b.Range && a.Attacks == b.Attacks && a.Ap == b.Ap
        && a.ApRule == b.ApRule && a.Rules.SequenceEqual(b.Rules);
}
