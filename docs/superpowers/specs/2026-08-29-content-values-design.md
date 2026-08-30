# Content values

Design session, 2026-08-29. Stats, weapons, properties and what a snapshot holds.

Eleventh module, and the last of the three putting real definition on
[`docs/data-model.md`](../../data-model.md). Geometry values came first, then identity and
enumerations.

Read out of the Grimdark Future advanced rulebook rather than reasoned about, following the
geometry session, which invented a rule that did not exist.

## What the rulebook actually says

Two facts shaped everything below.

**A Grimdark Future unit entry is: Name [Size], Quality, Defense, Equipment, Special Rules,
Cost.** Two stats, both "the score needed." **There is no movement stat** — movement comes
from the action taken (Hold, Advance 6", Rush or Charge 12") and is altered by properties
like `Slow` and `Fast`. Warhammer's M/T/Sv/W/Ld/OC is an entirely different set, which is
the case for stats being declared rather than typed into the engine.

**A weapon profile is `Name (Range, Attacks, Special Rules)`** — `Heavy Rifle (24", A1,
AP(1))`. Melee weapons have no range value. So a weapon has a stat line of its own, plus
properties, which is the same shape as a unit.

## Stats are typed and declared at three levels

**A stat value is typed, not a bare integer.** `Properties` declares each stat with a
**kind** from a closed set — target number, distance, count, dice expression — and the
stored value conforms to it.

`Quality 4+` is a target number, Warhammer's `M 6"` is a distance, `W 3` is a count, and
damage of `D6` is a dice expression. An integer-only model cannot hold the last one at all,
and would have to be reopened the moment a second game system landed. The engine reads the
kind, so a target number and a distance are never confused.

**Stats are declared at three levels, mirroring the three property kinds** — unit, model,
weapon. OPR puts Quality and Defense at unit level and Range and Attacks on weapons;
Warhammer puts most of its line at model level and gives weapons six of their own. Same
mechanism, same closed declaration, three attachment points.

## A weapon lives on the model slot

**In the slot's loadout, not on the unit and not on the army.** Ten models, one sergeant,
one flamer, eight plain — the flamer belongs to one slot.

```
Weapon   name
         stats        declared weapon stats; a melee weapon has no range at all
         properties   WeaponProperty instances
```

An absent range is absent, not zero.

**There is no weapon definition inside `Properties`.** Each slot carries its own resolved
weapon rather than pointing at a shared entry. Two Heavy Rifles in two armies are two
copies, because an army holds its own copy of every value and nothing may reach into it.

## A property entry

```
name
kind         unit | model | weapon
parameter    zero or one integer
effect       data — see below
aggregation  ModelProperty only: all | any | majority | none
```

**Parameters are zero or one integer, across the whole of Grimdark Future.** `AP(X)`,
`Blast(X)`, `Caster(X)`, `Deadly(X)`, `Fear(X)`, `Impact(X)` and `Tough(X)` take one; the
other two dozen take none. Nothing takes two, and nothing takes a non-numeric argument.

### Effects are data

**A property entry carries its effect**, expressed in a closed vocabulary — not a name that
C# matches on. The alternative would leave `Properties` a list of names with arities, and
would make user-authored properties impossible without shipping code, which is the opposite
of where this is going.

The vocabulary comes from the rulebook rather than from invention. Sorting all of Grimdark
Future's properties by what they actually do gives four families:

| Family | Examples |
| --- | --- |
| Numeric modifiers to a roll | `AP(X)`, `Stealth`, `Entrenched`, `Indirect`, `Lance`, `Slow`, `Fast`, `Reliable`, `Sniper` |
| Reroll and result reinterpretation | `Poison`, `Regeneration`, `Rending`, `Furious`, `Relentless`, `Fearless` |
| Extra dice and multiplication | `Impact(X)`, `Blast(X)`, `Deadly(X)`, `Counter` |
| Turn structure and deployment | `Ambush`, `Scout`, `Flying`, `Immobile`, `Aircraft`, `Hero`, `Limited`, `Caster(X)` |

Only the first family is expressible as the resolution session's "numeric plus or minus
applied before the roll." That session's model covers roughly a third of one ruleset's
properties, which is worth knowing before building on it.

**The grammar of those four families is its own design session**, not a subsection of this
one. Each family has a shape, and all four have to line up with the resolution pipeline.
This session settles that an entry *carries* an effect and what the families are.

## `upgrades` is dropped

OPR builds a list by picking units **and upgrades**, so an upgrade looked like something a
unit should record. It is not.

**An upgrade's entire job is to change a unit's equipment and rules, and by import time that
has already happened.** The unit holds its final equipment and its final properties. Keeping
an `upgrades` field would mean two sources for the same fact, and the first time they
disagreed the stored one would be wrong.

Upgrades are import-time input, not stored state.

## `list_entry` is dropped

It was the identifier of the entry in the source list that a unit came from. Nothing reads
it, and the argument for keeping it did not survive being stated plainly:

- The only consumer would have been a heuristic matching old slots to a changed list, and
  [the army](2026-08-28-army-design.md) has since removed that flow entirely — **a changed
  list means a re-import**, with no reconciliation to perform.
- Whether Army Forge even exports a stable per-unit identifier is **unverified**.

So it is a field stored on speculation, for work that no longer exists, from a payload nobody
has looked at. `CLAUDE.md` says not to scaffold ahead of a plan.

## The army's own two fields

**`game_system` is the `GameRules` identifier** — which game this army is for.

**`source` is where it was imported from**, and needs to be enough to find the original list
again. Nothing fetches it; the army is self-contained.

## `ARMYSNAPSHOT` is a deep copy

Units, slots, stats, loadout, properties, bases and heights. Everything, taken when the game
loaded.

**No edge back to `ARMY` and no partial fields.** A partial copy would need a rule for what
is shared and what is not, and nothing is shared — that is the whole point. Editing a saved
army cannot reach into a match in progress because the match is holding its own copy.

## Deliberately not decided

- The effect grammar for the four families. Next session.
- Whether a stat kind ever needs a unit of measure beyond inches.
- What a dice expression looks like, beyond existing as a kind.
- The Army Forge import format itself.

## Still open

- **Whether `Properties` needs anything beyond stat definitions and the three property
  sets.** `architecture.md` also listed "unit and weapon definitions," but armies are
  imported self-contained, so a shared definition of a unit or a weapon has nothing to do.
  It is written here as not needed; a second game system is what would prove that wrong.
- Whether a `UnitProperty` ever needs an aggregation of its own.
