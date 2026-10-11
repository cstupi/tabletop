# Table UX Implementation Plan, Tasks 7–8

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Continues [`05-table-ux-2.md`](05-table-ux-2.md). The header, *From plan 04*, Global Constraints, Review Focus and
File map in [`05-table-ux.md`](05-table-ux.md) apply. Task 7 needs Task 1 (`TableUx`); Task 8 needs every task.

## Task 7: Turn banner

**Files:** create `client/src/Core/TurnBanners.cs`, `client/src/Hud/TurnBanner.cs`, `tests/Tabletop.Client.Tests/TurnBannersTests.cs`.

**Interfaces:** consumes `TableUx`, `ClientSettings.TurnBanner` (plan 04 Task 4) and the `Label` variation `Variation.Banner`
(plan 04 Task 5). Produces `TurnBanners` (`Seed`, `OnBoard`, `OnBatch`), `Banner`, `TurnBanner.Bind(Session)`; Task 8 places
the node in `HudPart.Banner`.

- [ ] **Step 1: Failing tests.** Create `tests/Tabletop.Client.Tests/TurnBannersTests.cs`:

```csharp
using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class TurnBannersTests
{
    static readonly Names N = new(TableUx.Game, null);
    static IReadOnlyList<GameEvent> B(params EventPayload[] e) => TableUx.Events(e);

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public void A_turn_banner_only_when_the_team_changes()
    {
        var t = new TurnBanners();
        Assert.Equal(new Banner[] { new("Round 2", 1.5), new("Team 1's turn", 1.0) }, t.OnBatch(B(new RoundStarted(2, [TableUx.T1, TableUx.T2], []), new TurnStarted(TableUx.T1)), N, true));
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T1)), N, true));
        Assert.Equal("Team 2's turn", t.OnBatch(B(new TurnStarted(TableUx.T2)), N, true).Single().Text);
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T1)), N, false));                     // off: nothing shown, the team still tracked
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T1), new RoundStarted(0, [], [])), N, true));   // round 0 is deployment
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public void The_catch_up_batch_shows_nothing()
    {
        var t = new TurnBanners(); t.OnBoard();
        Assert.Empty(t.OnBatch(B(new RoundStarted(1, [], []), new TurnStarted(TableUx.T1)), N, true));
        Assert.Equal("Round 2", t.OnBatch(B(new RoundStarted(2, [], []), new TurnStarted(TableUx.T1)), N, true).Single().Text);
    }
}
```

  `dotnet test tests/Tabletop.Client.Tests --filter TurnBannersTests` → build fails: no `TurnBanners`.

- [ ] **Step 2: Implement.** Create `client/src/Core/TurnBanners.cs`:

```csharp
using Tabletop.Protocol;
namespace Tabletop.Client.Core;

public sealed record Banner(string Text, double Seconds);

/// <summary>Spec → Turn banner: "Round n" on RoundStarted from round 1, "Team k's turn" on a TurnStarted whose team differs from the previous TurnStarted's.
/// The first batch after a board, a batch with an undo, and a disabled banner show nothing but still track the team.</summary>
public sealed class TurnBanners
{
    public const double RoundSeconds = 1.5, TurnSeconds = 1.0;
    Guid? _lastTeam; bool _catchingUp;
    /// <summary>Bound mid-game: the active team is the last TurnStarted's.</summary>
    public void Seed(Guid? activeTeam) => _lastTeam = activeTeam;
    public void OnBoard() { _lastTeam = null; _catchingUp = true; }

    public IReadOnlyList<Banner> OnBatch(IReadOnlyList<GameEvent> batch, Names n, bool enabled)
    {
        var shown = new List<Banner>();
        foreach (var e in batch)
            switch (e.Payload)
            {
                case RoundStarted { Round: > 0 } r: shown.Add(new($"Round {r.Round}", RoundSeconds)); break;
                case TurnStarted t when t.TeamId != _lastTeam: _lastTeam = t.TeamId; shown.Add(new($"{n.Team(t.TeamId)}'s turn", TurnSeconds)); break;
            }
        var silent = _catchingUp || !enabled || batch.Any(e => e.Payload is Undone); _catchingUp = false;
        return silent ? [] : shown;
    }
}
```

- [ ] **Step 3: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter TurnBannersTests` → 2 passed.

- [ ] **Step 4: Node.** Create `client/src/Hud/TurnBanner.cs`:

```csharp
using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Across the upper third (HudPart.Banner), one banner after another; off with Animate off or the banner setting off. Clicks pass through.</summary>
public partial class TurnBanner : PanelContainer
{
    Session _s = null!; readonly TurnBanners _banners = new(); readonly Queue<Banner> _queue = new(); double _left;
    readonly Label _text = new() { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = Variation.Banner, MouseFilter = MouseFilterEnum.Ignore };
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session session)
    {
        _s = session; AddChild(_text); MouseFilter = MouseFilterEnum.Ignore; Visible = false;
        _banners.Seed(session.Projection?.Scheduler.ActiveTeamId);
        _onEvents = batch => { if (_s.Game is { } g) foreach (var b in _banners.OnBatch(batch, new Names(g, _s.Projection), _s.Settings.Animate && _s.Settings.TurnBanner)) _queue.Enqueue(b); };
        _onBoard = _banners.OnBoard;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
    }
    public override void _ExitTree() { if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    public override void _Process(double delta)
    {
        if ((_left -= delta) > 0) return;
        Visible = _queue.TryDequeue(out var b);
        if (b is not null) { _text.Text = b.Text; _left = b.Seconds; }
    }
}
```

- [ ] **Step 5: Build, commit.** `dotnet build client/Tabletop.csproj` → 0 errors;
  `git add client/src tests/Tabletop.Client.Tests && git commit -m "Show round and turn banners" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

## Task 8: Assembly, verification, documents

**Files:** modify `client/src/Screens/GameScreen.Play.cs`, `client/AGENTS.md`, `docs/design/client/design.md`.

**Interfaces:** consumes every node above, `TableInput.Select/Focus`, `CameraFocus.FollowTarget`, `OrbitCamera.Follow`,
`Animator.Played`, `ClientSettings.CameraFollow`; from plan 04a Task 9 `GameScreen.Layout()`, `HudPlacement.Top/Fill`,
`HudVisible.Roster`, `HudPart.Roster` and `HudPart.Banner`.

- [ ] **Step 1: Wire.** In `GameScreen.Play.cs` add the fields
  `Roster? _roster; TurnBanner? _banner; UnitMarkerNodes? _markers; FloatingText? _floating; RangeRingNode? _rings; Action<IReadOnlyList<GameEvent>>? _onFollow;`.
  In `ShowPlayPanels`, after the header: `_hud.AddChild(_roster = new Roster()); _roster.Bind(_session);`;
  before the end message: `_hud.AddChild(_banner = new TurnBanner()); _banner.Bind(_session);`; with the table overlays:
  `Table.AddChild(_rings = new RangeRingNode()); _rings.Bind(_session, Input);`; after `_tableLayer` is made and **before**
  `new Animator(...)` (the floating text must hear each batch before the Animator plays it):

```csharp
        _tableLayer.AddChild(_markers = new UnitMarkerNodes()); _markers.Bind(_session, Table);
        _tableLayer.AddChild(_floating = new FloatingText()); _floating.Bind(_session, Table);
```

  after the Animator and the part events:

```csharp
        _animator.Played += _floating.Show;
        _roster.UnitPicked += id => { Input.Select(id); Input.Focus(id); };
        // Follow (spec → Camera): an action frame starting glides to its actor, unless the player moved the camera in the last 2 s or holds a unit.
        _onFollow = batch => { if (_session.Projection is { } p && CameraFocus.FollowTarget(batch, p) is { } at) Table.Camera.Follow(WorldFrame.ToGodot(at), _session.Settings.CameraFollow); };
        _session.EventsApplied += _onFollow;
```

  In `Layout()`: `HudVisible`'s first argument becomes `_roster is { Visible: true }` in place of plan 04a's `false`;
  `HudPlacement.Top(_roster, r[HudPart.Roster])` and `HudPlacement.Fill(_banner, r[HudPart.Banner])` join the placements;
  `_roster` joins the parts whose `MinimumSizeChanged` and `VisibilityChanged` queue the layout (R changes its height).
  In `FreePlayPanels`: `_session.EventsApplied -= _onFollow; _onFollow = null;`, add `_roster, _banner, _markers, _floating, _rings`
  to the freed array (the two in `_tableLayer` go with it; freeing a queued node twice is harmless) and null the five fields.

- [ ] **Step 2: Verify.** Each must hold before the commit:
  - `dotnet test tests/Tabletop.Client.Tests` → all passed, none skipped.
  - `dotnet build` → 0 errors, 0 new warnings.
  - `./scripts/check-client` → no output.
  - `wc -l client/src/Table/TableInput.cs client/src/Screens/GameScreen.Play.cs` → each under 500.

- [ ] **Step 3: Documents.** `client/AGENTS.md` → Layout: the Core list gains `CameraFocus`, `RosterView`, `UnitMarkers`
  (`FloatingResults`), `RangeRings`, `TurnBanners`; `src/Hud/` gains `Roster`, `TurnBanner`; `src/Table/` gains
  `UnitMarkerNodes`, `FloatingText`, `RangeRingNode`. `docs/design/client/design.md` → *The play screen*: the parts table gains

```markdown
| `Roster` | left column, above the unit card | `RosterView`: per team, each unit's activated tick, wound pips summed, condition icons; off-table units dimmed. A click selects and centres; R collapses |
| `TurnBanner` | upper third, transient | `TurnBanners`: `Round n` 1.5 s; `Team k's turn` 1 s when the team differs from the previous `TurnStarted`'s; silent on the catch-up batch, with Animate off or the setting off |
```

  and after the table-overlay paragraph:

```markdown
`UnitMarkerNodes` and `FloatingText` draw in the table's canvas layer at projected anchors and take no clicks:
`UnitMarkers` rows (activated, each condition by the server's name with the theme's icon or a text chip, a wounded
multi-wound model's pips) over each unit's centroid above its tallest model, hidden at End's pitch; `FloatingResults`
lines (`-2 models`, `-1 wound`, a condition) shown as the `Animator` plays their event (`Animator.Played`), wounds diffed
against the last total seen, the first batch after a board silent. `RangeRingNode`: with V on (`ClientSettings.RangeRings`)
and a unit of mine selected, `RangeRings`' union outline per printed `Range` around the models carrying it, labelled
with the printed value. `OrbitCamera` orbits a moving focus (`CameraFocus`): WASD and Q/E read held each frame,
ignored while a text field has focus, Q/E the held unit's turn while one is in hand; pan speed scales with distance and
the focus stays within the table plus 12"; Space, Tab and the roster glide 0.3 s; an action frame's start glides to its
actor unless the camera moved in the last 2 s or a unit is held (`ClientSettings.CameraFollow`).
```

  The `HeaderBar` row gains: `k not activated` per team (units with a model on the table and no action recorded), objective
  icons in the controller's team colour, and `Team 2 activating: …` on the action line for another team's activation.

- [ ] **Step 4: Commit.** `git add client docs && git commit -m "Assemble the roster, markers, rings, banner and camera follow on the play screen" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

- [ ] **Step 5: Owner's on-screen check** (`./scripts/client`, a game against the sim's socket players, both themes):
  1. W A S D move the view the way the camera faces, faster zoomed out, and stop 12" past each table edge.
  2. Q / E orbit with an empty hand; while dragging a unit they turn its formation and the camera stays put.
  3. With a menu's text prompt focused, W A S D Q E type and the camera does not move.
  4. Space, Tab and a roster click glide the camera in about a third of a second.
  5. The opponent's activation glides the camera to their unit; it does not if you moved the camera in the last 2 s or hold a unit, nor with Camera follow off.
  6. Roster: both teams under coloured headers, mine first; ticks after activating, pips drop with wounds, condition icons or chips; reserves and wiped units dimmed; R collapses; a row click selects and centres.
  7. Header: `k not activated` per team counts down through the round; objective icons take the controller's colour; `Team 2 activating: …` during the opponent's activation.
  8. Markers over each unit with something to show; gone after End, back after Home.
  9. `-1 wound`, `-2 models`, `Shaken` rise for about a second as each step plays; with Animate off they appear together; joining a game in progress floats nothing.
  10. V shows labelled rings around my selected unit (none for melee-only units, none for an enemy), stays on after a restart, V again hides them.
  11. `Round 2` and `Team 1's turn` banners show once each, not again while the same team keeps the turn; clicks through a banner reach the table; none with Animate off or the setting off.
