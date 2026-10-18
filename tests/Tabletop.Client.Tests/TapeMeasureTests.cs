using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class TapeMeasureTests
{
    static PlacedModel Point(double x, double y, double z = 0) => new(new ModelBody(Base.Circle(0), 0), new Placement(new Point3(x, y, z), new Yaw(0)));
    static PlacedModel Model(double x, double y, double z = 0) => new(new ModelBody(Base.Circle(1), 1.5), new Placement(new Point3(x, y, z), new Yaw(0)));

    [Fact]
    public void Horizontal_reads_the_table_plane_and_straight_the_line_through_the_height()
    {
        var flat = MeasureReading.Of(new Point3(0, 0, 0), new Point3(3, 4, 12), MeasureMode.Horizontal);
        Assert.Equal(5, flat.Distance, 6); Assert.Equal(12, flat.Rise, 6);
        Assert.Equal("5.0\" (2D)", flat.Label);
        var straight = MeasureReading.Of(new Point3(0, 0, 0), new Point3(3, 4, 12), MeasureMode.Straight);
        Assert.Equal(13, straight.Distance, 6);
        Assert.Equal("13.0\" (3D)  Δh 12.0\"", straight.Label);
    }

    [Fact]
    public void The_rise_is_unsigned()
    {
        Assert.Equal(3, MeasureReading.Of(new Point3(0, 0, 3), new Point3(4, 0, 0), MeasureMode.Straight).Rise, 6);
        Assert.Equal("5.0\" (3D)  Δh 3.0\"", MeasureReading.Of(new Point3(0, 0, 3), new Point3(4, 0, 0), MeasureMode.Straight).Label);
    }

    [Fact]
    public void A_line_appears_once_the_cursor_leaves_the_anchor_and_follows_it()
    {
        var tape = new TapeMeasure();
        Assert.Null(tape.Line);
        tape.Start(Point(10, 10));
        Assert.True(tape.Following); Assert.Null(tape.Line);
        tape.Follow(Point(10, 10));
        Assert.Null(tape.Line);                                            // a press with no movement draws nothing (Ctrl+Z stays clean)
        tape.Follow(Point(16, 10));
        Assert.Equal(new Measurement(new Point3(10, 10, 0), new Point3(16, 10, 0), MeasureMode.Horizontal), tape.Line);
        tape.Follow(Point(10, 18));
        Assert.Equal(8, tape.Reading!.Distance, 6);
    }

    [Fact]
    public void A_model_measures_from_its_base_edge()
    {
        var tape = new TapeMeasure();
        tape.Start(Model(10, 10));                                         // a 1" base: its edge is 0.5" from the centre
        tape.Follow(Point(16, 10));
        Assert.Equal(5.5, tape.Reading!.Distance, 6);
        Assert.Equal(10.5, tape.Line!.From.X, 6);
        tape.Follow(Model(16, 10));
        Assert.Equal(5, tape.Reading!.Distance, 6);
    }

    [Fact]
    public void The_mode_changes_the_reading_and_rides_on_the_line()
    {
        var tape = new TapeMeasure { Mode = MeasureMode.Straight };
        tape.Start(Point(0, 0)); tape.Follow(Point(4, 0, 3));
        Assert.Equal(5, tape.Reading!.Distance, 6); Assert.Equal(MeasureMode.Straight, tape.Line!.Mode);
        tape.Mode = tape.Mode.Toggled();
        Assert.Equal(MeasureMode.Horizontal, tape.Mode);
        Assert.Equal(4, tape.Reading!.Distance, 6);
    }

    [Fact]
    public void Finish_fixes_the_line_and_clear_takes_it_away()
    {
        var tape = new TapeMeasure();
        tape.Start(Point(0, 0)); tape.Finish(Point(6, 0));
        Assert.False(tape.Following);
        tape.Follow(Point(12, 0));
        Assert.Equal(6, tape.Reading!.Distance, 6);                        // a fixed line no longer follows
        tape.Clear();
        Assert.Null(tape.Line); Assert.Null(tape.Reading); Assert.False(tape.Following);
    }

    [Fact]
    public void A_point_on_terrain_rests_on_its_top()
    {
        var hill = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Circle(4), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.None, null),
            new TerrainTransform(new Point3(20, 20, 0), new Yaw(0), 1));
        Assert.Equal(new Point3(21, 20, 3), TapeMeasure.OnSurface(new Point3(21, 20, 1.2), [hill]));   // a hit on the hill's side lands on its top
        Assert.Equal(new Point3(30, 20, 0), TapeMeasure.OnSurface(new Point3(30, 20, 0.4), [hill]));
    }

    [Fact]
    public void Mode_reads_from_settings_and_defaults_to_horizontal()
    {
        Assert.Equal(MeasureMode.Horizontal, ClientSettings.Default.MeasureMode);
        Assert.Equal(MeasureMode.Straight, MeasureMode.Horizontal.Toggled());
    }
}
