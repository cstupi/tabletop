# Themes, Settings and HUD Frame Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two selectable themes (`fantasy`, `scifi`) that skin every screen, a Settings dialog that stores the theme and
the play toggles, and one Hud frame that places every play part without overlap from 1280×720 up.

**Architecture:** `UiTheme`/`UiThemes` (Core) hold each theme as data; `ThemeBuilder` (Godot) turns one into a Godot
`Theme` set on the root window and the current screen, rebuilt on `Session.SettingsChanged`. `Ui` (Godot) exposes the
current theme's colours, icons and type-variation names, so Hud, panel and screen code holds no colour literal and
`./scripts/check-client` enforces it. `HudLayout` (Core) computes each part's slot from the viewport size and which parts
are visible; `GameScreen.Play` places parts by it.

**Tech Stack:** `net10.0`, C# 12, xUnit, Godot 4.7.2 Mono (C#); fonts from `google/fonts`; SVG art imported by Godot.

**Spec:** [`docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`](../../specs/2026-10-10-client-ux-and-themes-design.md),
sections *Themes*, *Settings*, *HUD frame*.

**Requires landed:** [`01-collision.md`](01-collision.md), [`01a-collision-server.md`](01a-collision-server.md),
[`01b-collision-client.md`](01b-collision-client.md), [`02-combat-log.md`](02-combat-log.md),
[`02a-combat-log-client.md`](02a-combat-log-client.md), [`03-unit-card.md`](03-unit-card.md) and
[`03a-unit-card-hud.md`](03a-unit-card-hud.md), on `main` after `7c4ec5d`. Plan 02a brings `Hud/CombatLogPanel`
inside `LogDrawer` (640 px, remembered filter and open state, the Animate checkbox still in the drawer) and plan 02
Task 6 rewrites `DiceTray` around `CombatLog.Latest`; plan 03a replaces `Hud/StatSheet` with `Hud/UnitCard`, its
colours in `Hud/CardColours`. Plan 05 ([`05-table-ux.md`](05-table-ux.md) and
its parts 2 and 3) lands after this one and consumes `HudPart.Roster`, `HudPart.Banner`, `HudPlacement`,
`GameScreen.Layout()`, `Ui`, `Variation` and the settings stored here.

## Global Constraints

- C# only, no GDScript. `client/src/Core/` stays Godot-free and test-driven; Godot nodes are checked by running the client.
- `.tscn`, `.tres` and `.import` files are editor-owned: the Godot `Theme` is built in C#, never as a `.tres`. Commit the
  `.import` files the editor writes for new assets, as `content/builtin/` does.
- Godot's import wipes `client/.godot/mono/temp/bin/`: run `dotnet build client/Tabletop.csproj` after every import.
- No dev tooling under `client/` (the editor scans it); no file over 500 lines. Fonts are SIL OFL 1.1; art is drawn here.
- After this plan, `client/src/Hud`, `client/src/Panels` and `client/src/Screens` hold no `new Color(`, no `Colors.`
  and no `Color X = new(…)`; colours come from `Ui` and `Variation`. Table code keeps `Palette`, whose team colours read
  the theme.
- Commit after every task, imperative one-line subject; the body ends with the two lines
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW`.

## Review Focus

| # | Failure mode | Test, in the owning task |
| --- | --- | --- |
| 1 | `ClientSettings.Equals` is hand-written and ignores the new fields, so a changed theme compares equal and round-trip tests pass vacuously | `Settings_round_trip_every_new_field` asserts each field after load and `Assert.NotEqual(Default, Default with { Theme = "fantasy" })` (Task 4) |
| 2 | A theme names a `res://` path whose file was never added or is misspelt; Godot loads null and the Hud falls back silently | `Every_theme_path_is_a_file_under_its_own_folder` maps each path to `client/` and asserts it exists (Task 3) |
| 3 | An unknown or hand-edited theme id in `settings.json` crashes the builder | `Unknown_theme_ids_resolve_to_scifi` and `Unknown_theme_in_settings_reads_scifi` (Tasks 3, 4) |
| 4 | Two Hud slots overlap at 1280×720 with the log open, or the log drawer covers the decision box or dice | `No_two_parts_overlap_at_any_visibility` over 3 sizes × 32 visibility sets (Task 6) |
| 5 | A colour literal survives the migration (including in plan 02/03 files) | `./scripts/check-client` fails on one; Task 8 runs it red first, then green |

## Task 1: Fonts and their licences

**Files:** Create the ten files Step 1 writes under `client/content/themes/{fantasy,scifi}/fonts/`, and `LICENSES.md` in each theme folder.

Pinned to `google/fonts` commit `bd8f81ddb5c74d5c8897b36ad88b440266245103`. Files are renamed on download (no `[` in
`res://` paths); OFL allows renaming files, and no font is modified, so Orbitron's Reserved Font Name is kept.

- [ ] **Step 1: Download and verify** (from the repo root; the shell loop is not checked in)

```bash
B=https://raw.githubusercontent.com/google/fonts/bd8f81ddb5c74d5c8897b36ad88b440266245103/ofl
get() { mkdir -p "$(dirname "$2")"; curl -sfL -o "$2" "$B/$1"; }
F=client/content/themes/fantasy/fonts S=client/content/themes/scifi/fonts
get 'cinzel/Cinzel%5Bwght%5D.ttf' $F/Cinzel-Variable.ttf;               get cinzel/OFL.txt $F/OFL-Cinzel.txt
get 'ebgaramond/EBGaramond%5Bwght%5D.ttf' $F/EBGaramond-Variable.ttf;   get ebgaramond/OFL.txt $F/OFL-EBGaramond.txt
get 'orbitron/Orbitron%5Bwght%5D.ttf' $S/Orbitron-Variable.ttf;         get orbitron/OFL.txt $S/OFL-Orbitron.txt
get rajdhani/Rajdhani-SemiBold.ttf $S/Rajdhani-SemiBold.ttf;            get rajdhani/OFL.txt $S/OFL-Rajdhani.txt
get 'jetbrainsmono/JetBrainsMono%5Bwght%5D.ttf' $S/JetBrainsMono-Variable.ttf; get jetbrainsmono/OFL.txt $S/OFL-JetBrainsMono.txt
sha256sum -c <<'SUMS'
f4d83d34d1f6c741193e4acf4b3dff9531e5a67b6aa65228d00a7db72a4e0f34  client/content/themes/fantasy/fonts/Cinzel-Variable.ttf
ef9512f92f6d579e5dc75af59a5a4b1b8b47d2eda89e00b954d44520e5369027  client/content/themes/fantasy/fonts/EBGaramond-Variable.ttf
f42db2dd16e642258e35782916eceb1dcdbea06fb958d77ad71dc5963587e8fd  client/content/themes/scifi/fonts/Orbitron-Variable.ttf
94bbd25a18ca665999feb05a537de9fd2b860dcfb78bbe9ca00270825bf235da  client/content/themes/scifi/fonts/Rajdhani-SemiBold.ttf
48715a42ec242c21e9f02692891e147d022299a52e48d5e413e1a942193ffeda  client/content/themes/scifi/fonts/JetBrainsMono-Variable.ttf
SUMS
grep -L 'SIL OPEN FONT LICENSE Version 1.1' client/content/themes/*/fonts/OFL-*.txt
```

Expected: five `OK` lines and no file printed by the `grep -L`. A mismatch stops the task.

- [ ] **Step 2: `LICENSES.md` per folder**: a table `| File | Font | Copyright | Licence | Source |`, one row per font
file: the copyright line is the first line of its `OFL-*.txt`; licence `SIL OFL 1.1, fonts/OFL-<Family>.txt`; source
`google/fonts@bd8f81d ofl/<family>/<upstream file name>`. Last line, fantasy: ``panel.svg`, `button.svg`, `dice/` and
`icons/` are drawn for this project.``; sci-fi: ``icons/` is drawn for this project.``

- [ ] **Step 3: Commit** `Add the fantasy and sci-fi theme fonts with their OFL licences` (trailer as in Global Constraints).

## Task 2: Theme art

**Files:** Create `client/content/themes/fantasy/{panel.svg,button.svg,dice/{1..6}.svg}`, `client/content/themes/{fantasy,scifi}/icons/{activated,objective,shaken,fatigued,settings}.svg`,
and the `.import` files the editor writes for them and the fonts. Sci-fi panels are `StyleBoxFlat` and its dice numerals.

- [ ] **Step 1: Fantasy nine-patches.** `panel.svg`, 64×64 (texture margin 12): `<rect width="64" height="64" rx="6" fill="#4A2F1B"/>`,
`<rect x="3" y="3" width="58" height="58" rx="4" fill="#6B4426"/>`, `<rect x="8" y="8" width="48" height="48" rx="2" fill="#E9DBB8" stroke="#B08D57" stroke-width="1.5"/>`.
`button.svg`, 64×32 (margin 10): `<rect width="64" height="32" rx="4" fill="#3A2414"/>`, `<rect x="2" y="2" width="60" height="28" rx="3" fill="#5E3B21"/>`,
`<rect x="3.5" y="3.5" width="57" height="25" rx="2" fill="none" stroke="#B08D57" stroke-width="1.5"/>`.

- [ ] **Step 2: Fantasy dice**, bone with dark pips. Each `dice/<n>.svg` is 64×64:
`<rect x="2" y="2" width="60" height="60" rx="10" fill="#E8DCC0" stroke="#8A7A5A" stroke-width="2"/>` plus one
`<circle r="6" fill="#2B2118"/>` per pip at: 1 (32,32); 2 (18,18) (46,46); 3 = 2 + (32,32); 4 (18,18) (46,18) (18,46)
(46,46); 5 = 4 + (32,32); 6 = 4 + (18,32) (46,32).

- [ ] **Step 3: Icons**, 32×32, `viewBox="0 0 32 32" fill="none" stroke-linecap="round" stroke-linejoin="round"`. The
shape is the same in both themes:

| Icon | Shape |
| --- | --- |
| `activated` | `<circle cx="16" cy="16" r="12"/><path d="M10 16 L14 21 L22 11"/>` |
| `objective` | `<circle cx="16" cy="16" r="12"/><circle cx="16" cy="16" r="6"/><circle cx="16" cy="16" r="1.5"/>` |
| `shaken` | `<path d="M16 4 L26 8 V16 C26 22 21 26 16 28 C11 26 6 22 6 16 V8 Z"/><path d="M16 6 L13 13 L18 17 L14 26"/>` |
| `fatigued` | `<path d="M10 5 H22 L16 16 L22 27 H10 L16 16 Z"/>` |
| `settings` | `<circle cx="16" cy="16" r="5"/><circle cx="16" cy="16" r="9"/><path d="M16 3 V7 M16 25 V29 M3 16 H7 M25 16 H29 M6.8 6.8 L9.6 9.6 M22.4 22.4 L25.2 25.2 M6.8 25.2 L9.6 22.4 M22.4 9.6 L25.2 6.8"/>` |

Fantasy (engraved line): the shape inside `<g stroke="#2B2118" stroke-width="2.5">`. Sci-fi (thin glowing line): the
shape twice, `<g stroke="#3FD0E0" stroke-width="4" stroke-opacity="0.35">` under `<g stroke="#3FD0E0" stroke-width="1.5">`
(no SVG filters: Godot's ThorVG importer does not blur).

- [ ] **Step 4: Import, then rebuild** (the import wipes the C# build output)

```bash
./scripts/godot --headless --import && dotnet build client/Tabletop.csproj
git status --short -uall client/content/themes | grep -c '\.import$'
```

Expected: build succeeds; `23` (fantasy 2 fonts, 2 nine-patches, 6 dice, 5 icons; sci-fi 3 fonts, 5 icons).
- [ ] **Step 5: Commit** `Draw the fantasy and sci-fi panels, dice and icons`.

## Task 3: `UiTheme` and `UiThemes`

**Files:** Create `client/src/Core/UiTheme.cs`, `client/src/Core/UiThemes.cs`, `tests/Tabletop.Client.Tests/UiThemesTests.cs`.

`ButtonText` is the one colour beyond the spec's list: the fantasy skin puts ivory text on dark-wood buttons over ink
text on parchment panels.

- [ ] **Step 1: Failing tests** — `tests/Tabletop.Client.Tests/UiThemesTests.cs`:

```csharp
namespace Tabletop.Client.Tests;   // using Tabletop.Client.Core;
public class UiThemesTests
{
    static readonly string ClientDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client"));
    [Fact] public void The_built_ins_are_fantasy_then_scifi() => Assert.Equal(new[] { "fantasy", "scifi" }, UiThemes.All.Select(t => t.Id));
    [Theory, InlineData(null), InlineData(""), InlineData("steampunk")] public void Unknown_theme_ids_resolve_to_scifi(string? id) => Assert.Same(UiThemes.SciFi, UiThemes.Resolve(id));
    [Fact] public void Every_theme_path_is_a_file_under_its_own_folder()
    {
        foreach (var t in UiThemes.All) foreach (var p in t.Paths)
        {
            Assert.StartsWith($"res://content/themes/{t.Id}/", p);
            Assert.True(File.Exists(Path.Combine(ClientDir, p["res://".Length..])), $"{t.Id}: {p} is missing");
        }
        Assert.Equal((15, 8), (UiThemes.Fantasy.Paths.Count(), UiThemes.SciFi.Paths.Count()));   // 2 fonts, 2 nine-patches, 6 faces, 5 icons; 3 fonts, 5 icons
    }
    [Fact] public void Every_theme_sets_fonts_six_teams_and_the_icons() => Assert.All(UiThemes.All, t =>
    {
        Assert.All(new[] { t.Display, t.Body, t.Numerals }, f => Assert.True(f.Size >= 12 && f.Path.EndsWith(".ttf"), f.Path));
        Assert.True(t.Teams.Count >= 6); Assert.All(new[] { "activated", "objective", "shaken", "fatigued", "settings" }, n => Assert.NotNull(t.Icon(n)));
    });
    [Fact] public void Teams_cycle_icons_ignore_case_and_faces_are_one_to_six()
    {
        var t = UiThemes.Fantasy;
        Assert.Equal(t.Teams[0], t.Team(1)); Assert.Equal(t.Teams[0], t.Team(0)); Assert.Equal(t.Teams[1], t.Team(t.Teams.Count + 2));
        Assert.Equal(t.Icon("shaken"), t.Icon("Shaken")); Assert.Null(t.Icon("pinned")); Assert.Same(t, UiThemes.Resolve("Fantasy"));
        Assert.Equal("res://content/themes/fantasy/dice/6.svg", t.Dice.FaceTexture(6)); Assert.Null(t.Dice.FaceTexture(7)); Assert.Null(UiThemes.SciFi.Dice.FaceTexture(3));
        Assert.Equal(new Rgba(1, 0, 0), Rgba.Hex("#FF0000")); Assert.Equal(0.85f, Rgba.Hex("#0D1620D9").A, 2);
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Client.Tests --filter UiThemesTests`. Expected: build error, `UiThemes` not found.

- [ ] **Step 3: `client/src/Core/UiTheme.cs`**

```csharp
namespace Tabletop.Client.Core;
/// <summary>A colour as four 0–1 channels ("#RRGGBB" or "#RRGGBBAA"). Core cannot name Godot's Color; Ui.Of converts.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static Rgba Hex(string hex)
    {
        var h = hex.TrimStart('#'); float C(int i) => Convert.ToInt32(h.Substring(i, 2), 16) / 255f;
        return h.Length switch { 6 => new(C(0), C(2), C(4)), 8 => new(C(0), C(2), C(4), C(6)), _ => throw new FormatException(hex) };
    }
}
public sealed record ThemeFont(string Path, int Size, int Weight = 400);
/// <summary>Clipped cuts the corners (Godot corner detail 1); a nine-patch replaces the flat box.</summary>
public sealed record PanelShape(int CornerRadius, int BorderWidth, int Padding, bool Clipped, string? PanelNinePatch = null,
    string? ButtonNinePatch = null, int PanelMargin = 12, int ButtonMargin = 10);
public enum DieFaceStyle { Pips, Numerals }
public sealed record DiceStyle(DieFaceStyle Style, Rgba Face, Rgba Mark, string? FaceDirectory = null)
{
    /// <summary>Null for numerals, or for a value off the die.</summary>
    public string? FaceTexture(int value) => FaceDirectory is { } d && value is >= 1 and <= 6 ? $"{d}/{value}.svg" : null;
}
/// <summary>A theme as data (spec → Themes). Icons is case-insensitive, keyed by condition name or marker kind.</summary>
public sealed record UiTheme(string Id, string Name, Rgba Background, Rgba Panel, Rgba PanelBorder, Rgba Text, Rgba DimText,
    Rgba ButtonText, Rgba Accent, Rgba Success, Rgba Failure, Rgba Warning, IReadOnlyList<Rgba> Teams,
    ThemeFont Display, ThemeFont Body, ThemeFont Numerals, PanelShape Shape, DiceStyle Dice, IReadOnlyDictionary<string, string> Icons)
{
    public Rgba Team(int ordinal) => Teams[(Math.Max(1, ordinal) - 1) % Teams.Count];
    public string? Icon(string name) => Icons.TryGetValue(name, out var path) ? path : null;
    public IEnumerable<string> Paths => new[] { Display.Path, Body.Path, Numerals.Path, Shape.PanelNinePatch, Shape.ButtonNinePatch }
        .Concat(Enumerable.Range(1, 6).Select(Dice.FaceTexture)).Concat(Icons.Values).OfType<string>().Distinct();
}
```

- [ ] **Step 4: `client/src/Core/UiThemes.cs`** (an unknown id resolves to Sci-fi; adding a theme is a value here and a folder)

```csharp
public static class UiThemes
{
    public const string DefaultId = "scifi";
    const string F = "res://content/themes/fantasy", S = "res://content/themes/scifi";
    static Rgba H(string hex) => Rgba.Hex(hex);
    static Dictionary<string, string> IconsIn(string dir) => new[] { "activated", "objective", "shaken", "fatigued", "settings" }.ToDictionary(n => n, n => $"{dir}/icons/{n}.svg", StringComparer.OrdinalIgnoreCase);
    public static UiTheme Fantasy { get; } = new("fantasy", "Fantasy",
        H("#C9B68E"), H("#E9DBB8"), H("#4A2F1B"), H("#2B2118"), H("#6E5E48"), H("#F3EAD3"),
        H("#8C6A2F"), H("#3F6B2A"), H("#8E2A1E"), H("#A3561A"),
        [H("#8E2A1E"), H("#2C4F86"), H("#3F6B2A"), H("#8C6A2F"), H("#5B3A78"), H("#2A6F6B")],
        new($"{F}/fonts/Cinzel-Variable.ttf", 20, 700), new($"{F}/fonts/EBGaramond-Variable.ttf", 18, 500), new($"{F}/fonts/Cinzel-Variable.ttf", 18, 700),
        new(4, 3, 12, false, $"{F}/panel.svg", $"{F}/button.svg"),
        new(DieFaceStyle.Pips, H("#E8DCC0"), H("#2B2118"), $"{F}/dice"), IconsIn(F));
    public static UiTheme SciFi { get; } = new("scifi", "Sci-fi",
        H("#0A0F14"), H("#0D1620D9"), H("#3FD0E0"), H("#DCEBF2"), H("#7A8C96"), H("#DCEBF2"),
        H("#3FD0E0"), H("#4BE08A"), H("#FF4D5E"), H("#FFB020"),
        [H("#FF5A4E"), H("#3F8CFF"), H("#4BE08A"), H("#FFC63F"), H("#B07CFF"), H("#3FD0E0")],
        new($"{S}/fonts/Orbitron-Variable.ttf", 18, 600), new($"{S}/fonts/Rajdhani-SemiBold.ttf", 20), new($"{S}/fonts/JetBrainsMono-Variable.ttf", 16, 500),
        new(8, 1, 10, true),
        new(DieFaceStyle.Numerals, H("#101A24"), H("#3FD0E0")), IconsIn(S));
    public static IReadOnlyList<UiTheme> All { get; } = [Fantasy, SciFi];
    public static UiTheme Resolve(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? SciFi;
}
```

- [ ] **Step 5: Run** `dotnet test tests/Tabletop.Client.Tests --filter UiThemesTests`. Expected: 7 passed.
- [ ] **Step 6: Commit** `Add UiTheme and the fantasy and sci-fi themes as data`.

## Task 4: Settings fields and `Session.SettingsChanged`

**Files:** Modify `client/src/Core/ClientSettings.cs`, `client/src/Core/Session.cs`, `tests/Tabletop.Client.Tests/SettingsTests.cs`, `SessionTests.cs`.

**Interfaces (produced):** `ClientSettings.Theme` (`string`, default `"scifi"`), `CameraFollow` (default `true`), `TurnBanner`
(default `true`), `RangeRings` (default `false`), as `init` properties so plan 02's positional fields stay untouched;
`Session.SettingsChanged : event Action`, raised by `UpdateSettings` after persisting.

- [ ] **Step 1: Failing tests.** Add to `SettingsTests`:

```csharp
[Fact] public void Settings_round_trip_every_new_field()
{
    var path = Path.Combine(_dir, "settings.json"); var d = ClientSettings.Default;
    Assert.Equal(("scifi", true, true, false), (d.Theme, d.CameraFollow, d.TurnBanner, d.RangeRings));
    Assert.NotEqual(d, d with { Theme = "fantasy" }); Assert.NotEqual(d, d with { CameraFollow = false }); Assert.NotEqual(d, d with { TurnBanner = false }); Assert.NotEqual(d, d with { RangeRings = true });
    var s = d with { Theme = "fantasy", CameraFollow = false, TurnBanner = false, RangeRings = true }; s.Save(path);
    var back = ClientSettings.Load(path);
    Assert.Equal(("fantasy", false, false, true), (back.Theme, back.CameraFollow, back.TurnBanner, back.RangeRings)); Assert.Equal(s, back);
}
[Theory, InlineData("""{ "theme": "steampunk" }"""), InlineData("""{ "animate": false }""")] public void Unknown_theme_in_settings_reads_scifi(string json)
{
    var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, json); var s = ClientSettings.Load(path);
    Assert.Equal(("scifi", true, true, false), (s.Theme, s.CameraFollow, s.TurnBanner, s.RangeRings));
}
```

Add to `SessionTests` `UpdateSettings_raises_SettingsChanged_after_persisting`: subscribe
`_s.SettingsChanged += () => seen.Add(_persisted[^1].Theme)`, call `_s.UpdateSettings(x => x with { Theme = "fantasy" })`,
`Assert.Equal(new[] { "fantasy" }, seen)`.

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Client.Tests --filter "SettingsTests|SessionTests"`. Expected: build error, `Theme` not defined.

- [ ] **Step 3: Implement.** Plan 02 Task 3 left the record as `ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds,
bool RememberMe, string ManagementUrl, string GameServerUrl, LogFilter LogFilter = LogFilter.All, bool LogOpen = false)` and
`Dto(…, string? GameServerUrl, string? LogFilter = null, bool? LogOpen = null)`. `ClientSettings`: the four `init` properties
(`Theme = UiThemes.DefaultId`, `CameraFollow = true`, `TurnBanner = true`, `RangeRings` false); `Dto` gains trailing
`string? Theme = null, bool? CameraFollow = null, bool? TurnBanner = null, bool? RangeRings = null` after plan 02's two;
`Load`'s `new ClientSettings(…)` adds `with { Theme = UiThemes.Resolve(d.Theme).Id, CameraFollow = d.CameraFollow ?? true, TurnBanner = d.TurnBanner ?? true, RangeRings = d.RangeRings ?? false }`;
`Save`'s `new Dto(…)` writes the four after `LogFilter.ToString(), LogOpen`; `Equals` adds the four; `GetHashCode` adds
`Theme` (plan 02 left six of `HashCode.Combine`'s eight arguments). `Session`: `public event Action? SettingsChanged;`,
raised as the last line of `UpdateSettings`.

- [ ] **Step 4: Run** the Step 2 command. Expected: all pass. Commit `Store the theme, camera follow, turn banner and range rings settings`.

## Task 5: `ThemeBuilder`, `Ui`, and the theme on every screen

**Files:** Create `client/src/Theme/{ThemeBuilder,Ui,DiceFaces}.cs`; modify `client/src/Main.cs`, `client/src/Table/Palette.cs`.
Namespace `Tabletop.Client.Theming` (a `Theme` namespace would shadow `Godot.Theme`). No Core logic: checked by running.

**Interfaces (produced):** `ThemeBuilder.Build(UiTheme) : Godot.Theme`; `Ui.{Current, Theme, Changed, Apply(UiTheme), Of(Rgba), Team(int), Accent, Success, Failure, Warning, Faded, Icon(string) : Texture2D?}`
(`Changed` is `static event Action?`);
`Variation.{Badge, Chip, DieFace, Banner, Dim, Accent, Success, Failure, Warning, Display, Numerals}` (string constants;
`Badge`, `Chip`, `DieFace` are `PanelContainer` variations, the rest `Label` variations);
`DiceFaces.Of(Die) : Control`.

- [ ] **Step 1: `ThemeBuilder.cs`** (Godot 4.7 C# API: `Theme.SetStylebox/SetColor/SetFont/SetFontSize/SetConstant/SetTypeVariation`)

```csharp
public static class ThemeBuilder
{
    public static Godot.Theme Build(UiTheme u)
    {
        var t = new Godot.Theme { DefaultFont = FontOf(u.Body), DefaultFontSize = u.Body.Size };
        var s = u.Shape; Color C(Rgba c) => Ui.Of(c);
        StyleBox panel = Box(s.PanelNinePatch, s.PanelMargin, s, C(u.Panel), C(u.PanelBorder), s.BorderWidth);
        foreach (var type in new[] { "PanelContainer", "Panel", "TooltipPanel", "PopupPanel", "PopupMenu", "AcceptDialog", "TabContainer" }) t.SetStylebox("panel", type, panel);
        StyleBox Btn(float light) { var b = Box(s.ButtonNinePatch, s.ButtonMargin, s, C(u.Background).Lightened(light), C(u.PanelBorder), s.BorderWidth); if (b is StyleBoxTexture x) x.ModulateColor = Colors.White.Lightened(light); return b; }
        foreach (var (state, light) in new[] { ("normal", 0f), ("hover", 0.15f), ("pressed", 0.3f), ("disabled", -0.2f) }) t.SetStylebox(state, "Button", Btn(light));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty()); t.SetStylebox("tab_selected", "TabContainer", Btn(0.3f)); t.SetStylebox("tab_unselected", "TabContainer", Btn(0f)); t.SetStylebox("tab_hovered", "TabContainer", Btn(0.15f));
        t.SetStylebox("normal", "LineEdit", Flat(s, C(u.Panel), C(u.PanelBorder), 1)); t.SetStylebox("focus", "LineEdit", Flat(s, C(u.Panel), C(u.Accent), 1)); t.SetStylebox("panel", "ItemList", Flat(s, C(u.Panel), C(u.PanelBorder), 1));
        foreach (var type in new[] { "Label", "CheckBox", "LineEdit", "ItemList", "TooltipLabel", "PopupMenu", "TabContainer" }) t.SetColor("font_color", type, C(u.Text));
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color" }) t.SetColor(name, "Button", C(u.ButtonText));
        t.SetColor("font_pressed_color", "Button", C(u.Accent)); t.SetColor("font_disabled_color", "Button", C(u.DimText)); t.SetColor("font_selected_color", "ItemList", C(u.Accent)); t.SetColor("font_selected_color", "TabContainer", C(u.ButtonText));
        Variant(t, Variation.Badge, "PanelContainer", "panel", Flat(s, C(u.Panel), C(u.Accent), 2));
        Variant(t, Variation.Chip, "PanelContainer", "panel", Flat(s, C(u.Panel) with { A = 0 }, C(u.Accent), 1, 4));
        Variant(t, Variation.DieFace, "PanelContainer", "panel", Flat(s, C(u.Dice.Face), C(u.Dice.Mark), 1, 2));
        foreach (var (v, c) in new[] { (Variation.Dim, u.DimText), (Variation.Accent, u.Accent), (Variation.Success, u.Success), (Variation.Failure, u.Failure), (Variation.Warning, u.Warning) }) LabelVariant(t, v, c: C(c));
        LabelVariant(t, Variation.Display, u.Display); LabelVariant(t, Variation.Numerals, u.Numerals, C(u.Dice.Mark)); LabelVariant(t, Variation.Banner, u.Display with { Size = 48 }, C(u.Accent));
        t.SetColor("font_outline_color", Variation.Banner, C(u.Background)); t.SetConstant("outline_size", Variation.Banner, 6);
        return t;
    }
    static void Variant(Godot.Theme t, string v, string baseType, string name, StyleBox box) { t.SetTypeVariation(v, baseType); t.SetStylebox(name, v, box); }
    static void LabelVariant(Godot.Theme t, string v, ThemeFont? f = null, Color? c = null)
    { t.SetTypeVariation(v, "Label"); if (f is not null) { t.SetFont("font", v, FontOf(f)); t.SetFontSize("font_size", v, f.Size); } if (c is { } col) t.SetColor("font_color", v, col); }
    static StyleBox Box(string? ninePatch, int margin, PanelShape s, Color bg, Color border, int width)
    {
        if (ninePatch is null) return Flat(s, bg, border, width);
        var b = new StyleBoxTexture { Texture = GD.Load<Texture2D>(ninePatch) }; b.SetTextureMarginAll(margin); b.SetContentMarginAll(s.Padding); return b;
    }
    static StyleBoxFlat Flat(PanelShape s, Color bg, Color border, int width, int? padding = null)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = s.Clipped ? 1 : 8 };
        b.SetBorderWidthAll(width); b.SetCornerRadiusAll(s.CornerRadius); b.SetContentMarginAll(padding ?? s.Padding); return b;
    }
    // Variable fonts at the theme's weight; a static font ignores the axis.
    static Font FontOf(ThemeFont f) => new FontVariation { BaseFont = GD.Load<FontFile>(f.Path),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = f.Weight } };
}
```

- [ ] **Step 2: `Ui.cs`** holds `Variation` (each constant equal to its name) and `Ui`: `Current` (starts `UiThemes.SciFi`),
`Theme`, `Changed`; `Apply(t)` sets both, clears the icon cache, raises `Changed`; `Of(Rgba c) => new Color(c.R, c.G, c.B, c.A)`;
`Team(o) => Of(Current.Team(o))`; `Accent`/`Success`/`Failure`/`Warning` likewise; `Faded = new Color(1, 1, 1, 0.45f)`;
`Icon(name)` caches `GD.Load<Texture2D>(Current.Icon(name))`, null when the theme has none.

- [ ] **Step 3: `DiceFaces.Of(Die d)`**: `PanelContainer { ThemeTypeVariation = Variation.DieFace, CustomMinimumSize = new(28, 28) }`,
`Modulate = d.Success ? Colors.White : Ui.Faded`, holding a `TextureRect` (`IgnoreSize`, `KeepAspectCentered`) of
`Current.Dice.FaceTexture(d.Raw)`, else a `Numerals` label of `d.Raw`; a `Numerals` label `→{d.Modified}` follows when it differs.

- [ ] **Step 4: `Main` and `Palette`.** `Main` is a `Node`, which breaks Control theme inheritance, so the theme goes
on the root window and on each screen. `Main.ApplyTheme()`: `Ui.Apply(UiThemes.Resolve(Session.Settings.Theme))`,
`GetTree().Root.Theme = Ui.Theme`, `if (_screen is Control c) c.Theme = Ui.Theme`,
`RenderingServer.SetDefaultClearColor(Ui.Of(Ui.Current.Background))`. `_Ready` calls it once the session is built, then
subscribes `Session.SettingsChanged += () => { if (UiThemes.Resolve(Session.Settings.Theme) != Ui.Current) ApplyTheme(); }`.
`Show` sets `screen.Theme = Ui.Theme` on each new screen before `AddChild`. `Palette.Team(int ordinal) => Ui.Team(ordinal);` and the `Teams` array goes.

- [ ] **Step 5: Verify.** `dotnet build client/Tabletop.csproj && ./scripts/check-client` (no output), then
`./scripts/client`: Login and Home draw in Orbitron/Rajdhani on dark cyan-bordered panels. Set `"theme": "fantasy"` in
the Windows `settings.json` and relaunch: parchment panels, wood buttons, Cinzel/EB Garamond.
- [ ] **Step 6: Commit** `Build the Godot theme from UiTheme and apply it to every screen`.

## Task 6: `HudLayout`

**Files:** Create `client/src/Core/HudLayout.cs`, `tests/Tabletop.Client.Tests/HudLayoutTests.cs`.

Slots, not sizes: a part fills its slot's width and takes at most its height, aligned to the slot's anchored edge
(Task 9). `Banner` and `EndMessage` are overlays (transient or modal) and exempt from the no-overlap rule. `Corner` is the
existing Home button and status line; the left column starts under it.

- [ ] **Step 1: Failing tests**

```csharp
public class HudLayoutTests   // using Tabletop.Client.Core;
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 } };
    static IEnumerable<HudVisible> All() => Enumerable.Range(0, 32).Select(i => new HudVisible((i & 1) != 0, (i & 2) != 0, (i & 4) != 0, (i & 8) != 0, (i & 16) != 0));
    [Theory, MemberData(nameof(Sizes))] public void No_two_parts_overlap_at_any_visibility(int w, int h)
    {
        foreach (var v in All())
        {
            var r = HudLayout.Of(w, h, v);
            Assert.All(r.Values, x => Assert.True(x.X >= 8 && x.Y >= 8 && x.Right <= w - 8 && x.Bottom <= h - 8, $"{v}: {x} leaves the margin"));
            var parts = r.Where(p => !HudLayout.Overlays.Contains(p.Key)).ToList();
            foreach (var a in parts) foreach (var b in parts) Assert.True(a.Key == b.Key || !a.Value.Overlaps(b.Value), $"{w}x{h} {v}: {a.Key} overlaps {b.Key}");
            Assert.Equal(v.LogOpen, r.ContainsKey(HudPart.LogPanel));
            if (r.TryGetValue(HudPart.LogPanel, out var log)) Assert.True(log.W == 640 && log.H >= 240, $"{v}: log {log}");
            if (v.Decision && v.Dice) Assert.True(r[HudPart.Dice].Bottom + 8 == r[HudPart.Decision].Y && r[HudPart.Dice].X == r[HudPart.Decision].X);
            if (v.Roster && v.UnitCard) Assert.True(r[HudPart.Roster].Bottom + 8 == r[HudPart.UnitCard].Y && r[HudPart.Roster].H == r[HudPart.UnitCard].H);
        }
    }
    [Fact] public void At_1280_the_open_log_sits_above_the_dice_and_beside_the_header()
    {
        var r = HudLayout.Of(1280, 720, new HudVisible(Roster: true, UnitCard: true, Decision: true, Dice: true, LogOpen: true));
        Assert.Equal((new HudRect(436, 512, 480, 200), new HudRect(632, 136, 640, 264), new HudRect(8, 56, 420, 324)), (r[HudPart.Decision], r[HudPart.LogPanel], r[HudPart.Roster]));
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Client.Tests --filter HudLayoutTests`. Expected: build error, `HudLayout` not found.

- [ ] **Step 3: `client/src/Core/HudLayout.cs`**

```csharp
namespace Tabletop.Client.Core;
public enum HudPart { Corner, Toasts, Header, Roster, UnitCard, Decision, Dice, LogButton, LogPanel, Banner, EndMessage }
public readonly record struct HudRect(float X, float Y, float W, float H)
{
    public float Right => X + W; public float Bottom => Y + H; public bool SpansX(HudRect o) => X < o.Right && o.X < Right;
    public bool Overlaps(HudRect o) => SpansX(o) && Y < o.Bottom && o.Y < Bottom;
}
public sealed record HudVisible(bool Roster, bool UnitCard, bool Decision, bool Dice, bool LogOpen);
/// <summary>Spec → HUD frame: an 8 px margin, each part anchored to its edge, no two overlapping from 1280×720 up.</summary>
public static class HudLayout
{
    public const float M = 8, Column = 420, CornerH = 40, HeaderW = 760, HeaderH = 96, ToastW = 360, ToastH = 120, DecisionW = 480,
        DecisionH = 200, DiceH = 96, LogW = 640, LogMaxH = 720, LogButtonW = 140, LogButtonH = 36, BannerH = 96, EndW = 480, EndH = 360;
    public static readonly IReadOnlySet<HudPart> Overlays = new HashSet<HudPart> { HudPart.Banner, HudPart.EndMessage };
    public static IReadOnlyDictionary<HudPart, HudRect> Of(float w, float h, HudVisible v)
    {
        var r = new Dictionary<HudPart, HudRect>();
        var corner = r[HudPart.Corner] = new(M, M, Column, CornerH);
        var toasts = r[HudPart.Toasts] = new(w - M - ToastW, M, ToastW, ToastH);
        float bandL = M + Column + M, bandR = toasts.X - M, hw = Math.Min(HeaderW, bandR - bandL);
        var header = r[HudPart.Header] = new(Math.Clamp(w / 2 - hw / 2, bandL, bandR - hw), M, hw, HeaderH);
        float top = corner.Bottom + M, colH = h - M - top, half = (colH - M) / 2;
        if (v.Roster) r[HudPart.Roster] = new(M, top, Column, v.UnitCard ? half : colH);
        if (v.UnitCard) r[HudPart.UnitCard] = v.Roster ? new(M, top + half + M, Column, half) : new(M, top, Column, colH);
        var button = r[HudPart.LogButton] = new(w - M - LogButtonW, h - M - LogButtonH, LogButtonW, LogButtonH);
        float left = v.Roster || v.UnitCard ? bandL : M, x = Math.Clamp(w / 2 - DecisionW / 2, left, button.X - M - DecisionW), floor = h - M;
        if (v.Decision) { r[HudPart.Decision] = new(x, floor - DecisionH, DecisionW, DecisionH); floor -= DecisionH + M; }
        if (v.Dice) { r[HudPart.Dice] = new(x, floor - DiceH, DecisionW, DiceH); floor -= DiceH + M; }
        if (v.LogOpen)
        {
            var span = new HudRect(w - M - LogW, 0, LogW, h);
            float logTop = new[] { toasts, header }.Where(span.SpansX).Max(o => o.Bottom) + M;
            float logBottom = (v.Decision || v.Dice) && span.SpansX(new HudRect(x, 0, DecisionW, h)) ? floor : button.Y - M;
            var lh = Math.Min(LogMaxH, logBottom - logTop);
            r[HudPart.LogPanel] = new(span.X, logBottom - lh, LogW, lh);
        }
        r[HudPart.Banner] = new(M, h / 3 - BannerH / 2, w - 2 * M, BannerH);
        r[HudPart.EndMessage] = new(w / 2 - EndW / 2, h / 2 - EndH / 2, EndW, EndH);
        return r;
    }
}
```

- [ ] **Step 4: Run** the Step 2 command. Expected: 4 passed. Commit `Lay out the Hud frame in HudLayout`.

## Tasks 7–10

In [`04a-themes-hud.md`](04a-themes-hud.md): the Settings dialog (7), colour literals to theme tokens and the gate (8),
placing the Hud by `HudLayout` (9), verification and documents (10).
