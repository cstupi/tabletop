# The 3.5.1 ruleset rebase

Design session, 2026-09-06, the second of that date. Every design and plan in this repository
was written against Grimdark Future v3.4.1. The project owner supplied v3.5.1 for both games
during this session, and 3.5.1 renames rules the plans are built on, removes others, and
changes behaviour in seventeen places.

This document records what changed and what was ruled. Its two open questions were closed on
2026-09-09 by [closing the open questions](2026-09-09-open-questions-design.md). It postdates
`2026-09-04-whole-design-review.md` and `2026-09-06-simulation-design.md`, and where it
disagrees with either, this document is newer.

The books are at `/mnt/c/Users/clays/Downloads/`:

- `GF - Advanced_Rules_v3_5_1.pdf`, 71 printed pages
- `Age_of_Fantasy_-_Advanced_Rules_v3_5_1_-_Print_Friendly.pdf`, 70 printed pages

The 3.4.1 and 3.5.0 files remain beside them and are superseded. Every citation in this
repository must move; see *The citation shift*.

## The books converged

Earlier in this session, before 3.5.1 was available, Grimdark Future 3.4.1 was compared against
Age of Fantasy 3.5.0 and the special rules diverged substantially: different names, different
text on shared names, and rules present in one book only. That finding was an artifact of
comparing two revisions. At 3.5.1 the special rules are word for word identical between the two
books, with three exceptions:

- Age of Fantasy has **Command Groups** (Sergeant, Musician, Banner). Grimdark Future does not.
- Grimdark Future has **Aircraft** and **Transport(X)**. Age of Fantasy does not.
- Age of Fantasy runs a **75% points scale**: 750 and 1500pts recommended against 1000 and
  2000, one hero per 375pts, `1+X` copies per 750pts, one unit per 150pts.

The core rules are identical apart from flavour naming (Bikes against Cavalry, Large Walkers
against Giants) and that points scale. One rules implementation for both game systems is
therefore confirmed by the text, not merely assumed. `GameRules` mapping `gf` and `aof` to one
`grimdark-future` implementation stands. The points scale is the only per-system constant, and
nothing reads it yet, because force organisation is not validated anywhere.

## Five rulings

The owner ruled these before 3.5.1 arrived. All five survive it, because 3.5.1 does not touch
the text any of them rests on.

1. **A joined hero attacks at its own Quality.** Interpreter question 13, overturning the
   assumption of the host squad's Quality. Page 4 defines a Quality test against *"the model's
   Quality value"*, and the Hero rule overrides Defense alone. The morale clause settles it: the
   hero *"may take morale tests on behalf of the unit"* would change nothing if the hero used
   the unit's Quality, so the rule presupposes the hero's own. `GfRolls.HitQuality` reads that value off
   each attacking model's slot, falling back to the unit's for a slot that declares none, and
   `roll-to-hit` rolls one pooled `RollRecord` per distinct value rather than one per weapon group. Page 4's
   *"we recommend having dice of multiple colors"* is the tabletop procedure for the case where
   the hero and the squad carry one weapon type and `fire-groups` has merged them.
2. **A joined hero takes morale tests when its Quality is better.** The Hero clause's *"may"*
   resolves as always-when-it-helps: while a joined hero is alive, the morale threshold is the
   better of the hero's Quality and the unit's. One comparison in `GfRolls.MoralePlan`. No Ask,
   which keeps interpreter question 8's residue closed: the end-of-turn `morale-test` frame
   stays a single step with an actor that pops before any boundary can gate it.
3. **The Hero Defense sunset is implemented.** *"must use the unit's Defense until all other
   models have been killed"*. Settled by the text rather than asked. The plan's reason for
   model-level Defense being expensive is that the block roll is thrown before any wound is
   assigned to a model, and that objection binds only for a mixed unit. A unit down to its hero
   holds exactly one model, so the threshold is unambiguous, and the cost is a model count passed
   into `GfRolls.DefensePlan`. It is the count settled before any group fired rather than a live
   one, because page 8 fires a unit's weapons simultaneously and question 3 already froze the same
   number for `Blast`'s cap.
4. **A disembarking model may take any facing that fits.** Client question 8, over the assumed
   container yaw. Page 7 says *"Models may move and turn in any direction regardless of their
   facing"*, and a unit exits a transport *"by using any move action"*, so a placement is legal
   if some yaw fits. `MoveRuler.ExitRegion` and `GfMoves.ExitPlacement` try more than one yaw,
   and the drawn region is the union over fitting yaws. Circles are yaw-invariant, so this binds
   only oval bases and models with no base. `Location.Inside` gains no yaw, because nothing is
   preserved.
5. **Fearless requires every model to have the rule.** 3.4.1 said *"most models"* and the
   interpreter plan's catalog reads `majority` on the strength of it. The owner ruled `all`
   before 3.5.1 was read, and 3.5.1 confirms it: both books now say *"a unit where all models
   have this rule"*. `Fearless` aggregation becomes `all` and its `ruleText` follows.

## Seventeen behaviour changes

| # | Change | 3.5.1 page |
| --- | --- | --- |
| 1 | Multiplayer drops the per-round roll-off | 50 |
| 2 | A Shaken unit may strike back, counting as fatigued | 10 |
| 3 | A Shaken unit may not cast, use an active special rule, or re-position | 10 |
| 4 | Elevated terrain allows 3" coherency across elevations | 11 |
| 5 | `Regeneration` needs every model, and rolls one die per wound | 14 |
| 6 | `Fearless` needs every model | 13 |
| 7 | `Relentless` triggers on shooting over 9", not on a Hold action | 14 |
| 8 | `Rending` is additive `AP(+4)`, not flat `AP(4)` | 14 |
| 9 | `Aircraft` loses its -1 to hit and keeps only -12" to range | 13 |
| 10 | `Scout` deploys fully within 12" of its deployment zone, not its position | 14 |
| 11 | `Tough` assigns heroes wounds last *"even if already wounded"* | 15 |
| 12 | `Takedown` replaces `Sniper` | 14 |
| 13 | `Entrenched` is gone; `Artillery` replaces it | 13 |
| 14 | `Lance` becomes `Thrust` word for word; `Poison` becomes `Bane`, reworded to one effect | 14 |
| 15 | `Lock-On` is gone with no replacement | n/a |
| 16 | `Surge` and `Unstoppable` are new; Command Groups are new in Age of Fantasy | 13-15 |
| 17 | Rules Priority gains a same-time tie-break, now in both books | 13 |

Four of these need more than a row.

### `Takedown` replaces `Sniper`, and overtakes a ruling

3.4.1: *"Sniper Shoots at Quality 2+, and each model with Sniper may pick any model in the
target unit as its individual target, which is resolved as if it was a unit of 1."*

3.5.1: *"Takedown This model may pick any model in the target unit as its individual target,
which is resolved as if it was a unit of [1]."*

Two changes. **The Quality 2+ clause is gone**, so a takedown weapon hits at the model's own
Quality unless its profile also carries `Reliable`, which still reads *"Models attacks at
Quality 2+ with this weapon"*. And *"each model with Sniper"* became *"This model"*.

The ruling of 2026-09-06 settled a Sniper group on a **per-group** target pick, and recorded the
per-model reading as unmodelled residue, because a group is one weapon type and `roll-to-hit`
pools its dice. That compromise was forced by *"each model with Sniper"*. 3.5.1 asks for the
per-model reading directly, so the residue is now the text and the ruling is overtaken.

`GfNames.Sniper`, `GfAttack.IsSniperGroup`, `GfAttack.SnipedModel`, the `snipe_target` Ask and
the `pick-target-model` step are all named for a rule that no longer exists. The Ask name is on
the wire and reaches generated TypeScript, which is the same spread that the invented "sealed
bid" reached before it was caught.

The other half of the clause is unchanged and still unmodelled: *"resolved as if it was a unit
of [1]"*. Cover, `Blast`'s cap and the defence roll still read the whole target unit, and only
the wound is redirected. 3.5.1's own example narrows to Stealth alone, having dropped 3.4.1's
Regeneration half, which is consistent with `Regeneration` now being a whole-unit rule.

### `Artillery` replaces `Entrenched`, and the aggregation question survives

3.4.1: *"Entrenched Enemies get -2 to hit when shooting at models with this special rule from
over 9" away, as long as the Entrenched model hasn't moved since the beginning of its last
activation."*

3.5.1: *"Artillery May only use Hold actions. When this model shoots at enemies over 9" away, it
gets +1 to hit rolls. When enemy units shoot at this model from over 9" away, they get -2 to hit
rolls."*

The movement condition is gone, and with it the only rule in the set that needed a model's
movement history: a unit that *"may only use Hold actions"* never moved. `Immobile` already
carries that clause, so `Artillery` is `Immobile` plus two range-banded hit modifiers.

**Interpreter question 4's aggregation problem is not dissolved, only renamed.** Both texts write
the -2 at model grain, and a to-hit roll is pooled per weapon group, so the penalty still cannot
be paid per model: page 8 sequences shooting as Determine Attacks, Roll to Hit, Roll to Block,
Remove Casualties, and *"the defending player may remove models from the target in any order"*,
so no hit belongs to a target model until after blocking. An aggregation has to be chosen, and
the books supply two precedents: `Stealth` is the nearer sibling, a to-hit penalty on shooting
from over 9" away, and says *"units where all models have this rule"*; cover is the nearer
mechanism, a per-model property modifying one pooled roll, and says *"the majority of models in
a unit"*. This is open; see *Open questions*.

The new +1 to hit when shooting over 9" is a second aggregation of the same shape, on the
attacking side. It is not the same question, because the attacker's roll can be split per
quality already under ruling 1.

### Multiplayer turn order simplifies

3.4.1 page 49: *"At the beginning of each round, including the first, players roll-off to
determine the order in which they activate their units. The player with the highest score goes
first, and then the players alternate in descending order."*

3.5.1 page 50: *"Turn Order: During the first round, turn order goes from the player that
deployed first to the one that deployed last. Starting from the second round, turn order goes
from the player that finished activating first to the one that finished last."*

3.5.1 unified multiplayer with the core game, which already said on page 7 that *"On each new
round the player that finished activating first on the last round gets to activate first"*. The
scheduler plan's `GfRules.FirstTeam` opens
`if (round == 0 || teams.Count > 2) return new RollOffNeeded(RollOffSeq);` and its own comment
cites *"Page 49: free-for-all rolls off every round, including the first"*. The fix deletes
`|| teams.Count > 2`, so the two-team branch becomes the branch for every team count, and the
comment cites page 50.

**The deployment roll-off is untouched.** Page 6 still reads *"the players roll-off and the
winner must start deploying their army first"*, and page 50 still reads *"All players roll-off
to determine the order in which they will deploy... starting with the player that got the
highest score, and then alternating in descending order"*. The ruling of 2026-09-06 that made
`GfRolls.RollOffSequence()` two steps, `roll-off` then `order`, so that a dice edit rewrites the
roll record and the ranking is derived after the gate, survives intact, and so does its reason:
the roll-off still has to yield the whole descending order rather than a winner. Only its
consumer narrows, from every round to deployment.

### `Shaken` gains a permission and loses several

3.4.1: *"Shaken units must stay idle, always count as fatigued, always fail morale tests, and
can't contest or seize objectives."*

3.5.1: *"Shaken units must stay idle, but may strike back counting as fatigued, always fail
morale tests, and can't seize or contest objectives."*

3.4.1: *"When activated, Shaken units must spend their activation being idle and doing
nothing."*

3.5.1: *"When activated, Shaken units must spend their activation being idle (can't take any
actions, or use any active special rules, such as casting spells, using buffs/debuffs on units,
re-positioning, etc.)."*

A Shaken defender is therefore offered the `strike_back` Ask, and fatigue applies to that strike
whatever else is true, which `GfRolls.HitPlan` already handles through its fatigued branch. A
Shaken caster may not cast, so `GfHooks.ReactionsAt` and `GfCasting.Casters` must exclude a
Shaken unit, and the exclusion reaches any effect that repositions a unit. `Shaken` appears 145
times across 12 documents, so this is the widest of the seventeen.

`Aircraft` keeps its exemption in both revisions: it *"may only use Advance actions... (even if
Shaken)"* and its *"mandatory move doesn't break the staying idle requirement to stop being
Shaken"*. The scheduler plan already reads `Aircraft` before `Shaken` for that reason.

## Not applicable yet

Every objectives and scoring change in 3.5.1 lands on ground this project has not built. The
repo places objective markers, in the setup phase and on the table, and implements no scoring:
`grep -riw seized docs` returns nothing, and no document holds a victory point.

- *"Seized"* becomes a distinct concept from *"control"*. A marker is seized by the act and then
  *"stay[s] under the player's control even if the unit moves away"*, and every mission and
  side-mission now scores control rather than seizure. One mission scores *"1 VP if they were
  the first to seize it"*, which is the only place the act still scores.
- A dropped objective marker is placed *"within 1” (placed by the opponent)"* rather than *"on
  the spot"*, and in one mission *"within 6” (placed by the defender)"*. Both are new player
  decisions, so a mission module would need an Ask that no spec describes.
- Six side-missions moved from *"3+ models"* to *"5+ models"*: Slaughter, Wipe Out, Exterminate,
  Butcher, Eradicate. Obliterate was rephrased to put the `Tough(12+)` floor after the
  superlative.
- The mission All-out-Assault went from *"+1 VP when seizing the objective markers inside
  deployment zones"* to *"+3 VP when seizing the objective marker in the enemy deployment
  zone"*, and Objective Denied gained *"can't be seized until the end of the round"*.

Recorded here so that whoever designs missions reads 3.5.1 rather than inheriting 3.4.1
vocabulary through this repository.

## Deferred modules that changed

Each is already deferred by an earlier decision, and none is implemented. Recorded so the
deferral is against the current text.

- **Action Boosting**, page 31: Hunker Down and Defensive Stance went from *"-3 to hit rolls
  (instead of -2)"* to *"-2 to hit rolls (instead of -1)"*.
- **Command Points**, page 45: Disrupted Sight went from *"-3” range"* to *"-1 to hit rolls"*.
- **Unit Psychology**, page 42: gained **Intimidation**, which decides a contested objective by
  comparing the point value of the contesting units, halving a unit at half or less of its
  starting size, and awarding the objective to a side with a total at least 50% higher.
- **Living Spells**, page 34: *"you may place it"* became *"it may be placed"*.
- **Random Events**, page 39: event 21 went from *"magic anomaly"* to *"psychic anomaly"*.

## The citation shift

Printed pages 1 to 14 hold the same material in both revisions except that the special rules
grew from two pages to three. Every printed page from 15 onward moved by one, because 3.5.1
inserts a Pre-made Rule Packs page at 16.

| Material | 3.4.1 | 3.5.1 |
| --- | --- | --- |
| Objectives, deployment roll-off | 6 | 6 |
| Rounds, actions, movement, coherency | 7 | 7 |
| Shooting, melee, melee resolution and morale | 8-10 | 8-10 |
| Terrain types and setup | 11-12 | 11-12 |
| Special rules | 13-14 | **13-15** |
| Pre-made rule packs | 15 | **16** |
| Terrain placement styles | 16 | **17** |
| Terrain generators | 17-18 | **18-19** |
| Army terrain | 19 | **20** |
| Solid buildings | 20 | **21** |
| Deployment styles | 21-23 | **22-24** |
| Missions | 24-26 | **25-27** |
| Side-missions | 27-28 | **28-29** |
| Extra actions, boosting, feats, advanced casting | 29-35 | **30-36** |
| Multiplayer | 49 | **50** |

Within the special rules, the entries moved as well:

| Entry | 3.4.1 | 3.5.1 |
| --- | --- | --- |
| `Aircraft` through `Flying` | 13 | 13 |
| `Furious`, `Hero` | 13 | **14** |
| `Immobile` through `Thrust` | 14 | 14 |
| `Tough`, `Transport`, `Unstoppable` | 14 | **15** |

The repository holds 535 `p.N` citations, 130 of them to page 13 and 125 to page 14, so the
special rules account for about half. `ExitWithin = 6` cites page 14 for *"fully within 6" of
it"* and must cite page 15, along with every other `Transport` reference.

`CLAUDE.md` and `docs/handoff.md` both carry an environment section naming the 3.4.1 file and a
useful-pages index built on 3.4.1 numbering. Both need the new filenames, the 71 and 70 page
counts, and a rebuilt index.

## Rulings made while propagating

Three of the four questions this document opened were put to the owner during the propagation
pass and answered the same day.

1. **`Takedown` picks per model.** *"This model may pick any model in the target unit as its
   individual target"* is taken at its word, so `GfShooting.Groups` splits a takedown weapon type
   into one group per model carrying it, each with its own `pick-target-model`, its own pooled
   `roll-to-hit` over its own attacks, and its own `deal-wounds`. A split group's name carries the
   model's ordinal, because the name is the key the target declaration and `fire-groups` use. This
   replaces the per-group compromise of the earlier ruling, which 3.4.1's *"each model with
   Sniper"* had forced on a pooled roll.
2. **Cover is p.11's majority, strictly more than half.** Five of ten models inside cover terrain
   is not a majority and gives nothing. This revises the 2026-09-06 ruling recorded as *"half or
   more inside cover terrain gives cover"*; that ruling's other clause, *"fewer than half the
   models visible gives cover"*, already means majority-hidden and needed no change, so both
   clauses now use one threshold. `GfRolls.InCover` is `* 2 > models`.
3. **`Artillery` aggregates `all`.** Every model in the unit must carry it for the -2 to apply.
   Decided after `Stealth`, which is the same to-hit penalty at the same range band and reads
   *"units where all models have this rule"* in both books; cover's majority was the alternative,
   and `any`, the plans' reading for `Entrenched`, the loosest.

## Open questions

Both were closed on 2026-09-09 by
[closing the open questions](2026-09-09-open-questions-design.md), and neither had blocked execution.

1. **Are weapons dealt onto models in order?** Management question 4, untouched by 3.5.1 because
   it is a question about Army Forge data rather than about the rules. **Ruled: an advancing
   cursor**, as assumed, so a lone flamer lands on the last model. Import stays deterministic and
   expands a unit on its own; the per-slot weapon editor and `PATCH` shape that asking the player
   would have needed are not written, and the player corrects a slot afterwards.
2. **How is the elevated 3" coherency measured?** Page 11's new sentence reads *"If not all models
   in a unit can fit on elevation, then a coherency of 3" is allowed to models at different
   elevation."* **Ruled: the allowance is applied to every cross-elevation pair and its precondition
   is never tested.** `Coherency.Check` gains an `elevatedChainDistance`, and a pair is at different
   elevation when `Distance.Vertical` puts its settled bottoms apart — which needs no ruleset
   judgement, because settling has already resolved Z. Whether the unit *can fit* stays untested:
   it is undefined in the book, and coherency is flagged rather than prevented, so the precondition
   can only ever widen the allowance. `maxSpread` is untouched, p.7's 9" having no elevation clause.

Interpreter question 4 is closed as asked, because `Entrenched` does not exist; it reopened as
`Artillery`'s aggregation and is ruled above. The `Sniper` per-group residue under interpreter
question 7 is closed by `Takedown`'s *"This model"* and the split that implements it.

**One propagation was owed rather than done, and was written on 2026-09-08.**
`StatReader.TargetNumber` takes an optional model and resolves either declaration level;
`GfRolls.HitQuality` names page 4's value per attacking model; `GfAttack.Attacks` sums a group's
attacks per Quality value, so `determine-attacks` writes `attacks_q<n>` and `fire-groups` settles
`attacks_<i>_q<n>` before it pushes the first group; `roll-to-hit` rolls one pooled `HitPlan` per
value; `GfRolls.DefensePlan` takes the defending unit's model count, settled with the attack counts
as `defender_models`, and reads the surviving model's own slot `Defense` when it is one; and
`GfRolls.MoralePlan` takes the best of the unit's Quality and every living `Hero` model's own.
Interpreter question 13 records the mechanism.

## Precedence

This document is the newest spec. It beats `2026-09-04-whole-design-review.md` and
`2026-09-06-simulation-design.md` on every point it touches, and it touches only the ruleset:
the rules text the engine implements, the names those rules carry, and the pages they are cited
from. It changes no module boundary, no wire shape beyond the `snipe_target` Ask name, and no
schema.
