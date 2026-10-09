using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class SheetFocusTests
{
    static readonly Guid A = Guid.Parse("0198b7ac-aaaa-7000-8000-00000000000a"), B = Guid.Parse("0198b7ac-bbbb-7000-8000-00000000000b"),
        C = Guid.Parse("0198b7ac-cccc-7000-8000-00000000000c");

    [Fact]
    public void Nothing_running_and_nothing_clicked_hides_the_sheet()
    {
        Assert.Null(new SheetFocus().UnitId);
    }

    [Fact]
    public void An_activation_shows_its_unit_and_a_click_shows_another_until_the_next_activation()
    {
        var f = new SheetFocus();
        f.OnActivationStarted(A);
        Assert.Equal(A, f.UnitId);
        f.OnClicked(B);
        Assert.Equal(B, f.UnitId);
        f.OnActivationEnded();
        Assert.Equal(B, f.UnitId);            // a click stays until the next activation begins
        f.OnActivationStarted(C);
        Assert.Equal(C, f.UnitId);
    }

    [Fact]
    public void Clearing_the_selection_falls_back_to_the_running_activation_or_hides()
    {
        var f = new SheetFocus();
        f.OnActivationStarted(A);
        f.OnClicked(B);
        f.OnCleared();
        Assert.Equal(A, f.UnitId);
        f.OnActivationEnded();
        Assert.Null(f.UnitId);
        f.OnClicked(B);
        f.OnCleared();
        Assert.Null(f.UnitId);
    }
}
