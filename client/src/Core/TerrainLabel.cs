using System.Globalization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>Spec → Terrain label: a terrain piece hovered in play, one line per volume: the piece's name, the volume's declarations
/// (Open with none) and its height range on the table. A built-in is named by its label; an asset piece is "Terrain", since the game's asset
/// manifest carries no names. The selected piece's details are the terrain card's (TerrainCardContent).</summary>
public static class TerrainLabel
{
    static readonly (TerrainDeclarations Flag, string Text)[] Order =
    [
        (TerrainDeclarations.Cover, "Cover"), (TerrainDeclarations.Difficult, "Difficult"), (TerrainDeclarations.Dangerous, "Dangerous"),
        (TerrainDeclarations.DangerousOnRushCharge, "Dangerous on Rush/Charge"),
        (TerrainDeclarations.Impassable, "Impassable"), (TerrainDeclarations.BlocksLineOfSight, "Blocks LOS"), (TerrainDeclarations.SeeInAndOut, "See in/out"),
    ];

    /// <summary>The seven declarations in the label's order, each with its words.</summary>
    public static IReadOnlyList<(TerrainDeclarations Flag, string Text)> Declarations => Order;

    public static string Of(TerrainPlacement t)
    {
        var name = NameOf(t);
        return string.Join("\n", t.Volumes.Select(tv =>
        {
            var flags = FlagsOf(tv.Volume);
            return $"{name} · {(flags.Count == 0 ? "Open" : string.Join(", ", flags))} · {HeightsOf(t, tv.Volume)}";
        }));
    }

    /// <summary>The built-in's label, else "Terrain".</summary>
    public static string NameOf(TerrainPlacement t) =>
        t.Mesh.Builtin is { } b && BuiltinContent.Terrain.TryGetValue(b, out var builtin) ? builtin.Label : "Terrain";

    /// <summary>The volume's declarations in the label's order; empty for an open volume.</summary>
    public static IReadOnlyList<string> FlagsOf(Volume v) => Order.Where(o => v.Declarations.HasFlag(o.Flag)).Select(o => o.Text).ToList();

    /// <summary>The volume's height range on the table, scale and elevation applied: <c>0–4"</c>.</summary>
    public static string HeightsOf(TerrainPlacement t, Volume v) => $"{Inches(t.Position.Z + v.Bottom * t.Scale)}–{Inches(t.Position.Z + v.Top * t.Scale)}\"";

    public static string Inches(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
