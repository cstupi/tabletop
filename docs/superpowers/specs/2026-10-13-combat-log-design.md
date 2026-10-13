# Combat log

The in-game combat log shows, for every roll, exactly what was rolled and what it was compared against, and drops every line
that is not a roll, an action or a change to the table. It replaces the combat-log section of
`2026-10-10-client-ux-and-themes-design.md` (→ Combat log); that spec's drawer, filters, dice tray and Undo stand.

## Why

The owner's first two-player game (`01a126db-1bb0-7e4c-bc9a-1d5602bdff14`, 1261 events) logged 58 rolls. The rest were
questions, answers, question closes, step completions with nothing in them and sequence starts and finishes: a charge with no
roll writes 17 lines. A roll showed modified dice (`2 → -3 fail`) where p.5 judges the face, did not say which rule made a
modifier (Rending's AP(+4) and the weapon's AP(1) both read `AP`), and did not say which dice set off a rule.

## Rulebook

- p.5: *"add or subtract the value from the roll and the new value counts as the final result, however a roll of 6 always counts
  as a success and a roll of 1 always counts as a fail, regardless of how much it is being modified by."*
- p.8: Roll to Hit is *"as many Quality tests as attacks. Each successful roll counts as a hit"*; Roll to Block: *"Each success
  counts as a blocked hit, and all failed rolls cause one wound each"*.
- p.10: the morale test is a Quality test. p.11: dangerous terrain, *"if the result is 1, then the unit takes one automatic
  wound"*. p.13 AP(X): *"Targets get -X to Defense rolls"*. p.14 Rending: *"on unmodified results of 6 to hit, those hits get
  AP(+4)"*; Furious, Relentless and Surge deal 1 extra hit on an unmodified 6, *"only the original hit counts as a 6 for special
  rules"*. p.14 Regeneration: *"roll one die for each. On a 5+ it is ignored."*

## The log

Three kinds of line, newest at the bottom.

**Action.** One header per action frame (a `sequence_started` with an action), its lines indented beneath it:

```
R2 · Team 1 · [unit] · Charge → [target] · moved 7.5"
R2 · Team 2 · [unit] · Rush · moved 12"
R2 · Team 1 · [unit] · Hold → shoots [target]
```

`moved` is the farthest any model of the actor moved in the action, to 0.5". A spell cast inside an action is a nested header,
`Cast [spell] → [target]`, with its casting roll and its hits beneath it.

**Roll.** One line per `RollRecord`:

```
Hit · Quality 3+ · 8 dice: ⚁ ⚅ ⚀ ⚅ ⚅ ⚀ ⚄ ⚀ · 4 hits (3 sixes: Rending)
Block · Defense 5+ · 3 hits at AP(1) + Rending AP(+4) → needs 6 · ⚅ ⚁ ⚀ · 1 blocked, 2 wounds
Block · Defense 5+ · 1 hit at AP(1) → needs 6 · ⚀ · 0 blocked, 1 wound
Regeneration · 5+ · 3 wounds: ⚀ ⚅ ⚀ · 1 ignored, 2 wounds stand
Morale · Quality 4+ · ⚀ · failed → Shaken
Casting · 4+ (+1 token) · ⚅ · cast
Dangerous terrain · 1 fails · ⚁ ⚀ · 1 wound
Roll-off · Team 1 ⚃ · Team 2 ⚀ · Team 1 wins
```

- **Faces only.** A die shows its raw face. The modifiers fold into the target instead: the printed value (`Defense 5+`), each
  change with the rule behind it (`cover +1`, `AP(1)`, `Rending AP(+4)`, a spell's `-1`), then `needs N`. Past 6 it reads
  `needs 6` and below 2 `needs 2`, by p.5. With no modifier the `needs` part is left off.
- **What the dice set off.** A die that triggered a rule is counted on the line: `(3 sixes: Rending)`, `(2 sixes: Furious +2
  hits)`. The hit total is the successes plus the extra hits, so the line adds up.
- **Pass and fail** are shown by the die (bright or faded, per theme), not by words. The result uses p.8's words: hits,
  blocked, wounds; a single-die test reads passed or failed, then its consequence.
- **Rerolls and edits** stay on the roll's line: the faces now standing, with `rerolled ⚀ → ⚄` or `set by hand ⚀ → ⚅`.
- A roll with no threshold and no required face (a property's own roll) shows its faces and no result.

**Change.** What a step did to the table, after the rolls that caused it: `2 models removed`, `1 model wounded`, `Shaken`,
`Shaken cleared`.

**Outside any action:** `Round 2`; deployment, one line per unit placed; an objective seized or contested; a menu edit, tagged
`by hand`; `Undone to: [the action's header]`, the undone lines struck through; the game's end.

**Not in the log:** `ask_opened`, `ask_answered`, `ask_closed`, a step completion with no roll and no change,
`sequence_started` without an action, `sequence_finished`, `sequence_abandoned`, `turn_started`, `turn_ended`, and a passed
react window. The events stay in the log the server keeps; the combat log does not draw them.

**Clicking a line** expands it under the line: Undo to here, Reroll, Set die, +1, the rule chips with their text
(`StepCompleted.Applied`). Clicking it again collapses it. The filters (All, Rolls, My units, This unit), the drawer, the
follow-the-newest scrolling and the dice tray (the latest roll's line) are unchanged.

## What the server adds

Two optional fields on the wire. A log written before them reads as before, with no rule names on modifiers and no triggers
on dice.

- **`RollModifier.Via`** (string, optional): the rule that granted the property whose effect made this modifier, when it was
  granted. Rending's hits carry `AP` with `Via` `Rending`; a weapon's own AP has none. `RollEffects.Attributed` reads it from
  `LiveSet`'s `GrantedBy`; a per-die grant applied with `LiveSet.WithGranted` for a block group carries its grantor, which the
  `granted_` key of `score-hits` names alongside the entry.
- **`Die.Triggered`** (list of string, optional): what this die set off, as `Rule` or `Rule +N hits`, written when the hit roll is
  recorded, from `RollEffects.PerDie` on that die. It names the rule (Rending, Furious, Relentless, Surge) that acted on
  the die, so the client counts sixes by rule without knowing any rule.
- A threshold's `ThresholdBasis` (stat, printed value, changes) is already on the wire and is the `Quality 3+` / `Defense 5+`
  part of the line.

The rules stay on the server: the client reads names and numbers from the record and composes words. It names no rule.

## Where it lands

- `libs/Tabletop.Protocol`: `RollModifier.Via`, `Die.Triggered`; the viewer's generated types regenerated
  (`TABLETOP_WRITE_GENERATED=1`).
- `server/game`: `RollEffects`, `GfAttack` (`score-hits` keys, `roll-to-block` groups), `Roller` / `StepContext.Roll` for the
  die triggers. Tests in `Gf*Tests` on a Rending weapon with AP(1) and a Furious charge.
- `client/src/Core`: `CombatLog` and `RollEntry` produce the three line kinds and the dropped set; `LogText` keeps its words for
  the lines outside actions. `CombatLogTests` against a recording of `01a126db` seq 299–307 and a sim game with Furious,
  cover, a spell modifier, a reroll and an edit.
- `client/src/Hud/CombatLogPanel.cs`, `DiceTray.cs`: one line per entry, expand on click.
- `viewer/src/lib/live/logLines.ts` reads the same records; it is not changed by this spec.

## Out of scope

- The six rolls that still decide in the rolling step (handoff → Open items from the defect fixes).
- Dice edits on a cast, a morale test or score-hits.
- Which entries "Undo to here" offers (an open owner question).
