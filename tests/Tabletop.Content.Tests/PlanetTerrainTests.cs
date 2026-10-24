using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class PlanetTerrainTests
{
    [Fact]
    public void Generic_fixed_pieces_are_the_rulebooks_nine()
    {
        var fixedNames = PlanetTerrain.Fixed(Planet.Generic);
        Assert.Equal(9, fixedNames.Count);
        Assert.Equal(2, fixedNames.Count(n => n == "barricade"));
        Assert.Equal(5, fixedNames.Count(n => n == "large-sight-blocker"));
        Assert.Equal(["large-hill"], PlanetTerrain.Rolled(Planet.Generic, 2));
        Assert.Equal(["large-hill"], PlanetTerrain.Rolled(Planet.Generic, 3));
        Assert.Equal(["barricade"], PlanetTerrain.Rolled(Planet.Generic, 7));
        Assert.Equal(["large-lake"], PlanetTerrain.Rolled(Planet.Generic, 12));
        Assert.Equal(["hill", "forest", "tall-crops-field"], PlanetTerrain.Rolled(Planet.Hospitable, 7));   // "Hill, Forest, or Field"
        Assert.Equal(["quicksand"], PlanetTerrain.Rolled(Planet.Death, 12));
    }

    /// <summary>The ruins pack (the owner's, not the rulebook's): ruins and what suits a ruined city, never a whole building, and
    /// its fixed nine keep p.12's balance (at least half block line of sight, a third give cover, a third are difficult).</summary>
    [Fact]
    public void The_ruins_pack_is_ruins_without_whole_buildings()
    {
        var fixedNames = PlanetTerrain.Fixed(Planet.Ruins);
        var every = fixedNames.Concat(Enumerable.Range(2, 11).SelectMany(roll => PlanetTerrain.Rolled(Planet.Ruins, roll))).ToList();
        Assert.Empty(every.Intersect(["large-building", "small-buildings", "large-sight-blocker"]));
        Assert.Equal(["ruined-wall", "ruined-wall", "ruined-wall", "ruin", "ruin", "ruined-corner", "ruined-corner", "rubble-field", "rubble-field"], fixedNames);
        var declarations = fixedNames.Select(n => RepoCatalog.Terrain[n].Volume.Declarations).ToList();
        Assert.True(declarations.Count(d => d.HasFlag(TerrainDeclarations.BlocksLineOfSight)) * 2 >= 9);
        Assert.True(declarations.Count(d => d.HasFlag(TerrainDeclarations.Cover)) * 3 >= 9);
        Assert.True(declarations.Count(d => d.HasFlag(TerrainDeclarations.Difficult)) * 3 >= 9);
        Assert.Equal(["crater"], PlanetTerrain.Rolled(Planet.Ruins, 2));
        Assert.Equal(["ruined-corner"], PlanetTerrain.Rolled(Planet.Ruins, 7));
    }

    [Fact]
    public void Every_generator_entry_names_a_built_in()
    {
        foreach (var planet in Enum.GetValues<Planet>())
        {
            Assert.All(PlanetTerrain.Fixed(planet), n => Assert.True(RepoCatalog.Terrain.ContainsKey(n), $"{planet}: {n}"));
            for (var roll = 2; roll <= 12; roll++)
                Assert.All(PlanetTerrain.Rolled(planet, roll), n => Assert.True(RepoCatalog.Terrain.ContainsKey(n), $"{planet} {roll}: {n}"));
        }
    }

    [Fact]
    public void A_roll_is_the_fixed_nine_then_4d3_more()
    {
        foreach (var planet in Enum.GetValues<Planet>())
            for (var seed = 0; seed < 20; seed++)
            {
                var names = PlanetTerrain.Roll(planet, new Random(seed));
                Assert.InRange(names.Count, 13, 21);
                Assert.Equal(PlanetTerrain.Fixed(planet), names.Take(9));
                var any = Enumerable.Range(2, 11).SelectMany(r => PlanetTerrain.Rolled(planet, r)).ToHashSet();
                Assert.All(names.Skip(9), n => Assert.Contains(n, any));
            }
    }

    [Fact]
    public void The_same_seed_rolls_the_same_names() =>
        Assert.Equal(PlanetTerrain.Roll(Planet.Wasteland, new Random(7)), PlanetTerrain.Roll(Planet.Wasteland, new Random(7)));
}
