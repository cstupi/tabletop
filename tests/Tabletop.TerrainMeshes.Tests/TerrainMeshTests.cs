using System.Numerics;
using System.Text.Json;

namespace Tabletop.TerrainMeshes.Tests;

public class TerrainMeshTests
{
    public static TheoryData<string> Names => new(Catalog.All.Select(m => m.Name));
    static Model Build(string name) => Catalog.All.Single(m => m.Name == name).Build();
    static IEnumerable<Vector3> Points(Model m) => m.Parts.SelectMany(p => p.Positions);

    [Fact]
    public void The_catalog_writes_the_clients_thirteen_mesh_groups()
    {
        Assert.Equal(["hill", "hill-rect", "woods", "ruins", "building", "wall", "rock", "field", "water", "lake", "pad", "crater", "haze"], Catalog.All.Select(m => m.Name));
    }

    [Theory, MemberData(nameof(Names))]
    public void Stays_in_the_unit_box_and_reaches_up_from_the_ground(string name)
    {
        var m = Build(name);
        const float e = 1e-4f;
        Assert.All(Points(m), p =>
        {
            Assert.InRange(p.Y, 0, 1 + e);
            Assert.InRange(p.X, -0.5f - e, 0.5f + e); Assert.InRange(p.Z, -0.5f - e, 0.5f + e);
            if (m.Round) Assert.True(p.X * p.X + p.Z * p.Z <= 0.25f + e);
        });
        Assert.True(Points(m).Max(p => p.Y) > 0.05f);
        Assert.True(Points(m).Max(p => p.X) > 0.45f && Points(m).Min(p => p.Z) < -0.45f, "fills its footprint");
    }

    /// <summary>The Solid built-ins' tops are their volume's top: a model rests on the volume (Collision.RestHeight), so the
    /// drawn surface reaches the unit box's top.</summary>
    [Theory, InlineData("hill"), InlineData("hill-rect"), InlineData("building"), InlineData("wall"), InlineData("rock")]
    public void A_solid_piece_reaches_its_volumes_top(string name) => Assert.Equal(1f, Points(Build(name)).Max(p => p.Y), 3);

    [Theory, MemberData(nameof(Names))]
    public void Stays_within_the_triangle_budget_with_coloured_unit_normals(string name)
    {
        var m = Build(name);
        Assert.InRange(m.Triangles, 500, 20_000);
        foreach (var part in m.Parts)
        {
            Assert.Equal(part.Positions.Count, part.Normals.Count); Assert.Equal(part.Positions.Count, part.Colours.Count);
            Assert.Equal(0, part.Indices.Count % 3);
            Assert.All(part.Indices, i => Assert.InRange(i, 0, part.Positions.Count - 1));
            Assert.All(part.Normals, n => Assert.Equal(1f, n.Length(), 3));
            Assert.All(part.Colours, c => Assert.True(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1 && c.W == 1));
        }
    }

    /// <summary>Front faces are counter-clockwise (glTF): every triangle faces the way its vertices' normals, summed, point.</summary>
    [Fact]
    public void Triangles_face_their_normals()
    {
        foreach (var (name, build) in Catalog.All)
            foreach (var part in build().Parts)
                for (var i = 0; i < part.Indices.Count; i += 3)
                {
                    var (a, b, c) = (part.Positions[part.Indices[i]], part.Positions[part.Indices[i + 1]], part.Positions[part.Indices[i + 2]]);
                    var cross = Vector3.Cross(b - a, c - a);
                    if (cross.Length() < 1e-6f) continue;
                    var normal = part.Normals[part.Indices[i]] + part.Normals[part.Indices[i + 1]] + part.Normals[part.Indices[i + 2]];
                    Assert.True(Vector3.Dot(cross, normal) > 0, $"{name}/{part.Material.Name} triangle {i / 3} is wound against its normals");
                }
    }

    [Fact]
    public void The_hill_tops_are_flat_plateaus_inside_the_rim()
    {
        Assert.Equal((1f, 0f), (Hill.Slope(0), Hill.Slope(1)));
        for (var s = 0f; s < 1; s += 0.01f) Assert.True(Hill.Slope(s + 0.01f) <= Hill.Slope(s) + 1e-6f);
        foreach (var m in new[] { Hill.Round(), Hill.Square() })
        {
            var ground = m[Hill.Ground].Positions;
            var top = ground.Where(p => p.Y >= 1 - 1e-5f).ToList();
            Assert.Contains(top, p => p.X == 0 && p.Z == 0);
            Assert.All(top, p => Assert.True(MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Z)) <= Hill.Top / 2 + 1e-4f));
            Assert.All(ground.Where(p => p.X * p.X + p.Z * p.Z < 0.22f * 0.22f), p => Assert.Equal(1f, p.Y));   // a model stands on the drawn top
        }
    }

    [Fact]
    public void The_ruins_floor_is_at_ground_level()
    {
        var floor = Ruins.Build()[Ruins.Floor].Positions;
        Assert.True(floor.Max(p => p.Y) < 0.015f);   // ruins are walk-in ground (Area): models stand on the table inside
    }

    [Theory, MemberData(nameof(Names))]
    public void Writes_a_small_binary_gltf_with_one_material_per_primitive(string name)
    {
        var model = Build(name);
        var glb = Glb.Of(model);
        Assert.True(glb.Length < 2 << 20);
        Assert.Equal((0x46546C67u, 2u, (uint)glb.Length), (BitConverter.ToUInt32(glb, 0), BitConverter.ToUInt32(glb, 4), BitConverter.ToUInt32(glb, 8)));
        var jsonLength = (int)BitConverter.ToUInt32(glb, 12);
        Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(glb, 16));
        var binLength = (int)BitConverter.ToUInt32(glb, 20 + jsonLength);
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = json.RootElement;
        Assert.Equal(binLength, root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());
        var accessors = root.GetProperty("accessors"); var views = root.GetProperty("bufferViews");
        foreach (var view in views.EnumerateArray())
            Assert.True(view.GetProperty("byteOffset").GetInt32() % 4 == 0 && view.GetProperty("byteOffset").GetInt32() + view.GetProperty("byteLength").GetInt32() <= binLength);
        var primitives = root.GetProperty("meshes").EnumerateArray().SelectMany(m => m.GetProperty("primitives").EnumerateArray()).ToList();
        Assert.Equal(model.Parts.Count(), primitives.Count);
        Assert.Equal(primitives.Count, root.GetProperty("materials").GetArrayLength());
        var triangles = 0;
        foreach (var p in primitives)
        {
            var attributes = p.GetProperty("attributes");
            var count = accessors[attributes.GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32();
            Assert.Equal(count, accessors[attributes.GetProperty("NORMAL").GetInt32()].GetProperty("count").GetInt32());
            Assert.Equal(count, accessors[attributes.GetProperty("COLOR_0").GetInt32()].GetProperty("count").GetInt32());
            triangles += accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3;
            var material = root.GetProperty("materials")[p.GetProperty("material").GetInt32()];
            Assert.InRange(material.GetProperty("pbrMetallicRoughness").GetProperty("roughnessFactor").GetSingle(), 0, 1);
        }
        Assert.Equal(model.Triangles, triangles);
        // Translucent materials (smog, water) sit in their own node, so the client can keep them out of the shadow pass.
        var nodes = root.GetProperty("meshes").EnumerateArray().Select(m => m.GetProperty("primitives").EnumerateArray()
            .Select(p => root.GetProperty("materials")[p.GetProperty("material").GetInt32()].GetProperty("alphaMode").GetString()).Distinct().ToList()).ToList();
        Assert.All(nodes, modes => Assert.Single(modes));
        Assert.All(root.GetProperty("nodes").EnumerateArray(), n => Assert.False(n.TryGetProperty("matrix", out _) || n.TryGetProperty("translation", out _) || n.TryGetProperty("scale", out _)));
    }

    [Theory, MemberData(nameof(Names))]
    public void Is_deterministic(string name) => Assert.Equal(Glb.Of(Build(name)), Glb.Of(Build(name)));

    [Fact]
    public void The_shipped_meshes_are_the_generators_output()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/builtin/terrain"));
        foreach (var (name, build) in Catalog.All)
            Assert.True(Glb.Of(build()).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir, $"{name}.glb"))), $"{name}.glb is stale: run scripts/terrain-meshes");
    }
}
