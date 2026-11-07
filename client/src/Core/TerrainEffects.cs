using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>One terrain declaration as the clients draw it: its snake_case name (BuiltinTerrain.DeclarationNames), the theme icon, the
/// colour of its chip, its legend swatch and its tint on the table, and the client's own word for it (the server's replaces it).</summary>
public sealed record TerrainEffect(TerrainDeclarations Flag, string Name, string Icon, Rgba Colour, string Word);

/// <summary>An effect as a piece shows it: icon, colour, the word (the server's where given) and the one sentence of what it does to a unit
/// (Session.TerrainRules), null when the server gave none.</summary>
public sealed record TerrainEffectRow(TerrainDeclarations Flag, string Icon, Rgba Colour, string Word, string? Rule);

/// <summary>
/// The one mapping from a terrain declaration to its icon and colour: the hover panel's chips, the terrain card's rows, the setup legend
/// and the table's tint all read it (the browser's <c>terrainEffects.ts</c>). <see cref="All"/> is in the label's order.
/// </summary>
public static class TerrainEffects
{
    public static IReadOnlyList<TerrainEffect> All { get; } =
    [
        new(TerrainDeclarations.Cover, "cover", "cover", Rgba.Hex("#4FB86A"), "Cover"),
        new(TerrainDeclarations.Difficult, "difficult", "difficult", Rgba.Hex("#E0B43C"), "Difficult"),
        new(TerrainDeclarations.Dangerous, "dangerous", "dangerous", Rgba.Hex("#F0722E"), "Dangerous"),
        new(TerrainDeclarations.DangerousOnRushCharge, "dangerous_on_rush_charge", "unstable", Rgba.Hex("#D9965A"), "Dangerous on Rush/Charge"),
        new(TerrainDeclarations.Steep, "steep", "steep", Rgba.Hex("#A8865C"), "Steep"),
        new(TerrainDeclarations.Impassable, "impassable", "impassable", Rgba.Hex("#D2455A"), "Impassable"),
        new(TerrainDeclarations.BlocksLineOfSight, "blocks_line_of_sight", "blocks-sight", Rgba.Hex("#9A6CE0"), "Blocks LOS"),
        new(TerrainDeclarations.SeeInAndOut, "see_in_and_out", "see-in-out", Rgba.Hex("#4E9CE6"), "See in/out"),
    ];

    /// <summary>A volume with no declaration.</summary>
    public static TerrainEffect Open { get; } = new(TerrainDeclarations.None, "open", "open-ground", Rgba.Hex("#8B93A1"), "Open");

    /// <summary>Which effect tints a volume of several: what stops or harms a unit first, then sight, then cover and movement.</summary>
    static readonly TerrainDeclarations[] Strength =
    [
        TerrainDeclarations.Impassable, TerrainDeclarations.Dangerous, TerrainDeclarations.DangerousOnRushCharge, TerrainDeclarations.BlocksLineOfSight,
        TerrainDeclarations.Cover, TerrainDeclarations.Difficult, TerrainDeclarations.Steep, TerrainDeclarations.SeeInAndOut,
    ];

    public static TerrainEffect Of(TerrainDeclarations flag) => All.FirstOrDefault(e => e.Flag == flag) ?? Open;

    public static TerrainEffect Primary(TerrainDeclarations d) => Strength.FirstOrDefault(f => d.HasFlag(f)) is var f and not TerrainDeclarations.None ? Of(f) : Open;

    /// <summary>The declarations' effects in the label's order, in the server's words and with its sentences where given; Open alone with none.</summary>
    public static IReadOnlyList<TerrainEffectRow> Rows(TerrainDeclarations d, IReadOnlyDictionary<string, string>? words, IReadOnlyDictionary<string, string>? rules)
    {
        var rows = All.Where(e => d.HasFlag(e.Flag))
            .Select(e => new TerrainEffectRow(e.Flag, e.Icon, e.Colour, TerrainLabel.WordOf(e.Flag, e.Word, words), rules?.GetValueOrDefault(e.Name))).ToList();
        return rows.Count > 0 ? rows : [new TerrainEffectRow(TerrainDeclarations.None, Open.Icon, Open.Colour, Open.Word, null)];
    }
}
