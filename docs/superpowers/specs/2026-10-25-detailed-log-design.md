# Detailed log

A second way to read the combat log, for checking each resolution against the rulebook: every roll written out as a worked
resolution, top to bottom, with what changed its target number, what was left out and why, every die, and the result. The
normal log (`2026-10-13-combat-log-design.md`) is unchanged; the detailed log reads the same entries.

## Why

Owner: "The log is still really hard for a human to parse what is going on to see if things were handled correctly." An
Unstoppable weapon that did not ignore Bad Shot (p.15 Unstoppable: *"ignores all negative modifiers to this weapon"*) read as an
ordinary `Hit · Quality 4+ (Bad Shot -1) → needs 5` line: nothing said the weapon was Unstoppable or that a suppression was
expected. The detailed log names the weapons and their rules, every modifier, and every modifier a rule took away.

## The option

A **Detailed** checkbox beside the log's filter (All, Rolls, My units, This unit) in both clients, saved with the other log
settings (`ClientSettings.LogDetailed`, the viewer's `logDetailed`). The filter still applies. Off, the log is the normal log.

## What it shows

Each block header and line stays, in monospace. An attack is its weapons, a table per roll of how many dice show each face, a table
per reroll and per rule a face set off, its hits, each block's modifiers over its table, and its totals; each change line that names
models lists them. One shooting attack:

```
R2 · Team 1 · Marksmen · Hold → Blue unit 1
  Marksmen → Blue unit 1 · Plasma Rifle
    Plasma Rifle ×3 (24", A1, AP(4), Unstoppable)
  Hit roll · Quality 4+ · Bad Shot -1 ignored (Unstoppable) (3 dice)
   1  2  3  4  5  6
   0  1  0  1  0  1
  2 hits

  Defense 4+ · Cover +1 · AP(4) -4 · needs 6+ (2 dice)
   1  2  3  4  5  6
   1  0  0  0  0  1
  Rerolled (Bane): 6 → 2
  H: 2  W: 2
  Rules: Unstoppable

  Regeneration ignored: Unstoppable
  Blue unit 1 (Blue 1.2) · 1 wound

  Blue unit 1 (Blue 1.1) · removed
  Blue unit 1 · Morale · Quality 4+
  rolled 3
  failed → Shaken
```

- **Head:** `{unit} → {targets}` (with `(melee)`, or the rule that dealt the hits), the weapons, then from the first hit roll one line per
  weapon name with its count, stats (`24"`, `A1`) and rules as the army lists them.
- **Tables:** a `1 … 6` row over the count of dice per face, columns two wide (wider for a count past 99). A roll's table holds its
  faces as rolled; each live reroll or edit follows (`Rerolled (Bane): 6 → 2` for one die, a labelled table of the new faces for
  more); each rule its faces set off rolls no dice of its own, so it is one line under the table, no table: what it added (`+2 hits (Relentless)`, `+1 wound (Shred)`), or for a rule that adds nothing countable the dice it rode on (`Rending (6s: 2 dice)`). A rule that rolls dice of its own (Impact, Ravage, a reroll, extra attacks rolled as new dice) is a record, and gets its own labelled table.
- **Target:** after the roll's label, ` · ` between the parts: the stat and its printed value (`Quality 4+`); each replace_stat
  (`Hero 3+`) and modify_stat row (`Shaken +1`); each stat row a suppression took away (`Hero 3+ ignored (Curse)`); cover a rule took
  away (`Cover ignored (Blast)`; no cover reads nothing); each roll modifier with the sign the roll applies (`AP(1) -1`,
  `Rending AP(4) -4`, `Cover +1`, `Stealth -1`); each roll modifier a suppression took away (`Bad Shot -1 ignored (Unstoppable)`);
  `needs N+` when anything changed it. A roll judged on the raw face reads `unmodified 6+`. When a 6 passed or a 1 failed against
  its final value: `a 6 always succeeds, a 1 always fails` (p.5).
- **Hits:** under the hit tables `X hits · Y bonus hits` (bonus: the hits past the ones rolled, from the faces' extra hits and
  `Hits ×N`, a multiplier); with no hit roll the rule's line, `Deathstrike: 3 hits`. **Totals:** under the blocks and the rolls per
  wound (`Regeneration · 5+ (3 wounds)`, its table, `N ignored`), `Wounds ×N` when the server multiplied each wound (Deadly), `H: X  W: Y`
  as the summary counts them, then `Rules:` (the steps'
  `Applied`) and each rule a suppression removed in a rolling step that no record names (`Regeneration ignored (Unstoppable)`).
- **Other rolls:** the label and target, `rolled N` for one die (`Team 1 rolled 3` per team for a roll-off) or a table, the
  rerolls, the result with its consequence, `Rules:`.
- **Wounds:** a change line of models wounded or removed lists each model: `Raider (Raiders) · 2 wounds` (the wounds the
  step put on it), `Raider (Raiders) · removed`; a roll-less step's removed rules lead it (`Regeneration ignored: Rending`).
- A paragraph of several lines ends with an empty line.

Melee (strikes, strike back, fatigue's `unmodified 6+`), casting, dangerous terrain, regeneration rolls and the roll-off read the same
way: the client composes from the record; it names no rule.

## What the server adds

Optional fields; a log written before them renders without them.

- **`IgnoredEffect { Source, By, Amount, SetTo? }`**: a rule's row that would have acted and was taken away, the rule whose row
  took it away, its signed amount (or the value a replace_stat would have set).
- **`RollRecord.Ignored`**: roll modifiers a suppression removed. `RollEffects.Attributed` writes the modify_roll rows of the
  properties pass one removed (`LiveSet.RemovedRows`, conditions holding) and the negative hit modifiers a `to_hit_penalties`
  suppression removed (`By` the suppressors, `LiveSet.Suppressors`). Cover is the `CoverVerdict`'s.
- **`ThresholdBasis.Ignored`**: replace_stat and modify_stat rows on the stat from properties pass one removed (`StatReader`).
- **`StepCompleted.Ignored`**: every removed rule a step would have read (`LiveSet.ReadRemoved`, into `AppliedEntries`), by
  entry and remover, no amount: Rending's Regeneration on a deal-wounds step that rolls no Regeneration die.
- **`DieReplacement.By`**: on a forced reroll, the rule whose `force_reroll` row matched the die (`StepContext.Roll`).

Nothing here changes a rules outcome: the writes sit beside the reads that already decide.

## Where it lands

- `libs/Tabletop.Protocol`: the fields; viewer's generated types.
- `server/game`: `LiveSet`, `AppliedEntries`, `RollEffects`, `StatReader`, `RollPlan`, `Roller`, `StepContext`,
  `SequenceInterpreter`. Tests in `GfDetailedLogTests`.
- `client/src/Core`: `LogSheet.Detail` (the tables, targets and totals), `LineEntry.Worked`, `WorkedLines.Attack` (the weapon lines),
  `Names` (weapons), `ClientSettings.LogDetailed`. `client/src/Hud/CombatLogPanel.cs`: the checkbox and the monospace text
  (`Variation.Mono`). `DetailedLogTests`.
- `viewer/src/lib/play/`: the port (`logSheet.ts`); `combatlog.json` carries each case's detailed paragraphs and a built `worked` case.
