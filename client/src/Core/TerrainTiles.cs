using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>A terrain group (TerrainGroups' six): its icon key, its legend word and its colour, the browser's declaration colours.</summary>
public sealed record TerrainKind(string Key, string Legend, Rgba Colour);

/// <summary>One tile of the setup terrain box: the piece, its group and what it does in words (the tile's tooltip).</summary>
public sealed record TerrainTile(BuiltinTerrain Entry, TerrainKind Kind, string Summary);

/// <summary>
/// Spec 2026-11-07-ux-redesign-1-client-shell.md → Setup: the catalog as tiles by group (TerrainGroups' order and rule: a piece's first
/// declaration of the group list decides), a legend of the six groups whose dots filter, and the name search.
/// </summary>
public static class TerrainTiles
{
    public static readonly TerrainKind BlocksSight = new("blocks-sight", "Blocks sight", Rgba.Hex("#C98CFF"));
    public static readonly TerrainKind Impassable = new("impassable", "Impassable", Rgba.Hex("#FF6B5C"));
    public static readonly TerrainKind Dangerous = new("dangerous", "Dangerous", Rgba.Hex("#FF9D4E"));
    public static readonly TerrainKind Cover = new("cover", "Cover", Rgba.Hex("#6DDF7C"));
    public static readonly TerrainKind Difficult = new("difficult", "Difficult", Rgba.Hex("#FFC44E"));
    public static readonly TerrainKind Open = new("open-ground", "Open", Rgba.Hex("#8B93A1"));

    /// <summary>The legend, in the order a table is balanced.</summary>
    public static IReadOnlyList<TerrainKind> Legend { get; } = [BlocksSight, Impassable, Dangerous, Cover, Difficult, Open];

    static readonly (TerrainKind Kind, TerrainDeclarations Any)[] Rule =
    [
        (BlocksSight, TerrainDeclarations.BlocksLineOfSight),
        (Impassable, TerrainDeclarations.Impassable),
        (Dangerous, TerrainDeclarations.Dangerous | TerrainDeclarations.DangerousOnRushCharge),
        (Cover, TerrainDeclarations.Cover),
        (Difficult, TerrainDeclarations.Difficult | TerrainDeclarations.Steep),
    ];

    /// <summary>The group of a piece with these declarations.</summary>
    public static TerrainKind KindOf(TerrainDeclarations declarations) => Rule.FirstOrDefault(r => (declarations & r.Any) != 0).Kind ?? Open;

    /// <summary>The tiles matching the query, the hidden groups left out, by group then name.</summary>
    public static IReadOnlyList<TerrainTile> Of(IEnumerable<BuiltinTerrain> catalog, string query = "", IReadOnlySet<string>? hidden = null,
        IReadOnlyDictionary<string, string>? given = null) =>
        catalog.Where(e => TerrainGroups.Matches(e, query, given))
            .Select(e => new TerrainTile(e, KindOf(e.Volume.Declarations), TerrainGroups.Summary(e, given)))
            .Where(t => hidden is null || !hidden.Contains(t.Kind.Key))
            .OrderBy(t => Legend.ToList().IndexOf(t.Kind)).ThenBy(t => t.Entry.Label, StringComparer.CurrentCulture).ToList();

    /// <summary>The legend's toggle: a dot hides its group, again shows it.</summary>
    public static IReadOnlySet<string> Toggle(IReadOnlySet<string> hidden, string key)
    {
        var next = new HashSet<string>(hidden);
        if (!next.Remove(key)) next.Add(key);
        return next;
    }
}
