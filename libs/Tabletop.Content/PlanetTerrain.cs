namespace Tabletop.Content;

public enum Planet { Generic, Hospitable, Wasteland, Death, Ruins }

/// <summary>Grimdark Future p.18–19's planets plus the owner's Ruins pack: nine fixed pieces, then 4D3 more rolled on 2D6.</summary>
/// <remarks>Generic, Hospitable, Wasteland and Death are the rulebook's. Ruins is the owner's pack in the same shape: a ruined city of ruins,
/// ruined walls and rubble with craters and barricades, no whole buildings, its fixed nine keeping p.12's balance.</remarks>
public static class PlanetTerrain
{
    static readonly Dictionary<Planet, string[]> FixedPieces = new()
    {
        [Planet.Generic] = ["barricade", "barricade", "large-forest", "large-field", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker"],
        [Planet.Hospitable] = ["wall-or-fence", "wall-or-fence", "hill", "plantation", "forest", "forest", "forest", "large-building", "large-building"],
        [Planet.Wasteland] = ["small-dunes", "small-dunes", "large-rock", "crater", "large-building", "large-building", "large-building", "toxic-smog", "toxic-smog"],
        [Planet.Death] = ["overgrown-ruins", "overgrown-ruins", "deadly-vegetation", "hill", "dense-vegetation", "dense-vegetation", "dense-vegetation", "steep-hill", "steep-hill"],
        [Planet.Ruins] = ["ruined-wall", "ruined-wall", "ruined-wall", "ruin", "ruin", "ruined-corner", "ruined-corner", "rubble-field", "rubble-field"],
    };

    // 2D6 → the entry's alternatives, index 0 = a roll of 2.
    static readonly Dictionary<Planet, string[][]> Table = new()
    {
        [Planet.Generic] = [["large-hill"], ["large-hill"], ["large-ruins"], ["large-field"], ["barricade"], ["barricade"], ["barricade"], ["large-forest"], ["large-sight-blocker"], ["large-lake"], ["large-lake"]],
        [Planet.Hospitable] = [["deep-river"], ["shallow-river"], ["tall-crops-field"], ["ruins"], ["forest", "plantation"], ["hill", "forest", "tall-crops-field"], ["hill"], ["wall-or-fence"], ["large-building"], ["small-buildings"], ["steep-hill"]],
        [Planet.Wasteland] = [["toxic-river"], ["sludge-stream"], ["sludge-field"], ["ruins"], ["large-rock"], ["small-dunes"], ["crater"], ["toxic-smog"], ["large-building"], ["mine-field"], ["small-buildings"]],
        [Planet.Death] = [["death-marsh"], ["crumbly-rock"], ["overgrown-ruins"], ["deadly-vegetation"], ["dense-vegetation"], ["dense-vegetation", "hill"], ["hill"], ["small-buildings"], ["steep-hill"], ["beast-lair"], ["quicksand"]],
        [Planet.Ruins] = [["crater"], ["barricade"], ["rubble-field"], ["ruins"], ["ruined-wall"], ["ruined-corner"], ["ruin"], ["ruined-wall"], ["rubble-field"], ["overgrown-ruins"], ["crater"]],
    };

    public static IReadOnlyList<string> Fixed(Planet planet) => FixedPieces[planet];
    public static IReadOnlyList<string> Rolled(Planet planet, int twoD6) => Table[planet][Math.Clamp(twoD6, 2, 12) - 2];

    /// <summary>The fixed nine then the rolled ones, drawn from <paramref name="rng"/> as 4D3, then per piece 2D6 and the alternative.</summary>
    public static IReadOnlyList<string> Roll(Planet planet, Random rng)
    {
        var names = new List<string>(Fixed(planet));
        var extra = Enumerable.Range(0, 4).Sum(_ => rng.Next(1, 4));                        // 4D3
        for (var i = 0; i < extra; i++)
        {
            var alternatives = Rolled(planet, rng.Next(1, 7) + rng.Next(1, 7));            // 2D6
            names.Add(alternatives[rng.Next(alternatives.Count)]);
        }
        return names;
    }
}
