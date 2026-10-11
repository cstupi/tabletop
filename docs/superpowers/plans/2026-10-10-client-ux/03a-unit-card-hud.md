# Unit card, Tasks 4–5

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Part of [`03-unit-card.md`](03-unit-card.md), split to keep each file under 500 lines; its Goal, Requires landed, Global
Constraints and Review Focus apply. Tasks 1–3 have landed.

## Task 4: UnitCard, assembled in place of StatSheet

**Files:**
- Create: `client/src/Hud/CardColours.cs`, `client/src/Hud/UnitCard.cs`
- Modify: `client/src/Screens/GameScreen.Play.cs`
- Delete: `client/src/Hud/StatSheet.cs`, `client/src/Core/RoundHistory.cs`, `tests/Tabletop.Client.Tests/RoundHistoryTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3; `HoverView.Of`; `Palette.Team`; `TableInput.Selected`, `.SelectionChanged`, `.HoveredUnitChanged`;
  `Session.EventsApplied`, `GameChanged`, `BoardChanged`, `FactsChanged`, `MyTeamId`, `Rules`, `MaxWounds`.
- Produces: `public partial class UnitCard : VBoxContainer` with `Bind(Session, TableView, TableInput)`,
  `Guid? UnitId`, `event Action<Guid> ModelPicked`, `event Action<Guid?> FocusChanged` (raised when the card's unit
  changes, null when it hides), the names plan 02a gave `StatSheet`. From plan 02a Task 5: `LogDrawer.CombatLog`, the
  `CombatLogPanel` whose `Guid? FocusUnit { get; set; }` is the unit `LogFilter.Unit` (*This unit*) keeps.

- [ ] **Step 1: Create `CardColours.cs`.** `public static class CardColours` in `Tabletop.Client.Hud`, summary "Every
  colour the unit card uses, in one place for the theme (plan 04) to replace": `Text = new(1, 1, 1)`,
  `Dim = new(1, 1, 1, 0.55f)`, `Within = new(0.45f, 0.85f, 0.45f)`, `Beyond = new(0.95f, 0.55f, 0.35f)`,
  `Color Chip(ChipKind k) => k switch { ChipKind.Condition => new(1f, 0.75f, 0.3f), ChipKind.Activated => Dim, _ => Text }`,
  `Color Range(bool? within) => within switch { true => Within, false => Beyond, null => Text }`.
- [ ] **Step 2: Create `UnitCard.cs`.**
```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

/// <summary>Lower left (client UX spec → Unit card): the unit SheetFocus names, and above it, inside the left column, the hovered enemy's strip. Hidden when it names none.</summary>
public partial class UnitCard : VBoxContainer
{
    Session _s = null!; TableInput _input = null!;
    readonly ScrollContainer _card = new() { CustomMinimumSize = new Vector2(440, 360), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _stripBody = new();
    readonly PanelContainer _strip = new() { Visible = false, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    Guid? _unit, _hovered;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged; Action<Guid?>? _onHover;
    public Guid? UnitId => _unit;
    public event Action<Guid>? ModelPicked;
    public event Action<Guid?>? FocusChanged;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _input = input;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.Minsize, 8);
        GrowVertical = GrowDirection.Begin;
        // The strip stacks above the card, not beside it, so the card never leaves the left column (the spec's Unit card → Hovered enemy).
        AddChild(_strip); _strip.AddChild(_stripBody);
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd }; AddChild(panel); panel.AddChild(_card); _card.AddChild(_body);
        _onEvents = _ => Render(); _onChanged = Render; _onHover = id => { _hovered = id; Render(); };
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged; session.BoardChanged += _onChanged; session.FactsChanged += _onChanged;
        input.SelectionChanged += _onChanged; input.HoveredUnitChanged += _onHover;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged; _s.FactsChanged -= _onChanged; }
        if (_input is not null && IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.HoveredUnitChanged -= _onHover; }
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        var unit = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended
            ? SheetFocus.Of(p, game, _s.MyTeamId, _input.Selected?.PieceId) : null;
        if (unit != _unit) { _unit = unit; FocusChanged?.Invoke(unit); }
        Visible = unit is not null;
        if (unit is not { } u) return;
        var names = new Names(game!, p!);
        var sheet = UnitSheet.Of(p!, names, u, _s.Rules, _s.MaxWounds);
        RenderCard(sheet, SheetFocus.TeamOf(p!, game!, u)?.Ordinal ?? 0);
        RenderStrip(p!, game!, names, sheet);
    }

    void RenderCard(UnitSheetModel m, int team)
    {
        Clear(_body);
        _body.AddChild(new ColorRect { Color = Palette.Team(team), CustomMinimumSize = new Vector2(0, 4) });
        _body.AddChild(new Label { Text = $"{m.Name} · {m.Alive}/{m.Size} models" });
        var badges = new HBoxContainer(); _body.AddChild(badges);
        badges.AddChild(Chip($"Quality {m.Quality ?? "-"}", CardColours.Text)); badges.AddChild(Chip($"Defense {m.Defense ?? "-"}", CardColours.Text));
        _body.AddChild(Flow(m.Chips.Select(c => Chip(c.Text, CardColours.Chip(c.Kind)))));
        foreach (var model in m.Models) _body.AddChild(WoundRow(model));
        if (m.Weapons.Count > 0) _body.AddChild(WeaponTable(m.Weapons));
        _body.AddChild(Flow(m.Rules.Select(RuleChip)));
    }

    /// <summary>One row per model: a pip per wound left out of ModelFacts' max, a removed model dimmed; a click selects the model.</summary>
    Button WoundRow(SheetModel model)
    {
        var b = new Button { Text = $"{model.Label}  {new string('●', model.Left)}{new string('○', model.Max - model.Left)}", Flat = true,
            Disabled = model.Removed, Modulate = model.Removed ? CardColours.Dim : CardColours.Text, Alignment = HorizontalAlignment.Left };
        var id = model.Id; b.Pressed += () => ModelPicked?.Invoke(id);
        return b;
    }

    static GridContainer WeaponTable(IReadOnlyList<SheetWeapon> weapons)
    {
        var grid = new GridContainer { Columns = 5 };
        foreach (var h in new[] { "Weapon", "Range", "Attacks", UnitSheet.ApLabel, "Rules" }) grid.AddChild(new Label { Text = h, Modulate = CardColours.Dim });
        foreach (var w in weapons)
        {
            grid.AddChild(new Label { Text = w.Count > 1 ? $"{w.Name} ×{w.Count}" : w.Name });
            grid.AddChild(new Label { Text = w.Range }); grid.AddChild(new Label { Text = w.Attacks });
            grid.AddChild(new Label { Text = w.Ap, TooltipText = w.ApRule is { } ap ? Tooltip(ap) : "", MouseFilter = MouseFilterEnum.Stop });
            grid.AddChild(Flow(w.Rules.Select(RuleChip)));
        }
        return grid;
    }

    static Control RuleChip(SheetRule r) => Chip(r.NotAutomated ? $"{r.Label} (not automated)" : r.Label, r.NotAutomated ? CardColours.Dim : CardColours.Text, Tooltip(r));
    static string Tooltip(SheetRule r) => !string.IsNullOrEmpty(r.Text) ? r.Text : r.NotAutomated ? "Not automated: no rule text." : "";
    static PanelContainer Chip(string text, Color colour, string tooltip = "")
    {
        var chip = new PanelContainer { TooltipText = tooltip, MouseFilter = MouseFilterEnum.Stop };
        chip.AddChild(new Label { Text = text, Modulate = colour });
        return chip;
    }
    static HFlowContainer Flow(IEnumerable<Control> items) { var flow = new HFlowContainer(); foreach (var c in items) flow.AddChild(c); return flow; }
    static void Clear(Node box) { foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); } }

    /// <summary>Beside the card when it shows my unit and the pointer is over an enemy (EnemyStrip). The sight check (L) answers who can shoot.</summary>
    void RenderStrip(Projection p, GameInfo game, Names names, UnitSheetModel mine)
    {
        _strip.Visible = false;
        if (_hovered is not { } h || !p.Units.ContainsKey(h) || !EnemyStrip.Shows(p, game, _s.MyTeamId, mine.UnitId, h)) return;
        var m = EnemyStrip.Of(mine, UnitSheet.Of(p, names, h, _s.Rules, _s.MaxWounds), HoverView.Of(p, game, names, h, mine.UnitId, _s.MaxWounds)?.Distance);
        Clear(_stripBody);
        _stripBody.AddChild(new Label { Text = m.Name });
        _stripBody.AddChild(new Label { Text = $"Defense {m.Defense ?? "-"} · {m.Alive} alive" + (m.Distance is { } d ? $" · {d:0.0}\"" : "") });
        foreach (var r in m.Ranges)
            _stripBody.AddChild(new Label { Text = $"{r.Weapon} {r.Range}{(r.Within switch { true => " within", false => " beyond", null => "" })}", Modulate = CardColours.Range(r.Within) });
        _strip.Visible = true;
    }
}
```
- [ ] **Step 3: Assemble.** In `GameScreen.Play.cs`: the field `StatSheet? _sheet` becomes `UnitCard? _card`, in the
  declaration, `ShowPlayPanels`, the `FreePlayPanels` node array and its nulling;
  `_hud.AddChild(_card = new UnitCard()); _card.Bind(_session, Table, Input);`; `_sheet.ModelPicked += SelectModel;`
  becomes `_card.ModelPicked += SelectModel;`; plan 02a's two lines after `_logDrawer.Bind(...)` become
  `_card.FocusChanged += id => { if (_logDrawer is { } d) d.CombatLog.FocusUnit = id; };` and
  `_logDrawer.CombatLog.FocusUnit = _card.UnitId;` (the card raised its first change in `Bind`, before this
  subscription); `SelectModel`'s summary reads "A model clicked in the unit card's wound rows". Delete the three files
  above. `grep -rn --include='*.cs' "StatSheet\|RoundHistory\|HistoryLine" client/src tests | grep -v /obj/` — no output.
- [ ] **Step 4: Build, test, check.** `dotnet build client/Tabletop.csproj` — 0 errors;
  `dotnet test tests/Tabletop.Client.Tests` — all pass; `./scripts/check-client` — no output, exit 0.
- [ ] **Step 5: Commit.** Subject `Replace the stat sheet with the unit card`, trailer above.

## Task 5: Verify and update the documents

**Files:** Modify: `client/AGENTS.md`, `docs/design/client/design.md`, `docs/handoff.md`, `docs/schema.md`. The
2026-10-08 spec and plans stay as written; the 2026-10-10 spec supersedes them where it says so.

- [ ] **Step 1: Full checks.** `dotnet test tests/Tabletop.Client.Tests` — all pass; `dotnet build` — 0 errors;
  `./scripts/check-client` — no output, exit 0.
- [ ] **Step 2: Documents.** `client/AGENTS.md` *Layout*: the play screen's models drop `RoundHistory` and add
  `EnemyStrip`; `src/Hud/` lists `UnitCard`, `CardColours` in place of `StatSheet`. `docs/design/client/design.md`: the
  Hud node of the diagram names `UnitCard`; the `StatSheet` row of the play-screen table becomes
  `` | `UnitCard` | lower left | `UnitSheet` for the unit `SheetFocus` names (the selected unit, else my team's activating unit): Quality and Defense badges, state chips, wound pips per model from `ModelFacts`, weapons with an AP column, rules with their `rule_text` and *not automated*; above it `EnemyStrip` for a hovered enemy (Defense, models alive, distance, each weapon's range within or beyond) | ``.
  `docs/handoff.md` (the merged-state paragraph and the owner's-check list) and `docs/schema.md` (the `played: false`
  paragraph): "stat sheet" becomes "unit card", "the stat sheet's two tabs" becomes "the unit card".
  `grep -rn "StatSheet\|RoundHistory\|This round\|stat sheet" docs AGENTS.md */AGENTS.md | grep -v superpowers/` — no output.
- [ ] **Step 3: Commit.** Subject `Document the unit card`, trailer above.
- [ ] **Step 4: The owner's on-screen check** (`./scripts/client`, two players): the card shows nothing until a unit is
  selected or my activation starts; selecting another unit keeps it through my next activation; an enemy activation
  leaves it; clicking empty table returns it to my activating unit or hides it; the AP column, pips dimming on a
  removed model, rule hover text; hovering an enemy with my unit on the card shows the strip with distance and ranges.
