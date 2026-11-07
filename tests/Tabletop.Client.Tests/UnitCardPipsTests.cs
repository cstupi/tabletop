using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class UnitCardPipsTests
{
    static SheetModel M(string label, int left, int max, bool removed, string kind, params string[] loadout) =>
        new(Guid.CreateVersion7(), label, left, max, removed, kind, loadout, false);

    static UnitCardContent Card(params SheetModel[] models) =>
        new(new UnitSheetModel(Guid.CreateVersion7(), "Team 1", "Warriors", models.Count(m => !m.Removed), models.Length, "4+", "4+",
            [new SheetChip(ChipKind.Activated, "Activated: Advance"), new SheetChip(ChipKind.Condition, "Shaken", "shaken")], [], [], models), 1, null);

    [Fact]
    public void Pips_per_model_and_a_note_naming_the_one_tough_model_s_wounds()
    {
        var c = Card(M("Warrior", 1, 1, false, "w", "Rifle"), M("Warrior", 1, 1, false, "w", "Rifle"), M("Warrior", 0, 1, true, "w", "Rifle"),
            M("Sergeant", 2, 3, false, "s", "Plasma Pistol"));
        Assert.Equal("●●○●", c.Pips);
        Assert.Equal("3 models · 2 wounds left on Sergeant", c.CountNote);
        var sgt = c.Groups[1];
        Assert.Equal(("●", "Sergeant · Plasma Pistol", "●●○", "2/3"), (sgt.Pips, sgt.Name, sgt.WoundPips, sgt.WoundCount));
        Assert.Equal(("●●○", "Warrior", (string?)null), (c.Groups[0].Pips, c.Groups[0].Name, c.Groups[0].WoundPips));
    }

    [Fact]
    public void The_note_is_the_count_alone_without_tough_models_and_sums_several()
    {
        Assert.Equal("1 model", Card(M("Warrior", 1, 1, false, "w")).CountNote);
        Assert.Equal("2 models · 5 wounds left", Card(M("Ogre", 2, 3, false, "o"), M("Ogre", 3, 3, false, "o")).CountNote);
    }

    [Fact]
    public void Past_twelve_pips_draw_a_count()
    {
        var c = Card(Enumerable.Range(0, 13).Select(i => M("Gaunt", 1, 1, i == 0, "g")).ToArray());
        Assert.Equal("12/13", c.Pips);
        Assert.Equal("12/13", c.Groups[0].Pips);
    }

    [Fact]
    public void The_activated_chip_reads_its_actions_with_the_activated_icon_and_a_condition_its_own()
    {
        var c = Card(M("Warrior", 1, 1, false, "w"));
        Assert.Equal(("Advance", "activated"), (UnitCardContent.ChipText(c.Sheet.Chips[0]), UnitCardContent.ChipIcon(c.Sheet.Chips[0])));
        Assert.Equal(("Shaken", "shaken"), (UnitCardContent.ChipText(c.Sheet.Chips[1]), UnitCardContent.ChipIcon(c.Sheet.Chips[1])));
        Assert.Null(UnitCardContent.ChipIcon(new SheetChip(ChipKind.Grant, "Hex from Wizard · 1 use")));
    }
}
