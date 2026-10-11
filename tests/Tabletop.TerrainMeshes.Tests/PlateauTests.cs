using System.Numerics;
using System.Text.Json;

namespace Tabletop.TerrainMeshes.Tests;

public class PlateauTests
{
    public static TheoryData<string> Shapes => new() { "round", "square" };
    static IReadOnlyList<(Vector3 Position, Vector3 Normal)> Of(string shape) => shape == "round" ? Plateau.Round() : Plateau.Square();

    [Theory, MemberData(nameof(Shapes))]
    public void Fills_the_unit_box_with_a_plateau_inside_the_rim(string shape)
    {
        var v = Of(shape);
        Assert.All(v, x => Assert.InRange(x.Position.Y, 0f, 1f));
        Assert.Equal((0.5f, -0.5f), (MathF.Round(v.Max(x => x.Position.X), 4), MathF.Round(v.Min(x => x.Position.Z), 4)));
        Assert.All(v.Where(x => x.Normal == Vector3.UnitY), x => Assert.Equal(1f, x.Position.Y));
        Assert.All(v.Where(x => x.Position.Y == 1f), x => Assert.True(MathF.Max(MathF.Abs(x.Position.X), MathF.Abs(x.Position.Z)) <= 0.5f - Plateau.Rim + 1e-4f));
    }

    [Theory, MemberData(nameof(Shapes))]
    public void Every_triangle_faces_its_normal_and_up(string shape)
    {
        var v = Of(shape);
        Assert.Equal(0, v.Count % 3);
        for (var i = 0; i < v.Count; i += 3)
        {
            var (a, b, c) = (v[i].Position, v[i + 1].Position, v[i + 2].Position);
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), v[i].Normal) > 0);   // counter-clockwise from the front: glTF's front face
            Assert.True(v[i].Normal.Y > 0);
        }
    }

    [Fact]
    public void Writes_one_binary_gltf_with_an_interleaved_buffer()
    {
        var glb = Glb.Of("hill", Plateau.Round(), [0.4f, 0.5f, 0.3f, 1]);
        Assert.Equal((0x46546C67u, (uint)glb.Length), (BitConverter.ToUInt32(glb, 0), BitConverter.ToUInt32(glb, 8)));
        var json = JsonDocument.Parse(glb.AsMemory(20, (int)BitConverter.ToUInt32(glb, 12)));
        var position = json.RootElement.GetProperty("accessors")[0];
        Assert.Equal((Plateau.Round().Count, 1f), (position.GetProperty("count").GetInt32(), position.GetProperty("max")[1].GetSingle()));
    }
}
