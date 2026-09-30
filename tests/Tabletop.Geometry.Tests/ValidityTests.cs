namespace Tabletop.Geometry.Tests;

/// <summary>The predicates agree with the outline constructors: valid exactly when building and using the outline does not throw.</summary>
public class ValidityTests
{
    static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }

    [Theory]
    [InlineData(FootprintKind.Rect, 6, 6, 1, true)]
    [InlineData(FootprintKind.Rect, 6, 6, 1e-4, false)]              // the 6×6 hill scaled to 6e-4 square: twice its area is 7.2e-7
    [InlineData(FootprintKind.Rect, 6, 6, 2e-4, true)]               // 2.88e-6: small, and a polygon
    [InlineData(FootprintKind.Rect, 1e-7, 2, 1, false)]
    [InlineData(FootprintKind.Rect, 1e-3, 2, 1, true)]
    [InlineData(FootprintKind.Rect, 0, 2, 1, false)]
    [InlineData(FootprintKind.Rect, -2, 2, 1, false)]
    [InlineData(FootprintKind.Circle, 1e-4, 1e-4, 1, true)]           // a circle has no area threshold
    [InlineData(FootprintKind.Circle, 0, 0, 1, false)]
    public void A_footprint_is_valid_exactly_when_its_outline_builds(FootprintKind kind, double a, double b, double scale, bool valid)
    {
        var footprint = new Footprint(kind, a, b);
        Assert.Equal(valid, footprint.IsValid(scale));
        if (a <= 0) return;                                                // a non-positive size builds a meaningless outline rather than throwing
        var volume = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, footprint, new Point2(0, 0), new Yaw(30), 0, 2, TerrainDeclarations.Cover, null),
            new TerrainTransform(new Point3(10, 10, 0), new Yaw(15), scale));
        Assert.Equal(valid, !Throws(() => Containment.VolumesContaining(Fx.Model(10, 10), [volume])));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(2, double.NaN)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    [InlineData(2, double.PositiveInfinity)]
    public void A_footprint_with_a_size_or_scale_that_is_not_a_positive_number_is_invalid(double a, double scale) =>
        Assert.False(Footprint.Rect(a, 2).IsValid(scale));

    [Fact]
    public void A_footprint_of_an_undefined_kind_is_invalid() => Assert.False(new Footprint((FootprintKind)7, 2, 2).IsValid(1));

    [Theory]
    [InlineData(72, 48, true)]
    [InlineData(0.05, 0.05, true)]                                     // small but every built-in zone still builds on it
    [InlineData(1e-4, 48, true)]                                       // a sliver, but every outline on it has twice its area above 1e-6
    [InlineData(1e-3, 1e-3, false)]                                    // the slab builds (2e-6); the Side Battle and Disordered triangles do not
    [InlineData(1e-7, 1, false)]
    [InlineData(0, 48, false)]
    [InlineData(72, double.PositiveInfinity, false)]
    [InlineData(double.NaN, 48, false)]
    public void A_table_is_valid_exactly_when_its_slab_and_every_built_in_zone_build(double width, double length, bool valid)
    {
        var size = new TableSize(width, length);
        Assert.Equal(valid, Table.IsValid(size));
        if (!(double.IsFinite(width) && double.IsFinite(length) && width > 0 && length > 0)) return;
        var model = Fx.Model(width / 2, length / 2, diameter: Math.Min(width, length) / 4);
        var builds = !Throws(() => Table.ContainsBase(size, model))
            && ZoneStyles.All.SelectMany(ZoneStyles.ShapesOf).All(id => !Throws(() => ZoneStyles.BaseFullyWithin(id, size, model)));
        Assert.Equal(valid, builds);
    }
}
