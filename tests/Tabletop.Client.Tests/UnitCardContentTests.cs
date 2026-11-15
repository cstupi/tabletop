using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class UnitCardContentTests
{
    static readonly Guid Unit = Guid.CreateVersion7(), Model = Guid.CreateVersion7();

    /// <summary>Built fresh on each call, as UnitSheet.Of builds it on each render: equal values, new lists.</summary>
    static UnitSheetModel Sheet(int left = 2, string chip = "Activated", string weaponRule = "AP(1)", IReadOnlyList<SheetModel>? models = null) =>
        new(Unit, "Team 1", "Squad", 1, 1, "4+", "5+",
            [new SheetChip(ChipKind.Activated, chip)],
            [new SheetWeapon("Rifle", 2, "24\"", "1", "-", null, [new SheetRule(weaponRule, "text", false)])],
            [new SheetRule("Tough(3)", "text", false)],
            models ?? [M(Model, "Trooper", left, 3, false, "t", ["Rifle"])]);

    static SheetModel M(Guid id, string label, int left, int max, bool removed, string kind, string[] loadout, bool hero = false) =>
        new(id, label, left, max, removed, kind, loadout, hero);

    static readonly Guid[] Ids = Enumerable.Range(0, 12).Select(_ => Guid.CreateVersion7()).ToArray();

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

    [Fact]
    public void A_group_holding_a_picked_model_is_marked_and_a_change_of_picks_is_a_change()
    {
        var models = new[] { M(Ids[0], "Warrior", 1, 1, false, "w", ["Rifle"]), M(Ids[1], "Warrior", 1, 1, false, "w", ["Rifle"]),
            M(Ids[2], "Sergeant", 1, 1, false, "s", ["Pistol"]) };
        var none = new UnitCardContent(Sheet(models: models), 1, null);
        Assert.All(none.Groups, g => Assert.False(none.Marked(g)));
        var picked = new UnitCardContent(Sheet(models: models), 1, null, [Ids[1]]);
        Assert.Equal([true, false], picked.Groups.Select(picked.Marked));
        Assert.NotEqual(none, picked);
        Assert.Equal(picked, new UnitCardContent(Sheet(models: models), 1, null, [Ids[1]]));
        Assert.NotEqual(picked, new UnitCardContent(Sheet(models: models), 1, null, [Ids[2]]));
    }

    /// <summary>Eight warriors, one more with a Field Doctor upgrade, and a sergeant with a plasma pistol; two warriors removed.</summary>
    [Fact]
    public void Like_models_group_and_each_group_names_only_what_sets_it_apart()
    {
        string[] warrior = ["Choppa", "Shoota"], doctor = ["Choppa", "Shoota", "Field Doctor"], sergeant = ["Choppa", "Plasma Pistol"];
        var models = Enumerable.Range(0, 8).Select(i => M(Ids[i], "Orc Warrior", i < 2 ? 0 : 1, 1, i < 2, "w", warrior))
            .Append(M(Ids[8], "Orc Warrior", 1, 1, false, "d", doctor))
            .Append(M(Ids[9], "Sergeant", 1, 1, false, "s", sergeant)).ToList();
        var c = new UnitCardContent(Sheet(models: models), 1, null);
        Assert.Equal([("Orc Warrior", "", 6, 8), ("Orc Warrior", "Field Doctor", 1, 1), ("Sergeant", "Plasma Pistol", 1, 1)],
            c.Groups.Select(g => (g.Label, g.Loadout, g.Alive, g.Size)));
        Assert.Equal(Ids[2..8], c.Groups[0].Standing);                     // removed models are counted, not listed
        Assert.Equal(["6/8 Orc Warrior", "1/1 Orc Warrior — Field Doctor", "1/1 Sergeant — Plasma Pistol"], c.Groups.Select(g => g.Line));
        Assert.Equal((8, 10), (c.WoundsLeft, c.WoundsMax));                // one-wound models: the wound total is the alive count
    }

    [Fact]
    public void Tough_groups_total_their_wounds_and_a_joined_hero_has_its_own_marked_group()
    {
        var models = new List<SheetModel>
        {
            M(Ids[0], "Brute", 3, 3, false, "b", ["Pick"]), M(Ids[1], "Brute", 1, 3, false, "b", ["Pick"]),
            M(Ids[2], "Brute", 0, 3, true, "b", ["Pick"]), M(Ids[3], "Brute", 3, 3, false, "b", ["Pick"]),
            M(Ids[4], "Champion", 2, 3, false, "h", ["Great Weapon", "Quality 3+"], hero: true),
        };
        var c = new UnitCardContent(Sheet(models: models), 1, null);
        Assert.Equal([("Brute", false, 3, 4, 7, 12), ("Champion", true, 1, 1, 2, 3)],
            c.Groups.Select(g => (g.Label, g.Hero, g.Alive, g.Size, g.WoundsLeft, g.WoundsMax)));
        Assert.Equal(["3/4 Brute · Wounds 7/12", "1/1 Champion — Great Weapon, Quality 3+ · Wounds 2/3"], c.Groups.Select(g => g.Line));
        Assert.Equal([("3/4 Brute", "Wounds 7/12"), ("1/1 Champion — Great Weapon, Quality 3+", "Wounds 2/3")], c.Groups.Select(g => (g.Title, g.Wounds)));
        Assert.Equal((9, 15), (c.WoundsLeft, c.WoundsMax));
    }

    [Fact]
    public void A_group_click_cycles_through_its_standing_models()
    {
        var models = Enumerable.Range(0, 3).Select(i => M(Ids[i], "Trooper", i == 1 ? 0 : 1, 1, i == 1, "t", ["Rifle"])).ToList();
        var g = new UnitCardContent(Sheet(models: models), 1, null).Groups.Single();
        Assert.Equal(Ids[0], g.Next(null));
        Assert.Equal(Ids[2], g.Next(Ids[0]));
        Assert.Equal(Ids[0], g.Next(Ids[2]));
        Assert.Equal(Ids[0], g.Next(Ids[11]));                              // a model of another group starts at the first
        var gone = new UnitCardContent(Sheet(models: [M(Ids[0], "Trooper", 0, 1, true, "t", [])]), 1, null).Groups.Single();
        Assert.Null(gone.Next(null));
    }

    [Fact]
    public void A_change_in_a_group_is_a_change()
    {
        SheetModel[] Two(string kind) => [M(Ids[0], "Trooper", 1, 1, false, "t", ["Rifle"]), M(Ids[1], "Trooper", 1, 1, false, kind, ["Rifle"])];
        Assert.NotEqual(new UnitCardContent(Sheet(models: Two("t")), 1, null), new UnitCardContent(Sheet(models: Two("u")), 1, null));
    }

    /// <summary>The owner, 2026-11-14: "Add the amount of points to the unit details card so a user can see how much something was worth from
    /// the army list". The count line ends with the unit's points when its army list gave them.</summary>
    [Fact]
    public void The_count_line_ends_with_the_units_points()
    {
        var sheet = new UnitSheetModel(Guid.CreateVersion7(), "Team 1", "Squad", 2, 2, "4+", "5+", [], [], [],
            [new SheetModel(Guid.CreateVersion7(), "Trooper", 1, 1, false, "t", [], false), new SheetModel(Guid.CreateVersion7(), "Trooper", 1, 1, false, "t", [], false)]);
        Assert.Equal("2 models", new UnitCardContent(sheet, 1, null).CountNote);
        Assert.Equal("2 models · 170 pts", new UnitCardContent(sheet with { Points = 170 }, 1, null).CountNote);
    }
}
