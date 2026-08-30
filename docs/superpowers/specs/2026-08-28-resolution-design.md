# Resolution

Design session, 2026-08-28. How an action produces an outcome, and what is recorded
about it.

Third module, after [the table domain model](2026-08-27-table-domain-model-design.md)
and [game flow](2026-08-28-game-flow-design.md). Those designed what stands on the
table and what happens; this designs what happens *inside* one of those actions.

`docs/architecture.md` already sketched this: resolution is a set of named ordered
pipelines, abilities subscribe to a step, RNG lives in server state as a seed, every
roll is an event, and pipeline step order stays C# until a second ruleset exists. This
document does not revisit those. It covers what that sketch predates — the tabletop
simulator framing, which changes who resolution is for.

Scope is the shape of resolution and the state it produces. Not the step list of any
particular pipeline, and not the ability catalog.

## Two settings, and they are orthogonal

**Whether the client animates the steps.** Auto-resolve means not visually playing the
sequence out — the result is simply displayed. It changes presentation and nothing else.

**Whether the pipeline waits between steps.** Waiting is what gives players a moment to
act mid-resolution: interrupts, reactive abilities, or anything the rules do not model.

These are independent. A group can watch dice animate without ever pausing, or pause at
every step without animation. Both are set at game setup, joining the configuration list
in [game flow](2026-08-28-game-flow-design.md).

The second is the one with consequences for the model.

## A pipeline in progress is state

Because resolution can wait, an action is not a function call that returns. **A
half-resolved pipeline is durable state**, recorded in the log like anything else: hits
rolled, defense not yet, sitting there until someone acts.

This falls out of the waiting setting rather than being designed for it. But once one
configuration can produce it, every consumer has to handle it, so it is the general case
and the run-to-completion path is the special one.

Undo needs nothing new here. Each step is a logged event, so stepping back lands between
steps the same way it lands between actions.

## Modifiers and abilities are different things

**A modifier is a numeric adjustment to a value, applied before the roll.** Plus or minus
to a threshold, to a number of dice, to a range.

**An ability acts on the result.** It reads the dice that came up and changes what they
mean.

Splitting on *when* rather than on *what* is what makes the first one cheap:

**Numeric modifiers sum, so their order does not matter.** `docs/architecture.md` names
stacking and ordering as the hard part and calls for explicit modifier priority rather
than registration order. Restricted to plus and minus on an input, that problem does not
arise — addition commutes, and no ability author has to pick a priority number that would
collide with another pack's.

The genuinely hard interactions do not disappear; they move to abilities reading results,
where they are fewer and where the pipeline step gives them a natural place to sit.

**Corrected 2026-08-29 by [the effect grammar](2026-08-29-effect-grammar-design.md).** This
section went further and said commutativity *removes* the explicit priority scheme. It does
not. Reading every special rule in the Grimdark Future rulebook gives twelve kinds of effect,
and **commutativity holds for exactly one of them** — the numeric modifier. Some properties
suppress other properties, some alter another property's parameter, and the rulebook orders
several by hand: `Deadly` is *"resolved first"*, `Blast` applies *"after resolving other
special rules"*. `architecture.md` was right, ordering is explicit, and it is a field on the
effect.

The wider point stands as a caution rather than a conclusion: **this section's model of an
effect — a numeric plus or minus applied before the roll — covers one of twelve kinds.** It was
designed against a hypothetical, before any ruleset had been read.

## Players roll their own dice, and can change them

The engine resolving an action does not make it the engine's action. Two affordances,
both ordinary logged commands:

**Reroll.** Per roll, not per die.

**Add a modifier by hand.** The same shape as one a pack declares.

Neither is an override mechanism bolted beside the automated path — **they are the
operations abilities already use.** "Reroll 1s to hit" is a pack-declared reroll; a player
clicking reroll is the same event with a different actor. This is the write-path rule from
game flow applied to resolution: automation and the player write the same things the same
way.

A reroll records which dice it replaced. A manual reroll is that with the selection set to
all, which is what keeps it one operation rather than two.

## Rerolling does not break undo

`docs/architecture.md` makes undo reproduce the same dice, so rewinding and redoing cannot
be used to fish for a better roll. A freely available reroll button might look like it
discards that.

It does not. The protection was never that dice are unchangeable — it is that changing
them is **visible**. A logged reroll carrying a player's name is a different act from
silently redoing an action until it lands well. Making fishing visible was always the
point; making it impossible never was.

## Deliberately not decided

- The step list of any pipeline. That is per-ruleset C#, per `docs/architecture.md`.
- What abilities exist, and how the catalog is authored.
- Dice conventions.
- **Conflicts between two abilities reading the same result** — whether both apply or one
  wins. Real, and deferred: every example of it is specific to one ruleset, and choosing
  now means designing against a single game's quirks rather than a pattern.

## Still open

- What a roll records beyond its results — whether the modifiers that produced its
  threshold are part of the event or reconstructed from the state at the time.
- Whether a waiting pipeline can be abandoned, and what happens to the half-applied
  result if so.
