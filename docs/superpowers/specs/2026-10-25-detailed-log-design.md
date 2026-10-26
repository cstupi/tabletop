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

Each block header and line stays. Each roll is drawn as monospace lines (its `Worked` lines), and each change line that names
models lists them. One shooting attack:

```
R2 · Team 1 · Marksmen · Hold → shoots Raiders
  Attack: Marksmen (3 models) → Raiders
    Plasma Rifle ×3 (24", A1, AP(4), Unstoppable)
  Hit
    Quality         4+   printed
    Bad Shot        -1   ignored: Unstoppable
    target 4+
    rolled    2  6  4
    hits      ·  ✓  ✓
    = 2 hits
  Block
    Defense         5+   printed
    cover           +1   2/3 models in cover
    AP(4)           -4
    target 5+, roll -3
    rolled    6  3
    final     3  0
    blocks    ✓  ·
    6 always passes, 1 always fails (p.5)
    = 1 blocked, 1 wound
  Raider (Raiders) · 1 wound
  Morale
    Quality         5+   printed
    target 5+
    rolled    3
    passes    ·
    = failed → Shaken
```

- **Attack:** on the first hit roll of a step: the attacking unit, how many models hold the weapons that attack, the target, then
  one line per weapon name with its count, its stats (`24"`, `A1`) and its rules as the army lists them.
- **Target number:** the stat and its printed value; each replace_stat (`Hero 3+  set`) and modify_stat row (`Shaken +1 → 5+`);
  each stat row a suppression took away (`... ignored: Rule`); the shooting block's cover verdict (`cover +1  3/5 models in
  cover`, `cover --  no cover: 2/6 models in cover`, `cover +1  ignored: Blast (4/5 models in cover)`, p.11); each roll modifier
  (`AP(1) -1`, `Rending AP(+4) -4`, `Stealth -1`); each roll modifier a suppression took away (`Bad Shot -1  ignored:
  Unstoppable`); then `target N+` and the net roll modifier. A roll judged on the raw face reads `target unmodified 6`.
- **Dice,** ten to a row: `rolled` (raw faces in order), `final` (raw plus the net modifier, when it is not zero), and a pass row
  named for the roll (`hits`, `blocks`, `passes`, `ignored` for a roll per wound) with `✓` or `·`. A roll with no target shows
  `rolled` only; a roll-off one line per team. When a 6 passed or a 1 failed against its final value: `6 always passes, 1 always
  fails (p.5)`.
- **Rerolls and edits:** `rerolled die 3: 1 → 5 (Rule)`, `set by hand die 2: 4 → 6`.
- **Rules a suppression removed** in the step that no record above names: `Regeneration  ignored: Rending`. Then `rules:` and the
  step's `Applied`.
- **Result:** `= ` and the normal log's result with its consequence.
- **Wounds:** a change line of models wounded or removed lists each model: `Raider (Raiders) · 2 wounds` (the wounds it now
  carries), `Raider (Raiders) · removed`.

Melee (strikes, strike back, fatigue's `target unmodified 6`), casting, dangerous terrain, regeneration rolls and the roll-off
read the same way: the client composes from the record; it names no rule.

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
- `client/src/Core`: `RollEntry.Worked`, `LineEntry.Worked`, `RollWords` (`Worked`), `CombatLog` (the attack lines, the per-model
  change lines), `Names` (weapons), `ClientSettings.LogDetailed`. `client/src/Hud/CombatLogPanel.cs`: the checkbox and monospace
  rows (`Variation.Mono`).
- `viewer/src/lib/play/`: the port; `combatlog.json` carries each case's detailed lines and a built `worked` case.
