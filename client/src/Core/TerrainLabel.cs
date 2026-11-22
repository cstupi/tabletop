using System.Globalization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>Spec → Terrain label: a terrain piece hovered in play, one line per volume: the piece's name, the volume's declarations
/// (Open with none) and its height range on the table. A built-in is named by its label; an asset piece is "Terrain", since the game's asset
/// manifest carries no names. The selected piece's details are the terrain card's (TerrainCardContent). A declaration's words are the server's
/// (Session.TerrainWords, from Welcome), else the label's own.</summary>
public static class TerrainLabel
{
    static readonly (TerrainDeclarations Flag, string Text)[] Order = TerrainEffects.All.Select(e => (e.Flag, e.Word)).ToArray();

    static readonly Dictionary<TerrainDeclarations, string> NameOfFlag = BuiltinTerrain.DeclarationNames.ToDictionary(n => n.Value, n => n.Key);

    /// <summary>The flag's word: the server's for its name when given, else `own`.</summary>
    public static string WordOf(TerrainDeclarations flag, string own, IReadOnlyDictionary<string, string>? words) =>
        words is not null && NameOfFlag.TryGetValue(flag, out var name) && words.TryGetValue(name, out var word) ? word : own;

    /// <summary>The eight declarations in the label's order, each with the label's own words.</summary>
    public static IReadOnlyList<(TerrainDeclarations Flag, string Text)> Declarations => Order;

    /// <summary>The eight declarations in the label's order, each with the server's words where given.</summary>
    public static IReadOnlyList<(TerrainDeclarations Flag, string Text)> DeclarationsIn(IReadOnlyDictionary<string, string>? words) =>
        Order.Select(o => (o.Flag, WordOf(o.Flag, o.Text, words))).ToList();

    public static string Of(TerrainPlacement t, IReadOnlyDictionary<string, string>? words = null)
    {
        var name = NameOf(t);
        return string.Join("\n", t.Volumes.Select(tv =>
        {
            var flags = FlagsOf(tv.Volume, words);
            return $"{name} · {(flags.Count == 0 ? "Open" : string.Join(", ", flags))} · {HeightsOf(t, tv.Volume)}";
        }));
    }

    /// <summary>Whether the terrain hover panel shows (owner, 2026-11-22: a Forest panel covered the ghosts): never while models are held, and while
    /// a move's or a deployment's ghosts are down only with the modifier (Alt) held.</summary>
    public static bool HoverShown(bool held, bool placing, bool modifier) => !held && (!placing || modifier);

    /// <summary>The hover panel (owner, 2026-11-07: "clearer terrain tooltips"): the piece's name, its height range on the table and one chip
    /// per effect any of its volumes carries, in the label's order; Open alone when none carries one.</summary>
    public static TerrainHover HoverOf(TerrainPlacement t, IReadOnlyDictionary<string, string>? words = null)
    {
        var all = t.Volumes.Aggregate(TerrainDeclarations.None, (d, tv) => d | tv.Volume.Declarations);
        return new TerrainHover(NameOf(t), RangeOf(t), TerrainEffects.Rows(all, words, null));
    }

    /// <summary>The piece's height range on the table, lowest bottom to highest top, scale and elevation applied; empty with no volume.</summary>
    public static string RangeOf(TerrainPlacement t) => t.Volumes.Count == 0 ? ""
        : $"{Inches(t.Position.Z + t.Volumes.Min(v => v.Volume.Bottom) * t.Scale)}–{Inches(t.Position.Z + t.Volumes.Max(v => v.Volume.Top) * t.Scale)}\"";

    /// <summary>The built-in's label, else "Terrain".</summary>
    public static string NameOf(TerrainPlacement t) =>
        t.Mesh.Builtin is { } b && BuiltinContent.Terrain.TryGetValue(b, out var builtin) ? builtin.Label : "Terrain";

    /// <summary>The volume's declarations in the label's order, in the server's words where given; empty for an open volume.</summary>
    public static IReadOnlyList<string> FlagsOf(Volume v, IReadOnlyDictionary<string, string>? words = null) =>
        Order.Where(o => v.Declarations.HasFlag(o.Flag)).Select(o => WordOf(o.Flag, o.Text, words)).ToList();

    /// <summary>The volume's height range on the table, scale and elevation applied: <c>0–4"</c>.</summary>
    public static string HeightsOf(TerrainPlacement t, Volume v) => $"{Inches(t.Position.Z + v.Bottom * t.Scale)}–{Inches(t.Position.Z + v.Top * t.Scale)}\"";

    public static string Inches(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>What the hover panel over a terrain piece draws (TerrainLabel.HoverOf).</summary>
public sealed record TerrainHover(string Name, string Height, IReadOnlyList<TerrainEffectRow> Effects)
{
    public bool Equals(TerrainHover? o) => o is not null && Name == o.Name && Height == o.Height && Effects.SequenceEqual(o.Effects);
    public override int GetHashCode() => HashCode.Combine(Name, Height, Effects.Count);
}
