using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum Planet { Generic, Hospitable, Wasteland, Death }

/// <summary>Grimdark Future p.18–19: nine fixed pieces, then 4D3 more each rolled on 2D6. Runs on the client; the result is a batch of adds.</summary>
public static class TerrainGenerators
{
    static readonly Dictionary<Planet, string[]> FixedPieces = new()
    {
        [Planet.Generic] = ["barricade", "barricade", "large-forest", "large-field", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker"],
        [Planet.Hospitable] = ["wall-or-fence", "wall-or-fence", "hill", "plantation", "forest", "forest", "forest", "large-building", "large-building"],
        [Planet.Wasteland] = ["small-dunes", "small-dunes", "large-rock", "crater", "large-building", "large-building", "large-building", "toxic-smog", "toxic-smog"],
        [Planet.Death] = ["overgrown-ruins", "overgrown-ruins", "deadly-vegetation", "hill", "dense-vegetation", "dense-vegetation", "dense-vegetation", "steep-hill", "steep-hill"],
    };

    // 2D6 → the entry's alternatives, index 0 = a roll of 2.
    static readonly Dictionary<Planet, string[][]> Table = new()
    {
        [Planet.Generic] = [["large-hill"], ["large-hill"], ["large-ruins"], ["large-field"], ["barricade"], ["barricade"], ["barricade"], ["large-forest"], ["large-sight-blocker"], ["large-lake"], ["large-lake"]],
        [Planet.Hospitable] = [["deep-river"], ["shallow-river"], ["tall-crops-field"], ["ruins"], ["forest", "plantation"], ["hill", "forest", "tall-crops-field"], ["hill"], ["wall-or-fence"], ["large-building"], ["small-buildings"], ["steep-hill"]],
        [Planet.Wasteland] = [["toxic-river"], ["sludge-stream"], ["sludge-field"], ["ruins"], ["large-rock"], ["small-dunes"], ["crater"], ["toxic-smog"], ["large-building"], ["mine-field"], ["small-buildings"]],
        [Planet.Death] = [["death-marsh"], ["crumbly-rock"], ["overgrown-ruins"], ["deadly-vegetation"], ["dense-vegetation"], ["dense-vegetation", "hill"], ["hill"], ["small-buildings"], ["steep-hill"], ["beast-lair"], ["quicksand"]],
    };

    public static IReadOnlyList<string> Fixed(Planet planet) => FixedPieces[planet];
    public static IReadOnlyList<string> Rolled(Planet planet, int twoD6) => Table[planet][Math.Clamp(twoD6, 2, 12) - 2];

    /// <summary>The pieces land beside the long edge, off the slab, in rows 8" apart, four to a row, each facing a random 15° step; players drag them on.</summary>
    public static IReadOnlyList<AddTerrain> Generate(Planet planet, TableSize table, Random rng)
    {
        var names = new List<string>(Fixed(planet));
        var extra = Enumerable.Range(0, 4).Sum(_ => rng.Next(1, 4));                        // 4D3
        for (var i = 0; i < extra; i++)
        {
            var alternatives = Rolled(planet, rng.Next(1, 7) + rng.Next(1, 7));            // 2D6
            names.Add(alternatives[rng.Next(alternatives.Count)]);
        }
        var yaws = names.Select(_ => new Yaw(rng.Next(0, 24) * 15)).ToList();
        return names.Select((name, i) => new AddTerrain(BuiltinContent.Place(name, new Point3(table.Width + 4 + (i % 4) * 8, 4 + (i / 4) * 8, 0), yaws[i]))).ToList();
    }
}
