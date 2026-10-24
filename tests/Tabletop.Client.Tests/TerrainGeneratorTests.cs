using Tabletop.Client.Core;
using Tabletop.Content;
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
