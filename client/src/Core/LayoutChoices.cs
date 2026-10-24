using Tabletop.Content;

namespace Tabletop.Client.Core;

/// <summary>The Load layout list: the predefined tables first, then the account's own; a pick is a whole TableState or a saved id.</summary>
public sealed record LayoutChoice(string Label, TableState? Builtin, Guid? SavedId);

public static class LayoutChoices
{
    public static IReadOnlyList<LayoutChoice> Of(IReadOnlyList<TableState> builtin, IReadOnlyList<LayoutSummary> saved) =>
    [
        .. builtin.Select(t => new LayoutChoice($"{t.Name} (built-in, {t.Size.Width}×{t.Size.Length}, {t.Terrain.Count} terrain)", t, null)),
        .. saved.Select(l => new LayoutChoice($"{l.Name} ({l.Size.Width}×{l.Size.Length}, {l.Terrain} terrain)", null, l.Id)),
    ];

    /// <summary>The draft Random sends: the current draft with the new terrain; size, texture, markers and zones kept.</summary>
    public static TableState WithTerrain(TableState draft, RandomTable table) => draft with { Terrain = table.Terrain };
}
