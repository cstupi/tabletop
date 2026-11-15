namespace Tabletop.Client.Core;

/// <summary>The unit card's parts, top to bottom: the band and name, the pips and count note, the stat badges and state chips, the
/// limited-choice note, the model groups, the weapons table, the rules.</summary>
public enum UnitCardPart { Title, Count, State, Note, Models, Weapons, Rules }

/// <summary>
/// Which parts of the unit card a new content redraws (UnitCard): every part for the first card or another unit, else only those whose drawn
/// values differ, so a move's activated chip rebuilds the state row and leaves the weapons table and the rules standing. Each new control
/// costs Godot its layout and text shaping in the frame it appears.
/// </summary>
public static class UnitCardParts
{
    public static IReadOnlyList<UnitCardPart> Changed(UnitCardContent? before, UnitCardContent now)
    {
        if (before is null || before.Sheet.UnitId != now.Sheet.UnitId) return Enum.GetValues<UnitCardPart>();
        var (a, b) = (before.Sheet, now.Sheet);
        var changed = new List<UnitCardPart>();
        if (before.Team != now.Team || a.Name != b.Name) changed.Add(UnitCardPart.Title);
        if (before.Team != now.Team || before.Pips != now.Pips || before.CountNote != now.CountNote) changed.Add(UnitCardPart.Count);
        if (a.Quality != b.Quality || a.Defense != b.Defense || !a.Chips.SequenceEqual(b.Chips)) changed.Add(UnitCardPart.State);
        if (before.Note != now.Note) changed.Add(UnitCardPart.Note);
        if (before.Team != now.Team || !before.Groups.SequenceEqual(now.Groups) || !before.Groups.Select(before.Marked).SequenceEqual(now.Groups.Select(now.Marked)))
            changed.Add(UnitCardPart.Models);
        if (a.Weapons.Count != b.Weapons.Count || a.Weapons.Zip(b.Weapons).Any(w => !Same(w.First, w.Second))) changed.Add(UnitCardPart.Weapons);
        if (!a.Rules.SequenceEqual(b.Rules)) changed.Add(UnitCardPart.Rules);
        return changed;
    }

    static bool Same(SheetWeapon a, SheetWeapon b) =>
        a.Name == b.Name && a.Count == b.Count && a.Range == b.Range && a.Attacks == b.Attacks && a.Ap == b.Ap && a.ApRule == b.ApRule && a.Rules.SequenceEqual(b.Rules) && (a.Carriers ?? []).SequenceEqual(b.Carriers ?? []);
}
