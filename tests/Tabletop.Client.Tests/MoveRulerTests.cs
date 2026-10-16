using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class MoveRulerTests
{
    [Fact]
    public void Clamp_pulls_a_target_past_the_allowance_back_along_the_line_from_the_origin()
    {
        Assert.Equal(new Point2(6, 0), MoveRuler.Clamp(new Point2(0, 0), new Point2(10, 0), 6));
        var c = MoveRuler.Clamp(new Point2(1, 1), new Point2(7, 9), 5);
        Assert.Equal(5, new Point2(1, 1).DistanceTo(c), 6);
        Assert.Equal(new Point2(4, 5), c);
    }

    [Fact]
    public void Clamp_leaves_a_target_within_the_allowance()
    {
        Assert.Equal(new Point2(3, 4), MoveRuler.Clamp(new Point2(0, 0), new Point2(3, 4), 6));
        Assert.Equal(new Point2(0, 0), MoveRuler.Clamp(new Point2(0, 0), new Point2(0, 0), 0));
    }

    [Fact]
    public void Gate_allows_a_commit_with_every_model_in_range()
    {
        var gate = MoveRuler.Gate([MoveRuler.Read(new Point2(0, 0), new Point2(3, 4), 6)], Enforcement.Strict);
        Assert.Equal(new MoveGate(true, null), gate);
    }

    [Fact]
    public void Gate_names_how_many_models_are_past_their_allowance_and_strict_disables_the_commit()
    {
        var over = MoveRuler.Read(new Point2(0, 0), new Point2(8, 0), 6);
        var fine = MoveRuler.Read(new Point2(0, 0), new Point2(2, 0), 6);
        Assert.Equal(new MoveGate(false, "2 models past their allowance."), MoveRuler.Gate([over, fine, over], Enforcement.Strict));
        Assert.Equal(new MoveGate(false, "1 model past its allowance."), MoveRuler.Gate([over], Enforcement.Strict));
    }

    [Fact]
    public void Gate_under_warn_or_silent_shows_the_reason_and_allows_the_commit()
    {
        var over = MoveRuler.Read(new Point2(0, 0), new Point2(8, 0), 6);
        Assert.Equal(new MoveGate(true, "1 model past its allowance."), MoveRuler.Gate([over], Enforcement.Warn));
        Assert.Equal(new MoveGate(true, "1 model past its allowance."), MoveRuler.Gate([over], Enforcement.Silent));
    }

    [Fact]
    public void Gate_counts_a_model_short_of_its_minimum()
    {
        var shortOf = MoveRuler.Read(new Point2(0, 0), new Point2(20, 0), 36, 30);
        var over = MoveRuler.Read(new Point2(0, 0), new Point2(40, 0), 36, 30);
        Assert.Equal(new MoveGate(false, "1 model past its allowance · 2 models short of their minimum."), MoveRuler.Gate([over, shortOf, shortOf], Enforcement.Strict));
    }
}
