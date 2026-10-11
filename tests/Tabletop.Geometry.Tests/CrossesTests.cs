namespace Tabletop.Geometry.Tests;

public class CrossesTests
{
    static readonly PlacedModel Round = Fx.Model(10, 10);                                // 1.26" base
    static readonly PlacedModel Oval = Fx.Model(Base.Oval(4, 2), 10, 10);                // x 9..11, y 8..12; its rectangle's corner at (11, 12)

    [Fact] public void Straight_through_a_base_crosses() => Assert.True(Collision.Crosses(Fx.Model(7, 10), new(13, 10), [Round]));
    [Fact] public void Clear_past_a_base_does_not_cross() => Assert.False(Collision.Crosses(Fx.Model(7, 12), new(13, 12), [Round]));
    [Fact] public void Ending_in_contact_does_not_cross() => Assert.False(Collision.Crosses(Fx.Model(7, 10), new(8.74, 10), [Round]));
    [Fact] public void A_base_it_starts_on_is_left_out() => Assert.False(Collision.Crosses(Fx.Model(10.5, 10), new(13, 10), [Round]));

    [Fact]
    public void An_oval_blocks_its_outline_not_its_rectangle()
    {
        var small = Fx.Model(10.6, 11.9, diameter: 0.2);                                 // inside the rectangle's corner, clear of the oval
        Assert.False(Collision.Crosses(small, new(13, 11.9), [Oval]));
        Assert.True(Collision.Crosses(Fx.Model(7, 10), new(13, 10), [Oval]));
    }
}
