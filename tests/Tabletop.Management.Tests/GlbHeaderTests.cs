using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class GlbHeaderTests
{
    [Fact]
    public void Recognises_the_magic_and_reads_the_json_length()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.True(GlbHeader.IsGlb(glb.AsSpan(0, 12)));
        Assert.False(GlbHeader.IsGlb("solid cube\n\n"u8));
        Assert.Equal(BitConverter.ToInt32(glb, 12), GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)));
    }

    [Theory]
    [InlineData(36, null, true, 12)]      // indexed triangles
    [InlineData(30, 4, false, 10)]        // unindexed triangles from POSITION count
    [InlineData(10, 5, true, 8)]          // triangle strip
    [InlineData(10, 6, true, 8)]          // triangle fan
    [InlineData(10, 1, true, 0)]          // lines count nothing
    public void Counts_triangles_per_mode(int count, int? mode, bool indexed, long expected)
    {
        var glb = TestGlb.Build((count, mode, indexed));
        var len = GlbHeader.JsonChunkLength(glb.AsSpan(0, 20));
        Assert.Equal(expected, GlbHeader.CountTriangles(glb.AsSpan(20, len)));
    }

    [Fact]
    public void Sums_over_primitives()
    {
        var glb = TestGlb.Build((300_003, null, true), (3, null, true));
        Assert.Equal(100_002, GlbHeader.CountTriangles(glb.AsSpan(20, GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)))));
        Assert.True(100_002 > GlbHeader.WarnAbove);
    }
}
