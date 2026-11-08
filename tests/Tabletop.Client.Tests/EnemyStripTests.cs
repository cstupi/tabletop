using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class EnemyStripTests
{
    static SheetWeapon W(string name, string range) => new(name, 1, range, "1", "-", null, []);
    static UnitSheetModel Sheet(string name, params SheetWeapon[] weapons) => new(Guid.CreateVersion7(), "Team 1", name, 2, 3, "4+", "5+", [], weapons, [], []);

    [Fact]
    public void Ranges_compare_the_printed_number_with_the_distance()
    {
        var s = EnemyStrip.Of(Sheet("Blue", W("Rifle", "24\""), W("Pistol", "12\""), W("CCW", "-")), Sheet("Red"), 12.0);
        Assert.Equal(("Red", "5+", 2, (double?)12.0), (s.Name, s.Defense, s.Alive, s.Distance));
        Assert.Equal([new RangeCheck("Rifle", "24\"", true), new RangeCheck("Pistol", "12\"", true)], s.Ranges);   // equal is within; melee left out
        Assert.Equal(false, EnemyStrip.Of(Sheet("Blue", W("Pistol", "12\"")), Sheet("Red"), 12.5).Ranges[0].Within);
    }

    [Fact]
    public void Without_a_distance_no_range_is_judged() =>
        Assert.Null(EnemyStrip.Of(Sheet("Blue", W("Rifle", "24\"")), Sheet("Red"), null).Ranges[0].Within);

    [Fact]
    public void Only_an_enemy_hovered_beside_my_unit_shows_a_strip()
    {
        var p = SheetFocusTests.Activating(null); var g = SheetFocusTests.Game; var a = SheetFocusTests.TeamA;
        Guid blue = SheetFocusTests.Blue.Id, blue2 = SheetFocusTests.Blue2.Id, red = SheetFocusTests.Red.Id;
        Assert.True(EnemyStrip.Shows(p, g, a, blue, red));
        Assert.False(EnemyStrip.Shows(p, g, a, blue, blue2));   // my own unit hovered
        Assert.False(EnemyStrip.Shows(p, g, a, red, blue));     // the card shows an enemy
        Assert.False(EnemyStrip.Shows(p, g, null, blue, red));  // a watcher has no units
    }

    [Fact]
    public void The_strip_measures_my_acting_unit_whatever_the_card_shows()
    {
        var g = SheetFocusTests.Game; var a = SheetFocusTests.TeamA;
        Guid blue = SheetFocusTests.Blue.Id, blue2 = SheetFocusTests.Blue2.Id, red = SheetFocusTests.Red.Id;
        Assert.Equal(blue, EnemyStrip.Measured(SheetFocusTests.Activating(blue), g, a, blue2));   // another of mine selected: the shooter's weapons
        Assert.Equal(blue, EnemyStrip.Measured(SheetFocusTests.Activating(blue), g, a, red));
        Assert.Equal(blue2, EnemyStrip.Measured(SheetFocusTests.Activating(null), g, a, blue2));  // no activation: the card's unit
        Assert.Equal(blue2, EnemyStrip.Measured(SheetFocusTests.Activating(red), g, a, blue2));   // another team's activation is not mine to measure
        Assert.Equal(blue2, EnemyStrip.Measured(SheetFocusTests.Activating(blue), g, null, blue2));
    }
}
