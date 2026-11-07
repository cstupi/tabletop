# Increased Shooting Range Mark: an over-reach shooter

Handoff item: "The ISR Mark games cannot catch a small over-reach. That needs a second shooter 18-20" from the marked unit."

## The rule

Increased Shooting Range Mark is an army-book rule (Eternal Dynasty), not in the rulebook. Its text, from
`server/game/Content/<edition>/army-rules.part.json`:

- 3.5.1: "Once per activation, before attacking, pick one enemy unit within 18" in line of sight, which friendly units get +6" range
  when shooting against once (next time the effect would apply)."
- 3.6.0: "Once per activation, before attacking, pick one enemy unit within 18" in line of sight, which friendly units get +6" range
  on their weapons when shooting against once (next time the effect would apply)."

Range is measured unit to unit, p.5 (3.6.0 p.5 the same words): "When measuring the distance between two units you always measure
from/to the closest model in each unit." p.8: "All models in a unit with line of sight to the target, and that have a weapon that is
within range of it, may fire at it."

With the games' 12" rifle the marked unit is in reach up to 18" and no further.

## The gap

`eternal-dynasty-increased-shooting-range-mark` (`SuiteCatalog.Army.Batch2.cs`) and `…@3.6` (`SuiteCatalog.Army36H.cs`) have one
rule unit, which marks the opponents' unit 1 about 15" away and shoots it at once, spending the mark. No shot is ever tried between
18" and 20", so an engine that gave +8" passes both games. Shown before the change: amount 6 → 8 on the Mark in both content files,
both games PASSED.

The mark is spent by the first shot against the marked unit (once), so the far shooter must try before anyone shoots that unit.

## The games after the change

Both games, one layout (`MarkReach` in `SuiteCatalog.Army.Batch2.cs`), 36" × 30" table, Frontline zones (rule side y 18–30,
opponents y 0–12), 32mm bases (1.26"), every distance edge to edge:

| Unit | Anchor (first model; rows run +x, 1.76" apart) | Role |
| --- | --- | --- |
| Opponents unit 1 (5 models) | (2, 11) | the marked unit |
| Opponents unit 2 (5 models) | (12, 11) | the marker's own target, ~9" from it |
| Rule unit 1 (3 models, model 1 carries the Mark) | (2, 19) | marks opponents unit 1 (6.74" away), shoots opponents unit 2 |
| Rule unit 3 | y 28, x set so it stands 18.5" from opponents unit 1 | past the reach: never shoots it |
| Rule unit 2 | y 25, x set so it stands 17.5" from opponents unit 1 | inside the reach: shoots it under the mark |

Script: the rule army activates unit 1, then unit 3, then unit 2 (Hold, its only action); the Mark's `rule_target` is opponents unit
1; unit 1 aims at opponents unit 2, units 2 and 3 at opponents unit 1, each only when the server offers that target (else the policy).
Round one therefore always has: the mark placed, unit 3 offered opponents unit 1 only if the engine over-reaches, unit 2 spending the
mark from 17.5".

Assertions (existing checks, unchanged):

- `RangeBonus` (12", +6"): every attack at a unit holding the mark made from beyond 12" is within 18". Unit 2's 17.5" shot is the
  evidence it needs; a shot by unit 3 from 18.5" fails it ("shot a unit 18.5" away").
- `Spent`: the mark acts only through its grant, which it spends.

The old `Overreach` wrapper (an unmarked opponents unit far off) is dropped from these two games: `RangeBonus` reads only attacks at
a marked unit, so it never judged that unit.

## Engine fix the layout exposed

Unit 3's Hold reads the mark while `shoot-or-not` asks `GfShooting.ValidTargets` whether any target is in reach, finds none, and
completes. The engine kept the grant (right: nothing was shot against the marked unit, so the effect did not apply) but listed the
Mark in that step's `Applied`, which `Spent` reads as "acted, not spent" ("a grant that acted outlived its activation", 4 of 16
occasions, both editions, every seed). The same listing came from unit 1's offer, whose shot went at opponents unit 2.

`AppliedEntries.Remove` already says a once grant read but not acted on is not listed, and `GfShooting.Valid` says "Offering reads
nothing". Fix: `GfShooting.Valid` settles the live set with no recorder (`LiveProperties.Settle(...)`), so offering targets lists
nothing; the attack that follows lists and spends what it reads.

Unit test first: `Batch2PickContentTests.An_offer_that_shoots_nothing_at_the_marked_unit_lists_no_range_mark_and_keeps_it`, both
editions: A (24" gun) holds with B marked 30.74" off (no Ask; failed before the fix: the Mark was in `shoot-or-not`'s Applied) and
26.74" off (offered, passed); in both the Mark is not listed and B keeps one grant.

## Validation

- Gap shown on the old games: Mark amount 6 → 8 in both `army-rules.part.json`: both games PASSED.
- New games, mutated engine (local edit, reverted with `git checkout -- server/game/Content`):
  - +8": `FAILED: 0 of 2 games passed`, each "12" + 6": 4 of 4 occasions: shot a unit 18.5" away".
  - +7": `FAILED: 0 of 2 games passed`, the same line.
- New games, real engine after the fix: PASSED 2 of 2 on seeds 1, 2, 3, 4, 5, 11, 42.
- Whole in-process rules suite: `PASSED: 685 of 685 games passed, 0 failing on known defects, 0 unexpected; 1122 of 1122 entries
  covered, 0 not automated; run 01a1b7220a537ccfaa560e95d8304c64`.
- `dotnet test tests/Tabletop.GameServer.Tests`: 1836 passed, 0 failed. `dotnet test tests/Tabletop.Sim.Tests`: 345 passed, 0 failed.
