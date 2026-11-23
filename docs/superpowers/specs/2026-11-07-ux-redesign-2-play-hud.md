# UX redesign, part 2: the play Hud

Index and principles: `2026-11-07-ux-redesign-design.md`. The play screen from deployment to the end, and the replay. `HudLayout`'s
slots stay (8 px margin, 420 px left column, 480 px decision column, 760×96 header, no overlap from 1280×720); each part redraws inside
its slot. The viewer's play page takes the same parts (part 3 → Viewer).

## Frame

```
┌ ⌂ ────────────────────┐ ┌─────────── HEADER ──────────────┐            ┌ toasts ┐
│ ROSTER                 │ │ round · teams · acting unit     │            └────────┘
│  team band, rows       │ │ steps · waiting                 │
│                        │ └─────────────────────────────────┘
├────────────────────────┤
│ UNIT CARD (or terrain) │                 the table                      ┌ DICE TRAY ─────────┐
│                        │                                                 ├ DECISION BOX ──────┤
│                        │                                                 │  the Ask as a card │
│                        │                                                 │                    │
└────────────────────────┘                                                 └────────────────────┘
(replay: REPLAY BAR here)                       loading strip           [ ↶ Undo ⌃Z ] [ ≡ Log  G ]
```

The corner's Home button becomes the `home` icon with the game name beside it, as today, with the status chip.

## Header

Today (`12-play.png`, `18-ask-1.png`): four text lines, `Round 4 · Team 1's turn   Grimdark Future v3.5.1 ⚙`, `Team 1 · 0 VP · all
activated`, `Team 2 · 0 VP · all activated`, `Red unit 1 · Hold   Shoot`, `Your move: declare targets` in warning colour. Whose turn it
is, which team is mine and what the game waits for are all words in the same weight.

Size: M (`HeaderView` gains the tiles' data; `HeaderBar` redrawn).

```
┌───────────────────────────────────────────────────────────────────────────┐
│ ⟳ ROUND 2        ▌TEAM 1  3 VP  ●●●○○▐ ▶   ▌TEAM 2  1 VP  ●●○○○▐      ⚙  │
│ ➔ Red Warriors · Advance      ✓ Move   ● Shoot   ○ Melee   ○ Morale        │
│ ⏳ Team 2 · removing casualties                                            │
└───────────────────────────────────────────────────────────────────────────┘
```

- Line 1: the round as `⟳ ROUND 2` in the display font; one tile per team: the band, `TEAM N`, the score as a large numeral with `VP`,
  then the activation dots, one per unit with a model on the table, filled when it has acted this round (`k not activated` today's
  count); more than twelve units draws `k/N` instead. The acting team's tile carries the `▶` turn mark at its edge and a brighter border;
  my team's tile carries the `player` icon. The rule set name moves to the corner's tooltip; the gear stays at the right.
- Line 2: the acting unit with its action's icon (`hold`, `advance`, `rush`, `charge` by the option label the activation answered; the
  log already names the action) and the step strip as today (done ✓, current ●, rest ○), each step's icon when the theme has one.
- Line 3: the waiting line with the hourglass and the waiting team's band. When the Ask is mine the line reads `▶ Your move` in my team's
  colour and the decision box's title band lights; the orange goes.
- After the end: line 1 keeps the tiles with the final scores; line 2 `✓ Game over · Team 1 wins`, a button that reopens the modal.
- Every line is one line. Each of the first two draws the fullest `HeaderFit` its slot holds: Compact drops the step strip's words but
  the current step's (each step's word its tooltip), the `Team 2 activating:` lead (the ▶ tile says whose), `VP` and the objectives'
  chip frames; Tight also reads the round as its number (`⟳ 3`), and moves the objectives to line 2 only when line 1 still overflows.
  Line 3 is cut with an ellipsis, the whole line its tooltip. `HudLayout`'s header slot takes the lines' actual height.

## Roster

Today (`13-play-unit-card.png`): `Team 1` / `Team 2` as coloured words, a row per unit with a tick and wound pips. Size: M.

```
┌▌ TEAM 1  ★ you ─────────────────────── ⌄ ┐    band, icon, collapse (R)
│  ▶ Red Warriors         ○   ●●●●●    ⚡   │    ▶ the acting unit; ○ not yet activated; pips; condition icons
│    Red unit 2           ✓   ●●○       ☠  │    ✓ activated; Shaken as its icon
│    Red unit 3           ✓   ●●●           │
│    Red unit 4  in Wagon      —            │    dim: off the table; "in Wagon", "Reserve: Ambush" as today
├▌ TEAM 2 ──────────────────────────────────┤
│    Blue unit 1          ✓   ●○○           │
└───────────────────────────────────────────┘
```

- Rows sort: units that may still act first, then activated, then off the table (dim), each by name; the acting unit's row carries `▶`
  and the selected unit's row the selection colour at its edge. Clicking centres the camera as today; `▸` on hover opens the roster
  menu (Set aside, Put in transport, …) in place of the right-click only.
- The team header: band, icon, name, `★ you` on mine (every team I hold), the collapse chevron.

## Unit card

Today: `Red unit 1 · 3/3 models · Wounds 3/3`, two bordered badges `Quality 4+` `Defense 4+`, a chip `Activated: Hold`, `3/3 Red`, then
the weapons table. Size: M.

```
┌▌ Red Warriors ────────────────────────── 👁 ┐    band; the eye centres the camera on it
│  ●●●●○  5 models · 2 wounds left on Sgt       │    pips per model; the wound note only when a model has more than one
│  🎯 Q 4+    🛡 D 4+     ✓ Advanced   ☠ Shaken  │    stat badges with icons; state chips with icons
│  ┌ MODELS ────────────────────────────────┐   │
│  │ ●●●● Warriors                           │   │    one row per group, as today; click cycles the group's models
│  │ ●    Sergeant · Plasma Pistol   ●●○ 2/3 │   │
│  └─────────────────────────────────────────┘   │
│  WEAPON              RANGE  A   AP  RULES      │
│  Rifle ×4            24"    1   –   –          │
│  Plasma Pistol ×1    12"    1   2   –          │
│  RULES  ⟨Furious⟩ ⟨Tough(3)⟩                    │    chips with hover text, as today
│  ⚠ Shaken: may only Hold                       │    the Ask's note, as today
└─────────────────────────────────────────────────┘
```

- The first line is the name alone; the counts become pips and a short note. Quality and Defense keep their printed values with the
  theme's `quality` and `defense` icons. The hovered-enemy strip above the card keeps its content and takes the enemy's band.
- A terrain piece in the slot draws the terrain card of part 1.

## Decision box

Today (`18-ask-1.png` to `25-move-ask.png`): every Ask is a title line, a column of full-width buttons with the key in the label
(`End the round (Enter)`, `Commit move (Enter)`, `Clear targets (Esc)`), then a dim hint line repeating the keys (`Enter: End the round`;
`Click a target · Click a chip: that group alone · Enter: fire · Esc: clear`). An activation offers `1. Red unit 1: Hold`, `2. Red unit
1: Advance`, `3. Red unit 1: Rush`, `4. Red unit 1: Charge` as four identical buttons. An Ask for the other team (`Team 2: remove
casualties`) is drawn in the same box as mine. The move box prints the allowance twice (`Move up to 12"` and `Move up to 12.0" (used
0.0") · Alt: place freely.`).

Size: L (`DecisionView` gains the card's title, the option tiles grouped by unit, the allowance bar and the key caps; `DecisionBox`
redrawn; the viewer's `DecisionBox.svelte` the same).

**The card.** Every Ask of mine is one card: a title band in my team's colour with the Ask's icon and its title in the display font;
the body per mode below; the primary action as the one lit button at the foot with its key cap; secondary actions flat beside it. A
refusal is a line in failure colour above the buttons, as today. The hint line goes: the keys are on the buttons, the rest is the key
overlay (`⌨` at the card's corner opens it on this Ask's keys).

**Not mine.** A quiet band, not a card: `⏳ Team 2 · removing casualties` with that team's band. Nothing in it is a control.

**Activate** (`activate`, `act_or_end_phase`):

```
┌▌ ▶ ACTIVATE A UNIT ────────────────────────── ⌨ ┐
│  click a glowing unit, or:                      │
│  ┌ Red Warriors  ●●●●○ ────────────────────┐    │   one group per unit with options, its pips
│  │ [ ⏸ Hold ¹ ] [ ➔ Advance ² ] [ ⏩ Rush ³ ] [ ⚔ Charge ⁴ ] │   tiles: icon, the action word, the key cap
│  └─────────────────────────────────────────┘    │
│  ┌ Red unit 2  ●●○ ─────────────────────────┐    │
│  │ [ ⏸ Hold ⁵ ] [ ➔ Advance ⁶ ] …            │    numbering continues across groups, as the keys do
│  └─────────────────────────────────────────┘    │
│  ⚠ Red unit 2 is Shaken: may only Hold          │   the option's Note once, under its group
│  [ End phase ]                                  │   the `act_or_end_phase` extra option, flat
└─────────────────────────────────────────────────┘
```

Clicking a glowing unit narrows the card to that unit's group, as today. The tile's icon comes from the action word after the colon
in the option label (`hold`, `advance`, `rush`, `charge`; a label with no known word draws the `activation` icon). Hovering a tile lights
the unit on the table, as today.

**Confirmation** (`confirm`, `end_round`): the title, the prompt, one primary `[ End the round ⏎ ]`. A sealed Ask's reveal draws each
team's choice as that team's band and chip (`▌TEAM 1 ▐ continue`), not the sentence `Revealed: Team 1 chose continue, Team 2 chose
continue` (`29-ended-game.png`). The reveal shows only while no card of the viewer's is up; the next card clears it.

**Move** (`move`, `disembark`):

```
┌▌ ➔ ADVANCE · Red Warriors ─────────────────── ⌨ ┐
│  ━━━━━━━━●━━━━━━━━━━━━━━━   7.5" of 12"          │   the allowance bar: the farthest ghost's path over the allowance; the cap when difficult terrain applies
│  ⚠ 1 model beyond 12"                            │   only while a gate reason holds (`MoveRuler.Gate`, `ContactCheck`); strict disables Commit
│  [ ✓ Commit ⏎ ]   [ ✦ Cast here C ]   [ Pass ]   │   Cast here / Stop here only when the Ask is Stoppable; Pass only with AllowPass
└──────────────────────────────────────────────────┘
```

The bar is the readout `MoveRuler` already computes; the words `Move up to 12.0" (used 0.0")` go. Alt's free placement is a line in the
key overlay, not here. A minimum distance draws as a mark on the bar.

**Deploy** (`deploy`, `deploy_scouts`, `deploy_ambusher`, a reserve's `place`): the shelved units as rows with their pips and the
`army` icon, click or key picks; once picked, the card reads `PLACE · Red Warriors` with `[ ✓ Place ⏎ ]` and the gate line as the move's.

**Targets** (`declare_targets`):

```
┌▌ 🎯 SHOOT · Red Warriors ────────────────────── ⌨ ┐
│  ⟨ Rifles ×4 → ▌Blue unit 1 ⟩  ⟨ Plasma ×1 · holds fire ⟩ │   chips: the group, an arrow and the target's band when aimed
│  [ ✓ Fire ⏎ ]                     [ Clear  Esc ]  │
└───────────────────────────────────────────────────┘
```

The chip reads the target's team colour and the line on the table matches it, as today. `holds fire` stays dim.

**Casualties** (`remove_casualties`): the title `REMOVE 2 MODELS` or `ASSIGN 3 WOUNDS`, a ring counter `1 of 2`, the picked models as
pips lighting as they are clicked, `[ ✓ Confirm ⏎ ]` disabled until the count matches, `[ Auto ]` flat.

**Pick a unit** (`charge_target`, `takedown_target`, `spell_targets`, `shoot_or_not`'s unit): the prompt and the glowing units; options
with a unit draw that unit's name with its team band as a tile.

**Options with no unit** (`shoot_or_not`, `react`, `strike_back`, `cast`, a rule's choice): tiles in a row when four or fewer fit, else
rows; the key cap on each.

**Tokens** (`spend_tokens`): a stepper per caster with the caster's name and pips of tokens, as today, in the card.

**Game over** (`game_over`): the modal (below) and the card both offer End and Continue.

## Dice tray

Today (`19-ask-2.png`): one line, `Dangerous terrain · 1 fails ⟨6⟩ passed`, the dice as small hex chips. Size: S.

```
┌ 🎲 Block · Defense 4+ ──────────────────────────┐
│   ⚂  ⚄  ⚀      1 blocked · 1 wound              │   faces at 40 px, a pass in success colour, a fail in failure colour; the result in the outcome's colour
└──────────────────────────────────────────────────┘
```

The title stays `RollWords`'s; the faces grow and take the outcome colour; clicking opens the log at the entry, as today.

## Log and undo

Today (`14-play-log.png`): the drawer with a filter dropdown, *Detailed* and *Copy*; a block header `R4 · Team 1 · Red unit 1 · Hold →
shoots Blue unit 1` with `Copy` and `Undo to here` as text on every header and change line; roll rows with hex dice. The buttons at the
foot read `Undo (Ctrl+Z)` and `Log (G)`. Size: S.

```
┌ ≡ LOG   [ All ▾ ]  ☐ Detailed                    ⧉ ┐   ⧉ copies the whole log
│ ▌R4 · Red unit 1 · Hold → shoots Blue unit 1   ↶ ⧉ │   band in the actor's team colour; icons on hover only
│   ▸ Hit · Quality 4+        ⚀ ⚄ ⚃   2 hits          │   faces coloured by pass/fail
│   ▸ Block · Defense 4+      ⚅ ⚂     1 blocked, 1 wound │
│   −1 model                                          │
│ ▌R4 · Blue unit 1 · wounds                      ↶   │
│   ▸ Morale · Quality 4+     ⚅       passed          │
└─────────────────────────────────────────────────────┘
                              [ ↶ Undo ⌃Z ]  [ ≡ Log G ]
```

- `Undo to here` and `Copy` become the `undo` and `copy` icons at the row's right, shown on hover (and always on a touch device); the
  tooltip carries the words. The struck-through undone rows, the row highlight, Detailed and the selectable text stay.
- The foot buttons: icon, word, key cap. Undo's tooltip keeps `Undo: ` and the action's words; the *Undo dice rolls?* dialog
  (`17-undo-dialog.png`) lists the rolls as the log draws them, `[ Undo ]` primary, `[ Cancel ]` flat.

## Banners, toasts, floating results

Unchanged in content. The turn banner carries the team's band and icon (`▌TEAM 2's turn`); the roll-off banner draws each team's die
in that team's colour. Toasts take the `warning` or `failure` icon by kind. Floating results (`-2 models`, `Shaken`) keep their words
and take the condition's icon.

## Game over

Today: a centred modal, *Game over*, the result, each team's score as `Team 1  0`, and Stay / Leave (or End / Continue while the Ask is
open). Size: S.

```
┌──────────────── ✓ GAME OVER ────────────────┐
│          Team 1 wins · 4 rounds              │
│   ▌TEAM 1  3 VP ▐        ▌TEAM 2  1 VP ▐     │   the header's tiles, larger
│   mission result: objectives 2–1             │   the reason, as today
│   [ Stay ]                    [ ⌂ Leave ]    │
└──────────────────────────────────────────────┘
```

While the `game_over` Ask is open the same frame reads `END OR CONTINUE?`, the prompt, `[ End ]` and `[ Continue ]`, then *Waiting for
Team 2* with its band once mine has answered.

## Replay bar

Today (`32-replay-fantasy.png`): `«R «T ‹A ‹ Play › A› T» R»`, a scrubber, `1×`, the caption `Before round 1 · 0 of 906`. Size: M.

```
┌ ▶ REPLAY ─────────────────────────────────────────────┐
│  ROUND    TURN    ACTION   STEP          STEP  ACTION  TURN   ROUND  │
│  [ ⏮ ]   [ ⏮ ]   [ ⏮ ]   [ ◀ ]  [ ▶ Play ]  [ ▶ ]  [ ⏭ ]  [ ⏭ ]  [ ⏭ ]  │   icons, the unit under each in small caps
│  ━━━━|━━━━━━|━━━━●━━━━|━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━   1× ▾  │   ticks at round boundaries (`ReplayTimeline.Stops`)
│  Round 2 · Team 1 · Red unit 1 · Charge → Blue unit 2            │   the caption: the position's block header
└───────────────────────────────────────────────────────────────────┘
```

The slot's height grows to the three rows (`HudLayout.ReplayH` takes the measured height, as it does today). The header reads
`Replay` in its chip; the roster and unit card draw as in play.

## Key overlay

Today (`15-key-overlay.png`): a full-screen two-column table of every key, over the Hud. Size: M.

```
┌ ⌨ KEYS ──────────────────────────────────────────────────┐
│  NOW · ADVANCE          │  CAMERA                          │   the first group is the open Ask's keys
│  ⏎  commit the move     │  W A S D  move    Q E  orbit     │
│  C  stop here to cast   │  wheel  zoom      Home End  top, level │
│  Esc  reset             │  Space  to the active unit  Tab  next of mine │
│  1–5  rows while holding│                                  │
│  Q E  turn held models  │  MEASURE                         │
│  ⇧ click  pick a model  │  Ctrl  ruler from the point     M  tool   H  2D/3D │
│  Alt  place freely      │                                  │
│  PgUp PgDn  level       │  TABLE                           │
│                         │  L  sight check   V  range rings   B ⇧B  hitboxes   R  roster   G  log   ⌃Z  undo   P  ping │
└───────────────────────────────────────────────────────────┘
```

A panel, not a sheet: groups with captions, key caps, four words a line. `Hotkeys` already knows the context; the first group is its
intents for the open Ask (`Hotkeys.Of`). F1, `?` and the card's `⌨` open it; any key closes it.

## Menus

The right-click menus (`PieceMenu`, `MenuModel`) keep their items and gain the item's icon (`lock`, `unlock`, `remove`, `army` for
transports, `undo`). The roster row's `▸` and the header team tile's `▸` open the same menus as their right-clicks.

## Where it lives

- `client/src/Core/`: `HeaderView` (team tiles: score, dots, acting, mine), `RosterView` (sort order, the acting row), `UnitCardContent`
  (pips, badges), `DecisionView` (card title and icon, option groups by unit with the action word, the allowance bar, key caps, the quiet
  band for an Ask not mine), `ReplayBarView` (new: the labelled groups and the round ticks), `KeyOverlayView` (new: the groups, the
  open Ask's group first), `LogRows` (hover actions).
- `client/src/Hud/`: `HeaderBar`, `Roster`, `UnitCard`, `DecisionBox` (+ `.Move`, `.Picks`), `DiceTray`, `CombatLogPanel`, `UndoButton`,
  `EndMessage`, `ReplayBar`, `KeyOverlay`, `Toasts`, `TurnBanner`: each draws its Core model with the shared `Variation`s.
- `client/src/Theme/`: `Ui` gains the icons and `Variation.TeamBand`, `StatusChip`, `KeyCap`, `Card`, `Tile`, `Pips`, `Quiet`.
- Nothing new on the wire. The viewer ports each Core change with its test (`viewer/AGENTS.md` → Conventions), and `calls.json` keeps
  every send: the redesign changes what is drawn, never what is sent.
