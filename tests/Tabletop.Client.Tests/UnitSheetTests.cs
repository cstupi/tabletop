using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class UnitSheetTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly RuleTexts Catalog = RuleTexts.From([new RuleTextDto("Tough", "model", true, "Must take X wounds to be killed.")]);
    static readonly IReadOnlyDictionary<Guid, int> NoFacts = new Dictionary<Guid, int>();

    static EventApplier Applier(Board board, params EventPayload[] history)
    {
        var a = new EventApplier(board, Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return a;
    }

    static UnitSheetModel Sheet(EventApplier a, Guid unit, IReadOnlyDictionary<Guid, int> maxWounds) =>
        UnitSheet.Of(a.Projection, new Names(Game, a.Projection), unit, Catalog, maxWounds);

    // A squad of three: two riflemen and a plasma gunner, each with a close-combat weapon (no Range stat).
    static Weapon Ranged(string name, string range, params PropertyInstance[] rules) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Range"] = range, ["Attacks"] = "1" }, rules);
    static Weapon Melee() => new(Guid.CreateVersion7(), "CCW", new Dictionary<string, string> { ["Attacks"] = "2" }, []);
    static ModelSlot Slot(int ordinal, params Weapon[] weapons) =>
        new(Guid.CreateVersion7(), ordinal, $"Trooper {ordinal}", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, weapons);
    static readonly Unit Squad = new(Guid.CreateVersion7(), 1, "Squad", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" }, [],
        [Slot(1, Ranged("Rifle", "24\""), Melee()), Slot(2, Ranged("Rifle", "24\""), Melee()), Slot(3, Ranged("Plasma Gun", "24\"", new PropertyInstance("AP", 4)), Melee())]);
    static Board SquadBoard => new(TableStateTests.Sample(), [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(
        new Army(Guid.CreateVersion7(), "Squads", "grimdark-future", "test", [Squad]), Guid.CreateVersion7()))]);

    [Fact]
    public void The_card_reads_the_snapshot_and_the_catalog()
    {
        var s = Sheet(Applier(Samples.Board), Samples.Unit, new Dictionary<Guid, int> { [Samples.Model] = 3 });
        Assert.Equal((Samples.Unit, "Team 1", "Battle Brothers", 1, 1), (s.UnitId, s.Team, s.Name, s.Alive, s.Size));
        Assert.Equal(("3+", "3+"), (s.Quality, s.Defense));
        Assert.Empty(s.Chips);
        // Fearless is the unit's, Tough(3) the slot's. Only Tough has a catalog entry here, so Fearless reads as not automated.
        Assert.Equal(new[] { ("Fearless", (string?)null, true), ("Tough(3)", (string?)"Must take X wounds to be killed.", false) },
            s.Rules.Select(r => (r.Label, r.Text, r.NotAutomated)));
        var rifle = Assert.Single(s.Weapons);
        Assert.Equal(("Heavy Rifle", 1, "24\"", "1", "1"), (rifle.Name, rifle.Count, rifle.Range, rifle.Attacks, rifle.Ap));
        Assert.Empty(rifle.Rules);                                   // AP(1) moved to its column
        Assert.Equal(("AP(1)", true), (rifle.ApRule!.Label, rifle.ApRule.NotAutomated));
        var model = Assert.Single(s.Models);
        Assert.Equal((Samples.Model, "Battle Brother", 3, 3, false), (model.Id, model.Label, model.Left, model.Max, model.Removed));
    }

    [Fact]
    public void Weapons_group_by_name_with_a_count_and_a_melee_weapon_has_no_range()
    {
        var s = Sheet(Applier(SquadBoard), Squad.Id, NoFacts);
        Assert.Equal(new[] { ("Rifle", 2, "24\"", "1"), ("CCW", 3, "-", "2"), ("Plasma Gun", 1, "24\"", "1") },
            s.Weapons.Select(w => (w.Name, w.Count, w.Range, w.Attacks)));
        Assert.Equal(new[] { "-", "-", "4" }, s.Weapons.Select(w => w.Ap));
        Assert.Empty(s.Weapons[2].Rules);
        Assert.Equal(("4+", "5+", 3, 3), (s.Quality, s.Defense, s.Alive, s.Size));
    }

    [Fact]
    public void A_removed_model_is_struck_through_and_its_weapons_leave_the_table()
    {
        var gunner = Squad.Models[2].Id;
        var s = Sheet(Applier(SquadBoard, new WoundsSet(gunner, 1)), Squad.Id, NoFacts);   // on the shelf with its one wound taken
        Assert.Equal((2, 3), (s.Alive, s.Size));
        Assert.Equal(new[] { ("Rifle", 2), ("CCW", 2) }, s.Weapons.Select(w => (w.Name, w.Count)));
        Assert.Equal(new[] { (1, 1, false), (1, 1, false), (0, 1, true) }, s.Models.Select(m => (m.Left, m.Max, m.Removed)));
    }

    [Fact]
    public void Wounds_left_come_from_the_facts_and_a_shelved_model_short_of_its_max_is_not_removed()
    {
        var s = Sheet(Applier(Samples.Board, new WoundsSet(Samples.Model, 1)), Samples.Unit, new Dictionary<Guid, int> { [Samples.Model] = 3 });
        Assert.Equal((2, 3, false), (s.Models[0].Left, s.Models[0].Max, s.Models[0].Removed));
        Assert.Equal(1, s.Alive);
        var unknown = Sheet(Applier(Samples.Board, new WoundsSet(Samples.Model, 1)), Samples.Unit, NoFacts);   // no fact: one wound
        Assert.Equal((0, 1, true), (unknown.Models[0].Left, unknown.Models[0].Max, unknown.Models[0].Removed));
    }

    [Fact]
    public void Chips_show_the_action_conditions_spell_tokens_and_grants()
    {
        var a = Applier(Samples.Board, new ActionRecorded(Samples.Unit, "Advance"),
            new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true),
            new SpellTokensSet(Samples.Model, 2), new GrantAdded(Samples.Unit, Samples.Grant));
        Assert.Equal([new SheetChip(ChipKind.Activated, "Activated: Advance"), new SheetChip(ChipKind.Condition, "Shaken", "shaken"),
            new SheetChip(ChipKind.SpellTokens, "2 spell tokens"), new SheetChip(ChipKind.Grant, "Regeneration from Battle Brothers · 1 use")],
            Sheet(a, Samples.Unit, NoFacts).Chips);
    }

    [Fact]
    public void An_AP_rule_without_a_parameter_stays_in_the_rules()
    {
        var odd = new Unit(Guid.CreateVersion7(), 1, "Odd", new Dictionary<string, string>(), [],
            [Slot(1, Ranged("Lance", "18\"", new PropertyInstance("AP", null), new PropertyInstance("Rending", null)))]);
        var board = new Board(TableStateTests.Sample(), [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(
            new Army(Guid.CreateVersion7(), "Odd", "grimdark-future", "test", [odd]), Guid.CreateVersion7()))]);
        var lance = Assert.Single(Sheet(Applier(board), odd.Id, NoFacts).Weapons);
        Assert.Equal(("-", (SheetRule?)null), (lance.Ap, lance.ApRule));
        Assert.Equal(["AP", "Rending"], lance.Rules.Select(r => r.Label));
    }

    /// <summary>Until the catalog loads, or after its fetch failed, no rule is tagged: an unknown catalog says nothing about automation.</summary>
    [Fact]
    public void Before_the_catalog_loads_no_rule_is_tagged()
    {
        var a = Applier(Samples.Board);
        var s = UnitSheet.Of(a.Projection, new Names(Game, a.Projection), Samples.Unit, RuleTexts.Empty, NoFacts);
        Assert.All(s.Rules.Concat(s.Weapons.SelectMany(w => w.Rules)), r => Assert.False(r.NotAutomated));
    }

    [Fact]
    public void An_unplayed_rule_shows_its_text_tagged_not_automated()
    {
        var catalog = RuleTexts.From([new RuleTextDto("Tough", "model", true, "Must take X wounds to be killed."),
            new RuleTextDto("Fearless", "model", false, "Gets +1 to morale tests.", Played: false)]);
        var a = Applier(Samples.Board);
        var s = UnitSheet.Of(a.Projection, new Names(Game, a.Projection), Samples.Unit, catalog, new Dictionary<Guid, int> { [Samples.Model] = 3 });
        Assert.Contains(("Fearless", (string?)"Gets +1 to morale tests.", true), s.Rules.Select(r => (r.Label, r.Text, r.NotAutomated)));
    }
}

public class UnitSheetLoadoutTests
{
    static readonly GameInfo Game = Samples.Game;
    static Weapon W(string name) => new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Range"] = "12\"", ["Attacks"] = "1" }, []);
    static ModelSlot Slot(int ordinal, string name, IReadOnlyDictionary<string, string>? stats, PropertyInstance[] rules, params Weapon[] weapons) =>
        new(Guid.CreateVersion7(), ordinal, name, stats ?? new Dictionary<string, string>(), rules, Base.Circle(1), 1.5, null, weapons);

    // As Army Forge imports: numbered slot names, an upgraded model, a joined hero with its own Quality and Defense.
    static readonly Unit Mixed = new(Guid.CreateVersion7(), 1, "Specialists", new Dictionary<string, string> { ["Quality"] = "5+", ["Defense"] = "5+" }, [],
    [
        Slot(1, "Specialists 1", null, [], W("CCW"), W("Railgun"), W("Bomb Drone")),
        Slot(2, "Specialists 2", null, [], W("CCW"), W("Railgun")),
        Slot(3, "Specialists 3.1", null, [], W("CCW"), W("Railgun")),
        Slot(4, "Orc Leader 1", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" },
            [new PropertyInstance("Hero", null), new PropertyInstance("Tough", 3)], W("CCW"), W("Rocket Launcher"), W("Rocket Launcher")),
    ]);

    static UnitSheetModel Sheet()
    {
        var board = new Board(TableStateTests.Sample(), [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(
            new Army(Guid.CreateVersion7(), "Mixed", "grimdark-future", "test", [Mixed]), Guid.CreateVersion7()))]);
        var a = new EventApplier(board, Game);
        return UnitSheet.Of(a.Projection, new Names(Game, a.Projection), Mixed.Id, RuleTexts.Empty, new Dictionary<Guid, int> { [Mixed.Models[3].Id] = 3 });
    }

    [Fact]
    public void A_model_carries_its_name_without_the_number_and_its_loadout()
    {
        var m = Sheet().Models;
        Assert.Equal(["Specialists", "Specialists", "Specialists", "Orc Leader"], m.Select(x => x.Label));
        Assert.Equal(["CCW", "Railgun", "Bomb Drone"], m[0].Loadout);
        // Hero marks the model rather than listing; a slot stat shows only where it differs from the card's badge.
        Assert.Equal(["CCW", "Rocket Launcher ×2", "Tough(3)", "Quality 4+"], m[3].Loadout);
        Assert.Equal([false, false, false, true], m.Select(x => x.Hero));
    }

    [Fact]
    public void Models_alike_in_name_weapons_rules_and_stats_share_a_kind()
    {
        var m = Sheet().Models;
        Assert.Equal(m[1].Kind, m[2].Kind);
        Assert.NotEqual(m[0].Kind, m[1].Kind);
        Assert.NotEqual(m[1].Kind, m[3].Kind);
    }
}
