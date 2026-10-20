using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class TerrainGeneratorTests
{
    static readonly TableSize Opr = new(72, 48);

    [Theory] [InlineData(Planet.Generic)] [InlineData(Planet.Hospitable)] [InlineData(Planet.Wasteland)] [InlineData(Planet.Death)] [InlineData(Planet.Ruins)]
    public void Nine_fixed_plus_4d3_rolled_land_beside_the_slab(Planet planet)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var batch = TerrainGenerators.Generate(planet, Opr, new Random(seed));
            Assert.InRange(batch.Count, 9 + 4, 9 + 12);
            Assert.All(batch, add => Assert.False(Table.ContainsPoint(Opr, add.Placement.Position.Horizontal)));
            Assert.All(batch, add => Assert.True(add.Placement.Position.X >= Opr.Width + 4));
            Assert.Equal(batch.Count, batch.Select(a => a.Placement.Id).Distinct().Count());
            Assert.All(batch, add => Assert.NotEmpty(add.Placement.Volumes));
        }
    }

    [Fact]
    public void Generic_fixed_pieces_are_the_rulebooks_nine()
    {
        var fixedNames = TerrainGenerators.Fixed(Planet.Generic);
        Assert.Equal(9, fixedNames.Count);
        Assert.Equal(2, fixedNames.Count(n => n == "barricade"));
        Assert.Equal(5, fixedNames.Count(n => n == "large-sight-blocker"));
        Assert.Equal(["large-hill"], TerrainGenerators.Rolled(Planet.Generic, 2));
        Assert.Equal(["large-hill"], TerrainGenerators.Rolled(Planet.Generic, 3));
        Assert.Equal(["barricade"], TerrainGenerators.Rolled(Planet.Generic, 7));
        Assert.Equal(["large-lake"], TerrainGenerators.Rolled(Planet.Generic, 12));
        Assert.Equal(["hill", "forest", "tall-crops-field"], TerrainGenerators.Rolled(Planet.Hospitable, 7));   // "Hill, Forest, or Field"
        Assert.Equal(["quicksand"], TerrainGenerators.Rolled(Planet.Death, 12));
    }

    /// <summary>The ruins pack (the owner's, not the rulebook's): ruins and what suits a ruined city, never a whole building, and
    /// its fixed nine keep p.12's balance (at least half block line of sight, a third give cover, a third are difficult).</summary>
    [Fact]
    public void The_ruins_pack_is_ruins_without_whole_buildings()
    {
        var fixedNames = TerrainGenerators.Fixed(Planet.Ruins);
        var every = fixedNames.Concat(Enumerable.Range(2, 11).SelectMany(roll => TerrainGenerators.Rolled(Planet.Ruins, roll))).ToList();
        Assert.Empty(every.Intersect(["large-building", "small-buildings", "large-sight-blocker"]));
        Assert.Equal(["ruined-wall", "ruined-wall", "ruined-wall", "ruin", "ruin", "ruined-corner", "ruined-corner", "rubble-field", "rubble-field"], fixedNames);
        var declarations = fixedNames.Select(n => BuiltinContent.Terrain[n].Volume.Declarations).ToList();
        Assert.True(declarations.Count(d => d.HasFlag(TerrainDeclarations.BlocksLineOfSight)) * 2 >= 9);
        Assert.True(declarations.Count(d => d.HasFlag(TerrainDeclarations.Cover)) * 3 >= 9);
        Assert.True(declarations.Count(d => d.HasFlag(TerrainDeclarations.Difficult)) * 3 >= 9);
        Assert.Equal(["crater"], TerrainGenerators.Rolled(Planet.Ruins, 2));
        Assert.Equal(["ruined-corner"], TerrainGenerators.Rolled(Planet.Ruins, 7));
    }

    [Fact]
    public void The_same_seed_gives_the_same_batch() =>
        Assert.Equal(TerrainGenerators.Generate(Planet.Wasteland, Opr, new Random(7)).Select(a => a.Placement.Mesh.Builtin),
                     TerrainGenerators.Generate(Planet.Wasteland, Opr, new Random(7)).Select(a => a.Placement.Mesh.Builtin));

    [Fact]
    public void Each_piece_faces_a_random_15_degree_step()
    {
        var yaws = Enumerable.Range(0, 10).SelectMany(seed => TerrainGenerators.Generate(Planet.Generic, Opr, new Random(seed))).Select(a => a.Placement.Yaw.Degrees).ToList();
        Assert.All(yaws, y => { Assert.InRange(y, 0, 345); Assert.Equal(0, y % 15); });
        Assert.True(yaws.Distinct().Count() > 4);
    }

    [Fact]
    public void A_piece_the_catalog_does_not_list_is_skipped()
    {
        var hillsOnly = BuiltinCatalog.Parse(System.Text.Json.JsonSerializer.Serialize(new
        {
            meshes = new[] { "hill" }, textures = new[] { "grass" },
            terrain = new[] { new { id = "hill", label = "Hill", mesh = "hill", kind = "solid", footprint = new { kind = "circle", a = 3.0 }, top = 2.0, declarations = new[] { "cover" } } },
        }));
        var batch = TerrainGenerators.Generate(Planet.Hospitable, Opr, new Random(1), hillsOnly);
        Assert.NotEmpty(batch);
        Assert.All(batch, a => Assert.Equal("hill", a.Placement.Mesh.Builtin));
    }
}
