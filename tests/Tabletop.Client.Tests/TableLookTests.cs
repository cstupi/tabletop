using Tabletop.Client.Core;
namespace Tabletop.Client.Tests;

public class TableLookTests
{
    /// <summary>A w × h RGBA image whose alpha (the height) is <paramref name="height"/>(x, y).</summary>
    static byte[] Image(int w, int h, Func<int, int, byte> height, byte grey = 100)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) { var i = (y * w + x) * 4; rgba[i] = rgba[i + 1] = rgba[i + 2] = grey; rgba[i + 3] = height(x, y); }
        return rgba;
    }

    static (int X, int Y, int Z) At(byte[] n, int w, int x, int y) { var i = (y * w + x) * 4; return (n[i], n[i + 1], n[i + 2]); }

    [Fact]
    public void A_flat_height_is_a_straight_up_normal()
    {
        var n = HeightNormals.FromHeights(Image(8, 8, (_, _) => 90), 8, 8, 4);
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal((128, 128, 255), At(n, 8, i % 8, i / 8)));
    }

    /// <summary>Rising toward +x the surface leans back toward −x (red below the middle); rising toward later rows it leans toward the
    /// image's top (green above the middle, the OpenGL convention Godot reads).</summary>
    [Fact]
    public void A_slope_leans_the_normal_away_from_the_rise()
    {
        var alongX = HeightNormals.FromHeights(Image(8, 8, (x, _) => (byte)(x * 10)), 8, 8, 4);
        var (r, g, _) = At(alongX, 8, 3, 3);
        Assert.True(r < 120 && Math.Abs(g - 128) <= 1, $"{r} {g}");
        var alongY = HeightNormals.FromHeights(Image(8, 8, (_, y) => (byte)(y * 10)), 8, 8, 4);
        (r, g, _) = At(alongY, 8, 3, 3);
        Assert.True(g > 136 && Math.Abs(r - 128) <= 1, $"{r} {g}");
    }

    /// <summary>The prints tile, so the first column's slope reads the last column as its neighbour.</summary>
    [Fact]
    public void Edges_wrap()
    {
        var n = HeightNormals.FromHeights(Image(8, 8, (x, _) => (byte)(x == 7 ? 200 : 0)), 8, 8, 4);
        Assert.True(At(n, 8, 0, 4).X > 136, "column 0 falls from column 7 behind it");
        Assert.True(At(n, 8, 6, 4).X < 120, "column 6 rises toward column 7");
    }

    /// <summary>A print with no height in its alpha (all opaque) reads its height from its luminance.</summary>
    [Fact]
    public void An_opaque_print_takes_its_height_from_its_luminance()
    {
        var rgba = Image(8, 8, (_, _) => 255);
        for (var x = 0; x < 8; x++) for (var y = 0; y < 8; y++) { var i = (y * 8 + x) * 4; rgba[i] = rgba[i + 1] = rgba[i + 2] = (byte)(x * 20); }
        Assert.True(At(HeightNormals.FromHeights(rgba, 8, 8, 4), 8, 3, 3).X < 120);
    }

    [Fact]
    public void The_mean_colour_is_the_average_of_the_pixels()
    {
        var rgba = new byte[] { 0, 100, 200, 255, 100, 100, 100, 0 };
        Assert.Equal((50f / 255, 100f / 255, 150f / 255), HeightNormals.Mean(rgba));
    }

    [Theory]
    [InlineData("masonry", 0f, false, SurfaceFamily.Stone)]
    [InlineData("rock", 0f, false, SurfaceFamily.Stone)]
    [InlineData("flagstones", 0f, false, SurfaceFamily.Stone)]
    [InlineData("forest-floor", 0f, false, SurfaceFamily.Ground)]
    [InlineData("churned-dirt", 0f, false, SurfaceFamily.Ground)]
    [InlineData("leaves", 0f, false, SurfaceFamily.Foliage)]
    [InlineData("dead-reeds", 0f, false, SurfaceFamily.Foliage)]
    [InlineData("bark", 0f, false, SurfaceFamily.Wood)]
    [InlineData("timber", 0f, false, SurfaceFamily.Wood)]
    [InlineData("rusted-steel", 0.5f, false, SurfaceFamily.Metal)]
    [InlineData("panel", 0.3f, false, SurfaceFamily.Metal)]
    [InlineData("water", 0f, true, SurfaceFamily.Liquid)]
    [InlineData("sludge", 0f, false, SurfaceFamily.Liquid)]
    [InlineData("smog", 0f, true, SurfaceFamily.Plain)]
    [InlineData("lit-window", 0f, false, SurfaceFamily.Plain)]
    [InlineData("glass", 0.2f, false, SurfaceFamily.Plain)]
    [InlineData("something-new", 0f, false, SurfaceFamily.Stone)]
    public void A_surface_takes_its_family_from_its_material_name(string name, float metallic, bool blended, SurfaceFamily family) =>
        Assert.Equal(family, SurfaceFamilies.Of(name, metallic, blended));
}
