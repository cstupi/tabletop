# Unit card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The lower-left unit card (selected unit, else my team's activating unit) with badges, chips, wound pips, an AP
column and a hovered-enemy strip, replacing `StatSheet`; its *This round* tab (`RoundHistory`) goes to the combat log's
*This unit* filter, which follows the card's unit.

**Architecture:** Godot-free and test-driven: `UnitSheet` (AP column, typed chips), `SheetFocus` (a pure function of
projection, selection and my team), `EnemyStrip` (new). `Hud/UnitCard` renders them; its colours sit in
`Hud/CardColours` for plan 04 to replace. `GameScreen.Play` swaps it in and feeds its unit to `CombatLogPanel`.

**Tech Stack:** `net10.0`, C# 12, xUnit, Godot 4.7.2 Mono (C#).

**Spec:** [`docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`](../../specs/2026-10-10-client-ux-and-themes-design.md),
*Unit card*, and the rows for `SheetFocus`, `UnitSheet`, `Hud/UnitCard` and `RoundHistory` under *Client structure*.

**Requires landed:** [`01-collision.md`](01-collision.md), [`01a-collision-server.md`](01a-collision-server.md),
[`01b-collision-client.md`](01b-collision-client.md), [`02-combat-log.md`](02-combat-log.md) and
[`02a-combat-log-client.md`](02a-combat-log-client.md), on `main` after `7c4ec5d`. Plan 02a Task 5 adds
`LogDrawer.CombatLog`, `CombatLogPanel.FocusUnit`, `StatSheet.UnitId` and `StatSheet.FocusChanged`; this plan keeps the
last two names on `UnitCard`.

## Global Constraints

- The client names no rule; `./scripts/check-client` prints nothing. Max wounds come from `ModelFacts`
  (`Session.MaxWounds`), rule texts from `Session.Rules`.
- **The AP column.** `check-client` greps for rules-engine types only, so reading a property named `AP` passes. The
  principle holds: `AP` is read as a column label of the printed army list (Range, Attacks, AP, Special), as `UnitSheet`
  already reads `Quality`, `Defense`, `Range` and `Attacks`; the card moves the parameter between columns and computes
  nothing from it. The label lives once, in `UnitSheet.ApLabel`. No server or catalog change.
- `client/src/Core/` is Godot-free and compiled into `tests/Tabletop.Client.Tests`; every type with logic is test-driven.
- Hud code holds no colour literal outside `Hud/CardColours` and `Table/Palette`; chips show condition names only (icons
  are plan 04's).
- Every file stays under 500 lines. `.tscn` files are not touched; nodes are built in C#.
- Commit after every task, imperative one-line subject, message ending with:
```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW
```

## Review Focus

1. An `AP` with no parameter vanishes from the weapon (Task 1, `An_AP_rule_without_a_parameter_stays_in_the_rules`).
2. My activation starting steals the card from a selected unit (Task 2, `An_activation_never_changes_a_selection`).
3. An enemy activation, or any activation for a watcher, takes the card (Task 2, `An_enemy_activation_never_takes_the_card`).
4. A selected terrain piece or marker hides the card instead of falling back (Task 2, `A_selected_piece_that_is_not_a_unit_falls_back`).
5. The strip shows for my own unit, lists melee weapons, or calls a range equal to the distance beyond (Task 3,
   `Only_an_enemy_hovered_beside_my_unit_shows_a_strip`, `Ranges_compare_the_printed_number_with_the_distance`).

## Task 1: UnitSheet — AP column and typed chips

**Files:**
- Modify: `client/src/Core/UnitSheet.cs`, `client/src/Hud/StatSheet.cs` (one line, so the client builds until Task 4)
- Test: `tests/Tabletop.Client.Tests/UnitSheetTests.cs`

**Interfaces:**
- Consumes: `RuleTexts.TextOf`, `RuleTexts.NotAutomated`; `PropertyInstance(string Name, int? Parameter)`.
- Produces:
```csharp
public enum ChipKind { Activated, Condition, SpellTokens, Grant }
public sealed record SheetChip(ChipKind Kind, string Text, string? Condition = null);
public sealed record SheetWeapon(string Name, int Count, string Range, string Attacks, string Ap, SheetRule? ApRule, IReadOnlyList<SheetRule> Rules);
public sealed record UnitSheetModel(Guid UnitId, string Team, string Name, int Alive, int Size, string? Quality, string? Defense,
    IReadOnlyList<SheetChip> Chips, IReadOnlyList<SheetWeapon> Weapons, IReadOnlyList<SheetRule> Rules, IReadOnlyList<SheetModel> Models);
public static class UnitSheet { public const string ApLabel = "AP"; /* Of unchanged */ }
```
`Ap` is the first `AP` property's parameter as written, `-` when the weapon has none with a parameter; that property
leaves `Rules` and becomes `ApRule` (its catalog text for the column's hover). A condition chip reads the name with its
first letter capitalised; `Condition` keeps the raw name, the key plan 04 looks an icon up by. Wound pips need no change: `SheetModel.Left`/`Max` already come from `ModelFacts`.
- [ ] **Step 1: Update the tests.** In `UnitSheetTests.cs`, in `The_stats_tab_reads_the_snapshot_and_the_catalog`,
  replace the two `rifle` assertions with:
```csharp
        Assert.Equal(("Heavy Rifle", 1, "24\"", "1", "1"), (rifle.Name, rifle.Count, rifle.Range, rifle.Attacks, rifle.Ap));
        Assert.Empty(rifle.Rules);                                   // AP(1) moved to its column
        Assert.Equal(("AP(1)", true), (rifle.ApRule!.Label, rifle.ApRule.NotAutomated));
```
In `Weapons_group_by_name_with_a_count_and_a_melee_weapon_has_no_range`, replace the `AP(4)` assertion with:
```csharp
        Assert.Equal(new[] { "-", "-", "4" }, s.Weapons.Select(w => w.Ap));
        Assert.Empty(s.Weapons[2].Rules);
```
Replace the body of `Chips_show_the_action_conditions_spell_tokens_and_grants`'s assertion with:
```csharp
        Assert.Equal([new SheetChip(ChipKind.Activated, "Activated: Advance"), new SheetChip(ChipKind.Condition, "Shaken", "shaken"),
            new SheetChip(ChipKind.SpellTokens, "2 spell tokens"), new SheetChip(ChipKind.Grant, "Regeneration from Battle Brothers · 1 use")],
            Sheet(a, Samples.Unit, NoFacts).Chips);
```
Add:
```csharp
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
```
- [ ] **Step 2: Run them to see them fail.**
  `dotnet test tests/Tabletop.Client.Tests --filter UnitSheetTests` — expected: build errors (`SheetWeapon.Ap`,
  `SheetChip`, `ChipKind` do not exist).
- [ ] **Step 3: Implement.** In `UnitSheet.cs`, add the records above (replacing `SheetWeapon` and `UnitSheetModel`),
  update the class summary to "The unit card (client UX spec → Unit card)", and replace `Chips` and `Weapons`:
```csharp
    /// <summary>The printed army list's AP column label: the card moves this property's parameter into its own column and computes nothing from it.</summary>
    public const string ApLabel = "AP";

    static IReadOnlyList<SheetChip> Chips(Projection p, Names n, UnitInPlay unit)
    {
        var chips = new List<SheetChip>();
        if (unit.Actions.Count > 0) chips.Add(new(ChipKind.Activated, $"Activated: {string.Join(", ", unit.Actions.Order(StringComparer.Ordinal))}"));
        chips.AddRange(unit.Conditions.Order(StringComparer.Ordinal).Select(c => new SheetChip(ChipKind.Condition, Capitalised(c), c)));
        var tokens = p.Models.Values.Where(m => m.UnitId == unit.Id).Sum(m => m.SpellTokens);
        if (tokens > 0) chips.Add(new(ChipKind.SpellTokens, $"{tokens} spell token{(tokens == 1 ? "" : "s")}"));
        chips.AddRange(unit.Grants.Select(g => new SheetChip(ChipKind.Grant,
            $"{g.EntryName} from {n.Unit(g.CasterUnitId)} · {g.RemainingUses} use{(g.RemainingUses == 1 ? "" : "s")}")));
        return chips;
    }

    static string Capitalised(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static IReadOnlyList<SheetWeapon> Weapons(IReadOnlyList<ModelSlot> standing, RuleTexts rules) =>
        standing.SelectMany(s => s.Weapons).GroupBy(w => w.Name, StringComparer.Ordinal).Select(g => WeaponRow(g.Key, g.Count(), g.First(), rules)).ToList();

    static SheetWeapon WeaponRow(string name, int count, Weapon w, RuleTexts rules)
    {
        var ap = w.Properties.Select((x, i) => (x, i)).FirstOrDefault(t => t.x.Name == ApLabel && t.x.Parameter is not null);
        var found = ap.x is not null;
        return new SheetWeapon(name, count, w.Stats.GetValueOrDefault("Range") ?? "-", w.Stats.GetValueOrDefault("Attacks") ?? "-",
            found ? ap.x!.Parameter!.Value.ToString() : "-", found ? Rule(ap.x!, rules) : null,
            w.Properties.Where((_, i) => !found || i != ap.i).Select(x => Rule(x, rules)).ToList());
    }
```
In `client/src/Hud/StatSheet.cs`, `RenderStats`: `new Label { Text = chip }` becomes `new Label { Text = chip.Text }`.
- [ ] **Step 4: Run the tests.** `dotnet test tests/Tabletop.Client.Tests` — all pass; `dotnet build client/Tabletop.csproj` — 0 errors.
- [ ] **Step 5: Commit.** `git add -A client/src tests/Tabletop.Client.Tests && git commit` with subject
  `Give the unit sheet an AP column and typed chips` and the trailer above.

## Task 2: SheetFocus — selection first, then my activation

**Files:**
- Modify: `client/src/Core/SheetFocus.cs` (rewrite), `client/src/Hud/StatSheet.cs` (focus lines, until Task 4)
- Test: `tests/Tabletop.Client.Tests/SheetFocusTests.cs` (rewrite)

**Interfaces:**
- Consumes: `HeaderView.ActionFrame(Projection)`; `TableInput.Selected` (`Selection(Guid PieceId, …)`: a unit id for a
  unit click, a terrain or marker id otherwise; null after a click on empty table or Esc); `Session.MyTeamId` (null for
  a watcher).
- Produces: `static Guid? SheetFocus.Of(Projection p, GameInfo game, Guid? myTeamId, Guid? selectedPiece)` and
  `static Team? SheetFocus.TeamOf(Projection p, GameInfo game, Guid unitId)` (null: unknown unit, or no team), code in Step 3.

Stateless: the card recomputes on every selection change and every applied batch, so "an activation start never
changes a selection" holds by construction, and clearing the selection falls back to my activating unit at once.
- [ ] **Step 1: Write the failing tests.** Replace `tests/Tabletop.Client.Tests/SheetFocusTests.cs`:
```csharp
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class SheetFocusTests
{
    internal static readonly Guid TeamA = Guid.CreateVersion7(), TeamB = Guid.CreateVersion7(), PA = Guid.CreateVersion7(), PB = Guid.CreateVersion7();
    static Unit UnitOf(string name) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [],
        [new ModelSlot(Guid.CreateVersion7(), 1, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, [])]);
    internal static readonly Unit Blue = UnitOf("Blue"), Blue2 = UnitOf("Blue Two"), Red = UnitOf("Red");
    internal static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, PA, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(TeamA, 1), new Team(TeamB, 2)],
        [new Participant(PA, Guid.CreateVersion7(), "Clay", TeamA, null), new Participant(PB, Guid.CreateVersion7(), "Sam", TeamB, null)]);
    static readonly Board Board = new(TableStateTests.Sample(),
        [new ArmyInPlay(PA, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Blue", "grimdark-future", "test", [Blue, Blue2]), Guid.CreateVersion7())),
         new ArmyInPlay(PB, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Red", "grimdark-future", "test", [Red]), Guid.CreateVersion7()))]);

    internal static Projection Activating(Guid? actor)
    {
        var a = new EventApplier(Board, Game);
        if (actor is { } u)
            a.ApplyAll([new GameEvent(1, null, new SequenceStarted(Guid.CreateVersion7(), "advance", new SequenceContext(u, [], [], "Advance")), null)]);
        return a.Projection;
    }

    [Fact]
    public void The_selection_shows_else_my_activating_unit_else_none()
    {
        Assert.Equal(Red.Id, SheetFocus.Of(Activating(null), Game, TeamA, Red.Id));   // a selected unit of any team
        Assert.Equal(Blue.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, null));
        Assert.Null(SheetFocus.Of(Activating(null), Game, TeamA, null));
    }

    [Fact]
    public void An_activation_never_changes_a_selection()
    {
        Assert.Equal(Blue2.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, Blue2.Id));
        Assert.Equal(Red.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, Red.Id));
    }

    [Fact]
    public void An_enemy_activation_never_takes_the_card()
    {
        Assert.Null(SheetFocus.Of(Activating(Red.Id), Game, TeamA, null));
        Assert.Null(SheetFocus.Of(Activating(Blue.Id), Game, null, null));   // a watcher has no team: no activation is theirs
    }

    [Fact]
    public void A_selected_piece_that_is_not_a_unit_falls_back() =>
        Assert.Equal(Blue.Id, SheetFocus.Of(Activating(Blue.Id), Game, TeamA, Guid.CreateVersion7()));   // a terrain piece or a marker

    [Fact]
    public void Team_of_a_unit_comes_from_its_participant() => Assert.Equal(2, SheetFocus.TeamOf(Activating(null), Game, Red.Id)?.Ordinal);
}
```
- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/Tabletop.Client.Tests --filter SheetFocusTests` — expected: build errors (`SheetFocus.Of` does not exist).
- [ ] **Step 3: Implement.** Replace `client/src/Core/SheetFocus.cs`:
```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// Which unit the unit card shows (client UX spec → Unit card, Which unit): the selected unit; with no unit selected, my team's
/// activating unit; otherwise none, and the card hides. A selection stays until the player clears it, so an activation never
/// changes it, and another team's activation never takes the card: the header names that one.
/// </summary>
public static class SheetFocus
{
    public static Guid? Of(Projection p, GameInfo game, Guid? myTeamId, Guid? selectedPiece)
    {
        if (selectedPiece is { } selected && p.Units.ContainsKey(selected)) return selected;
        return myTeamId is { } mine && HeaderView.ActionFrame(p)?.Context.ActorUnitId is { } actor && TeamOf(p, game, actor)?.Id == mine
            ? actor : null;
    }

    public static Team? TeamOf(Projection p, GameInfo game, Guid unitId) =>
        p.Units.GetValueOrDefault(unitId) is { } unit && game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId is { } team
            ? game.Teams.FirstOrDefault(t => t.Id == team) : null;
}
```
In `client/src/Hud/StatSheet.cs`: delete the `_focus` field and the two `_focus.On…` lines in `FollowActivation`;
`_onUnitClicked = _ => Render();`; `_onSelection = Render;`. Plan 02a's `UnitId => _focus.UnitId` becomes
`public Guid? UnitId { get; private set; }`, and `Render` opens with
`var p = _s.Projection; var game = _s.Game; var unit = p is not null && game is not null ? SheetFocus.Of(p, game, _s.MyTeamId, _input.Selected?.PieceId) : null;`,
then `UnitId = unit;` and plan 02a's line reading `if (_told != unit) { _told = unit; FocusChanged?.Invoke(_told); }`.
- [ ] **Step 4: Run the tests.** `dotnet test tests/Tabletop.Client.Tests` — all pass; `dotnet build client/Tabletop.csproj` — 0 errors.
- [ ] **Step 5: Commit.** Subject `Show the selected unit, else my activating unit, on the sheet`, trailer above.

## Task 3: EnemyStrip — the hovered enemy beside my unit

**Files:**
- Create: `client/src/Core/EnemyStrip.cs`
- Test: `tests/Tabletop.Client.Tests/EnemyStripTests.cs`

**Interfaces:**
- Consumes: `UnitSheetModel` (Task 1), `SheetFocus.TeamOf` and `SheetFocusTests`' internal fixture (Task 2);
  `HoverView.Of(...).Distance` at the call site (closest base to closest base, null when either unit is off the table).
- Produces: `RangeCheck`, `EnemyStripModel`, `EnemyStrip.Shows(Projection, GameInfo, Guid? myTeamId, Guid cardUnit, Guid hoveredUnit)`,
  `EnemyStrip.Of(UnitSheetModel mine, UnitSheetModel hovered, double? distance)`, code in Step 3.

Within is `distance <= printed inches`: a comparison of two printed numbers, no claim about shooting (the spec leaves
that to the sight check). A weapon whose `Range` is `-` (melee) is left out.
- [ ] **Step 1: Write the failing tests.** Create `tests/Tabletop.Client.Tests/EnemyStripTests.cs`:
```csharp
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
}
```
- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/Tabletop.Client.Tests --filter EnemyStripTests` — expected: build errors (`EnemyStrip` does not exist).
- [ ] **Step 3: Implement.** Create `client/src/Core/EnemyStrip.cs`:
```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed record RangeCheck(string Weapon, string Range, bool? Within);
public sealed record EnemyStripModel(Guid UnitId, string Name, string? Defense, int Alive, double? Distance, IReadOnlyList<RangeCheck> Ranges);

/// <summary>
/// The strip beside the unit card (client UX spec → Unit card, Hovered enemy): the hovered enemy's name, Defense and models alive, the
/// distance between the two units, and each of my weapons' printed range within or beyond it. No claim about shooting.
/// </summary>
public static class EnemyStrip
{
    /// <summary>The card shows one of my units and the pointer is over a unit of another team.</summary>
    public static bool Shows(Projection p, GameInfo game, Guid? myTeamId, Guid cardUnit, Guid hoveredUnit) =>
        myTeamId is { } mine && SheetFocus.TeamOf(p, game, cardUnit)?.Id == mine
        && SheetFocus.TeamOf(p, game, hoveredUnit) is { } theirs && theirs.Id != mine;

    public static EnemyStripModel Of(UnitSheetModel mine, UnitSheetModel hovered, double? distance) =>
        new(hovered.UnitId, hovered.Name, hovered.Defense, hovered.Alive, distance,
            mine.Weapons.Where(w => w.Range != "-").Select(w => new RangeCheck(w.Name, w.Range, Within(w.Range, distance))).ToList());

    static bool? Within(string range, double? distance) =>
        distance is { } d && int.TryParse(new string(range.TakeWhile(char.IsDigit).ToArray()), out var inches) ? d <= inches : null;
}
```
- [ ] **Step 4: Run the tests.** `dotnet test tests/Tabletop.Client.Tests` — expected: all pass.
- [ ] **Step 5: Commit.** Subject `Add the hovered-enemy strip model`, trailer above.

## Tasks 4–5

In [`03a-unit-card-hud.md`](03a-unit-card-hud.md): `UnitCard` assembled in place of `StatSheet` (4), verification and
documents (5).
