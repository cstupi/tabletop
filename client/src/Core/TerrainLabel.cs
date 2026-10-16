using System.Globalization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>Spec → Terrain label: a terrain piece hovered or pinned in play, one line per volume: the piece's name, the volume's declarations
/// (Open with none) and its height range on the table. A built-in is named by its label; an asset piece is "Terrain", since the game's asset
/// manifest carries no names.</summary>
public static class TerrainLabel
{
    static readonly (TerrainDeclarations Flag, string Text)[] Order =
    [
        (TerrainDeclarations.Cover, "Cover"), (TerrainDeclarations.Difficult, "Difficult"), (TerrainDeclarations.Dangerous, "Dangerous"),
        (TerrainDeclarations.Impassable, "Impassable"), (TerrainDeclarations.BlocksLineOfSight, "Blocks LOS"), (TerrainDeclarations.SeeInAndOut, "See in/out"),
    ];

    public static string Of(TerrainPlacement t)
    {
        var name = t.Mesh.Builtin is { } b && BuiltinContent.Terrain.TryGetValue(b, out var builtin) ? builtin.Label : "Terrain";
        static string In(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        return string.Join("\n", t.Volumes.Select(tv =>
        {
            var v = tv.Volume;
            var flags = Order.Where(o => v.Declarations.HasFlag(o.Flag)).Select(o => o.Text).ToList();
            return $"{name} · {(flags.Count == 0 ? "Open" : string.Join(", ", flags))} · {In(t.Position.Z + v.Bottom * t.Scale)}–{In(t.Position.Z + v.Top * t.Scale)}\"";
        }));
    }
}
