using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>A new setup marker goes to the table's centre, or the nearest free spot around it, so markers added in a row never stack.</summary>
public class MarkerSpotTests
{
    static readonly TableSize Size = new(72, 48);
    static Marker At(double x, double y, double diameter = 3) => new(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(x, y, 0), new Yaw(0), diameter, null, VisualRef.OfBuiltin("objective-ring"));

    [Fact]
    public void An_empty_table_takes_the_centre()
    {
        Assert.Equal(new Point3(36, 24, 0), SetupLimits.FreeSpot(Size, [], 3));
    }

    [Fact]
    public void Each_new_marker_lands_clear_of_every_marker_already_placed()
    {
        var markers = new List<Marker>();
        for (var i = 0; i < 12; i++)
        {
            var spot = SetupLimits.FreeSpot(Size, markers, 3);
            Assert.All(markers, m => Assert.True(m.Position.Horizontal.DistanceTo(spot.Horizontal) >= 3, $"{spot} on {m.Position}"));
            Assert.True(Tabletop.Geometry.Table.ContainsPoint(Size, spot.Horizontal), $"{spot} off the table");
            markers.Add(At(spot.X, spot.Y));
        }
        Assert.NotEqual(new Point3(36, 24, 0), SetupLimits.FreeSpot(Size, [At(36, 24)], 3));
    }
}
