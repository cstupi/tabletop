# Identity and enumerations

Design session, 2026-08-29. What a ruleset *is*, and every closed set in the model.

Tenth module, second of the three putting real definition on
[`docs/data-model.md`](../../data-model.md). Geometry values came first; content values —
stats, loadout, `list_entry`, `game_system`, and what an army snapshot holds — is last.

The governing principle for this session, stated during it: **predefined properties, not
magic strings.** Closed sets are far easier to reason about and far simpler to enforce, and
a mistyped key that silently does nothing is the failure mode being avoided. This is
`architecture.md`'s closed-schema instinct applied to every enumeration in the
model.

## A ruleset is two things, and neither is called "rules pack"

`RULEPACK` is gone as an entity and as a name. What existed under it was two different
things with two different lifecycles, and calling both of them "rules" made them impossible
to talk about.

**`GameRules`** — the per-ruleset **C#**. Turn structure, action definitions, resolution
pipeline order, scheduler policy, dice conventions. Ships with the build, sits behind an
interface, selected per game.

**`Properties`** — the per-ruleset **data**. Stat definitions and the property sets.
JSON, authored in-repo, content-addressed, pinned per game.

This is `architecture.md`'s existing line — *what is edited every day is JSON; what is
edited once per ruleset stays C#* — with names that distinguish the halves.

The generic interpreter that runs a `Properties` set through a `GameRules`
implementation **needs no name**. It has no identity and nothing references it; it is just
code. Three concepts collapse to two names.

**So a game names a `GameRules` and pins a `Properties` hash.** There is no registry table.
A registry would answer "what rulesets exist," which the build already knows, and a pin only
has to be exact — it does not have to resolve to a row.

### Who authors them

**In-repo now; user-authored eventually.** The order matters: OPR's rulebook gives a
complete, finite vocabulary to design the DSL against, which is a much better starting point
than guessing at one and generalising later.

When users can author `Properties`, that adds an upload path, a validation surface and a
registry table. The pin mechanism is unchanged, so it is additive. `architecture.md` says
the goal is "not end users uploading rules" — that is true of the current phase and is not
the long-term intent.

## `Properties` holds one catalog, and its kinds are disjoint

Not one bag with a scope tag. **Three distinct kinds**, sharing a base but differing in
function, application, and where they attach:

```
UnitProperty     squad-wide            Ambush, Scout
ModelProperty    per model             Tough(X)
WeaponProperty   per weapon            AP(X), Blast(X), Rending, Reliable
```

**A fourth kind was added on 08-30: `spell`.** These three are *attachment points* — where a
property sits and what it travels with. A spell attaches to nothing: it is cast, it belongs to a
faction's army book, and it costs spell tokens, which is a field no property has. It shares the
effect vocabulary and nothing else, so it sits beside them in the catalog rather than inside
one of them. The rule below still holds across all four.

A single bag would be as hard to reason about as the magic strings this session exists to
avoid. The three attach in different places — a `UnitProperty` lives on `UNIT` and is never
copied onto models, which the table domain model already decided; a `ModelProperty` lets a
sergeant differ from the eight plain models beside him; a `WeaponProperty` travels with a
weapon in a model's loadout.

**A name belongs to exactly one kind.** If names could appear in two sets the
distinction softens back into the bag.

### The Stealth case

OPR's `Stealth` reads *"enemies get -1 to hit rolls when shooting at units where **all
models** have this rule."* It is written on models but evaluated at unit level.

Rather than special-case it, a `ModelProperty` declares **how a unit reads it**:

```
ModelProperty   aggregation   all | any | majority | none
```

`Stealth` is `all` — the unit has it when every model does. `Tough(X)` is `none` and is read
per model only. The evaluation engine reads a value instead of knowing a name, which is the
same principle as terrain declaring rather than the engine recognising a forest.

`Properties` also holds the **stat definitions** — which stats exist and what they are
called. Weapons have their own stat lines (range, attacks, AP) alongside model stats. How
stats are keyed and typed is the content session's question.

## Enforcement is a boolean

*Reopened 2026-09-04 by [the whole-design review](2026-09-04-whole-design-review.md): action sequences and the
scheduler had both written a refusing mode under the "dial" name, and the review kept it.
**`GAME.enforcement` is `silent | warn | strict`**, replacing `warns`. `strict` refuses an
out-of-set Ask answer or an illegal command; the menu's edits are accepted under every setting,
because they are the override path. The argument below stands for `silent` and `warn`.*

Not a dial. **The server warns, or it says nothing.**

`architecture.md` called it "hybrid, dialled per game," and game flow listed "the
enforcement dial — how much the server warns about." Neither named a value. Working through
it, there are only two: a third position that *blocks* illegal commands contradicts the
product. This is a virtual tabletop — if people want to do something wrong, they can.

The ruleset supplies the default; the game sets it. Overrides stay visible and logged
either way, per `architecture.md`.

## The closed sets

```
ASSET.kind             miniature | terrain | texture
MARKER.kind            objective | token
GAME.status            lobby | setup | playing | ended
GAME.scheduler_shape   player-turn | alternating-activation
GAME.enforcement       silent | warn | strict      (was `warns boolean`; reopened 09-04)
GAME.waits_between_steps   boolean                 (added 09-04; animate is a per-player client setting)
ModelProperty.aggregation   all | any | majority | none
Volume.kind            solid | area          (geometry values)
Volume.footprint       circle | rect         (geometry values)
base shape             circle | oval | rect (geometry values)
```

**Added 2026-08-30, when `Properties` was given tables.** Every one of these was a closed set in
a design doc's prose and a `text` column with nothing naming its contents:

```
CatalogEntry.kind        unit | model | weapon | spell
StatDefinition.level     unit | model | weapon
StatDefinition.kind      target number | distance | count | dice expression
Effect.subject           self | opponent
Effect.kind              the twelve       (the effect grammar)
Effect.target_quantity   hits | wounds
Effect.target_result     wound | morale_test
Effect.target_modifier   cover | to_hit_penalties | range_penalties
Effect.cap_kind          models_in_target_unit
Condition.kind           the eight        (the effect grammar)
Condition.comparison     eq | neq | gte | lte | gt | lt
Effect.duration          interaction | once          (spell effects, 09-04)
CatalogEntry.target_side   friendly | enemy          (spell effects, 09-04; spell kind only)
CatalogEntry.target_grain  unit | model              (spell effects, 09-04; spell kind only)
```

Actions are named per ruleset rather than enumerated — `Hold`, `Advance`, `Rush`, `Charge` in
Grimdark Future — because a ruleset with an action nobody anticipated needs content, not a code
change.

`Effect.target_stat`, `Effect.target_entry`, `Effect.target_action` and, since 09-03,
`Effect.target_roll` are **not** enumerations — they are foreign keys into the same `Properties`
set, which is stronger. (`target_roll` was listed above as `hit | defense | morale | casting`
until the review of 09-04; rolls became rows on 09-03.) `neq` exists because `Impact(X)` applies
*unless fatigued*; the rulebook has no *or* and no *not* in its structure, but one predicate
still has to test a state's absence.

**`ASSET` profiles are per kind, and one of them is empty:**

| kind | profile |
| --- | --- |
| miniature | the modelled base — shape and dimensions |
| terrain | volumes |
| texture | nothing |

A board image is just an image. Writing the emptiness down so nobody later invents a field
for it.

**`ended` is reversible.** Restarting an ended game is the same hydrate-from-the-log
operation as recovering a crashed task, so `ended` marks a game nobody is currently playing
rather than a terminal state. Settled in the setup content session; restated because a
status enum invites the assumption that the last value is final.

**Both scheduler shapes get built**, per game flow. That was chosen so a second example
exists at the scheduler seam immediately, which is the first place "should this move to
data" becomes answerable.

## Deliberately not decided

- How stats are keyed, named or typed — the content session.
- What a property's parameters look like, and how `AP(X)` carries its X.
- The upload, validation and registry surface for user-authored `Properties`.
- Whether `majority` needs a tie rule.

## Only `Properties` is pinned, and that is accepted

Undo is replay, and replay needs the rules that produced the log. `Properties` is
content-addressed and pinned. **`GameRules` is not** — it is C# shipped with the build, so it
changes whenever the build does.

`architecture.md` treated pinning as solved by content-addressing. It is solved for the data
half only. The decision is to **accept the divergence rather than pin a build version**:
`GameRules` is not expected to change meaningfully in the evaluation engine, and replaying
across versions is not a scenario worth engineering for.

The exposure is narrower than it first looks. Undo within a session is unaffected, because the
engine cannot change while a session is live. The only case is **restarting an ended game after
a `GameRules` deploy**, which is possible because ended is a flag rather than a terminal state.
That game may replay differently. It is a friendly-play tabletop with no victory conditions,
and nothing rides on it.

The alternative — pinning a build version and refusing to replay across it — would turn every
deploy into a wall old games cannot cross, which is a worse outcome than a rare divergence.

## Still open

- Whether a `UnitProperty` ever needs an aggregation of its own.
