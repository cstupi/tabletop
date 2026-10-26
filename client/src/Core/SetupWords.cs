using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>The setup toolbar's names as words (the browser's terrainGroups.ts): a table texture "grass" → "Grass"; a deployment style by
/// its rulebook name (p.22, p.50), "ground-war" → "Ground War".</summary>
public static class SetupWords
{
    static readonly Dictionary<string, string> Styles = new(StringComparer.Ordinal)
    {
        [ZoneStyles.Frontline] = "Frontline", [ZoneStyles.GroundWar] = "Ground War", [ZoneStyles.SideBattle] = "Side Battle", [ZoneStyles.Disordered] = "Disordered",
        [ZoneStyles.Spearhead] = "Spearhead", [ZoneStyles.OpposingForces] = "Opposing Forces", [ZoneStyles.FreeForAll] = "Free-for-all",
    };

    /// <summary>A catalog name as words: "large-forest" → "Large forest".</summary>
    public static string Name(string name)
    {
        var spaced = name.Replace('-', ' ').Replace('_', ' ');
        return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    public static string Texture(string name) => Name(name);
    public static string ZoneStyle(string style) => Styles.GetValueOrDefault(style) ?? Name(style);
}
