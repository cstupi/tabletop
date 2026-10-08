using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a selection shows: the models to light and the line naming them. A plain click selects the unit (no model ids); shift selects one model.</summary>
public static class SelectionView
{
    public static IReadOnlyList<Guid> Models(Projection p, Selection? selection) => selection is null ? []
        : selection.ModelIds.Count > 0 ? selection.ModelIds.Where(p.Models.ContainsKey).ToList()
        : p.Models.Values.Where(m => m.UnitId == selection.PieceId).Select(m => m.Id).ToList();

    public static string? Describe(Names names, Projection p, Selection? selection)
    {
        if (selection is null) return null;
        if (selection.ModelIds.Count > 0) return string.Join(", ", selection.ModelIds.Select(names.Model));
        if (!p.Units.ContainsKey(selection.PieceId)) return names.Piece(selection.PieceId);
        var models = p.Models.Values.Where(m => m.UnitId == selection.PieceId).ToList();
        return $"{names.Unit(selection.PieceId)}: {models.Count(m => m.Location is OnTable)} of {models.Count} models on the table";
    }
}
