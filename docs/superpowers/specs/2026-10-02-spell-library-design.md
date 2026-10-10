# The spell library

Design session, 2026-10-02. Every spell in every official Grimdark Future army book is castable in a
game and resolved by the engine, and an army imported from Army Forge brings its book's spells with it.

[Spell effects](2026-09-04-spell-effects-design.md) read 48 spells from eight books and fixed the
grammar's spell families, `duration = once`, the grants store and target selection. Casting is built
(`GfCasting`), and the shipped `grimdark-future-core.properties.json` holds no spells. This session reads
all 282 spells and settles the translation, where the library lives, which caster may cast what, and the
grammar additions the remaining shapes need. It replaces the ruling in
[open questions](2026-09-09-open-questions-design.md) that the core document holds no spells *"because a
spell list belongs to an army book this project has not designed"*.

## Read, not reasoned

The corpus is Army Forge's own data, downloaded once: `GET /api/army-books?filters=official&gameSystemSlug=grimdark-future`
lists 48 books, and `GET /api/army-books/{uid}?gameSystem=2` returns each with `spells[]` and `specialRules[]`.

- **47 books publish six spells each, 282 in all**; *Wormhole Daemons* (`i4ZEAmqeFSfAcw3f`) publishes none and
  no version. Every book's six are two of value 1, two of value 2 and two of value 3.
- **The spell's value is Army Forge's `threshold`**, not its `cost`. `threshold` is 1, 2 or 3 on all 282, and
  `cost` is a points figure (2 to 44, null on 45) that the list builder prices with. p.13 says a caster
  *"spend[s] as many tokens as the spell's value"*, so `catalog_entry.cost` is written from `threshold`.
- **Every spell carries `generation`**, an assignment and an effect, each a parameter list. The assignment is
  always `friendly-enemy`, `target` (`unit | model`), `inRange` (`within 6" | 9" | 12" | 18" | 24"`) and
  `numberText` (`one | up to two | up to three`). One spell (*Burn the Heretic*) carries an extra empty
  `with-without`, which is ignored.
- **The effect is one of sixteen Army Forge effect ids** with fifteen distinct parameter sets, tabulated in
  *Translation* below.
- **Spell names are not unique across books.** 185 names cover the 282 spells. 50 names repeat with identical
  text, because detachment, disciple and aspect books reprint their parent's spells. Five names repeat with
  different effects: *Shield Breaker* (Blood Brothers, Blood Prime Brothers), *Aura of Peace* (DAO Union, Rebel
  Guerrillas), *Psy-Injected Courage* (Human Defense Force against Human Inquisition and Titan Lords),
  *Electric Tempest* (the same three) and *Rapid Putrefaction* (Titan Lords Plague Disciples against two others).
  All six Dwarf Guilds names end in a space (`"Battle Rune "`).
- **Rule names are unique across books.** The 48 books' `specialRules` hold 1 473 rows under 361 names, and no
  name has two descriptions. A rule is one thing everywhere it appears, so army-book rules share one namespace
  with the core rules. Ten core names differ from the core document's text only in wording or quote marks.
- **Books are versioned on their own.** 43 books are `3.5.3` and the four Titan Lords disciple books `3.5.2`,
  against the rulebook's v3.5.1.

## A caster casts its own book's spells

p.13, *Caster(X)*: *"Note that Casters get spell tokens each round even if they are not on the table (waiting to
Ambush for example), and that the Caster's spells must be picked from their own faction."* A faction is an
army book: Army Forge has no unit of spells smaller or larger than a book, and every book's six are its own
list. So **a caster model may try exactly the spells of the army book its own Army Forge entry came from**,
under p.13's existing limits: as many tokens as the spell's value, one try per spell per caster model per activation
(`GfCasting.TriedPicks`), and none while Shaken.

**The book travels on the model slot.** Army Forge's TTS export carries `armyId`, the book `uid`, on every
unit entry. Import writes it to each slot built from that entry: `ModelSlot.ArmyBook`, a nullable string, and
`model_slot.army_book`. It is the slot's and not the unit's because every entry, a joined one included, carries its own `armyId`,
p.13's *"their own faction"* is the caster's, and `Caster(X)` is a model rule. Every slot of the fixture
`army-forge-c2-6YsWRH3py.json` reads `prsajf5ot936qznk`, Wormhole Daemons of War.

**`GfCasting.SpellOptions` filters by it.** For each caster model the options are the spells the pinned set
lists under that slot's book, and the cast Ask's labels and ids are unchanged (`"{spell} ({cost})"`,
`"{spell}|{modelId}"`). A slot with no book, or a book the pinned set does not list, offers no spell, so its
caster never opens the casting window. An army imported before this change has no book on any slot and casts
nothing until it is imported again from its Army Forge link; there is no backfill and no call to Army Forge
outside import. A game pinned to a set seeded before this change lists no books, so its casters cast nothing. This replaces the handoff's *"The spell list is every catalog spell, not
the faction's (p.13), until the army snapshot carries a faction"* and `schema.md`'s *"It is not enforced"*.

Advanced Casting (pp. 33–36) stays out. Its *Spell Books* draw X+1 random spells *"from their spell list"*,
which is this same per-book list, so the filter here is what that module would narrow later. Living Spells
and Relic Wargear carry their own lists and are not army-book content.

## One set holds every book

**A game's pinned set holds every official book's spells, not only those of the books its armies use.** A game
pins one `PropertySet` by hash at creation (`CreateGameRequest.PropertiesHash`), and its armies are chosen
later, in the lobby. A set narrowed to the armies' books would have to be minted when the lobby closes, which
is a second pin, and would give two games of the same ruleset different hashes for nothing but their
match-ups. So the library is part of the ruleset: one set, one hash, and the caster's book decides what is
offered. A set of 33 core rules, 73 army-book rules, 190 spells and 48 book records is small next to
what hydration already reads.

**The authored content is three kinds of file, composed into one document before hashing:**

| File | Holds | Written by |
|---|---|---|
| `server/game/Content/grimdark-future-core.properties.json` | the rulebook's pp. 13–15 rules, as today | hand |
| `server/game/Content/grimdark-future-army-rules.part.json` | the army-book rules spells name, and the rules those name (*Army-book rules spells name*) | hand |
| `server/game/Content/army-books/<uid>.book.json`, 48 files | one book: `uid`, `name`, `version`, `gameRules`, and its spells as entries | the generator |

`PropertiesSeeder.EmbeddedDocuments` returns, per `*.properties.json`, that document with every `*.part.json`
and `*.book.json` of the same `gameRules` folded in: entries are unioned, and a spell that two books reprint is
written once. Two entries with one name are one entry when their canonical forms are equal, and a refusal
otherwise. The composed document then validates, hashes and seeds exactly as one authored document does
today, so `PropertiesJson.Hash` covers the books, and regenerating any book makes a new set while old games
keep theirs. The core document's `name` becomes `Grimdark Future v3.5.1`, because the set now holds 48 books as
well as the core rules. `GET /v1/properties` lists the newest set per `(game_rules, name)`, so the composed set
is listed under the new name and the core-only set stays listed under `Grimdark Future v3.5.1 core` for the
games that pinned it.

**`PropertySetDocument` gains `books`**: `uid`, `name`, `version` and `spells`, the entry names the book lists,
in its order. `PropertySet` gains `Books`, keyed by `uid`. The validator refuses a book spell name with no
`spell` entry, two books with one `uid`, and a `spell` entry no book lists. Books sort by `uid` in the canonical
form; a book's spell order is kept.

**A book's version is a property of the set, not of the game.** OPR revises army books apart from the
rulebook within the 3.5 line, so the set pins rulebook v3.5.1 and each book at the `version` its record carries
(`3.5.3`, `3.5.2`). A newer revision of one book is a regenerated file, a new hash and a new listed set. An army
imported against a newer revision than the pinned one still names the same `uid` and gets the pinned
revision's spells, which is the same trade every pinned rule already makes.

**Spell names are made unique by the generator.** A name every book holding it uses for one spell stays bare:
*Blessed Ammo* is one entry, listed by six books. A name used for two or more different spells gives every
variant the name of the alphabetically first book holding it, in parentheses: *Shield Breaker (Blood Brothers)*
and *Shield Breaker (Blood Prime Brothers)*; *Psy-Injected Courage (Human Defense Force)* and *Psy-Injected
Courage (Human Inquisition)*, the second listed by Titan Lords too. That touches 10 entries. Names are trimmed.
`catalog_entry.faction` is left null on generated entries: one entry may be listed by six books, and
`book_spell` is now what says which. Nothing reads `faction`.

## The generator

`scripts/ArmyBooks/` is a console project, `Tabletop.ArmyBooks`, in `Tabletop.sln`, run as
`./scripts/army-books`. It is repo-level support: neither shipped nor referenced by a deliverable. It references
`server/game` for `PropertySetDocument` and `PropertiesJson.Validate`, as `sim/` does.

- `./scripts/army-books fetch [--to DIR]` downloads the official listing and every book into `DIR` (default
  `.superpowers/armyforge/`, git-ignored). It is the only path that calls Army Forge, run by hand.
- `./scripts/army-books generate [--from DIR]` reads `DIR/index.json` and `DIR/books/*.json`, translates, and
  rewrites `server/game/Content/army-books/`. It writes a file per listed book, the empty one included, and
  deletes none it did not write.

`generate` fails, and writes nothing, on any of: a generation shape not in the table below; a spell whose
`effect` text differs from the text the translation renders back (*Text and generation*); a rule a spell names
that is neither in the core document nor in the army-rules part; a name collision it cannot qualify; or a
composed document that `PropertiesJson.Validate` refuses. Each failure names the book and spell. Its output is
deterministic: the same dump writes the same bytes, so a regeneration with no Army Forge change is an empty
diff.

**The dump is not checked in; the generated books are.** Reviewing a book revision means reading the diff of
its `.book.json`. Tests read checked-in fixtures, never Army Forge (*Testing*).

## Translation

**The assignment is the targeting**, unchanged from spell effects: `friendly-enemy` → `target_side`, `target`
→ `target_grain`, `numberText` → `target_count` (*one* 1, *up to two* 2, *up to three* 3), `inRange` →
`target_range`. The effect, by Army Forge effect id; every grant is `duration = once`, and *rule* means the
entry Army Forge names, with its `rating` as `amount`:

| # | Effect id, parameters | Spells | Effect document |
|---|---|---|---|
| 1 | `QFjxWsOHTCSi`: `numberDigit`, `hits-wounds`, up to two `weaponRule`, optional `spellHitRoll`; unit target | 118 | `add_hit`, amount `numberDigit × value`, `target_roll` hit when `spellHitRoll` is not empty; one `grant_property` child per weapon rule |
| 2 | the same, model target | 23 | the same, resolved as a unit of one (below) |
| 3 | `RWnJrrMKAUbg`: `unitRule`, `gets` | 76 | `grant_property` rule, subject `self` |
| 4 | `1yTPcanTyckX`: `unitRule`, *"which friendly units get … against"* | 10 | `grant_property` rule, subject `opponent` |
| 5 | `54xO9EKTj-ix`: `weaponRule`, `attackType`, *"against"* | 17 | `grant_property` rule, subject `opponent`, `attack_kind` |
| 6 | `i1BVqztsr1V6`: `weaponRule`, `attackType`, `gets`/`loses` | 9 | *gets*: `grant_property` rule, subject `self`, `attack_kind`; *loses AP(1)*: `modify_parameter` AP −1 |
| 7 | `4xqVFHJrq78d`: `rollModifier` | 4 | `modify_roll` casting, subject `self` |
| 8 | `txtrnRX2fPJJ`: `rollModifier` | 4 | `modify_roll` defense, subject `self` |
| 9 | `nxIQS1UnUBL_`: `rollModifier` | 4 | `modify_roll` morale, subject `self` |
| 10 | `gqflE2vy38aQ`: `rollModifier`, `attackType` | 2 | `modify_roll` hit, subject `self`, `attack_kind` |
| 11 | `YbOvC9oIIli9`: `rollModifier`, `attackType`, *"against"* | 1 | `modify_roll` hit, subject `opponent`, `attack_kind` |
| 12 | `rz0b1dShgnvg`: `advanceSpeed`, `rushSpeed` | 4 | three `modify_stat` rows: `Advance` by `advanceSpeed`, `Rush` and `Charge` by `rushSpeed` |
| 13 | `7w-FrWUjKqqG`, `LixCCuyRq4-_`: `rangeModifier` | 2 | `modify_stat` weapon `Range` +6, `attack_kind` shooting; subject `self`, or `opponent` for *against* |
| 14 | `_m0wja4blbg8`: `terrainType` | 4 | `count_as_terrain` dangerous or difficult (new) |
| 15 | `ZcZbsyKjR5yN`: `moraleTest`, `becomes` | 3 | `take_test` morale; on failure `set_condition` fatigued (new) |
| 16 | `q9OJ_WVwJw5w`: `moraleMove`, `failed` | 1 | `take_test` morale; on failure `move` up to 6" (new) |

`attackType` maps *when shooting* to `attack_kind = shooting`, *in melee* to `melee`, and *when attacking* to
both, as one condition with two values. `Conditions.Membership` already reads a value set, but no authored row
uses more than one value today, so the first rows to do so are these. *Shock Speed*'s *"+4" when using Rush/Charge
actions"* is two rows because `Rush` and `Charge` are two actions in the pinned set, exactly as `Fast` and
`Slow` are written.

**Hits are `numberDigit` times the spell's value.** `numberDigit` is not the hit count: *Psychic Terror*
(value 3) carries `numberDigit = 3` and reads *"takes 9 hits with Bane"*. `hits = numberDigit × threshold`
holds on all 141 damage spells and nowhere fails, so the translation multiplies. `hits-wounds` is `hits` on all
141. `weaponRule1` and `weaponRule2` become `grant_property` children of the `add_hit`, with `amount` the
weapon rule's `rating` (`AP(2)` → AP 2, `Blast(3)` → Blast 3, `Deadly(3)` → Deadly 3), which is how the
built `Fireball` test spell is written.

**A spell's weapon rules reach its multipliers.** Spell effects ruled that damage spells need nothing new, and the
grant children do carry `AP` to the block. They do not carry `Blast` or `Deadly`: the spell-hits step writes
`hit_multiplier = 1` whatever the spell holds, and the assign step's wound multiplier reads a live set that holds
none of the spell's granted rules. So the 16 spells with `Blast(3)` and the 9 with `Deadly(3)` multiply nothing
today. The spell-hits step computes `hit_multiplier` from the spell's granted rules, capped at one model for a
model target, and the assign step folds the attacking frame's `granted_*` rules into its wound multiplier.

**A spell rolls dice for its hits only when it says so.** `spellHitRoll` is non-empty on 20 spells, 19 reading
*"Roll as many dice as hits to see if "on rolls of 6" effects trigger"* and one (*Power Surge*) *"… on rolls of
1 …"*. On those the `add_hit` carries `target_roll = hit`, and the spell-hits step rolls one die per hit at
threshold 1, the hit roll that cannot fail from spell effects. On the other 121 `target_roll` is null and the
hits land with no die rolled, so no per-die rule triggers. This replaces spell effects' *"Spell hits are rolled
as a hit roll that cannot fail"* for every spell, which read a sentence 121 spells do not print. The column
exists; the validator admits `target_roll` on a spell's top-level `add_hit`.

**A model-target spell is a Takedown target.** Army Forge's model shape always ends *"This effect is resolved
as if the target was a unit of [1]"*, which is p.14's *Takedown* wording (*"resolved as if it was a unit of
[1]"*). The casting frame's `spell_targets` answer names the model, and `GfAttack.TakedownModel` reads it there
as it reads `pick-target-model` from a Takedown group, so its wounds go to that model with no
`remove_casualties` Ask, Blast is capped at one model, and Deadly's excess does not carry over. Today the
effects step pushes `spell-hits` at the model's unit and the model is lost; this closes the interpreter's
model-grain residue.

**Every *against* effect reaches only the caster's side.** *"Which friendly units get X against"* is a grant on
the enemy unit with subject `opponent` (spell effects → *The grants store*). In a game of more than two teams the
opponent of a marked unit is not always friendly to the caster, so a subject-`opponent` once-effect applies only
when the opponent's team is the team of the grant's `CasterUnitId`. That is p.13's *friendly* read against the
grant row, and needs no predicate.

### Text and generation

**The printed text wins, and a disagreement stops the generator.** `effect` is the rule a player reads, the
text the rulebook's casting rule resolves, and the only form OPR prints; `generation` is Army Forge's structure
behind it. The generator renders, from each spell's generation, the targeting clause (count, side, grain,
range), the hit count, each named rule with its rating, the attack-type phrase and the roll modifier, and
requires each in the text. All 282 agree once hits are read as `numberDigit × value`; the 95 that seemed to
disagree were exactly the damage spells of value 2 or 3. A future disagreement is not resolved by either side:
`generate` fails on it, and the fix is an entry in the generator's override table, keyed by the Army Forge
spell `id` and holding the hand-written entry. The table is empty.

Two spells carry `Surge`, whose trigger is an unmodified 6 to hit, with no *"roll as many dice"* sentence in
text or generation: *Zap!* (Goblin Reclaimers) and *Rapid Putrefaction* as Titan Lords Plague Disciples prints
it. The printed text wins: they roll no dice, so `Surge` does nothing on them. The Titan Lords printing of *Rapid
Putrefaction* stays its own entry, apart from the identical Plague Disciples and Wormhole Daemons of Plague
printings, because its `rule_text` and its `target_roll` differ.

## A grant is its effect row

The built effects step writes one `Grant` per once-effect with `EntryName = effect.TargetEntry ?? spell.Name`,
and `LiveProperties.Gather` makes the named entry live with all its effects. Three of the corpus's shapes break
on that reading. *"Get Shred when shooting"* would grant an unconditional `Shred`, dropping the condition that
lives on the spell's row. *"Friendly units get Relentless against"* would give the enemy unit `Relentless`.
*Shock Speed*'s three rows would write three grants naming the spell, each making all three rows live, so the
unit would move +6" on Advance. So:

- **`Grant.EntryName` is the spell's name** and **`Grant.EffectId` is the one row the grant holds**. A granted
  instance in `LiveProperties.Gather` exposes that row and its children, and nothing else of the spell. A
  `grant_property` row then works as it does on any property: pass one adds the named rule to the side its
  subject names, under the row's own conditions, and the added instance carries the grant's id. Every field
  keeps its name; only what `EntryName` holds changes, so no Protocol type is renamed.
- **The change of meaning reaches four readers**, each keyed today on `EntryName` naming the target entry:
  `LiveProperties.Gather` (`props.Entry(g.EntryName)`), `Lookup.UnitProperties`, `GfAttack`'s assign step
  (`regen.Source?.GrantId`, line 123, which must also find the grant id on an instance pass one added) and the
  `Ward` tests in `GfCastingTests`. `LiveProperty` gains a trailing `Effect? GrantRow`: set, the instance exposes
  that row and its children instead of `Entry.Effects`.
- **One cast writes one grant per once-row per target**, and **the grants a spell left on a unit are consumed
  together.** *"+2" Advance and +4" Rush/Charge once"* is one effect that applies at the next move, whichever
  action it is. Grants are grouped by `(unit, EntryName)`.
- **The same spell does not stack.** p.13: *"Effects from multiple instances of the same special rule or spell
  don't stack, unless it is a rule with (X) in its name"*. A cast whose target already holds grants of that
  spell writes none, and the cast's tokens are still spent. Different spells stack as modifiers do.
- **Name checks resolve a grant through its row.** `Lookup.UnitProperties` and `UnitHas` count a granted rule
  when the grant's row is a subject-`self` `grant_property` naming it. A subject-`opponent` row grants the unit
  nothing, so *Relentless against* no longer reads as the enemy's own `Relentless`.

**A grant is consumed by the first step that would apply it.** *"Once (next time the effect would apply)"*,
read with the grammar: the grant's row is returned by a query the step acts on, with every condition holding
except a per-die one. The per-die exception is what makes *"gets Rending once"* last until the unit's next
attack rather than until its next 6. Concretely, a step consumes when:

| The granted row is | Consumed by |
|---|---|
| a roll modifier | the step that rolls that roll for the side it acts on (built, `RollEffects.ConsumeGrants`) |
| a property's own roll | the step that takes the roll (built for `Regeneration`) |
| a stat modifier on an action's distance | the step that measures that action's move |
| a stat modifier on `Range` | the shooting attack step whose eligibility read it |
| a granted rule (`grant_property`) | the first step whose query returns any effect of the granted rule, under the same test |
| a parameter modifier, a suppression | the step that settles the interaction it changed |
| `count_as_terrain` | the step the terrain type would trigger (*Grammar additions*) |
| a granted `Flying` | the end of the activation in which it first applied (owner ruling below) |

`LiveSet` records which granted sources its queries returned, and the step that settled it emits
`GrantRemoved` for each grant in their groups. **Reading a grant to build a legal set does not consume it**:
`GfMelee.ChargeTargets` reads *Shock Speed*'s Charge row to decide whether Charge is offered, and only the
charge move's measuring step consumes it. A grant whose conditions never hold is never consumed, as spell
effects already ruled.

**A granted Flying lasts the activation.** The owner's ruling: *"Granted flying applies to the whole move - its per
activation"*. The first step that applies it (the activation-start dangerous terrain test it skips, or an applied move of
the unit) keeps that spell's grants on the unit and names them in its `Values`; they stay live for every later leg of the
move and its landing, and the turn-end hook removes them when that activation ends (`GfMoves.ConsumeKeepingFlying`,
`GfMoves.FlyingHeldThisActivation`).

## Grammar additions

Twelve kinds become sixteen and eight predicates ten. Each addition is quoted from the text that needs it;
nothing here is used by the core rulebook.

**`add_hit` takes `target_quantity`**, `hits` by default or `wounds`. With `wounds`, each die meeting the row's
conditions adds one wound to the side the subject names. Subject `opponent` on a defense die is *Shred*:
*"On unmodified results of 1 to block hits, this weapon deals 1 extra wound"*, and *Warbound*, *Infected* and
their Boosts. Subject `self` on a hit die is *Hazardous*: *"this model's unit takes one wound on unmodified
rolls of 1 to hit"*. An extra wound from a defense die is a wound of the hit whose block failed, so a wound
multiplier (`Deadly`) applies to it. The column and its check already exist and the validator admits it on kind
6, but the engine work is new: `RollEffects.PerDie` sums every `add_hit` as extra hits and ignores
`target_quantity`, and no step reads a defense die for anything but success. `PerDie` returns extra wounds apart
from extra hits; `GfAttack.WoundsToDeal` adds the defense dice's extra wounds to the failures; a hit die's
subject-`self` wounds push a `deal-wounds` at the attacker's own unit after the attack resolves.

**`attack_kind` gains `spell`.** Spell hits resolve with attack kind `spell`, so no *when shooting* or *in
melee* rule reaches them, `Stealth`'s *"shot from over 9" away"* included. This is a fix as well as an addition:
today `GfAttack.KindOf` answers `Melee` for any weapon list without a `Range` stat, and a spell's hits carry no
weapon, so they resolve as melee and every *in melee* rule on the target applies to them. *Shielded* (*"+1 to defense rolls
against hits that are not from spells"*) is `attack_kind neq spell`; *Resistance* (*"If the wounds were from a
spell, then they are ignored on a 2+ instead"*) is two rolls split on it. **A morale test's interaction carries
the attack kind that caused it**: `melee` for p.10's melee test, the kind of the unit's last wound that
activation for the general test, `spell` for `take_test`. *Hive Bond* and *Hold the Line* (*"+1 to morale test
rolls triggered by shooting/melee"*) are `attack_kind` in `{shooting, melee}`.

**In a Charge's melee, `range` reads the distance at the charge's start.** *"When it shoots or charges enemies
over 9" away"* and *"shot or charged from over 9" away"* measure the charge, and once the charger is in base
contact the two units are under 1" apart. The melee interaction of a Charge takes `RangeInches` from the
distance between the two units before the charge move, read from the log: the charge sequence's
`charge-target` step, which closes before the move, records `charge_range` in hundredths of an inch among its
values (`Values` holds integers), and the melee interaction reads it off the charge frame, the nearest ancestor
whose sequence is `charge`. Shooting is unchanged.

**`GfAttack` changes in four places** for the above: `KindOf` (a weapon-less group under a casting frame is
`Spell`); `AttackInteraction` (the charge-start range, and the attack kind passed through for spells);
`TakedownModel` (it walks to the nearest attack-sequence frame today, and reading the target model from a
casting frame's `spell_targets` answer is new); and `AssignStep` (its interaction carries the attack kind so
*Resistance*'s split and *Shielded* read it, its wound count includes extra wounds, and it finds a granted
`Regeneration`'s grant through the row-bearing instance).

**Predicate `holds_property`**, values `[entry]`, with a subject: the subject side holds the entry, under the
entry's own aggregation, granted instances included. A suppression does not unhold, so a Boost that suppresses
its base rule still sees it. Every Boost needs it: *"If this model has Warbound"*, *"If all models in this unit
have Changebound"*.

**Predicate `most_models`**, values `[name]`, with a subject, a comparison and a number: more than half the
subject side's models meet the comparison. A model stat compares its target number, so *"most models have
Defense 2+ to Defense 3+"* is `Defense lte 3`; a rule compares its parameter, and a model without it fails, so
*"most models have Tough(3) or higher"* is `Tough gte 3`. *Shatter*, *Tear*, *Disintegrate*, *Slayer*.

**Kind 13, `count_as_terrain`**, new column `target_terrain` (`difficult | dangerous`): the holder counts as being
in that terrain type (p.11). *Difficult*: *"all models in the unit may not move more than 6" for that
movement"*, applied to the holder's next move unless it ignores difficult terrain, and consumed by it.
*Dangerous*: *"If a model moves in or through dangerous terrain, or is activated in it, then it must take a
dangerous terrain test"*. The holder's next activation begins with the built `dangerous-terrain` sequence over
all its models, which is the first of those triggers to arrive, and consumes the grant. **An Idle activation is
an activation**, so it runs the test too: today Idle runs no sequence (`Activation.RunsSequence` is false), and a
Shaken unit standing in real dangerous terrain skips p.11's *"or is activated in it"*. `GrimdarkFutureRules.ReadAnswer`
gives Idle `RunsSequence: true`, `ActionSequence("Idle")` names a new one-step `idle` sequence holding
`GfMoves.ActivationTerrainStep`, and `GfHooks.OnTurnEnded` still reads the action as `Idle`.

**Kind 14, `take_test`**, with `target_roll` (`morale` in the corpus): the subject's unit takes the test now,
through the built `morale-test` sequence and its ordinary consequence (p.10: *"If the test is failed, the unit
is Shaken"*). Its children apply when the test fails. It is the third spell family, so a `spell` entry's
top-level effects are `add_hit`, `duration = once`, or `take_test`.

**Kind 15, `set_condition`**, new column `target_condition` (`fatigued` in the corpus): sets the condition on
the subject's unit. *Terrifying Fury*: *"If failed, it becomes fatigued"*.

**Kind 16, `move`**, with `amount` in inches: the caster's team gets one `move` Ask, a `MoveRegion` over the
subject unit's alive models with that allowance and the impassable and difficult volumes and cap that unit's own move has
(the owner's ruling: *"Spells move as however that unit moves"*, so a Flying, Strider or Aircraft target keeps its rules), and
may pass. *Deep Hypnosis*: *"If failed you may move it by up to 6" in a straight line in any direction"*. Every
move the engine measures is a straight path per model (`GfMoves`), and `LegalSetMembership` checks a
`Destinations` answer's model ids only, so moving another team's unit needs no new legal set.

**The casting roll reads the live set.** `GfRolls.CastingPlan`, which builds its plan from the tokens alone,
settles a lone interaction for the caster's unit and adds its `casting` modifiers, so *Burn the Heretic*'s *"-3 to
casting rolls once"* applies, and is consumed, at that unit's next attempt.

## Army-book rules spells name

The spells name 64 rules. Fourteen are in the core document: `AP`, `Bane`, `Blast`, `Deadly`, `Fearless`,
`Flying`, `Furious`, `Indirect`, `Regeneration`, `Relentless`, `Rending`, `Stealth`, `Surge`, `Unstoppable`. The
other 50 are army-book rules, and 23 of those are Boosts that name a base rule of their own (*"If this model has
Warbound, … (instead of only on 1)"*), so 73 entries go into `grimdark-future-army-rules.part.json`, each with
its book's `description` as `rule_text`. Kind and aggregation follow the text as the core document reads it:
*"units where all models have this rule"* is a model rule with aggregation `all`, a weapon rule is `weapon`.
Authoring them also makes them work on every imported unit that carries them, which today they silently do not
(`LiveProperties.Gather` skips a name the set lacks). **The other 261 army-book rule names** (361 in the books,
less 27 core names and these 73) still have no entry, so on an imported unit they still do nothing. Each is a
later authoring task in this same part file.

| Rules | Written as |
|---|---|
| *Crack*, *Destructive* | `grant_property` AP 2, AP 4 on an unmodified 6 to hit, as `Rending` |
| *Lacerate* | `force_reroll` defense on an unmodified 6, `Bane`'s second clause |
| *Shred*, *Warbound*, *Infected* | `add_hit` wounds, subject `opponent`, on an unmodified defense 1 |
| *Hazardous* | `grant_property` AP 4; `add_hit` wounds, subject `self`, on an unmodified hit 1 |
| *Shatter*, *Tear* | `grant_property` AP 2, AP 4, `most_models` `Tough gte 3`, `gte 9` |
| *Disintegrate* | `suppress_property` Regeneration; `grant_property` AP 2, `most_models` `Defense lte 3` |
| *Slayer* | `grant_property` AP 2, `most_models` `Tough gte 3`; one row shooting over 9", one row on a Charge |
| *Evasive*, *Melee Evasion* | `modify_roll` hit −1, subject `opponent`; the second in melee |
| *Bane when Shooting*, *Piercing Assault*, *Piercing Hunter* | `grant_property` Bane shooting; AP 1 on a Charge; AP 1 shooting over 9" |
| *Rapid Advance*, *Rapid Rush* | `modify_stat` Advance +4; Rush +6 |
| *Shielded*, *Resistance* | defense +1 when `attack_kind neq spell`; `roll` 6+ and 2+ overriding a wound, split on `spell` |
| *Changebound*, *Machine-Fog*, *Sturdy*, *Guardian*, *Havocbound*, *Targeting Visor*, *Devout*, *Ferocious*, *Mischievous*, *Scrapper*, *Plaguebound*, *Self-Repair*, *Highborn*, *Scurry*, *Hive Bond*, *Hold the Line*, *Lustbound* | their own text, with the charge-start range and the morale attack kind above |
| *Counter-Attack*, *Unwieldy* | names `GfMelee`'s strike order checks: *"Strikes first when charged"* beside `Counter`, *"Strikes last when charging"* |

**A Boost is written in one of three forms**, by what its *instead* replaces, each conditioned on
`holds_property` of its base:

- **A condition dropped** (*Changebound*, *Machine-Fog*, *Sturdy*, *Guardian*, *Havocbound*, *Targeting Visor
  Boost*: *"always … (instead of only when …)"*): `suppress_property` the base, and the base's effect with no
  condition.
- **A face widened** (*Devout*, *Ferocious*, *Mischievous*, *Scrapper*, *Warbound*, *Infected Boost*: *"5-6
  (instead of only on 6)"*, *"1-2 (instead of only on 1)"*): the base stays live, and the Boost adds the extra
  face's row under its own conditions. *Plaguebound* and *Self-Repair Boost* change a roll's threshold rather
  than a face, so they suppress the base and roll at 5+.
- **An amount raised** (*Hive Bond*, *Hold the Line*, *Highborn*, *Scurry*, *Lustbound Boost*: *"+2 (instead of
  only +1)"*): `suppress_property` the base, and the base's rows at the boosted amount.

**Fifteen entries are names with no effect rows in this plan**, because each needs a hook the engine does not have:
*Hit & Run Fighter*, *Hit & Run Shooter*, *Harassing* and its Boost (a move after shooting or melee),
*Teleport* (a placement before attacking), *Quick Shot* (shooting after Rush), *Rapid Blink* and its Boost (a
placement on activation), *Unpredictable Fighter*, *Unpredictable Shooter* (a die rolled at the attack whose
face picks the modifier), *Primal* and *Clan Warrior* with their Boosts (extra attacks, *"This rule doesn't
apply to newly generated attacks"*), and *Grounded Reinforcement* (*"most of them within 1" of terrain"*). Fifteen
spells, counting reprints, grant one of them. Their hooks are
[army-rule hooks](2026-10-02-army-rule-hooks-design.md), built by the same tree in a second plan after this one;
until that plan lands, the spells are castable, write their grants, and the grants change nothing.

## Schema and wire

Management migration `005_spell_library.sql`:

- `book (properties_hash, uid, name, version)`, key `(properties_hash, uid)`, cascading from `properties`;
  `version` is null for the book that publishes none.
- `book_spell (properties_hash, book_uid, entry_name, ordinal)`, key `(properties_hash, book_uid, entry_name)`,
  referencing `book` and `catalog_entry`.
- `effect.kind`'s check admits `count_as_terrain`, `take_test`, `set_condition`, `move`; `effect` gains
  `target_terrain` (`difficult | dangerous`) and `target_condition`; `effect_condition.kind`'s check admits
  `holds_property` and `most_models`.
- `model_slot.army_book text null`.

`PostgresPropertiesStore` and `InMemoryPropertiesStore` write and load books. `ArmyRepository` and
`PostgresGameRows.ReadArmyAsync` carry `army_book`; an army snapshot is JSON, so the simulator's seeded snapshots
carry it with no change to `SocketRowSeeder`.

**No wire kind is added and `ProtocolVersion` stays.** `ModelSlot` gains a trailing optional `ArmyBook = null`,
which is additive JSON, so existing constructors compile and an old snapshot reads as no book. The new effects
travel as built changes and Asks: `GrantAdded`/`GrantRemoved`, `ConditionSet`, a `move` `MoveRegion`, the
`morale-test` and `dangerous-terrain` sequences. `viewer/src/lib/protocol/wire.generated.ts` is regenerated for
the field and `cd viewer && npm run check` stays green; recordings change additively, so
`RecordingJson.SchemaVersion` stays.

## Testing

**The generator** (`tests/Tabletop.ArmyBooks.Tests`, against fixture books trimmed from the dump and checked in
under its `Fixtures/`): one test per row of the translation table, each on a real spell; hits as `numberDigit ×
value`; a model target; *against* rows; the five name collisions qualified and the 50 reprints merged; trailing
spaces trimmed; a text and generation disagreement failing with the book and spell named; a rule missing from
both documents failing; and byte-identical output from two runs. No test calls Army Forge.

**The content** (`tests/Tabletop.GameServer.Tests`, `SpellLibraryTests`): the composed set validates; it holds
48 books and every listed spell resolves; the hash is stable across two compositions and changes when one book
file changes; `PropertiesStoreTests` round-trips books through Postgres. **Every spell is cast**: a theory over
every spell entry of the shipped set fields a caster of a book listing it and a legal target in range, casts it
with scripted dice, and asserts the outcome its family promises: hits at its count, rolled exactly when the spell
prints the roll sentence; a grant written per once-row; or the test's sequence pushed. That covers each of the sixteen shapes on real content and fails the
day a regeneration produces a spell the engine cannot cast.

**The engine additions**, each with its own tests beside the built ones: grants holding one row (*Shred when
shooting* applies to a shooting attack and not a melee one; *Relentless against* gives the enemy nothing; *Shock
Speed* moves +2" once and its three grants go together); no stacking of one spell; consumption per the table;
the *against* team check with three teams; `add_hit` wounds on both subjects; `attack_kind spell` against
`Stealth` and *Shielded*; the charge-start range; both predicates; the four new kinds; the casting roll
modifier; the model target taking its wounds as a unit of one; *Zap!* rolling no die; a Shaken unit idling in
dangerous terrain taking its test; Charge offered from *Shock Speed*'s grant without consuming it; the cast
options filtered by the slot's book; and import writing `ArmyBook` from the TTS fixture's `armyId`.

**The simulator.** The `casting` scenario fields a `Caster(2)` slot whose `ArmyBook` is Human Defense Force
(`z65fgu0l29i4lnlu`), whose six spells cover a unit-rule grant, an *against* grant, a speed grant, unit and model
damage, and one to three targets. `ScenarioBuilder.Spells`, `Scenario.Spells`, the fold in
`ScenarioCompiler.PropertiesFor` and the `Bolt` spell in `ScenarioCatalog` are deleted, so every scenario runs on
the production hash. **`--driver socket` no longer refuses `casting`**: the refusal in `sim/Program.cs` and its
line in `SimOptions` go, because the game server seeds the same composed set the scenario pins. One invariant is
added to `sim/Invariants/`, numbered after the enforcement tree's sixth (`NothingAdvancesAfterTheEnd`), so it is
the seventh, `SpellsComeFromTheCastersBook`: every closed `pick-spell` names a spell its caster's book lists, and every grant's
`EffectId` resolves in the pinned set. `TestProperties.WithSpells` stays for unit tests that fabricate a spell, and lists
its spells under a test book so the validator's rule that every spell is listed holds.

**The viewer's agreement fixtures are not regenerated.** `skirmish` and `terrain` embed the old core hash as
data, and `AgreementFixtureTests` replays their own events through `FrameProjector`, which never loads a set; an
old snapshot's slots read as no book. `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`
rewrites `wire.generated.ts` for `ArmyBook`.


## Files touched

New fields on authored records (`EffectDocument`, `EntryDocument`, `ConditionDocument`, `Effect`, `CatalogEntry`,
`LiveProperty`, `ModelSlot`) are trailing optional parameters, so the positional builders in `PropertySetTests`,
`GfRollsTests` (line 134), `OutOfSetAnswerTests` (lines 186 and 245) and `GfCastingTests` keep compiling.

Created:

- `server/game/Content/grimdark-future-army-rules.part.json`; `server/game/Content/army-books/<uid>.book.json`, 48 files
- `server/management/Persistence/Migrations/005_spell_library.sql`
- `scripts/army-books`; `scripts/ArmyBooks/Tabletop.ArmyBooks.csproj`, `Program.cs`, `ArmyForgeBook.cs` (the dump's
  shape), `SpellTranslator.cs` (the table and the override table), `TextCheck.cs`, `BookWriter.cs` (names, merge, files)
- `tests/Tabletop.ArmyBooks.Tests/` (`Tabletop.ArmyBooks.Tests.csproj`, `SpellTranslatorTests.cs`,
  `BookWriterTests.cs`, `Fixtures/*.json`)
- `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs`, `GrantRowTests.cs`, `SpellShapeTests.cs`

Modified:

- `Tabletop.sln`; `server/game/Tabletop.GameServer.csproj` (embeds `Content/*.part.json` and `Content/army-books/*.book.json`)
- `server/game/Properties/PropertySet.cs`, `PropertySetDocument.cs`, `PropertiesSeeder.cs`, `PostgresPropertiesStore.cs`,
  `IPropertiesStore.cs`
- `server/game/Effects/Conditions.cs`, `Interaction.cs`, `LiveProperties.cs`, `Lookup.cs`, `RollEffects.cs`, `StatReader.cs`
- `server/game/Rules/GrimdarkFuture/GfCasting.cs`, `GfAttack.cs`, `GfMelee.cs`, `GfMorale.cs`, `GfMoves.cs`, `GfRolls.cs`,
  `GfNames.cs`, `GfRules.Sequences.cs`, `GfRules.Scheduling.cs` (`ReadAnswer` for Idle only)
- `server/game/Persistence/PostgresGameRows.cs` (`ReadArmyAsync` only)
- `libs/Tabletop.Content/Army.cs`
- `server/management/Content/Armies/ArmyForgeList.cs`, `ArmyForgeImport.cs`, `ArmyRepository.cs`
- `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs` (the core set comes from
  `PropertiesSeeder.EmbeddedDocuments`), `Infrastructure/Armies.cs` and `Infrastructure/TestArmies.cs` (a caster
  slot carries a book, or the book filter offers it nothing), `GfCastingTests.cs`, `CoreContentTests.cs`,
  `PropertySetDocumentTests.cs`, `PropertySetTests.cs`, `PropertiesStoreTests.cs`, `LivePropertiesTests.cs`,
  `RollEffectsTests.cs`, `GameRowsReadTests.cs`, `RoundBoundaryTests.cs`, `CommandDispatcherTests.cs`,
  `GfRollsTests.cs`, `OutOfSetAnswerTests.cs`, `GrimdarkFutureRulesTests.cs`
- `tests/Tabletop.Management.Tests/ArmyForgeImportTests.cs`, `MigrationTests.cs`
- `sim/Program.cs`, `sim/SimOptions.cs`, `sim/Scenarios/Scenario.cs`, `ScenarioBuilder.cs`, `ScenarioCompiler.cs`,
  `ScenarioCatalog.cs`, `sim/Invariants/Invariants.cs`; `tests/Tabletop.Sim.Tests/ProgramTests.cs`, `RunLoopTests.cs`,
  `DriverAgreementTests.cs`, `InvariantTests.cs`
- `viewer/src/lib/protocol/wire.generated.ts` (regenerated); `web/src/lib/api/types.ts` (`armyBook?: string | null`
  on the slot type, for completeness; the web app never reads it)

## Against the enforcement tree

The enforcement tree merges first, and this tree takes it in by merge before its simulator and engine-test
tasks. What it brings that this tree must absorb:

- **`Emit`.** `IRulesEngine.Advance` returns `Emit` rather than a bare event list, so this tree's engine tests that
  call `Advance` read `.Events`.
- **`ended`.** A `strict` run that plays four rounds ends `ended` at round 4's boundary. The `casting` scenario's
  expectations, and `RunLoopTests` and `ProgramTests` lines this tree adds, accept `ended`.
- **Invariant numbering.** Enforcement's `NothingAdvancesAfterTheEnd` is the sixth; this tree's is the seventh.
- **Generated types.** After the merge, `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests` is run
  afresh and `cd viewer && npm run check` passes, since both trees change wire types.

Textual overlaps only: `sim/Invariants/Invariants.cs`, `sim/Scenarios/ScenarioCatalog.cs`, `GfNames.cs`,
`GfRules.Scheduling.cs` (this tree changes `ReadAnswer`; enforcement adds `JudgeAdvance` to the same partial
class, possibly the same file), the migration number (this tree takes `005`, and renumbers if enforcement lands a
migration first), and docs. This tree touches nothing in `server/game/Dispatch/`, `server/game/Scheduler/`,
`server/game/Session/`, `PostgresGameRows.Writes.cs`, `IGameRows.cs` or `server/management/Games/`.

## Open for the owner

Nothing. The Surge sentence, re-import, the faction column and the hookless rules were settled on 2026-10-02.
