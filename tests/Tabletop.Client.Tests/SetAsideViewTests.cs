using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>The owner, 2026-11-21: "During placement there needs to be more in the unit list to show what has ambush so users know what they can
/// ambush." The chip names the rule the server sends (ModelFacts.SetAsideRules), never one the client knows.</summary>
public class SetAsideViewTests
{
    static readonly Dictionary<Guid, SetAsideRule> Rules = new() { [TableUx.Reserve.Id] = new("Ambush", "May be set aside before deployment. At the start of any round after the first, may be deployed anywhere over 9\" away from enemy units.") };
    static readonly HashSet<Guid> May = [TableUx.Reserve.Id];
    static Subject Unit(Guid id) => new(SubjectKind.Unit, id);

    [Fact]
    public void A_unit_the_server_names_gets_a_chip_with_the_rule_s_name_its_text_and_how_to_set_it_aside()
    {
        var chip = SetAsideView.Of(TableUx.After(TableUx.Deploy), TableUx.Reserve.Id, Rules)!;
        Assert.Equal("Ambush", chip.Name);
        Assert.Equal("May be set aside before deployment.\n" + SetAsideView.How, chip.Tip);
        Assert.Null(SetAsideView.Of(TableUx.After(TableUx.Deploy), TableUx.Squad.Id, Rules));                 // not named: no chip
        Assert.Null(SetAsideView.Of(TableUx.After(TableUx.Deploy), TableUx.Reserve.Id, new Dictionary<Guid, SetAsideRule>()));   // round one: the server names none
    }

    [Fact]
    public void A_held_unit_s_chip_says_how_to_return_it()
    {
        var p = TableUx.After(TableUx.Deploy, new ConditionSet(Unit(TableUx.Reserve.Id), MenuModel.SetAside, true));
        Assert.Equal("May be set aside before deployment.\n" + SetAsideView.HowBack, SetAsideView.Of(p, TableUx.Reserve.Id, Rules)!.Tip);
        Assert.Equal(SetAsideView.How, SetAsideView.Of(TableUx.After(TableUx.Deploy), TableUx.Reserve.Id,
            new Dictionary<Guid, SetAsideRule> { [TableUx.Reserve.Id] = new("Ambush", "") })!.Tip);                   // no text: the how alone
    }

    [Fact]
    public void The_deploy_pick_s_set_aside_sends_what_the_roster_menu_sends()
    {
        var p = TableUx.After(TableUx.Deploy);
        var menu = MenuModel.ForRosterUnit(TableUx.Game, p, TableUx.Reserve.Id, TableUx.T1, maySetAside: May).Single(i => i.Label == "Set aside").Build(null);
        Assert.Equal(menu, SetAsideView.SetAside(p, TableUx.Reserve.Id, May));
        Assert.Equal(new SetCondition(Unit(TableUx.Reserve.Id), MenuModel.SetAside, true), SetAsideView.SetAside(p, TableUx.Reserve.Id, May));
        Assert.Null(SetAsideView.SetAside(p, TableUx.Squad.Id, May));                                              // not named
        var held = TableUx.After(TableUx.Deploy, new ConditionSet(Unit(TableUx.Reserve.Id), MenuModel.SetAside, true));
        Assert.Null(SetAsideView.SetAside(held, TableUx.Reserve.Id, May));                                         // already held
    }

    [Fact]
    public void The_roster_row_carries_the_chip_and_compares_by_it()
    {
        var p = TableUx.After(TableUx.Deploy);
        var rows = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T1, new Dictionary<Guid, int>(), setAsideRules: Rules)[0].Rows;
        Assert.Equal("Ambush", rows.Single(r => r.UnitId == TableUx.Reserve.Id).SetAside!.Name);
        Assert.Null(rows.Single(r => r.UnitId == TableUx.Squad.Id).SetAside);
        var plain = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T1, new Dictionary<Guid, int>())[0].Rows.Single(r => r.UnitId == TableUx.Reserve.Id);
        Assert.NotEqual(plain, rows.Single(r => r.UnitId == TableUx.Reserve.Id));                                 // the roster redraws the row when the chip comes or goes
    }
}

/// <summary>The owner, 2026-11-21: "I have no idea what the stats or info of the unit I am placing is anywhere." The card shows a unit not yet on
/// the table: the one being placed, a deploy pick's tile under the pointer, an off-table roster row clicked.</summary>
public class SheetFocusDeployTests
{
    static readonly Projection P = SheetFocusTests.Activating(null);
    static readonly GameInfo G = SheetFocusTests.Game;
    static readonly Guid A = SheetFocusTests.TeamA;

    [Fact]
    public void A_deploy_pick_tile_under_the_pointer_takes_the_card_then_the_unit_being_placed_then_the_selection()
    {
        var (blue, blue2, red) = (SheetFocusTests.Blue.Id, SheetFocusTests.Blue2.Id, SheetFocusTests.Red.Id);
        Assert.Equal(blue2, SheetFocus.Of(P, G, A, red, hovered: blue2, placing: blue));
        Assert.Equal(blue, SheetFocus.Of(P, G, A, red, placing: blue));
        Assert.Equal(blue, SheetFocus.Of(P, G, A, null, placing: blue));
        Assert.Equal(red, SheetFocus.Of(P, G, A, red));
        Assert.Equal(red, SheetFocus.Of(P, G, A, red, hovered: Guid.NewGuid()));                        // a hovered id that is no unit is ignored
    }

    [Fact]
    public void The_unit_picked_holds_the_card_until_a_new_selection_and_a_new_pick_takes_it_again()
    {
        var (blue, blue2, red) = (SheetFocusTests.Blue.Id, SheetFocusTests.Blue2.Id, SheetFocusTests.Red.Id);
        var f = new PlacingFocus();
        Assert.Equal(blue, f.Placing(blue, selected: red));        // picked while Red was selected: the picked unit shows
        Assert.Equal(blue, f.Placing(blue, selected: red));
        Assert.Null(f.Placing(blue, selected: blue2));             // a roster row clicked while placing: the click shows
        Assert.Equal(blue, f.Placing(blue, selected: null));       // the selection cleared: the unit being placed again
        Assert.Equal(blue2, f.Placing(blue2, selected: null));     // the next pick
        Assert.Null(f.Placing(null, selected: null));              // placed
    }
}

public class SetAsideDrawnKeyTests
{
    [Fact]
    public void The_key_changes_with_the_units_and_their_rules_and_not_with_order_or_new_objects()
    {
        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7();
        var rules = new Dictionary<Guid, SetAsideRule> { [a] = new("Ambush", "x"), [b] = new("Scout", "y") };
        Assert.Equal(SetAsideView.DrawnKey(new HashSet<Guid> { a, b }, rules), SetAsideView.DrawnKey(new HashSet<Guid> { b, a }, new Dictionary<Guid, SetAsideRule>(rules)));
        Assert.NotEqual(SetAsideView.DrawnKey(new HashSet<Guid> { a }, rules), SetAsideView.DrawnKey(new HashSet<Guid> { a, b }, rules));
        Assert.NotEqual(SetAsideView.DrawnKey(new HashSet<Guid> { a }, new Dictionary<Guid, SetAsideRule>()), SetAsideView.DrawnKey(new HashSet<Guid> { a }, rules));   // the rules arrive later
        Assert.Equal("", SetAsideView.DrawnKey(new HashSet<Guid>(), new Dictionary<Guid, SetAsideRule>()));                                                         // play: nothing, so no redraw
    }
}
