using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a selection shows: the models to light and the line naming them. A plain click selects the unit (no model ids); picked models (ModelPicks) are its model ids.
/// A terrain piece has no line: the terrain card names it.</summary>
public static class SelectionView
{
    public static IReadOnlyList<Guid> Models(Projection p, Selection? selection) => selection is null ? []
        : selection.ModelIds.Count > 0 ? selection.ModelIds.Where(p.Models.ContainsKey).ToList()
        : p.Models.Values.Where(m => m.UnitId == selection.PieceId).Select(m => m.Id).ToList();

    public static string? Describe(Names names, Projection p, Selection? selection)
    {
        if (selection is null) return null;
        if (selection.ModelIds.Count > 0) return string.Join(", ", selection.ModelIds.Select(names.Model));
        if (p.Board.Table.Terrain.Any(t => t.Id == selection.PieceId)) return null;     // the terrain card shows it
        if (!p.Units.ContainsKey(selection.PieceId)) return names.Piece(selection.PieceId);
        var models = p.Models.Values.Where(m => m.UnitId == selection.PieceId).ToList();
        return $"{names.Unit(selection.PieceId)}: {models.Count(m => m.Location is OnTable)} of {models.Count} models on the table";
    }

    /// <summary>Tab (step 1) and Shift+Tab (-1): the next unit after the selected one, with no selection as one more stop after the last
    /// (before the first), where the decision box shows every unit's options again. A selection outside the units starts from that stop.</summary>
    public static Guid? Cycle(IReadOnlyList<Guid> units, Guid? selected, int step)
    {
        if (units.Count == 0) return null;
        var stops = units.Count + 1;                                   // the units, then no selection at index units.Count
        var at = selected is { } s && units.ToList().IndexOf(s) is var i and >= 0 ? i : units.Count;
        var next = ((at + step) % stops + stops) % stops;
        return next == units.Count ? null : units[next];
    }
}
