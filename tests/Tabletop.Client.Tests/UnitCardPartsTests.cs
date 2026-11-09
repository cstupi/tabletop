using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>The unit card redraws only the parts a change shows in: a move's activated chip is the state row, not the weapons table.</summary>
public class UnitCardPartsTests
{
    static readonly Guid Unit = Guid.CreateVersion7(), Model = Guid.CreateVersion7(), Other = Guid.CreateVersion7();

    static UnitSheetModel Sheet(Guid? unit = null, int left = 2, string chip = "Activated", string weaponRule = "AP(1)", string rule = "Tough(3)", string? quality = "4+") =>
        new(unit ?? Unit, "Team 1", "Squad", 1, 1, quality, "5+",
            [new SheetChip(ChipKind.Activated, chip)],
            [new SheetWeapon("Rifle", 2, "24\"", "1", "-", null, [new SheetRule(weaponRule, "text", false)])],
            [new SheetRule(rule, "text", false)],
            [new SheetModel(Model, "Trooper", left, 3, false, "t", ["Rifle"], false)]);

    static UnitCardContent C(UnitSheetModel sheet, int team = 1, string? note = null, IReadOnlyList<Guid>? picked = null) => new(sheet, team, note, picked);

    [Fact]
    public void A_first_card_or_another_unit_redraws_every_part()
    {
        var all = Enum.GetValues<UnitCardPart>();
        Assert.Equal(all, UnitCardParts.Changed(null, C(Sheet())));
        Assert.Equal(all, UnitCardParts.Changed(C(Sheet()), C(Sheet(Other))));
    }

    [Fact]
    public void A_change_redraws_only_the_parts_that_show_it()
    {
        var before = C(Sheet());
        Assert.Empty(UnitCardParts.Changed(before, C(Sheet())));
        Assert.Equal([UnitCardPart.State], UnitCardParts.Changed(before, C(Sheet(chip: "Activated: Advance"))));   // moved: the activated chip
        Assert.Equal([UnitCardPart.State], UnitCardParts.Changed(before, C(Sheet(quality: "5+"))));
        Assert.Equal([UnitCardPart.Count, UnitCardPart.Models], UnitCardParts.Changed(before, C(Sheet(left: 1))));   // a wound
        Assert.Equal([UnitCardPart.Note], UnitCardParts.Changed(before, C(Sheet(), note: "Shaken: may only Idle.")));
        Assert.Equal([UnitCardPart.Models], UnitCardParts.Changed(before, C(Sheet(), picked: [Model])));
        Assert.Equal([UnitCardPart.Weapons], UnitCardParts.Changed(before, C(Sheet(weaponRule: "AP(2)"))));
        Assert.Equal([UnitCardPart.Rules], UnitCardParts.Changed(before, C(Sheet(rule: "Tough(6)"))));
        Assert.Equal([UnitCardPart.Title, UnitCardPart.Count, UnitCardPart.Models], UnitCardParts.Changed(before, C(Sheet(), team: 2)));   // the team colour
    }
}
