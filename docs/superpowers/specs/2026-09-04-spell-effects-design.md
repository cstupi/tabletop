# Spell effects

Design session, 2026-09-04. What real spells need from the effect grammar that Grimdark
Future's core rules did not.

[The effect grammar](2026-08-29-effect-grammar-design.md) was derived from every special rule in
the core rulebook. [Action sequences](2026-09-03-action-sequences-design.md) designed the casting
sequence and, reading one army book, found two gaps: a grant with a duration and multi-target
selection. This session reads eight army books and closes them.

## Read, not reasoned

Forty-eight spells from eight official Grimdark Future army books, via the Army Forge API:
Alien Hives, Battle Brothers, Dwarf Guilds, High Elf Fleets, Human Defense Force, Machine Cults,
Orc Marauders, Ratmen Clans. Each spell carries not only its text but Army Forge's own
**generated structure**, which is the grammar One Page Rules writes spells in:

- **Assignment** — side (`friendly | enemy`), grain (`unit | model`), count (`one | up to two |
  up to three`), range (`6" | 9" | 12" | 18" | 24"`).
- **Effect**, two families:
  - **Damage** — *N hits* carrying up to two weapon rules (`AP(1)`, `Deadly(3)`, `Blast(3)`,
    `Bane`, `Hazardous`, `Crack`, `Destructive`, `Lacerate`, `Shred`, `Tear`), with *"roll as
    many dice as hits to see if 'on rolls of 6' effects trigger"* whenever a rule needs a die,
    and *"resolved as if the target was a unit of 1"* for a model-grain target.
  - **Grant** — a unit *gets* or *loses* a rule (`Regeneration`, `Hit & Run Fighter`, `Hive
    Bond Boost`, `AP(1)`), or a stat modifier (*+6" range when shooting*, *−1 to defense
    rolls*, *+2" Advance and +4" Rush/Charge*), optionally *in melee* or *when shooting* only,
    and always with one duration: **once (next time the effect would apply)**. One form marks
    enemy units so that *friendly units get X against* them.

Read against the twelve kinds, most of this was already expressible. Four things were not.

## Duration

`effect` gains **`duration`**, a closed set of two:

- **`interaction`** — the default and every core rule's behaviour: the effect applies whenever
  its conditions hold in the interaction being resolved.
- **`once`** — applying the effect means **writing a grant** onto its target instead. The grant
  is consumed the first time its effect actually applies. That is *"next time the effect would
  apply"* read literally: a grant that never gets a chance to apply is never consumed.

The optional extra actions (page 30) use two more — *until its next activation*, *this round* —
and they are not added until those actions are. Adding a value is a code change, like every
closed set here.

## The grants store

A unit gains a **fourth store**, beside the action record, conditions and placement from
[game flow](2026-08-28-game-flow-design.md): **grants**. Each grant holds the effect row it came
from, the entry that granted it, the casting unit, and remaining uses (one).

- **Written** by the `step_completed` of the spell's effect step; **consumed** by the
  `step_completed` of whatever step applied it. Both are explicit in the payload, so the
  client's projection copies rather than derives.
- **Rebuilt from the log, never a row**, like the other stores.
- **Editable from the menu** — added or removed like any property, logged with an actor.
- **Read by the two effect passes** alongside the unit's own properties, **and by `GameRules`'
  name checks** — a granted `Hit & Run Fighter` is a hook-name rule with no effect rows, and the
  hook that checks for it by name reads the grants store too. A granted
  `Regeneration` is settled and applied exactly as an innate one; *loses AP(1)* is a stored
  parameter-modify of −1 on `AP`; *friendly units get Rending in melee against* is a stored grant
  on the enemy unit whose subject is `opponent`, so any friendly unit interacting with it gets
  the rule.

**Conditions stay names.** Game flow kept containment out of the condition store because it
carried a reference; a grant carries three, so it is not a condition either. The alternative of
writing the rule into the unit's property list was rejected because that list is the frozen army
snapshot's copy.

## Damage spells need nothing new

*Takes 4 hits with AP(2)* is **kind 6, add hits, amount 4**, with a **child grant** of `AP`,
amount 2, subject `self` — the spell's hits are the caster's attack, and a grant nested under
the hits applies to them, exactly as `Rending`'s nested `AP(4)` does. The nesting is
`parent_effect_id`, as `Impact(X)` and `Rending` already use it. Kind 6 therefore takes an
`amount` (default 1) — noted in the schema changes below. The casting sequence pushes resolve-hits with those hits, as
action sequences had it.

**Spell hits are rolled as a hit roll that cannot fail**: one die per hit, every result a hit.
That is *"roll as many dice as hits to see if 'on rolls of 6' effects trigger"* with nothing
invented — roll-result predicates read a real die, `Hazardous`'s *rolls of 1* included, and a
spell with no such rule rolls dice that change nothing. No new roll row and no surrogate concept.

*Resolved as if the target was a unit of 1* is `Takedown`'s rule (page 14) applied to a
model-grain target, and reuses whatever Takedown's implementation does.

## Target selection

A spell entry gains four columns, nullable and checked to the `spell` kind:

```
target_side    friendly | enemy
target_grain   unit | model
target_count   the maximum; "one" is 1, "up to two" is 2
target_range   inches from the caster
```

The casting sequence's target Ask computes its legal set from these plus line of sight, and the
answer is one to `target_count` targets. **Each target gets the effect independently**, which is
*"take 4 hits each"*. This replaces action sequences' *"Ask the target in line of sight"*, which
assumed one.

## Faction rules are catalog entries

`Hive Bond`, `Hive Bond Boost`, `Scurry Boost` and their kin are unit rules from an army book
that spells grant by name, so they must exist in the pinned set for `target_entry` to point at.
`catalog_entry.faction` is therefore meaningful for every kind — null for a core rule, the book's
name for a faction rule — and `schema.md`'s *"null for the other three kinds"* is corrected. The
rows themselves still arrive with the content import, not here.

## Schema changes

Flagged, and made in `schema.md` and `data-model.md`:

- `effect.duration` — `interaction | once`, check constraint, default `interaction`.
- `effect.amount` is meaningful for kind 6 (add hits), default 1; the per-kind check constraint
  admits it.
- `catalog_entry.target_side`, `target_grain`, `target_count`, `target_range` — spell kind only.
- `catalog_entry.faction` — the comment, not the column.

## Deliberately not decided

- The content import that turns Army Forge's generated structure into rows. The mapping above
  is what it will implement.
- The two further durations the optional extra actions need.

## Still open

Nothing of its own. Carried unchanged from the effect grammar: percentages, which only the
optional boosting rules need, and whether twelve kinds survive 40k.
