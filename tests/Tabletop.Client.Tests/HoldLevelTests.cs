using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Held pieces lift over spec → Held: PageDown lowers one surface, PageUp raises one; above the highest is lifted (null) again.</summary>
public class HoldLevelTests
{
    static readonly double[] Bridge = [0, 4];

    [Fact] public void Lowering_a_lifted_piece_sets_it_on_the_highest_surface() => Assert.Equal(4, HoldLevel.Lower(null, Bridge));
    [Fact] public void Lowering_steps_down_one_surface() => Assert.Equal(0, HoldLevel.Lower(4, Bridge));
    [Fact] public void The_lowest_surface_stays() => Assert.Equal(0, HoldLevel.Lower(0, Bridge));
    [Fact] public void Raising_steps_up_one_surface() => Assert.Equal(4, HoldLevel.Raise(0, Bridge));
    [Fact] public void Raising_past_the_highest_lifts_it() => Assert.Null(HoldLevel.Raise(4, Bridge));
    [Fact] public void A_lifted_piece_stays_lifted() => Assert.Null(HoldLevel.Raise(null, Bridge));

    [Fact]
    public void A_level_between_surfaces_steps_to_the_next_one_past_it()
    {
        Assert.Equal(0, HoldLevel.Lower(2, Bridge));
        Assert.Equal(4, HoldLevel.Raise(2, Bridge));
    }
}
