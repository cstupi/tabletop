# The effect grammar

Design session, 2026-08-29. How a property entry says what it does.

Twelfth module. It closes the one thing
[content values](2026-08-29-content-values-design.md) opened rather than settled: property
entries carry their effect as data, and the vocabulary for that was left undefined.

Derived by going through every special rule in the Grimdark Future advanced rulebook and
writing down what it actually does, rather than by designing a grammar and checking it
after.

## What a property entry is

```
name
kind          unit | model | weapon        (`spell` added 08-30; it takes a cost, not a parameter)
parameter     zero or one integer
aggregation   ModelProperty only: all | any | majority | none
effects       a list, which may be empty
```

**The list may be empty.** Some properties carry no effect data at all and are names
`GameRules` checks for at hooks it owns — see the last section. A property may have both.

## An effect

```
subject      self | opponent
conditions   a flat list of predicates, all of which must hold
order        when this resolves relative to other effects
effect       one of the twelve kinds
```

### Subject

**`self`** is the model or unit holding the property; **`opponent`** is the other side of
the interaction.

Not attacker and target, because a property can sit on either side. `AP(X)` sits on the
attacker's weapon and affects the opponent's Defense roll; `Stealth` sits on the target and
affects the opponent's hit roll. With `self` and `opponent`, both are written the same way
and the engine resolves which is which from where the property is attached.

`Entrenched` needs all of this at once: the property is on the target model, the effect is
−2 to the **opponent's** hit roll, and the condition — *hasn't moved since the beginning of
its last activation* — tests **self**.

**Relational predicates take no subject.** A range test is *inherently* about both parties —
"the distance from self to opponent is over 9 inches" — so naming a subject for it would be
meaningless. The relationship is what the predicate is, not something it points at.

### Conditions

**A flat list, all of which must hold.** Not a boolean expression tree: there is no *or* and
no *not* anywhere in Grimdark Future's properties. `Rush/Charge` looks like an *or* and is
set membership over action types. `Entrenched` is the deepest case in the book — shooting
**and** over 9" **and** hasn't moved.

**Eight predicate kinds, closed.** Adding a ninth for another ruleset is a code change, which
is accepted, and is the same trade as the six terrain declarations.

| Kind | From |
| --- | --- |
| Action taken | `Slow` *when using Advance*, `Impact` *after charging*, `Relentless` *uses a Hold action* |
| Attack kind — shooting or melee | `Stealth`, `Lance`, `Furious` |
| Range | `Stealth` *over 9" away*, `Caster` *within 18"* |
| Roll result, raw or modified | `Rending` *unmodified rolls of 6*, `Poison` *unmodified Defense rolls of 6* |
| Actor state | `Impact` *unless fatigued*, `Entrenched` *hasn't moved since its last activation* |
| Position and terrain | cover *fully inside*, difficult *moves in or through* |
| Round number | `Ambush` *any round after the first* |
| Usage count | `Limited` *once per game* |

**"Unmodified" is a first-class concept.** `Rending`, `Furious`, `Relentless` and `Poison`
all trigger on the raw die rather than the modified result, so a roll carries both.

**Aggregation is not a predicate.** `Stealth`'s *"units where all models have this rule"* is
the `aggregation` field already on the entry, from
[identity and enumerations](2026-08-29-identity-and-enumerations-design.md). Writing it as a
condition too would state one fact twice.

**Actor state has history behind it already.** *Fatigued* and *hasn't moved since its last
activation* need more than current state, and [game flow](2026-08-28-game-flow-design.md)
separated a unit's **action record** from its **conditions** precisely because the two have
different lifetimes. The predicates test exactly those.

### Conditions belong to the effect, not the entry

`Indirect` settles this. It has three effects with **different** conditions:

- −1 to hit — *when shooting after moving*
- may target enemies not in line of sight — **unconditional**
- ignores cover from sight obstructions — **unconditional**

A single condition list on the entry would get it wrong. The cost is repetition when effects
do share conditions — `Lance`'s +1 to hit and AP(+1) are both *when charging, in melee* —
which is preferable to being wrong.

## The twelve effect kinds

| | Kind | From |
| --- | --- | --- |
| 1 | Modify a roll | `AP(X)`, `Stealth`, `Entrenched`, `Indirect`, `Lance` |
| 2 | Modify a stat | `Slow`, `Fast`, `Aircraft` (−12" range) |
| 3 | Replace a stat | `Reliable`, `Sniper` (*attacks at Quality 2+*), `Hero` (*must use the unit's Defense*) |
| 4 | Force a reroll | `Poison` |
| 5 | Roll | `Regeneration`, `Fearless`, `Impact(X)` |
| 6 | Add a hit | `Furious`, `Relentless` |
| 7 | Multiply hits or wounds | `Blast(X)`, `Deadly(X)` |
| 8 | Override a result | `Regeneration` (the wound is ignored), `Fearless` (counts as passed) |
| 9 | Grant a property | `Rending` (unmodified 6s *get AP(4)*) |
| 10 | Suppress a property | `Poison` and `Rending` (*ignores Regeneration*) |
| 11 | Modify a property's parameter | `Lance` (*AP(+1)*), `Counter` (*−1 Impact rolls*) |
| 12 | Suppress modifiers | `Lock-On` (*ignores cover and all negative modifiers to hit rolls and range*) |

### `Roll` nests

```
Roll   count        a number, or the property's parameter
       threshold    the score needed
       per success  an effect
```

`Impact(X)` is *X dice at 2+, each success adds a hit* — and "add a hit" is kind 6, reused
rather than special-cased. `Regeneration` is *one die at 5+, success overrides the wound*.
`Fearless` is *one die at 4+, success overrides the failed test*.

Rolling dice and deciding what a success does were two separate kinds in an earlier draft.
They are one kind with a nested effect. **Effects nest**; one level covers everything in the
rulebook, and nothing forbids deeper.

### Multiplication carries a cap

`Blast(X)` multiplies each hit by X, *"up to as many hits as models in the target unit."*
The cap is part of the effect, not a separate rule.

## Properties act on other properties

**Four of the twelve — grant, suppress, modify a parameter, and suppress modifiers — operate
on other properties rather than on the game state.** Nothing in the design anticipated this
layer.

It has a direct consequence: **resolving an interaction is two passes, not one.** Which
properties are live and what their parameters are has to be settled *before* any effect is
applied, because `Poison` deletes `Regeneration` from the interaction and `Lance` changes
what `AP` contributes.

### This restores explicit ordering

`order` is a field on the effect because **the rulebook specifies ordering directly**:
`Deadly` — *"Hits from Deadly must be resolved first"*; `Blast` — *"after resolving other
special rules"*.

[Resolution](2026-08-28-resolution-design.md) concluded that restricting modifiers to
arithmetic makes them commute, which *"removes the explicit priority scheme
`architecture.md` called for."* **That holds for kind 1 and fails everywhere else.**
Suppression and parameter-modification do not commute, and the rules order them by hand.
`architecture.md` was right the first time; the requirement comes back.

More broadly: the resolution session's model of an effect — a numeric plus or minus applied
before the roll — covers **one of twelve kinds**. It was designed against a hypothetical, and
this is what a real ruleset asks for.

## What `GameRules` handles instead

Some properties carry no effect data. They are **names `GameRules` checks for at hooks it
already owns**, grouped by where the check happens rather than by what they resemble:

| Hook | Properties |
| --- | --- |
| Deployment | `Ambush`, `Scout` — check for undeployed models each round; `Scout` at deployment end |
| Movement | `Flying`, `Strider` — checked on every move |
| Action legality | `Immobile` |
| Usage | `Limited` |
| Melee strike order | `Counter` — *strikes first with this weapon when charged* |
| Interrupt | `Caster` — casting happens mid-activation, which nothing else in the model does |

**This is name-matching, and it is accepted deliberately here.** The alternative is inventing
grammar for deployment timing, movement exemption and a token economy, most of which would
be used by one property each. That is where effects-as-data stops paying.

**A property may be in both places.** `Aircraft` has two ordinary modifier effects — *units
targeting them get −12" range and −1 to hit* — and is also checked by name during movement
and targeting. `Hero` is mostly list-building and deployment that players handle, plus one
real effect: *"must use the unit's Defense until all other models have been killed"* is a
stat replacement conditioned on other models remaining.

`Strider` was missing from the four families listed in
[content values](2026-08-29-content-values-design.md). It belongs with `Flying`.

## Deliberately not decided

- How `order` is expressed — a number, a phase name, or a relation to another effect.
- Whether `Caster`'s token economy needs anything in `Properties` beyond its parameter.
- What a stat's dice expression looks like, still.
- How the two passes are implemented.

## Still open

- **Whether twelve kinds survive a genuinely different ruleset.** They were derived from one,
  and Age of Fantasy shares the engine. Warhammer is the test.
- Whether effects ever need to nest more than one level.
