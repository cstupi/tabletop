# Task 10: Unit sheet

**Wave:** 2 · **Depends on:** 1 · **Model:** sonnet

**Files:**
- Create: `client/src/Core/UnitSheet.cs`
- Create: `client/src/Core/SheetFocus.cs`
- Test: `tests/Tabletop.Client.Tests/UnitSheetTests.cs`, `tests/Tabletop.Client.Tests/SheetFocusTests.cs`

**Interfaces:**
- Consumes (Task 1): `RuleTexts.From(IReadOnlyList<RuleTextDto>)`, `RuleTexts.TextOf(string)`, `RuleTexts.IsAutomated(string)`,
  `RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText)`. Existing: `Projection`, `UnitInPlay`,
  `ModelInPlay`, `Names`, `Tabletop.Content` (`Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`).
- Consumes (Task 12, at the call site only): `Session.Rules`, `Session.MaxWounds`.
- Produces (README *Fixed interfaces → Client core*):

```csharp
public sealed record SheetRule(string Label, string? Text, bool Automated);
public sealed record SheetWeapon(string Name, int Count, string Range, string Attacks, IReadOnlyList<SheetRule> Rules);
public sealed record SheetModel(Guid Id, string Label, int Left, int Max, bool Removed);
public sealed record UnitSheetModel(Guid UnitId, string Team, string Name, int Alive, int Size, string? Quality, string? Defense,
    IReadOnlyList<string> Chips, IReadOnlyList<SheetWeapon> Weapons, IReadOnlyList<SheetRule> Rules, IReadOnlyList<SheetModel> Models);
public static class UnitSheet { public static UnitSheetModel Of(Projection p, Names n, Guid unitId, RuleTexts rules, IReadOnlyDictionary<Guid, int> maxWounds); }
public sealed class SheetFocus { public Guid? UnitId { get; } public void OnActivationStarted(Guid unitId); public void OnClicked(Guid unitId);
    public void OnCleared(); public void OnActivationEnded(); }
```

`OnActivationEnded` serves the spec's "with no activation running and nothing selected, the sheet is hidden": a
running activation and a finished one differ only by it. Task 15 calls `OnActivationStarted` when
`HeaderView.ActionFrame(p)` (Task 9) changes to a frame and `OnActivationEnded` when it changes to none.

Spec: *Stat sheet → Which unit, Stats tab*; *Model facts*; *Rule text*.

What the sheet reads, settled here:

| Row | Source |
| --- | --- |
| Team | the team of the participant owning the unit's first model, `Names.Team` |
| Quality, Defense | the unit's `Stats["Quality"]`/`Stats["Defense"]`, else its first slot's |
| Chips | `Activated: {actions}`; each unit condition as written; `{n} spell token(s)` summed over models; `{entry} from {caster} · {k} use(s)` per grant |
| Weapons | over models not removed, grouped by `Weapon.Name` in slot-ordinal order, `Count` the instances; `Range` the stat or `-` (a melee weapon has none); `Attacks` the stat or `-`; rules from the first instance |
| Rules | the unit's properties, then each not-removed slot's, distinct by label |
| Rule label | `Name(X)` when the property has a parameter, else `Name`; `Text` and `Automated` look up the bare `Name` |
| Models | by slot ordinal; `Max` from `maxWounds`, absent = 1; `Left = max(0, Max − Wounds)`; `Removed` = on the shelf with wounds at `Max` |

A model on the shelf with no wounds is waiting to deploy, not removed. The sheet names no rule: wounds come from
`ModelFacts`, and every rule label comes from the snapshot.

- [ ] **Step 1: Write the failing sheet tests**

Create `tests/Tabletop.Client.Tests/UnitSheetTests.cs`:

```csharp
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
    public void The_stats_tab_reads_the_snapshot_and_the_catalog()
    {
        var s = Sheet(Applier(Samples.Board), Samples.Unit, new Dictionary<Guid, int> { [Samples.Model] = 3 });
        Assert.Equal((Samples.Unit, "Team 1", "Battle Brothers", 1, 1), (s.UnitId, s.Team, s.Name, s.Alive, s.Size));
        Assert.Equal(("3+", "3+"), (s.Quality, s.Defense));
        Assert.Empty(s.Chips);
        // Fearless is the unit's, Tough(3) the slot's. Only Tough has a catalog entry here, so Fearless reads as not automated.
        Assert.Equal(new[] { ("Fearless", (string?)null, false), ("Tough(3)", (string?)"Must take X wounds to be killed.", true) },
            s.Rules.Select(r => (r.Label, r.Text, r.Automated)));
        var rifle = Assert.Single(s.Weapons);
        Assert.Equal(("Heavy Rifle", 1, "24\"", "1"), (rifle.Name, rifle.Count, rifle.Range, rifle.Attacks));
        Assert.Equal(new[] { ("AP(1)", false) }, rifle.Rules.Select(r => (r.Label, r.Automated)));
        var model = Assert.Single(s.Models);
        Assert.Equal((Samples.Model, "Battle Brother", 3, 3, false), (model.Id, model.Label, model.Left, model.Max, model.Removed));
    }

    [Fact]
    public void Weapons_group_by_name_with_a_count_and_a_melee_weapon_has_no_range()
    {
        var s = Sheet(Applier(SquadBoard), Squad.Id, NoFacts);
        Assert.Equal(new[] { ("Rifle", 2, "24\"", "1"), ("CCW", 3, "-", "2"), ("Plasma Gun", 1, "24\"", "1") },
            s.Weapons.Select(w => (w.Name, w.Count, w.Range, w.Attacks)));
        Assert.Equal(["AP(4)"], s.Weapons[2].Rules.Select(r => r.Label));
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
        Assert.Equal(["Activated: Advance", "shaken", "2 spell tokens", "Regeneration from Battle Brothers · 1 use"],
            Sheet(a, Samples.Unit, NoFacts).Chips);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter UnitSheetTests`
Expected: compile errors: `UnitSheet`, `UnitSheetModel`, `SheetRule`, `SheetWeapon`, `SheetModel` do not exist.

- [ ] **Step 3: Write the sheet**

Create `client/src/Core/UnitSheet.cs`:

```csharp
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A rule as the sheet shows it: its label with the parameter, its catalog text, and whether the catalog has it at all.</summary>
public sealed record SheetRule(string Label, string? Text, bool Automated);
public sealed record SheetWeapon(string Name, int Count, string Range, string Attacks, IReadOnlyList<SheetRule> Rules);
public sealed record SheetModel(Guid Id, string Label, int Left, int Max, bool Removed);
public sealed record UnitSheetModel(Guid UnitId, string Team, string Name, int Alive, int Size, string? Quality, string? Defense,
    IReadOnlyList<string> Chips, IReadOnlyList<SheetWeapon> Weapons, IReadOnlyList<SheetRule> Rules, IReadOnlyList<SheetModel> Models);

/// <summary>
/// The Stats tab (in-game UX spec → Stat sheet) from the army snapshot, the projection, the pinned catalog and the server's model
/// facts. Names no rule: a label is the snapshot's, a model's wounds are the ModelFacts value, and a rule with no catalog entry is
/// not automated.
/// </summary>
public static class UnitSheet
{
    /// <summary>Throws KeyNotFoundException for a unit the projection does not hold; the caller shows a sheet only for a unit it can see.</summary>
    public static UnitSheetModel Of(Projection p, Names n, Guid unitId, RuleTexts rules, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var unit = p.Units[unitId];
        var models = unit.Definition.Models.OrderBy(s => s.Ordinal).Select(slot => Model(p.Models[slot.Id], maxWounds)).ToList();
        var standing = unit.Definition.Models.Where(slot => !models.Single(m => m.Id == slot.Id).Removed).OrderBy(s => s.Ordinal).ToList();
        var first = unit.Definition.Models.OrderBy(s => s.Ordinal).FirstOrDefault();
        var team = first is not null && n.TeamOfModel(first.Id) is { } t ? n.Team(t) : "";
        return new UnitSheetModel(unitId, team, n.Unit(unitId), models.Count(m => !m.Removed), unit.Definition.Models.Count,
            Stat(unit.Definition, first, "Quality"), Stat(unit.Definition, first, "Defense"),
            Chips(p, n, unit), Weapons(standing, rules), Rules(unit.Definition, standing, rules), models);
    }

    static SheetModel Model(ModelInPlay m, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var max = maxWounds.GetValueOrDefault(m.Id, 1);
        return new SheetModel(m.Id, m.Slot.Name, Math.Max(0, max - m.Wounds), max, m.Location is OnShelf && m.Wounds >= max);
    }

    static string? Stat(Unit unit, ModelSlot? first, string name) =>
        unit.Stats.GetValueOrDefault(name) ?? first?.Stats.GetValueOrDefault(name);

    static IReadOnlyList<string> Chips(Projection p, Names n, UnitInPlay unit)
    {
        var chips = new List<string>();
        if (unit.Actions.Count > 0) chips.Add($"Activated: {string.Join(", ", unit.Actions.Order(StringComparer.Ordinal))}");
        chips.AddRange(unit.Conditions.Order(StringComparer.Ordinal));
        var tokens = p.Models.Values.Where(m => m.UnitId == unit.Id).Sum(m => m.SpellTokens);
        if (tokens > 0) chips.Add($"{tokens} spell token{(tokens == 1 ? "" : "s")}");
        chips.AddRange(unit.Grants.Select(g => $"{g.EntryName} from {n.Unit(g.CasterUnitId)} · {g.RemainingUses} use{(g.RemainingUses == 1 ? "" : "s")}"));
        return chips;
    }

    static IReadOnlyList<SheetWeapon> Weapons(IReadOnlyList<ModelSlot> standing, RuleTexts rules) =>
        standing.SelectMany(s => s.Weapons).GroupBy(w => w.Name, StringComparer.Ordinal)
            .Select(g => new SheetWeapon(g.Key, g.Count(), g.First().Stats.GetValueOrDefault("Range") ?? "-",
                g.First().Stats.GetValueOrDefault("Attacks") ?? "-", g.First().Properties.Select(x => Rule(x, rules)).ToList()))
            .ToList();

    static IReadOnlyList<SheetRule> Rules(Unit unit, IReadOnlyList<ModelSlot> standing, RuleTexts rules) =>
        unit.Properties.Concat(standing.SelectMany(s => s.Properties)).Select(x => Rule(x, rules)).DistinctBy(r => r.Label).ToList();

    static SheetRule Rule(PropertyInstance x, RuleTexts rules) =>
        new(x.Parameter is { } v ? $"{x.Name}({v})" : x.Name, rules.TextOf(x.Name), rules.IsAutomated(x.Name));
}
```

`GroupBy` keeps first-seen order, which over `standing` in slot-ordinal order is the order the tests read.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter UnitSheetTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Write the failing focus tests**

Create `tests/Tabletop.Client.Tests/SheetFocusTests.cs`:

```csharp
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
```

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter SheetFocusTests`
Expected: compile error: `SheetFocus` does not exist.

- [ ] **Step 7: Write the focus**

Create `client/src/Core/SheetFocus.cs`:

```csharp
namespace Tabletop.Client.Core;

/// <summary>
/// Which unit the stat sheet shows (in-game UX spec → Stat sheet, Which unit): the activating unit when an activation begins; a
/// clicked unit until the next activation begins; with no activation running and nothing selected, none, and the sheet hides.
/// </summary>
public sealed class SheetFocus
{
    Guid? _activating, _clicked;

    public Guid? UnitId => _clicked ?? _activating;

    public void OnActivationStarted(Guid unitId) { _activating = unitId; _clicked = null; }
    public void OnActivationEnded() => _activating = null;
    public void OnClicked(Guid unitId) => _clicked = unitId;
    public void OnCleared() => _clicked = null;
}
```

- [ ] **Step 8: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: every test passes, the build succeeds, `check-client` prints nothing.

- [ ] **Step 9: Commit**

```bash
git add client/src/Core/UnitSheet.cs client/src/Core/SheetFocus.cs \
  tests/Tabletop.Client.Tests/UnitSheetTests.cs tests/Tabletop.Client.Tests/SheetFocusTests.cs
git commit -m "Build the stat sheet's rows and track which unit it shows" \
  -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
