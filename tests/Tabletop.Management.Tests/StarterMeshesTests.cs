using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class StarterMeshesTests
{
    [Fact]
    public void Five_figures_on_import_s_round_bases_and_the_default_hull()
    {
        var all = StarterMeshes.All;
        Assert.Equal(["Figure, 25mm round", "Figure, 32mm round", "Figure, 40mm round", "Figure, 50mm round", "Figure, 60mm round", "Hull, 221 × 119mm"], all.Select(m => m.Name));
        Assert.Equal(Base.Circle(32 / 25.4), all[1].ModelledBase);
        Assert.Equal(ArmyForgeImport.DefaultBody.Base, all[5].ModelledBase);
    }

    [Fact]
    public void Each_mesh_fills_its_base_and_reaches_import_s_height()
    {
        foreach (var m in StarterMeshes.All)
        {
            var (min, max) = PositionBounds(m.Glb);
            var height = m.ModelledBase.Shape == BaseShape.Circle ? ArmyForgeImport.HeightFromBase(m.ModelledBase) : ArmyForgeImport.DefaultBody.Height;
            Assert.Equal(-m.ModelledBase.B / 2, min[0], 3); Assert.Equal(m.ModelledBase.B / 2, max[0], 3);   // width across X
            Assert.Equal(-m.ModelledBase.A / 2, min[1], 3); Assert.Equal(m.ModelledBase.A / 2, max[1], 3);   // length along the facing, +Y
            Assert.Equal(0, min[2], 3); Assert.Equal(height, max[2], 3);
        }
    }

    [Fact]
    public async Task Each_is_a_glb_the_upload_check_accepts_and_its_bytes_are_stable()
    {
        foreach (var m in StarterMeshes.All)
        {
            var check = await GlbHeader.Check(m.Glb.Length, (first, last) => Task.FromResult(m.Glb[(int)first..(int)(last + 1)]));
            Assert.Null(check.Rejection);
            Assert.InRange(check.Triangles!.Value, 12, 5_000);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(m.Glb.AsSpan(8)), (uint)m.Glb.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(m.Glb)), m.Hash);
        }
        Assert.Equal(StarterMeshes.All.Select(m => m.Hash), StarterMeshes.Build().Select(m => m.Hash));
        Assert.Equal(6, StarterMeshes.All.Select(m => m.Hash).Distinct().Count());
    }

    private static (double[] Min, double[] Max) PositionBounds(byte[] glb)
    {
        using var doc = JsonDocument.Parse(glb.AsSpan(GlbHeader.HeaderBytes, GlbHeader.JsonChunkLength(glb)).ToArray());
        var root = doc.RootElement;
        var pos = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();
        var acc = root.GetProperty("accessors")[pos];
        return (acc.GetProperty("min").EnumerateArray().Select(v => v.GetDouble()).ToArray(), acc.GetProperty("max").EnumerateArray().Select(v => v.GetDouble()).ToArray());
    }
}
