using Tabletop.Content.Tests;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>Smoke tests for Task 1: the three libs and the two sample projects resolve, and the geometry library answers in the wire frame — corner origin, 0 ≤ x ≤ width, 0 ≤ y ≤ length — so nothing in the client converts.</summary>
public class ReferencesTests
{
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void Zone_polygons_are_in_the_wire_frame()
    {
        var zone = ZoneStyles.Resolve("frontline/1", Opr)[0];
        Assert.Equal(36, zone.Vertices.Min(v => v.Y), 6);
        Assert.Equal(48, zone.Vertices.Max(v => v.Y), 6);
        Assert.Equal(0, zone.Vertices.Min(v => v.X), 6);
    }
    [Fact] public void On_slab_is_the_game_servers_rule()
    {
        Assert.True(Table.ContainsPoint(Opr, new Point2(72, 48)));
        Assert.False(Table.ContainsPoint(Opr, new Point2(72.5, 48)));
        Assert.False(Table.ContainsPoint(Opr, new Point2(-1, 10)));
    }
    [Fact] public void The_sample_table_state_is_on_the_slab()
    {
        var sample = TableStateTests.Sample();
        Assert.All(sample.Terrain, t => Assert.True(Table.ContainsPoint(sample.Size, t.Position.Horizontal)));
        Assert.All(sample.Markers, m => Assert.True(Table.ContainsPoint(sample.Size, m.Position.Horizontal)));
    }
}
