namespace Tabletop.Geometry.Tests;

public class TableTests
{
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void Bounds_run_from_the_corner_origin()
    {
        var b = Table.Bounds(Opr);
        Assert.Equal(4, b.Vertices.Count);
        Assert.Equal(0, b.Vertices.Min(v => v.X));
        Assert.Equal(0, b.Vertices.Min(v => v.Y));
        Assert.Equal(72, b.Vertices.Max(v => v.X));
        Assert.Equal(48, b.Vertices.Max(v => v.Y));
    }
    [Fact] public void Points_on_and_off_the_slab()
    {
        Assert.True(Table.ContainsPoint(Opr, new(0, 0)));
        Assert.True(Table.ContainsPoint(Opr, new(36, 24)));
        Assert.True(Table.ContainsPoint(Opr, new(72, 48)));         // the game server's rule: 0 ≤ x ≤ width, 0 ≤ y ≤ length, edge inclusive
        Assert.False(Table.ContainsPoint(Opr, new(72.1, 24)));
        Assert.False(Table.ContainsPoint(Opr, new(-1, 10)));
    }
    [Fact] public void A_base_must_be_wholly_on_the_slab()
    {
        Assert.True(Table.ContainsBase(Opr, Fx.Model(71, 24)));
        Assert.False(Table.ContainsBase(Opr, Fx.Model(71.5, 24)));
        Assert.True(Table.ContainsBase(Opr, Fx.Model(Base.Rect(2, 2), 71, 47)));
        Assert.False(Table.ContainsBase(Opr, Fx.Model(Base.Rect(2, 2), 71, 47, yaw: 45)));
    }
    [Fact] public void A_zero_size_table_has_no_slab_to_contain_a_base() =>
        Assert.Throws<ArgumentException>(() => Table.ContainsBase(new TableSize(0, 0), Fx.Model(0, 0)));
}
