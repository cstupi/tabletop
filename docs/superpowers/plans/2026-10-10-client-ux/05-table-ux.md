# Table UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The army roster, the header additions, unit markers and floating results, range rings, the turn banner and
the camera's WASD, Q/E orbit, glide and follow, as the spec settles them.

**Architecture:** Each part is a Godot-free model in `client/src/Core/`, test-driven in `tests/Tabletop.Client.Tests`,
and a Godot node that only renders it. Tasks 1–7 own disjoint files; Task 8 wires the nodes into `GameScreen.Play` and
levels the docs. Tasks 1–4 are here, 5–6 in [`05-table-ux-2.md`](05-table-ux-2.md), 7–8 in [`05-table-ux-3.md`](05-table-ux-3.md) (the 500-line limit).

**Tech Stack:** `net10.0`, C# 12, xUnit, Godot 4.7.2 Mono (C#), `Tabletop.Geometry`.

**Spec:** [`docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`](../../specs/2026-10-10-client-ux-and-themes-design.md)
→ *Army roster*, *Header additions*, *Table markers and floating results*, *Range rings*, *Turn banner*, *Camera*.

**Requires landed:** [`01-collision.md`](01-collision.md), [`01a-collision-server.md`](01a-collision-server.md),
[`01b-collision-client.md`](01b-collision-client.md), [`02-combat-log.md`](02-combat-log.md),
[`02a-combat-log-client.md`](02a-combat-log-client.md), [`03-unit-card.md`](03-unit-card.md), [`03a-unit-card-hud.md`](03a-unit-card-hud.md),
[`04-themes.md`](04-themes.md) and [`04a-themes-hud.md`](04a-themes-hud.md), on `main` after `7c4ec5d`.

**From plan 04** (the task that produces each):

```csharp
// Core
ClientSettings { string Theme; bool CameraFollow = true, TurnBanner = true, RangeRings = false; }   // init properties (04 Task 4)
public enum HudPart { Corner, Toasts, Header, Roster, UnitCard, Decision, Dice, LogButton, LogPanel, Banner, EndMessage }   // 04 Task 6
public sealed record HudVisible(bool Roster, bool UnitCard, bool Decision, bool Dice, bool LogOpen);                        // 04 Task 6
// Godot, namespace Tabletop.Client.Theming (04 Task 5)
Ui.Icon(string name) : Texture2D?           // the current theme's icon for a condition name or "activated", "objective"; null when it has none
Ui.Changed                                   // static event Action?, raised when the theme changes
Variation.Chip                               // a PanelContainer variation; Variation.Banner and Variation.Dim are Label variations
Palette.Team(int ordinal)                    // reads Ui.Team (04 Task 5)
// 04a Task 9
HudPlacement.Top/Bottom/Fill(Control c, HudRect r); GameScreen.Layout() in GameScreen.Play.cs, which places every Hud part by HudLayout.Of
```

## Global Constraints

- `client/src/Core/` stays Godot-free; every Core type here is test-driven. `./scripts/check-client` prints nothing.
- The client names no rule. A condition shows by the name the server sends (first letter capitalised); its icon is
  looked up by that name with `Ui.Icon`, a text chip (`Roster.Chip`) when absent. `Range` is the printed stat, read as `UnitSheet` reads it.
- Hud and table nodes hold no colour literal: `Palette.Team`, `Ui` and `Variation` only (plan 04a Task 8's gate covers
  `Hud`, `Panels` and `Screens`).
- One world unit is one inch; Core works in the table frame, `WorldFrame` converts.
- No file over 500 lines; `TableInput.cs` (354, plus plan 01b Task 9's `Region` and `_stationary`) gains one line.
- Commit after every task, ending with the two trailer lines shown in each commit step.

## Review Focus

1. **Follow fights the player.** A follow glide while the camera moved in the last 2 s, or while a unit is in hand.
   Test: `CameraFocusTests.Follow_waits_for_a_quiet_camera_and_an_empty_hand` (Task 2).
2. **Camera keys leak.** WASD moving the camera while a text field has focus, or Q/E orbiting while a unit is held.
   Test: `CameraFocusTests.Steer_is_silent_while_typing_and_q_e_turn_the_held_unit` (Task 2).
3. **History replays on join.** The catch-up batch after a board floats every old result and banners every old turn.
   Tests: `UnitMarkersTests.The_catch_up_batch_after_a_board_floats_nothing`, `TurnBannersTests.The_catch_up_batch_shows_nothing` (Tasks 5, 7).
4. **Rings lie.** A melee weapon drawing a ring, a ring around a model that does not carry that range, or an arc inside
   another model's ring. Test: `RangeRingsTests.Rings_per_printed_range_around_their_carriers_as_one_outline` (Task 6).
5. **Repeated turn banners.** A banner on every `TurnStarted` of the same team. Test:
   `TurnBannersTests.A_turn_banner_only_when_the_team_changes` (Task 7).

## File map

| Task | Creates | Modifies |
| --- | --- | --- |
| 1 Hotkeys, fixture | `tests/Tabletop.Client.Tests/TableUx.cs` | `client/src/Core/Hotkeys.cs`, `tests/…/HotkeysTests.cs`, `client/src/Hud/KeyOverlay.cs` |
| 2 Camera | `client/src/Core/CameraFocus.cs`, `tests/…/CameraFocusTests.cs` | `client/src/Table/OrbitCamera.cs`, `client/src/Table/TableInput.cs`, `client/src/Table/TableInput.Keys.cs` |
| 3 Roster | `client/src/Core/RosterView.cs`, `client/src/Hud/Roster.cs`, `tests/…/RosterViewTests.cs` | — |
| 4 Header | `tests/…/HeaderAdditionsTests.cs` | `client/src/Core/HeaderView.cs`, `client/src/Hud/HeaderBar.cs` |
| 5 Markers | `client/src/Core/UnitMarkers.cs`, `client/src/Table/{UnitMarkerNodes,FloatingText}.cs`, `tests/…/UnitMarkersTests.cs` | `client/src/Table/Animator.cs` |
| 6 Rings | `client/src/Core/RangeRings.cs`, `client/src/Table/RangeRingNode.cs`, `tests/…/RangeRingsTests.cs` | — |
| 7 Banner | `client/src/Core/TurnBanners.cs`, `client/src/Hud/TurnBanner.cs`, `tests/…/TurnBannersTests.cs` | — |
| 8 Assembly, docs | — | `client/src/Screens/GameScreen.Play.cs`, `client/AGENTS.md`, `docs/design/client/design.md` |

Order: 1; then 2 and 3; then 4, 5 (needs 2 and 3), 6 and 7 in any order; 8 last. Tasks 4–5 reuse Task 3's `RosterView.Words/Pips` and `Roster.Icon`.

## Task 1: Hotkeys and the test fixture

**Files:** create `tests/Tabletop.Client.Tests/TableUx.cs`; modify `client/src/Core/Hotkeys.cs`,
`tests/Tabletop.Client.Tests/HotkeysTests.cs`, `client/src/Hud/KeyOverlay.cs`.

**Interfaces:** produces `Intent.CameraForward/CameraBack/CameraLeft/CameraRight/ToggleRoster/ToggleRangeRings`,
`HotKey.W/A/S/D/R/V`, and the fixture `TableUx` (two teams, three units) the later tests share.

- [ ] **Step 1: Fixture.** Create `tests/Tabletop.Client.Tests/TableUx.cs`:

```csharp
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>Team 1 (P1): Squad of three on the table (two rifles 24", one pistol 12", a knife with no Range) and Reserve on the shelf. Team 2 (P2): Orcs, one model with a melee weapon.</summary>
public static class TableUx
{
    public static readonly Guid T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7(), P1 = Guid.CreateVersion7(), P2 = Guid.CreateVersion7();
    static Weapon W(string name, string? range) => new(Guid.CreateVersion7(), name, range is null ? new Dictionary<string, string> { ["Attacks"] = "1" } : new Dictionary<string, string> { ["Range"] = range, ["Attacks"] = "1" }, []);
    static ModelSlot M(int ordinal, params Weapon[] w) => new(Guid.CreateVersion7(), ordinal, $"M{ordinal}", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, w);
    static Unit U(string name, params ModelSlot[] m) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" }, [], m);
    public static readonly Unit Squad = U("Squad", M(1, W("Rifle", "24\""), W("Knife", null)), M(2, W("Rifle", "24\"")), M(3, W("Pistol", "12\"")));
    public static readonly Unit Reserve = U("Reserve", M(1, W("Rifle", "24\"")));
    public static readonly Unit Orcs = U("Orcs", M(1, W("Choppa", null)));
    public static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, P1, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(T1, 1), new Team(T2, 2)],
        [new Participant(P1, Guid.CreateVersion7(), "Clay", T1, null), new Participant(P2, Guid.CreateVersion7(), "Ann", T2, null)]);
    static ArmyInPlay Of(Guid participant, params Unit[] units) => new(participant, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", units), Guid.CreateVersion7()));
    public static readonly Board Board = new(TableStateTests.Sample(), [Of(P1, Squad, Reserve), Of(P2, Orcs)]);
    public static Guid ModelOf(Unit u, int i = 0) => u.Models[i].Id;
    static ModelRelocation Place(ModelSlot m, double x, double y) => new(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
    /// <summary>Squad at (10,10), (12,10), (14,10); Orcs at (40,30).</summary>
    public static EventPayload Deploy => new ModelsMoved([Place(Squad.Models[0], 10, 10), Place(Squad.Models[1], 12, 10), Place(Squad.Models[2], 14, 10), Place(Orcs.Models[0], 40, 30)]);
    public static IReadOnlyList<GameEvent> Events(params EventPayload[] payloads) => payloads.Select((x, i) => new GameEvent(i + 1, P1, x, null)).ToList();
    public static EventApplier Applier(params EventPayload[] history) { var a = new EventApplier(Board, Game); a.ApplyAll(Events(history)); return a; }
    public static Projection After(params EventPayload[] history) => Applier(history).Projection;
}
```

- [ ] **Step 2: Failing test.** In `HotkeysTests.Every_key_in_the_spec_s_table_maps_to_its_intent` add to the table
  `(HotKey.W, false, false, Intent.CameraForward), (HotKey.S, false, false, Intent.CameraBack), (HotKey.A, false, false, Intent.CameraLeft), (HotKey.D, false, false, Intent.CameraRight), (HotKey.R, false, false, Intent.ToggleRoster), (HotKey.V, false, false, Intent.ToggleRangeRings), (HotKey.S, false, true, null),`
  and to `Hot_key_names_are_godot_key_names`' list `"W", "A", "S", "D", "R", "V"`.
  Run `dotnet test tests/Tabletop.Client.Tests --filter HotkeysTests`. Expected: build fails, `Intent` has no `CameraForward`.

- [ ] **Step 3: Implement.** In `client/src/Core/Hotkeys.cs` append to `Intent`
  `CameraForward, CameraBack, CameraLeft, CameraRight, ToggleRoster, ToggleRangeRings,`; append to `HotKey`
  `W, A, S, D, R, V`; in `Of` before `_ => null`:

```csharp
        (HotKey.W, _, _) => Intent.CameraForward,
        (HotKey.S, _, _) => Intent.CameraBack,
        (HotKey.A, _, _) => Intent.CameraLeft,
        (HotKey.D, _, _) => Intent.CameraRight,
        (HotKey.R, _, _) => Intent.ToggleRoster,
        (HotKey.V, _, _) => Intent.ToggleRangeRings,
```

  In `KeyOverlay.Rows` replace the `"Q / E, while holding a unit"` and `"Space"` rows and add three:

```csharp
        ("W A S D", "move the camera across the table"),
        ("Q / E", "turn the held unit; otherwise orbit the camera"),
        ("Space", "glide the camera to the active unit, else the selected one"),
        ("R", "collapse or expand the roster"),
        ("V", "range rings for my selected unit"),
```

- [ ] **Step 4: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter HotkeysTests` → 2 passed.
- [ ] **Step 5: Commit.** `git add client/src/Core/Hotkeys.cs client/src/Hud/KeyOverlay.cs tests/Tabletop.Client.Tests && git commit -m "Add camera, roster and range-ring hotkeys" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

## Task 2: Camera

**Files:** create `client/src/Core/CameraFocus.cs`, `tests/Tabletop.Client.Tests/CameraFocusTests.cs`; modify
`client/src/Table/OrbitCamera.cs`, `client/src/Table/TableInput.cs` (one line in `Bind`), `client/src/Table/TableInput.Keys.cs`.

**Interfaces:** consumes Task 1; produces `CameraFocus` (below), `OrbitCamera.Holding`, `Pitch`, `Follow(Vector3, bool)`,
a gliding `Centre`, and a public `TableInput.Focus(Guid)`.

- [ ] **Step 1: Failing tests.** Create `tests/Tabletop.Client.Tests/CameraFocusTests.cs`:

```csharp
using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class CameraFocusTests
{
    static readonly TableSize Size = new(72, 48);
    static void Near(Point3 e, Point3 a) { Assert.Equal(e.X, a.X, 9); Assert.Equal(e.Y, a.Y, 9); Assert.Equal(e.Z, a.Z, 9); }
    static void Near(Point2 e, Point2 a) => Near(new Point3(e.X, e.Y, 0), new Point3(a.X, a.Y, 0));

    [Fact]
    public void Pan_moves_along_and_across_the_heading_at_a_speed_set_by_distance_and_stops_12_inches_past_the_table()
    {
        var north = new Point2(0, 1);
        Near(new(36, 32), CameraFocus.Pan(new(36, 24), north, 1, 0, 10, 1, Size));        // 10" away: 8"/s
        Near(new(44, 24), CameraFocus.Pan(new(36, 24), north, 0, 1, 10, 1, Size));        // D: across, to the right
        Near(new(84, 60), CameraFocus.Pan(new(80, 58), north, 1, 1, 100, 1, Size));
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public void Steer_is_silent_while_typing_and_q_e_turn_the_held_unit()
    {
        var held = new HashSet<Intent> { Intent.CameraForward, Intent.CameraLeft, Intent.TurnLeft };
        Assert.Equal((1, -1, 1), CameraFocus.Steer(held, holding: false, typing: false));
        Assert.Equal((1, -1, 0), CameraFocus.Steer(held, holding: true, typing: false));
        Assert.Equal((0, 0, 0), CameraFocus.Steer(held, holding: false, typing: true));
    }

    /// <summary>Review Focus 1.</summary>
    [Fact]
    public void Follow_waits_for_a_quiet_camera_and_an_empty_hand()
    {
        Assert.True(CameraFocus.Follows(true, 2.0, false)); Assert.False(CameraFocus.Follows(true, 1.9, false));
        Assert.False(CameraFocus.Follows(true, 10, true)); Assert.False(CameraFocus.Follows(false, 10, false));
    }

    [Fact]
    public void A_glide_eases_over_three_tenths_of_a_second_to_the_actor_of_the_last_action_frame()
    {
        Point3 a = new(0, 0, 0), b = new(10, 20, 2);
        Near(new(5, 10, 1), CameraFocus.Glide(a, b, 0.15)); Near(b, CameraFocus.Glide(a, b, 0.3)); Near(b, CameraFocus.Glide(a, b, 5));
        var p = TableUx.After(TableUx.Deploy);
        SequenceStarted Start(Guid unit, string? action) => new(Guid.CreateVersion7(), "s", new SequenceContext(unit, [], [], action));
        Near(new(12, 10, 0), CameraFocus.FollowTarget(TableUx.Events(Start(TableUx.Squad.Id, "Advance"), Start(TableUx.Orcs.Id, null)), p)!.Value);
        Assert.Null(CameraFocus.FollowTarget(TableUx.Events(Start(TableUx.Reserve.Id, "Advance")), p));   // nothing on the table
    }
}
```

  `dotnet test tests/Tabletop.Client.Tests --filter CameraFocusTests` → build fails: no `CameraFocus`.

- [ ] **Step 2: Implement.** Create `client/src/Core/CameraFocus.cs`:

```csharp
using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>The camera's focus point in the table frame (spec → Camera): WASD pan clamped to the table plus 12", the 0.3 s glide, the follow rule.</summary>
public static class CameraFocus
{
    public const double Margin = 12, GlideSeconds = 0.3, QuietSeconds = 2, PanPerInchOfDistance = 0.8, OrbitRadiansPerSecond = 1.5, TableLevelPitch = 0.1;
    public static readonly HotKey[] HeldKeys = [HotKey.W, HotKey.A, HotKey.S, HotKey.D, HotKey.Q, HotKey.E];
    public static Point2 Clamp(Point2 f, TableSize s) => new(Math.Clamp(f.X, -Margin, s.Width + Margin), Math.Clamp(f.Y, -Margin, s.Length + Margin));

    /// <summary>Held intents to (forward, right, orbit), each −1, 0 or 1: nothing while typing; with a unit in hand Q/E are its turn.</summary>
    public static (int Forward, int Right, int Orbit) Steer(IReadOnlySet<Intent> held, bool holding, bool typing)
    {
        if (typing) return (0, 0, 0);
        int On(Intent i) => held.Contains(i) ? 1 : 0;
        return (On(Intent.CameraForward) - On(Intent.CameraBack), On(Intent.CameraRight) - On(Intent.CameraLeft), holding ? 0 : On(Intent.TurnLeft) - On(Intent.TurnRight));
    }

    public static Point2 Pan(Point2 focus, Point2 heading, int forward, int right, double distance, double seconds, TableSize size)
    {
        var dir = heading * forward + new Point2(heading.Y, -heading.X) * right;
        return dir.Length < 1e-9 ? Clamp(focus, size) : Clamp(focus + dir * (distance * PanPerInchOfDistance * seconds / dir.Length), size);
    }

    public static Point3 Glide(Point3 a, Point3 b, double elapsed)
    {
        var t = Math.Clamp(elapsed / GlideSeconds, 0, 1); var k = t * t * (3 - 2 * t);
        return new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Z + (b.Z - a.Z) * k);
    }

    public static bool Follows(bool enabled, double secondsSinceMoved, bool holding) => enabled && !holding && secondsSinceMoved >= QuietSeconds;
    /// <summary>End's pitch: the markers hide so the sight check stays clean.</summary>
    public static bool TableLevel(double pitch) => pitch < TableLevelPitch;

    public static Point3? Centroid(Projection p, Guid unitId)
    {
        var at = p.Models.Values.Where(m => m.UnitId == unitId).Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
        return at.Count == 0 ? null : new Point3(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z));
    }

    /// <summary>Where follow glides: the actor of the last action frame (SequenceStarted with an action) the batch starts.</summary>
    public static Point3? FollowTarget(IReadOnlyList<GameEvent> batch, Projection p) =>
        batch.Select(e => e.Payload).OfType<SequenceStarted>().LastOrDefault(s => s.Context is { Action: not null, ActorUnitId: not null }) is { } s ? Centroid(p, s.Context.ActorUnitId!.Value) : null;
}
```

- [ ] **Step 3: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter CameraFocusTests` → 4 passed.

- [ ] **Step 4: OrbitCamera.** Add the members below; replace `Centre`; `FocusOn` sets `_size = size;` first; every
  `_UnhandledInput` case calls `Moved();`, and the middle-drag case ends `Pan(CameraFocus.Clamp(WorldFrame.FromGodot(_target + offset).Horizontal, _size));`
  with `offset` today's `(-right * … ) * _distance * 0.001f`.

```csharp
    TableSize _size = new(72, 48); Point3 _glideFrom, _glideTo; double _glide = double.MaxValue; ulong _movedAt;
    /// <summary>TableInput's "a unit is in hand": Q/E turn it, and follow waits.</summary>
    public Func<bool> Holding { get; set; } = () => false;
    public float Pitch => _pitch;
    public void Centre(Vector3 target) { _glideFrom = WorldFrame.FromGodot(_target); _glideTo = WorldFrame.FromGodot(target); _glide = 0; }
    public void Follow(Vector3 target, bool enabled) { if (CameraFocus.Follows(enabled, (Time.GetTicksMsec() - _movedAt) / 1000.0, Holding())) Centre(target); }
    void Moved() { _movedAt = Time.GetTicksMsec(); _glide = double.MaxValue; }   // the player's own move stops a glide and holds follow off
    void Pan(Point2 to) { _target = new Vector3((float)to.X, _target.Y, (float)-to.Y); Apply(); }   // WorldFrame: Godot z = −wire Y

    /// <summary>WASD and Q/E read held every frame. The heading is the camera's over the table: offset (sin yaw, cos yaw) in Godot x, z looks back along (−sin yaw, cos yaw) in the wire frame.</summary>
    public override void _Process(double delta)
    {
        if (_glide <= CameraFocus.GlideSeconds) { _glide += delta; _target = WorldFrame.ToGodot(CameraFocus.Glide(_glideFrom, _glideTo, _glide)); Apply(); }
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        var held = CameraFocus.HeldKeys.Where(k => Input.IsKeyPressed(Enum.Parse<Key>(k.ToString()))).Select(k => Hotkeys.Of(k, false, ctrl)).OfType<Intent>().ToHashSet();
        var (forward, right, orbit) = CameraFocus.Steer(held, Holding(), GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit);
        if (forward == 0 && right == 0 && orbit == 0) return;
        Moved(); _yaw += orbit * (float)(CameraFocus.OrbitRadiansPerSecond * delta);
        Pan(CameraFocus.Pan(WorldFrame.FromGodot(_target).Horizontal, new Point2(-Mathf.Sin(_yaw), Mathf.Cos(_yaw)), forward, right, _distance, delta, _size));
    }
```

- [ ] **Step 5: TableInput.** `Bind` gains `view.Camera.Holding = () => HoldingModels;`. In `TableInput.Keys.cs`, `Act` gains
  `case Intent.CameraForward or Intent.CameraBack or Intent.CameraLeft or Intent.CameraRight: return false;` (the camera
  polls them) and `Focus` becomes
  `public void Focus(Guid unitId) { if (_session.Projection is { } p && CameraFocus.Centroid(p, unitId) is { } c) _view.Camera.Centre(WorldFrame.ToGodot(c)); }`.

- [ ] **Step 6: Build, commit.** `dotnet build client/Tabletop.csproj` → 0 errors; `./scripts/check-client` → no output;
  `git add client/src tests/Tabletop.Client.Tests && git commit -m "Move the camera with WASD, orbit with Q/E, glide and follow" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

## Task 3: Army roster

**Files:** create `client/src/Core/RosterView.cs`, `client/src/Hud/Roster.cs`, `tests/Tabletop.Client.Tests/RosterViewTests.cs`.

**Interfaces:** consumes `TableUx`, `Session.MaxWounds`, `Session.MyTeamId`, `Ui.Icon`, `Ui.Changed`, `Palette.Team`,
`Variation.Dim`, `Variation.Chip` (plan 04). Produces `RosterView.Of/Pips/Words`, `Roster.Bind(Session)`,
`Roster.UnitPicked`, `Roster.Icon(key)` and `Roster.Chip(text)` (Tasks 4, 5 reuse them).

- [ ] **Step 1: Failing test.** Create `tests/Tabletop.Client.Tests/RosterViewTests.cs`:

```csharp
using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class RosterViewTests
{
    [Fact]
    public void My_team_first_with_activation_summed_wounds_conditions_and_off_table_units()
    {
        var s = TableUx.Squad; var third = TableUx.ModelOf(s, 2);
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(s.Id, "Advance"), new WoundsSet(TableUx.ModelOf(s), 1), new ConditionSet(new Subject(SubjectKind.Unit, s.Id), "shaken", true),
            new ModelsMoved([new(third, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))), new OnShelf())]), new WoundsSet(third, 1));
        var max = new Dictionary<Guid, int> { [TableUx.ModelOf(s)] = 3, [TableUx.ModelOf(s, 1)] = 3 };
        var r = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T2, max);
        Assert.Equal(new[] { "Team 2", "Team 1" }, r.Select(t => t.Name));
        Assert.Equal(("Squad", true, 5, 7, false, "shaken"), (r[1].Rows[0].Name, r[1].Rows[0].Activated, r[1].Rows[0].Left, r[1].Rows[0].Max, r[1].Rows[0].OffTable, r[1].Rows[0].Conditions.Single()));   // 2 + 3 + a removed model's 0
        Assert.Equal(("Reserve", true), (r[1].Rows[1].Name, r[1].Rows[1].OffTable));
        Assert.Equal((1, 1, false), (r[0].Rows[0].Left, r[0].Rows[0].Max, r[0].Rows[0].Activated));                  // a one-wound unit counts alive models
        Assert.Equal(("●●○", "Shaken"), (RosterView.Pips(2, 3), RosterView.Words("shaken")));
    }
}
```

  `dotnet test tests/Tabletop.Client.Tests --filter RosterViewTests` → build fails: no `RosterView`.

- [ ] **Step 2: Implement.** Create `client/src/Core/RosterView.cs`:

```csharp
using Tabletop.Protocol;
namespace Tabletop.Client.Core;

public sealed record RosterRow(Guid UnitId, string Name, bool Activated, int Left, int Max, bool OffTable, IReadOnlyList<string> Conditions);
public sealed record RosterTeam(Guid TeamId, string Name, int Ordinal, IReadOnlyList<RosterRow> Rows);

/// <summary>The roster (spec → Army roster) from the projection only: my team first, the rest by ordinal, units in army order. Left is wounds left over
/// models not removed (UnitSheet's Removed: on the shelf with every wound taken); off table is every model on the shelf.</summary>
public static class RosterView
{
    public static IReadOnlyList<RosterTeam> Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyDictionary<Guid, int> maxWounds) =>
        game.Teams.OrderBy(t => t.Id == myTeam ? 0 : 1).ThenBy(t => t.Ordinal).Select(t => new RosterTeam(t.Id, n.Team(t.Id), t.Ordinal,
            p.Units.Values.Where(u => game.Participants.Any(x => x.Id == u.ParticipantId && x.TeamId == t.Id)).Select(u => Row(p, u, maxWounds)).ToList())).ToList();

    static RosterRow Row(Projection p, UnitInPlay u, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var models = p.Models.Values.Where(m => m.UnitId == u.Id).Select(m => (M: m, Max: maxWounds.GetValueOrDefault(m.Id, 1))).ToList();
        var left = models.Where(x => !(x.M.Location is OnShelf && x.M.Wounds >= x.Max)).Sum(x => Math.Max(0, x.Max - x.M.Wounds));
        return new(u.Id, u.Definition.Name, u.Actions.Count > 0, left, models.Sum(x => x.Max), models.All(x => x.M.Location is OnShelf),
            u.Conditions.Concat(models.SelectMany(x => x.M.Conditions)).Distinct().Order(StringComparer.Ordinal).ToList());
    }

    public static string Pips(int left, int max) => new string('●', left) + new string('○', Math.Max(0, max - left));
    /// <summary>A server name as shown: first letter capitalised, the rest as sent.</summary>
    public static string Words(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
```

- [ ] **Step 3: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter RosterViewTests` → 1 passed.

- [ ] **Step 4: Node.** Create `client/src/Hud/Roster.cs`:

```csharp
using Godot; using Tabletop.Client.Core; using Tabletop.Client.Table; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Left column, above the unit card: team headers and unit rows; a click selects and centres the unit, R collapses to the headers.</summary>
public partial class Roster : PanelContainer
{
    Session _s = null!; readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; bool _collapsed;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged;
    public event Action<Guid>? UnitPicked;

    public void Bind(Session session)
    {
        _s = session; var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; AddChild(scroll); scroll.AddChild(_list);
        _onEvents = _ => Render(); _onChanged = Render;
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged; session.BoardChanged += _onChanged; session.FactsChanged += _onChanged;
        Ui.Changed += _onChanged;                                                                                      // the theme's icons
        Render();
    }
    public override void _ExitTree() { Ui.Changed -= _onChanged; if (_s is null) return; _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged; _s.FactsChanged -= _onChanged; }
    public override void _Input(InputEvent e) { if (KeyOverlay.IntentOf(e, GetViewport()) == Intent.ToggleRoster) { _collapsed = !_collapsed; Render(); GetViewport().SetInputAsHandled(); } }

    void Render()
    {
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        if (_s.Projection is not { } p || _s.Game is not { } game) return;
        foreach (var team in RosterView.Of(p, game, new Names(game, p), _s.MyTeamId, _s.MaxWounds))
        {
            _list.AddChild(new Label { Text = team.Name, Modulate = Palette.Team(team.Ordinal) });
            if (_collapsed) continue;
            foreach (var row in team.Rows)
            {
                var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop }; var v = row.OffTable ? Variation.Dim : "";   // off the table: dimmed
                line.AddChild(new Label { Text = row.Name, ThemeTypeVariation = v, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
                line.AddChild(new Label { Text = row.Activated ? "✓" : " ", ThemeTypeVariation = v });
                line.AddChild(new Label { Text = row.Max <= 12 ? RosterView.Pips(row.Left, row.Max) : $"{row.Left}/{row.Max}", ThemeTypeVariation = v });
                foreach (var c in row.Conditions) line.AddChild(Icon(c));
                var id = row.UnitId;
                line.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) UnitPicked?.Invoke(id); };
                _list.AddChild(line);
            }
        }
    }

    /// <summary>The theme's icon for a condition name or marker kind, else a text chip of the name.</summary>
    public static Control Icon(string key) => Ui.Icon(key) is { } texture
        ? new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(16, 16), TooltipText = RosterView.Words(key) }
        : Chip(RosterView.Words(key));

    /// <summary>Text in the theme's chip box.</summary>
    public static Control Chip(string text) { var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip }; chip.AddChild(new Label { Text = text }); return chip; }
}
```

- [ ] **Step 5: Build, commit.** `dotnet build client/Tabletop.csproj` → 0 errors;
  `git add client/src tests/Tabletop.Client.Tests && git commit -m "Add the army roster" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

## Task 4: Header additions

**Files:** create `tests/Tabletop.Client.Tests/HeaderAdditionsTests.cs`; modify `client/src/Core/HeaderView.cs`, `client/src/Hud/HeaderBar.cs`.

**Interfaces:** consumes `TableUx`, `Roster.Icon` (Task 3, the theme's `objective` icon), `Variation.Dim`, `Palette.Team`;
`HeaderBar` as plan 04 leaves it (colours by `Variation`, the settings button kept last in `_top`, re-rendered on `Ui.Changed`).
Produces `HeaderView.NotActivated`, `Objectives`, `ActivationLine` (below).

- [ ] **Step 1: Failing tests.** Create `tests/Tabletop.Client.Tests/HeaderAdditionsTests.cs`:

```csharp
using Tabletop.Client.Core; using Tabletop.Content; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class HeaderAdditionsTests
{
    [Fact]
    public void Not_activated_counts_units_on_the_table_with_no_action_and_objectives_carry_their_controller()
    {
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "Advance"));
        Assert.Equal((0, 1), (HeaderView.NotActivated(p, TableUx.Game)[TableUx.T1], HeaderView.NotActivated(p, TableUx.Game)[TableUx.T2]));   // Reserve is on the shelf
        Assert.Equal(1, HeaderView.NotActivated(TableUx.After(TableUx.Deploy), TableUx.Game)[TableUx.T1]);
        var objective = TableUx.Board.Table.Markers.Single(m => m.Kind == MarkerKind.Objective).Id;
        Assert.Equal(new (Guid, Guid?)[] { (objective, null) }, HeaderView.Objectives(p));
        Assert.Equal(new (Guid, Guid?)[] { (objective, TableUx.T2) }, HeaderView.Objectives(TableUx.After(new ObjectiveControlSet(objective, TableUx.T2))));
    }

    [Fact]
    public void Another_team_s_activation_is_named_on_the_action_line()
    {
        var p = TableUx.After(TableUx.Deploy, new SequenceStarted(Guid.CreateVersion7(), "charge", new SequenceContext(TableUx.Orcs.Id, [], [], "Charge")));
        var n = new Names(TableUx.Game, p);
        Assert.Equal("Team 2 activating: Orcs · Charge", HeaderView.ActivationLine(p, TableUx.Game, n, TableUx.T1));
        Assert.Equal("Orcs · Charge", HeaderView.ActivationLine(p, TableUx.Game, n, TableUx.T2));
        Assert.Null(HeaderView.ActivationLine(TableUx.After(TableUx.Deploy), TableUx.Game, n, TableUx.T1));
    }
}
```

  `dotnet test tests/Tabletop.Client.Tests --filter HeaderAdditionsTests` → build fails: no `NotActivated`.

- [ ] **Step 2: Implement.** Add `using Tabletop.Content;` and these members to `HeaderView`:

```csharp
    /// <summary>Per team: units with a model on the table and no action recorded this round. Who may activate is the scheduler's, not this count's.</summary>
    public static IReadOnlyDictionary<Guid, int> NotActivated(Projection p, GameInfo game) => game.Teams.ToDictionary(t => t.Id, t => p.Units.Values.Count(u =>
        u.Actions.Count == 0 && game.Participants.Any(x => x.Id == u.ParticipantId && x.TeamId == t.Id) && p.Models.Values.Any(m => m.UnitId == u.Id && m.Location is OnTable)));

    /// <summary>Objective markers, setup's then play's, with their controller (Projection.Control); null is neutral.</summary>
    public static IReadOnlyList<(Guid MarkerId, Guid? TeamId)> Objectives(Projection p) => p.Board.Table.Markers.Concat(p.Markers.Values)
        .Where(m => m.Kind == MarkerKind.Objective).DistinctBy(m => m.Id).Select(m => (m.Id, p.Control.TryGetValue(m.Id, out var t) ? t : (Guid?)null)).ToList();

    /// <summary>"Orcs · Charge"; led by "Team 2 activating: " when the actor is not my team's (a watcher has none).</summary>
    public static string? ActivationLine(Projection p, GameInfo game, Names n, Guid? myTeam)
    {
        if (ActionFrame(p)?.Context is not { ActorUnitId: { } unit } c) return null;
        var line = c.Action is { } a ? $"{n.Unit(unit)} · {a}" : n.Unit(unit);
        var team = p.Units.GetValueOrDefault(unit) is { } u ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
        return team is { } t && t != myTeam ? $"{n.Team(t)} activating: {line}" : line;
    }
```

- [ ] **Step 3: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter "HeaderAdditionsTests|HeaderViewTests"` → all passed.

- [ ] **Step 4: HeaderBar.** In `Render`: before the team loop `var waiting = HeaderView.NotActivated(p!, game!);`;
  after each team's score label `_top.AddChild(new Label { Text = $" · {waiting.GetValueOrDefault(team.Id)} not activated", ThemeTypeVariation = Variation.Dim });`;
  after the loop, before `_gameOver` and the settings button move last,
  `foreach (var (_, owner) in HeaderView.Objectives(p!)) { var icon = Roster.Icon("objective"); if (owner is { } t) icon.Modulate = Palette.Team(game.Teams.First(x => x.Id == t).Ordinal); _top.AddChild(icon); }`;
  and the activation label's text becomes `HeaderView.ActivationLine(p!, game!, names, _s.MyTeamId)`.

- [ ] **Step 5: Build, commit.** `dotnet build client/Tabletop.csproj` → 0 errors;
  `git add client/src tests/Tabletop.Client.Tests && git commit -m "Show units not activated, objective control and the enemy's activation in the header" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

Tasks 5–6 continue in [`05-table-ux-2.md`](05-table-ux-2.md), 7–8 in [`05-table-ux-3.md`](05-table-ux-3.md).
