# Task 11a: Decision view and hotkeys

**Wave:** 2 · **Depends on:** 1 · **Model:** opus

**Files:**
- Create: `client/src/Core/DecisionView.cs`, `client/src/Core/Hotkeys.cs`
- Test: `tests/Tabletop.Client.Tests/DecisionViewTests.cs`, `tests/Tabletop.Client.Tests/HotkeysTests.cs` (new)

**Interfaces:**
- Consumes (existing): `AskViewModel.Mine`, `AskViewModel.ConfirmLabel`, `AskMode`, `AskQuestions`, `Names`.
- Produces:

```csharp
public sealed record DecisionModel(Ask? Ask, AskMode? Mode, string Title, IReadOnlyList<(int Key, string Label, string OptionId)> Numbered,
    string Hint, bool AutoPass = false);
public static class DecisionView { public static DecisionModel Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyCollection<string> muted); }
public enum Intent { Option1, …, Option9, Confirm, Cancel, SingleModel, Sight, Measure, Ping, NextUnit, PreviousUnit, FocusCamera, UndoMove,
    ToggleLog, CameraTop, CameraLevel, KeyOverlay, TurnLeft, TurnRight }
public enum HotKey { Key1, …, Key9, Enter, KpEnter, Escape, Shift, Q, E, L, M, P, G, Z, Tab, Backtab, Space, Home, End, F1, Slash, Question }
public static class Hotkeys { public static Intent? Of(HotKey key, bool shift, bool ctrl); }
```

`AutoPass` is `AskViewModel.Mine`'s auto-pass of a muted window: the box sends `Passed` for it. `HotKey` member names
equal `Godot.Key` member names, so the Godot side maps a key with `Enum.TryParse<HotKey>(godotKey.ToString(), out var k)`;
a Core enum named `Key` would collide with `Godot.Key` (CS0104) in every file that imports both namespaces.

Spec: *Decision box*, *Hotkeys*.

## Decision box content

`DecisionView` renders `AskViewModel.Mine`. Title is the Ask's prompt, except an `Allocation`, whose title is
`Remove {Amount}` (the box appends `: {k} chosen` from `CasualtyPick.Chosen`). Every entry is numbered in order;
entries 1–9 carry that key, the tenth on carry key 0 and are clicked.

| Mode (legal set) | Numbered | Hint |
| --- | --- | --- |
| Choose (`Options`) | each choice: label, option id | `1–{k}: pick`, plus ` · click a glowing unit` when a choice names pieces |
| PickPieces, question `deploy`/`deploy_scouts`/`deploy_ambusher` | each eligible unit: name, unit id | `1–{k}: pick a unit to deploy` |
| PickPieces, any other question | — | `Click a glowing unit` (or `model` when the first eligible id is a model), plus ` · Enter: done` when `Max > 1` |
| Move (`MoveRegion`) | — | `Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · Esc: reset` |
| Assign (`Assignment`) | — | `Click a target · Click a chip: that group alone · Enter: fire · Esc: clear` |
| Allocate (`Allocation`) | — | `Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm` |
| TokenSpend | — | `Set the tokens per caster · Enter: spend` |
| Confirm (`Confirmation`) | — | `Enter: {AskViewModel.ConfirmLabel}` |

`{k}` is the number of keyed entries; one entry reads `1: …`. With nothing addressed to me (or a watcher) the model has
`{k}` is the number of keyed entries; one entry reads `1: …`. With nothing addressed to me (or a watcher) the model has
no Ask, an empty title, no entries and no hint. The waiting line has one source, `HeaderView.Waiting` (Task 9): the
decision box (Task 16a) shows it when the model has no Ask.

## Hotkeys

`Of` maps a key to its intent with no context; the caller decides by context: `Option1`–`Option5` are rows while a unit
is held and options otherwise, `TurnLeft`/`TurnRight` act only while a unit is held, `Cancel` is innermost first.

| Key | Intent |
| --- | --- |
| 1–9 | `Option1`–`Option9` |
| Enter, keypad Enter | `Confirm` |
| Esc | `Cancel` |
| Shift | `SingleModel` |
| L / M / P / G | `Sight` / `Measure` / `Ping` / `ToggleLog` |
| Tab, Shift+Tab (or Backtab) | `NextUnit`, `PreviousUnit` |
| Space | `FocusCamera` |
| Ctrl+Z | `UndoMove`; any other Ctrl chord is null |
| Home / End | `CameraTop` / `CameraLevel` |
| F1, ?, Shift+/ | `KeyOverlay` |
| Q / E | `TurnLeft` / `TurnRight` |

- [ ] **Step 1: Write the failing decision view tests**

Create `tests/Tabletop.Client.Tests/DecisionViewTests.cs`:

```csharp
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class DecisionViewTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly string[] Unmuted = [];

    static Ask AskOf(string question, LegalSet legal, string prompt = "prompt", Guid? team = null, bool allowPass = false, string? window = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team ?? Samples.Team1], legal, allowPass, false, window, null);

    static DecisionModel Of(Ask ask, Guid? me, IReadOnlyCollection<string>? muted = null)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        return DecisionView.Of(a.Projection, Game, new Names(Game, a.Projection), me, muted ?? Unmuted);
    }

    [Fact]
    public void Options_are_numbered_and_the_hint_names_the_keys()
    {
        var ask = AskOf(AskQuestions.Activate, new Options([new AskOption("a", "Advance", [Samples.Unit]), new AskOption("h", "Hold", [Samples.Unit])]), "Activate a unit");
        var d = Of(ask, Samples.Team1);
        Assert.Equal((ask.Id, AskMode.Choose, "Activate a unit"), (d.Ask!.Id, d.Mode, d.Title));
        Assert.Equal(new[] { (1, "Advance", "a"), (2, "Hold", "h") }, d.Numbered);
        Assert.Equal("1–2: pick · click a glowing unit", d.Hint);
        Assert.Equal("1: pick", Of(AskOf("shoot_or_not", new Options([new AskOption("s", "Shoot", [])])), Samples.Team1).Hint);
    }

    [Fact]
    public void Past_nine_an_entry_has_no_key()
    {
        var many = new Options(Enumerable.Range(1, 11).Select(i => new AskOption($"o{i}", $"Option {i}", [])).ToList());
        var d = Of(AskOf(AskQuestions.Activate, many), Samples.Team1);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0], d.Numbered.Select(x => x.Key));
        Assert.Equal("1–9: pick", d.Hint);
    }

    [Fact]
    public void A_deploy_ask_numbers_the_shelved_units_and_a_target_pick_asks_for_a_click()
    {
        var deploy = Of(AskOf(AskQuestions.Deploy, new PieceSelection([Samples.Unit], 1, 1)), Samples.Team1);
        Assert.Equal(new[] { (1, "Battle Brothers", Samples.Unit.ToString()) }, deploy.Numbered);
        Assert.Equal("1: pick a unit to deploy", deploy.Hint);
        var charge = Of(AskOf("charge_target", new PieceSelection([Samples.Unit], 1, 1)), Samples.Team1);
        Assert.Empty(charge.Numbered);
        Assert.Equal("Click a glowing unit", charge.Hint);
        Assert.Equal("Click a glowing model", Of(AskOf("takedown_target", new PieceSelection([Samples.Model], 1, 1)), Samples.Team1).Hint);
        Assert.Equal("Click a glowing unit · Enter: done", Of(AskOf("spell_targets", new PieceSelection([Samples.Unit], 1, 2)), Samples.Team1).Hint);
    }

    [Fact]
    public void Each_mode_has_its_hint_and_casualties_title_the_count()
    {
        Assert.Equal("Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · Esc: reset",
            Of(AskOf("move", new MoveRegion([Samples.Model], 6, [], [], null)), Samples.Team1).Hint);
        Assert.Equal("Click a target · Click a chip: that group alone · Enter: fire · Esc: clear",
            Of(AskOf("declare_targets", new Assignment([new AskOption("Rifles", "Rifles", [])], [Samples.Unit], 2)), Samples.Team1).Hint);
        var casualties = Of(AskOf("remove_casualties", new Allocation([Samples.Model], 3), "Assign 3 wound(s)"), Samples.Team1);
        Assert.Equal(("Remove 3", "Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm"), (casualties.Title, casualties.Hint));
        Assert.Equal("Set the tokens per caster · Enter: spend",
            Of(AskOf(AskQuestions.SpendTokens, new TokenSpend(new Dictionary<Guid, int> { [Samples.Model] = 1 })), Samples.Team1).Hint);
        Assert.Equal("Enter: Continue", Of(AskOf(AskQuestions.Confirm, new Confirmation()), Samples.Team1).Hint);
        Assert.Equal("Enter: End the round", Of(AskOf(AskQuestions.EndRound, new Confirmation()), Samples.Team1).Hint);
    }

    [Fact]
    public void Nothing_addressed_to_me_shows_the_waiting_line()

    [Fact]
    public void Nothing_addressed_to_me_gives_no_ask()
    {
        var ask = AskOf("remove_casualties", new Allocation([Samples.Model], 2), team: Samples.Team2);
        foreach (var me in new Guid?[] { Samples.Team1, null })
        {
            var d = Of(ask, me);
            Assert.Null(d.Ask); Assert.Null(d.Mode);
            Assert.Equal(("", ""), (d.Title, d.Hint));
            Assert.Empty(d.Numbered);
        }
    }

    [Fact]
    public void A_muted_window_is_an_auto_pass()
    {
        var ask = AskOf(AskQuestions.React, new Options([new AskOption("r", "React", [])]), allowPass: true, window: "reaction");
        Assert.True(Of(ask, Samples.Team1, ["reaction"]).AutoPass);
        Assert.False(Of(ask, Samples.Team1).AutoPass);
    }
}
```

The question strings that are not `AskQuestions` constants (`charge_target`, `remove_casualties`, …) are test data: the
client code under test never names them.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter DecisionViewTests`
Expected: compile errors: `DecisionView` and `DecisionModel` do not exist.

- [ ] **Step 3: Write the decision view**

Create `client/src/Core/DecisionView.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What the decision box shows (in-game UX spec → Decision box): the first open Ask addressed to my team, its numbered entries
/// (keys 1–9, then 0 for entries past nine), and the hint line; with nothing addressed to me, no Ask. The waiting line is
/// HeaderView.Waiting's, which the box shows then.
/// </summary>
public sealed record DecisionModel(Ask? Ask, AskMode? Mode, string Title, IReadOnlyList<(int Key, string Label, string OptionId)> Numbered,
    string Hint, bool AutoPass = false);

public static class DecisionView
{
    const int Keyed = 9;
    static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher];

    public static DecisionModel Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyCollection<string> muted)
    {
        var mine = AskViewModel.Mine(p, game, myTeam, muted as IReadOnlySet<string> ?? muted.ToHashSet(StringComparer.Ordinal));
        if (mine is null) return new DecisionModel(null, null, "", [], "");
        var ask = mine.Ask;
        var numbered = Numbered(ask, n);
        var title = ask.Legal is Allocation a ? $"Remove {a.Amount}" : ask.Prompt;
        return new DecisionModel(ask, mine.Mode, title, numbered, Hint(p, ask, numbered), mine.AutoPass);
    }

    static IReadOnlyList<(int Key, string Label, string OptionId)> Numbered(Ask ask, Names n)
    {
        IEnumerable<(string Label, string Id)> entries = ask.Legal switch
        {
            Options o => o.Choices.Select(c => (c.Label, c.Id)),
            PieceSelection s when DeployQuestions.Contains(ask.Question) => s.Eligible.Select(id => (n.Unit(id), id.ToString())),
            _ => [],
        };
        return entries.Select((e, i) => (i < Keyed ? i + 1 : 0, e.Label, e.Id)).ToList();
    }

    static string Hint(Projection p, Ask ask, IReadOnlyList<(int Key, string Label, string OptionId)> numbered)
    {
        var keyed = numbered.Count(x => x.Key > 0);
        var keys = keyed == 1 ? "1" : $"1–{keyed}";
        return ask.Legal switch
        {
            Options when keyed == 0 => "",
            Options o => $"{keys}: pick" + (o.Choices.Any(c => c.Pieces.Count > 0) ? " · click a glowing unit" : ""),
            PieceSelection when DeployQuestions.Contains(ask.Question) => $"{keys}: pick a unit to deploy",
            PieceSelection s => $"Click a glowing {(s.Eligible.Count > 0 && p.Models.ContainsKey(s.Eligible[0]) ? "model" : "unit")}"
                                + (s.Max > 1 ? " · Enter: done" : ""),
            MoveRegion => "Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · Esc: reset",
            Assignment => "Click a target · Click a chip: that group alone · Enter: fire · Esc: clear",
            Allocation => "Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm",
            TokenSpend => "Set the tokens per caster · Enter: spend",
            Confirmation => $"Enter: {AskViewModel.ConfirmLabel(ask)}",
            _ => "",
        };
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter DecisionViewTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Write the failing hotkey tests**

Create `tests/Tabletop.Client.Tests/HotkeysTests.cs`:

```csharp
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HotkeysTests
{
    [Fact]
    public void Every_key_in_the_spec_s_table_maps_to_its_intent()
    {
        var table = new (HotKey Key, bool Shift, bool Ctrl, Intent? Intent)[]
        {
            (HotKey.Key1, false, false, Intent.Option1), (HotKey.Key5, false, false, Intent.Option5), (HotKey.Key9, false, false, Intent.Option9),
            (HotKey.Enter, false, false, Intent.Confirm), (HotKey.KpEnter, false, false, Intent.Confirm), (HotKey.Escape, false, false, Intent.Cancel),
            (HotKey.Shift, true, false, Intent.SingleModel), (HotKey.L, false, false, Intent.Sight), (HotKey.M, false, false, Intent.Measure),
            (HotKey.P, false, false, Intent.Ping), (HotKey.Tab, false, false, Intent.NextUnit), (HotKey.Tab, true, false, Intent.PreviousUnit),
            (HotKey.Backtab, true, false, Intent.PreviousUnit), (HotKey.Space, false, false, Intent.FocusCamera),
            (HotKey.Z, false, true, Intent.UndoMove), (HotKey.G, false, false, Intent.ToggleLog),
            (HotKey.Home, false, false, Intent.CameraTop), (HotKey.End, false, false, Intent.CameraLevel),
            (HotKey.F1, false, false, Intent.KeyOverlay), (HotKey.Question, true, false, Intent.KeyOverlay), (HotKey.Slash, true, false, Intent.KeyOverlay),
            (HotKey.Q, false, false, Intent.TurnLeft), (HotKey.E, false, false, Intent.TurnRight),
            (HotKey.Z, false, false, null), (HotKey.Slash, false, false, null), (HotKey.G, false, true, null), (HotKey.Key1, false, true, null),
        };
        foreach (var (key, shift, ctrl, intent) in table) Assert.True(intent == Hotkeys.Of(key, shift, ctrl), $"{key} shift={shift} ctrl={ctrl}");
    }

    [Fact]
    public void Hot_key_names_are_godot_key_names()
    {
        // The Godot side maps with Enum.TryParse(keycode.ToString()); these are Godot.Key members as Godot 4.7 spells them.
        string[] godot = ["Key1", "Key2", "Key3", "Key4", "Key5", "Key6", "Key7", "Key8", "Key9", "Enter", "KpEnter", "Escape", "Shift",
            "Q", "E", "L", "M", "P", "G", "Z", "Tab", "Backtab", "Space", "Home", "End", "F1", "Slash", "Question"];
        Assert.Equal(godot.Order(), Enum.GetNames<HotKey>().Order());
    }
}
```

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter HotkeysTests`
Expected: compile errors: `HotKey`, `Intent` and `Hotkeys` do not exist.

- [ ] **Step 7: Write the hotkeys**

Create `client/src/Core/Hotkeys.cs`:

```csharp
namespace Tabletop.Client.Core;

/// <summary>What a key asks for (in-game UX spec → Hotkeys). The caller reads it in context: options or rows, Q/E only while holding a unit.</summary>
public enum Intent
{
    Option1, Option2, Option3, Option4, Option5, Option6, Option7, Option8, Option9, Confirm, Cancel, SingleModel, Sight,
    Measure, Ping, NextUnit, PreviousUnit, FocusCamera, UndoMove, ToggleLog, CameraTop, CameraLevel, KeyOverlay, TurnLeft, TurnRight,
}

/// <summary>The keys the game screen binds, named as Godot.Key names them so the Godot side maps by name. Not `Key`: that would
/// collide with Godot.Key in every file that imports both namespaces.</summary>
public enum HotKey { Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9, Enter, KpEnter, Escape, Shift, Q, E, L, M, P, G, Z, Tab, Backtab, Space, Home, End, F1, Slash, Question }

public static class Hotkeys
{
    public static Intent? Of(HotKey key, bool shift, bool ctrl) => (key, shift, ctrl) switch
    {
        (HotKey.Z, _, true) => Intent.UndoMove,
        (_, _, true) => null,
        (>= HotKey.Key1 and <= HotKey.Key9, _, _) => Intent.Option1 + (key - HotKey.Key1),
        (HotKey.Enter or HotKey.KpEnter, _, _) => Intent.Confirm,
        (HotKey.Escape, _, _) => Intent.Cancel,
        (HotKey.Shift, _, _) => Intent.SingleModel,
        (HotKey.L, _, _) => Intent.Sight,
        (HotKey.M, _, _) => Intent.Measure,
        (HotKey.P, _, _) => Intent.Ping,
        (HotKey.G, _, _) => Intent.ToggleLog,
        (HotKey.Tab, false, _) => Intent.NextUnit,
        (HotKey.Tab, true, _) or (HotKey.Backtab, _, _) => Intent.PreviousUnit,
        (HotKey.Space, _, _) => Intent.FocusCamera,
        (HotKey.Home, _, _) => Intent.CameraTop,
        (HotKey.End, _, _) => Intent.CameraLevel,
        (HotKey.F1 or HotKey.Question, _, _) or (HotKey.Slash, true, _) => Intent.KeyOverlay,
        (HotKey.Q, _, _) => Intent.TurnLeft,
        (HotKey.E, _, _) => Intent.TurnRight,
        _ => null,
    };
}
```

`Option1 + (key - Key1)` relies on both enums declaring their nine members first and in order.

- [ ] **Step 8: Run everything and check the client**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: every test passes (the two new classes: 6 + 2), the build succeeds, `check-client` prints nothing. The build
also proves `HotKey` does not collide with `Godot.Key` in `TableInput.cs`, `OrbitCamera.cs` and `GameScreen.Play.cs`.

- [ ] **Step 9: Commit**

```bash
git add client/src/Core/DecisionView.cs client/src/Core/Hotkeys.cs \
  tests/Tabletop.Client.Tests/DecisionViewTests.cs tests/Tabletop.Client.Tests/HotkeysTests.cs
git commit -m "Model the decision box's content and the hotkeys" \
  -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
