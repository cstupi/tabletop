using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class UnitCardContentTests
{
    static readonly Guid Unit = Guid.CreateVersion7(), Model = Guid.CreateVersion7();

    /// <summary>Built fresh on each call, as UnitSheet.Of builds it on each render: equal values, new lists.</summary>
    static UnitSheetModel Sheet(int left = 2, string chip = "Activated", string weaponRule = "AP(1)") => new(Unit, "Team 1", "Squad", 1, 1, "4+", "5+",
        [new SheetChip(ChipKind.Activated, chip)],
        [new SheetWeapon("Rifle", 2, "24\"", "1", "-", null, [new SheetRule(weaponRule, "text", false)])],
        [new SheetRule("Tough(3)", "text", false)],
        [new SheetModel(Model, "Trooper 1", left, 3, false)]);

    [Fact]
    public void The_same_sheet_built_again_is_the_same_content() =>
        Assert.Equal(new UnitCardContent(Sheet(), 1, null), new UnitCardContent(Sheet(), 1, null));

    [Fact]
    public void A_wound_a_chip_a_weapon_rule_the_team_or_the_note_is_a_change()
    {
        var before = new UnitCardContent(Sheet(), 1, null);
        Assert.NotEqual(before, new UnitCardContent(Sheet(left: 1), 1, null));
        Assert.NotEqual(before, new UnitCardContent(Sheet(chip: "Shaken"), 1, null));
        Assert.NotEqual(before, new UnitCardContent(Sheet(weaponRule: "AP(2)"), 1, null));
        Assert.NotEqual(before, new UnitCardContent(Sheet(), 2, null));
        Assert.NotEqual(before, new UnitCardContent(Sheet(), 1, "Shaken: may only Idle."));
    }
}
