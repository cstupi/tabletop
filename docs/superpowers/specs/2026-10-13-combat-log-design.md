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

**Attack.** An attack's rolls (its hit rolls, Impact's roll, its blocks and its rolls per wound) are one line, the hits that reached the
block, the wounds dealt and the cover verdict:

```
Shooter Mob → Death Walkers  H: 19  W: 9  Cover: Y
Brute Mob → Death Walkers (melee)  H: 7  W: 3  Cover: N
Red unit 1 → Blue unit 1 (Impact)  H: 2  W: 1  Cover: N
Daemons of Change unit 1 → Targets unit 1 (Mutating Inferno)  H: 3  W: 2  Cover: N
Red unit 1 → Blue unit 1  H: 7  W: 3  Cover: Y
Regeneration 5+: rolled 6 2 3 1, 1 ignored, 3 wounds stand
```

- **Which rolls are one attack.** A block or a roll per wound belongs to the frame that pushed the `resolve-hits` frame above it
  (`attack-sequence`, `spell-hits`, `rule-hits`, or the `melee` frame for Impact); a hit roll to its own frame. Consecutive rolls
  of one attack, live or undone alike, draw as one line. A group with neither a hit roll nor a block (Ravage's dice, a self-wound's
  Regeneration) reads as rolls.
- **H** is the dice the attack's blocks rolled (every hit that reached them, bonus hits and multipliers included); with no block,
  the hits its rolls scored with the extra hits their faces set off, times `hit_multiplier`. **W** is the wounds the attack dealt:
  the blocks' failed dice and the extra wounds their faces set off, times the multiplier the server applied to each wound (Deadly's
  `wound_multiplier` on the attack's assign steps; past what a model has left too), less the wounds a roll per wound ignored. An
  unblocked Deadly(3) hit is `W: 3`. **Cover** is `Y` when a block's verdict applied.
- `(melee)` marks a melee frame's weapons; a rule that dealt the hits with no weapon names itself (`(Impact)`, a spell, Deathstrike).
- A roll per wound in the attack reads under it as a roll.

**Roll.** Any other roll is one line, its faces as numbers:

```
Blue unit 1 · Morale Quality 4+: rolled 3, failed → Shaken
Casting 4+ (tokens +2) → needs 2: rolled 6, cast
Dangerous terrain 1 fails: rolled 1 4 5, 1 wound
Roll-off: Team 1 rolled 3 · Team 2 rolled 2, Team 1 wins
```

- The target folds its modifiers in: the printed value (`Quality 4+`), each change with the rule behind it and its signed amount
  as the record carries it, AP as p.13 and p.14 write it, then `needs N` (past 6 `needs 6`, below 2 `needs 2`, by p.5); none
  without a modifier. A record without a basis starts from its threshold. A roll judged on the raw face reads `unmodified 6+`.
- A test (one judged die, not a hit, a block or a roll per wound) names its unit first when it is not the block's actor.
- **Consequence.** A test takes the change words of its frame's later steps that roll nothing, up to the frame's end, after its
  result: `failed → Shaken`, `failed → [unit] Fled!`. Those steps draw no change line of their own.
- A rule's forced reroll and a `SetDice` (the rules suite's) follow the faces now standing: `· rerolled 1 → 5`, `· set by hand 2 → 6`.
- A roll with no threshold and no required face (a property's own roll) shows its faces and no result.
- **Roll-off ties.** Each record's dice are labelled by the teams they belong to, in ordinal order: every team on the first
  record, then the teams still tied. `Team k wins` goes on the record after which no team is tied.

**Change.** What a step did to the table, after the rolls that caused it: `2 models removed`, `1 model wounded` (a model whose
wounds the step changed; a step that leaves them where they were, a wound Regeneration ignored, draws none), `[unit] · Shaken`,
`[unit] · Shaken cleared`, and `[unit] Fled!` for models a step sent to the shelf with no wound on them (a rout after a failed morale
test). A condition always names its unit (`Red unit 1 · Fatigued`); a test's consequence reads bare after it (`failed → Shaken`). A bookkeeping condition (`moved`, `moved this round` and the like; client UX spec →
Army roster) draws no line: the header's `moved 2"` says it. A grant reads `[unit] gets [rule]`.

**Outside any action:** `Round 1`, `Round 2` (deployment's round 0 and the phase starts draw none); deployment, one line per unit
placed; an objective seized or contested; a menu edit, tagged
`by hand`; `Undone to: [the action's header]`, the undone lines struck through; the game's end.

**Not in the log:** `ask_opened`, `ask_answered`, `ask_closed`, a step completion with no roll and no change,
`sequence_started` without an action, `sequence_finished`, `sequence_abandoned`, `turn_started`, `turn_ended`, `phase_started`, and a passed
react window. The events stay in the log the server keeps; the combat log does not draw them.

**The text.** Both clients draw the log as one text (`LogSheet`, the viewer's `logSheet.ts`), one paragraph per entry, two spaces of
indent per block: Godot one selectable `RichTextLabel`, the browser one `<pre>`. A drag selects across any number of entries and a copy
holds the lines as drawn and nothing else; an undone entry is struck, one past a pending undo faded, a block's header in its team's
colour, by style only. A click that selects nothing picks the entry under it (tinted, its pieces lit on the table); **Undo to here**
in the log's head undoes to the picked entry; **Copy** puts the whole log as shown on the clipboard. The filters (All, Rolls, My units,
This unit), the drawer, the follow-the-newest scrolling and the dice tray (the latest roll's line) are unchanged.

## What the server adds

Three optional fields on the wire. A log written before them reads as before, with no rule names on modifiers, no triggers
on dice and no cover verdict.

- **`RollModifier.Via`** (string, optional): the rule that granted the property whose effect made this modifier, when it was
  granted. Rending's hits carry `AP` with `Via` `Rending`; a weapon's own AP has none. `RollEffects.Attributed` reads it with
  `LiveSet.GrantorOf`: pass one's `GrantedBy`, or for a per-die grant applied with `LiveSet.WithGranted` for a block group, the
  live rule whose per-die `grant_property` row names that entry and amount. The `granted_` key of `score-hits` is unchanged.
- **`Die.Triggered`** (list of string, optional): what this die set off, as `Rule` or `Rule +N hit(s)` (attacks, wounds), written
  when the roll is recorded, from `RollEffects.Triggers` on that die (pure; `PerDie` still records in `score-hits`). It names
  the rule (Rending, Furious, Relentless, Surge) that acted on the die, so the client counts sixes by rule without knowing any
  rule. A die a forced reroll or `SetDice` replaces carries what its new face sets off, from its record's `FaceTriggers`. A spell's hit roll carries them too (Crack on Electric Tempest), and so
  does a defense roll, from the defender's side (Shred's `+1 wound` on a 1, added to the line's wounds). The dice an
  extra-attacks row adds carry no `+N attacks`: those rows do not apply to newly generated attacks.
- **`RollRecord.FaceTriggers`** (list of `{ Raw, Triggered }`, optional): what each raw face sets off on this roll, every face of
  the convention judged under the record's plan and passed to `RollEffects.Triggers` when the roll is recorded; faces that set off
  nothing are left out. A forced reroll and `SetDice` (the rules suite's) stamp the new die's `Triggered` from it.
- **`RollRecord.Cover`** (`CoverVerdict { InCover, Models, Of, IgnoredBy? }`, optional): on every defense roll of a shooting attack,
  the target unit's p.11 verdict: `InCover` the majority test passed, `Models` of `Of` living models fully inside cover terrain or
  behind a sight blocker, `IgnoredBy` the rule that took the +1 away from a unit in cover (a `suppress_modifiers` cover row such
  as Blast or Ignores Cover, or Indirect, when dropping the sight half lost the majority). The +1 applies when `InCover` and
  nothing ignored it. `fire-groups` and `determine-attacks` settle it as `in_cover`, `cover_models`, `cover_of` and
  `cover_indirect`; `roll-to-block` passes the one verdict to every group's `GfRolls.DefensePlan`. Melee and spell hits carry none.
- **`StepCompleted.Values["wound_multiplier"]`** on an attack's `assign` steps, when it is above 1: what each wound of the segment
  was multiplied by (Deadly(X), `GfAttack.Wounds`' `WoundEffects`), which the line's `W` multiplies by. A step value, no wire change.
- A threshold's `ThresholdBasis` (stat, printed value, changes) is already on the wire and is the `Quality 3+` / `Defense 5+`
  part of the line.

The rules stay on the server: the client reads names and numbers from the record and composes words. It names no rule.

## Where it lands

- `libs/Tabletop.Protocol`: `RollModifier.Via`, `Die.Triggered`, `RollRecord.FaceTriggers`, `CoverVerdict` and `RollRecord.Cover`; the viewer's generated
  types regenerated (`TABLETOP_WRITE_GENERATED=1`).
- `server/game`: `RollEffects`, `LiveSet`, `GfRolls` (`Cover`, `DefensePlan`), `GfAttack` (`roll-to-block` groups, the cover
  values), `GfShooting` (`fire-groups`), `RollPlan` / `Roller` / `StepContext.Roll` for the die triggers and the verdict. Tests in
  `GfCombatLogTests` (a Rending weapon with AP(1), a Furious charge, an added attack's die, Shred on a defense die),
  `SpellWeaponRuleTests` (Crack on a spell's hit roll) and `GfCoverTests` (screened, half, Blast, Indirect, a Hold
  action, melee).
- `client/src/Core`: `CombatLog` (`CombatLog.Words`: change lines, the nested block's and header's words, the consequences and
  roll-off labels; `CombatLog.Attacks`: `AttackTag`, the frame tree) and `RollEntry` produce the entries and the dropped set;
  `LogSheet` (`LogSheet.Detail`) the text, `LogBbcode` Godot's drawing of it; `LogText` keeps its words for the lines
  outside actions. `LogSheetTests`, `LogBbcodeTests`. `CombatLogTests` on built records: cover in, out and ignored, an old record, Rending and Furious triggers, a
  basis change, a reroll and an edit, a fled unit, the dropped set, the header's move. `CombatLogBlockTests`: `shoots`, an
  answered target, a strike-back, the nested cast, a consequence, condition subjects, a grant, repeated undos, deployment and a
  round end's changes, roll-off ties. `RollLineTests`: raw-face and all-pass targets, a roll per wound, `cast`, dangerous terrain.
  `CombatLogFixtureTests` against the owner's game `01a126db`, seq 245 and 251–354.
- `client/src/Hud/CombatLogPanel.cs`: one `RichTextLabel` of `LogBbcode`; `DiceTray.cs`: the tray clips a long line and carries all of
  it as its tooltip.
- `viewer/src/lib/play/`: `combatLogAttacks.ts`, `logSheet.ts`, `CombatLogPanel.svelte` (one `<pre>`); `tests/fixtures/combatlog.json`
  carries each case's paragraphs, summary and detailed, over the recorded games, four 3.6 suite games and a 3.5.1 Deadly game (`tests/fixtures/log/`).
- `viewer/src/lib/live/logLines.ts` reads the same records; it is not changed by this spec.

## Out of scope

- The six rolls that still decide in the rolling step (handoff → Open items from the defect fixes).
- Dice edits on a cast, a morale test or score-hits.
- Which entries "Undo to here" offers (an open owner question).
