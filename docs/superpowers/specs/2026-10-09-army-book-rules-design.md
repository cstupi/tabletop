# Army-book rules: every rule played

Owner decisions (2026-10-09): every rule the official army books put on a unit or an upgrade is played by the engine. Four
patterns (Aura, Buff, Mark, Debuff) are built once and generated per name. Rules whose text is their own are authored. Until
a rule is played, the stat sheet shows its text tagged "not automated", and the suite lists it without failing. Done is
that list empty.

## The gap

The 48 official Grimdark Future books (`.superpowers/armyforge/books/*.json`) put 267 rule names on units and upgrades. The
catalog (`grimdark-future-core.properties.json`, 33 entries; `grimdark-future-army-rules.part.json`, 73 entries) defines
53 of them. `LiveProperties.Gather` (`server/game/Effects/LiveProperties.cs:187`, `:204`, `:212`) skips a name the catalog
lacks, so the other 214 play as if absent.

| Group | Names | Shape |
| --- | --- | --- |
| Aura, template text, base in catalog | 45 | generated |
| Aura, template text, base missing | 23 | generated once the base is authored |
| Aura, effect written out | 21 | authored on the Aura mechanism |
| Buff, template text, base in catalog | 6 | generated |
| Buff, template text, base missing | 4 | generated once the base is authored |
| Buff, effect written out | 8 | authored on the pick mechanism |
| Mark, template text, base in catalog | 9 | generated |
| Mark, effect written out | 6 | authored on the pick mechanism |
| Debuff, effect written out | 10 | authored on the pick mechanism |
| Standalone, own text | 82 | authored |

Counts are recomputed by the generator's report (*Generator*), not maintained here.

## The four patterns

Army Forge's texts (`specialRules[].description`):

- **Aura**: *"This model and its unit get X."*
- **Buff**: *"Once per activation, before attacking, pick one friendly unit within 12", which gets X once (next time the
  effect would apply)."*
- **Mark**: *"Once per activation, before attacking, pick one enemy unit within 18" in line of sight, which friendly units
  gets X against once (next time the effect would apply)."*
- **Debuff**: *"Once per activation, before attacking, pick one enemy unit within 18" in line of sight, which <effect> once
  (next time the effect would apply)."* One variant adds *"with Caster"* to the pick.

### Aura

A `model` entry, aggregation `any`, one `grant_property` row of X, subject `self`. `Gather` emits an `any` entry as one
unit-level instance when any model carries it (`LiveProperties.cs:206`), so the unit holds X: *"this model and its unit"*.
A written-out Aura (*"get +1 to hit rolls when shooting"*) is the same entry with that row authored in place of the grant.
This is the existing grammar: `Bane when Shooting` (`grimdark-future-army-rules.part.json:3`) is a `grant_property` entry.

### Buff, Mark, Debuff: the pick

One mechanism, three targets:

| Pattern | Picks | Range, sight | Grant placed on | Row acts |
| --- | --- | --- | --- | --- |
| Buff | a friendly unit | 12" | the picked unit | for it (`self`) |
| Mark | an enemy unit | 18", line of sight | the picked unit | for its attacker (`opponent`), as the marked-unit spells |
| Debuff | an enemy unit (*"with Caster"* where written) | 18", line of sight | the picked unit | against it (`self`, a penalty) |

- **The entry.** A `model` entry with `targeting` (side, range, line of sight, an optional required property), an
  `at_point` of `before-attacking`, its use counted per carrying model and activation by the mechanism (`usage_count`
  counts per unit), and its effect rows with duration `once`, or use rows an active resolves on its target (`take_test`,
  `add_hit` hits, a `roll` of hits, `remove_wounds`).
  `PropertiesJson.Validate` admits `targeting`, `once` rows and use rows on a `model` entry whose rows have a
  `before-attacking` `at_point`.
- **Where it is offered.** `GfHooks.ReactionsAt` (`server/game/Rules/GrimdarkFuture/GfHooks.cs:82-100`) offers at
  `before-attacking` the cast reaction and the live Move/Place rows. It also offers one `use` reaction per carrying model
  whose pick entry has not been used this activation (`GfPick.Uses`) and has a target (`GfPick.Offers`). A Shaken unit is
  offered none (`GfHooks.cs:89`). A pick is offered at the casting window's timing, the `before-attacking` point as the
  code runs it: `MoveStep` opens it on every move action, Rush included, and again after a cast while move budget remains
  (`GfMoves.cs:274`, `:342`), and `CastingWindowStep` (`GfMoves.cs:268-274`) opens it for Hold.
- **What it does.** A new sequence `pick-rule`: an Ask `rule_target` with `PieceSelection` over the eligible units (the
  spell-targeting reading, `GfCasting.Targets`, measured from the carrying model), pass allowed; then one `GrantAdded`
  per `once` row on the picked unit (`Grant(Id, EffectId, EntryName, CasterUnitId, 1)`, as `GfCasting.cs:227`), and the use
  counted.
- **How it is spent.** By the existing grant reading and consumption (`LiveProperties.cs:188-196`, `RollEffects.Consume`),
  which is *"once (next time the effect would apply)"* (spell-library spec, *A grant is its effect row*). Nothing new.
- **Per model, optional.** Each carrying model has its own use per activation (*"this model"*). The pick may be passed, as
  a cast may.

## Authored rules

Authored entries go in `grimdark-future-army-rules.part.json` (hand-authored) with the Army Forge text as `ruleText`.
Each needs a suite game, as every hand-authored entry does (*Coverage*). They are built in batches, each its own plan,
ordered so that each batch's mechanisms are in place before it:

1. **Grammar only.** Rules the current effect kinds express: Agile, Bad Shot, Bane in Melee, Bloodborn, Brutal Fighter,
   Fortified, Good Shot, Grounded Stealth, Guerrilla, Ignores Cover, Ignores Cover when Shooting, Ignores Regeneration,
   Ignores Regeneration in Melee, Increased Shooting Range, Knightborn, Melee Slayer, Point-Blank Surge, Precise, Predator
   Fighter, Protected, Rapid Charge, Rending in Melee, Sergeant, Shred in Melee, Shred when Shooting, Surge when Shooting,
   Swift, Unique, Unpredictable, Unstoppable in Melee, Wolfborn, Banner, Musician; the bases that only template wrappers
   name (Guarded, Guerrilla Boost, Point-Blank Piercing, Predator Shooter, Ranged Slayer, Screened); and the 21 written-out Auras. A rule that turns out to need a new effect kind moves to
   the batch that builds it.
2. **The pick.** The mechanism above, the 10 template Buffs and 9 template Marks, the 8 written-out Buffs and 6 written-out Marks, and the
   10 Debuffs; and the before-attacking actives that share it: Mend, Mind Control, Breath Attack, Storm of Change, Storm of
   Lust, Storm of Plague, Storm of War.
3. **Deployment.** Fanatic, Infiltrate, Rapid Ambush, Ambush Beacon, Repel Ambushers, Vanguard, Re-Deployment, Ambush
   Re-Deployment, Ambushing Piercing Shot, Surprise Attack.
4. **Activation.** Bounding, Dash, Coordinate, Delayed Action, Instinctive, Versatile Attack, Hit & Run, Re-Position
   Artillery, Inquisitorial Agent, Martial Prowess, Watchborn, Speed Feat, Versatile Defense, Versatile Reach.
5. **Wounds and melee.** Deathstrike, Retaliate, Self-Destruct, Ravage, Crossing Attack, Strafing, Takedown Shot, Takedown
   Strike, Protection Feat, Regenerative Strength.
6. **Round start and unit markers.** Steadfast, Battleborn, Honor Code (a die at the round's start that clears Shaken);
   one marker count on a unit, placed by a trigger and read by a row: Defensive Frenzy, Piercing Frenzy, Precision
   Frenzy, Fortified Growth, Piercing Growth, Precision Growth, Piercing Spotter, Precision Spotter, Piercing Tag,
   Precision Tag, Piercing Target, Precision Target, Vengeance.
7. **New units and casting.** Spawn, Split, Reinforcement, Spell Accumulator, Spell Conduit, Extended Buff Range, Caster
   Group.
8. **Grammar additions.** Armor (a stat set from the parameter), Heavy Impact (AP on Impact's hits), Melee Shrouding,
   Ranged Shrouding, Darkborn (a floor on a modified distance), Grounded Precision (the attacker's terrain), Entrenched,
   Mobile Artillery (a move this round), Quick Readjustment (one row of a rule suppressed), No Retreat (wounds after a
   passed test).

### Readings of the batches

Where an Army Forge text leaves a reading open, the one most consistent with the core rulebook is played:

- **Batch 2.** Written-out Buffs, Marks and Debuffs take the rows of the books' spells of the same wording: *"when
  attacking"* is melee or shooting (Toxin Mist), *"loses AP(+1)"* lowers a weapon's AP and gives none to a weapon without
  it (Corrode Weapons), a speed row's three actions are one grant spent together (Shock Speed). *"With Caster"*, *"with
  Tough"* is `targeting.requires`. Before-attacking actives are pick entries with use rows: Mend's *"remove D3 wounds"* is
  a `remove_wounds` row (p.4 D3); Fatigue Debuff and Mind Control are a `take_test` with Terrifying Fury's and Deep
  Hypnosis's consequences, p.10's ordinary consequence included; Breath Attack and the Storms roll their dice under their
  own name (*"roll one die. On a 2+"*, *"roll 3 dice. For each 2+"*), then ask one target per success, and a unit may be
  picked for more than one success (*"for each 2+ one enemy unit … takes 3 hits"*). Their hits resolve as a spell's hits
  (`spell-hits`): they are neither shooting nor melee, so no *when shooting* or *in melee* rule reaches them. A Buff
  that raises the move of the unit using it, picked mid-move, applies to the rest of that move: the grant applies the
  next time the move is read. Primeborn's text is Guardian's and so are its rows. Bane in Melee Buff is the Buff
  template with X in running-text case (*"Bane in melee"*); the generator reads X ignoring case.
- **Batch 3.** *"Counts as having Ambush"* (Infiltrate, Rapid Ambush, Ambushing Piercing Shot) is a `grant_property` of
  Ambush, and Lookup's Aura grants follow a chain (Infiltrate Aura gives Infiltrate, which gives Ambush). Where an
  Ambusher is placed, the rules are read by name off the unit definitions (`GfAmbush`), since deployment is judged
  before a unit has a position: Infiltrate and Surprise Attack (*"counts as having Infiltrate"*) deploy over 3" from
  enemy units, an enemy unit with Repel Ambushers holds every Ambusher over 12" from it, and a base within 6" of a
  friendly model with Ambush Beacon ignores the distance. Rapid Ambush adds round one's boundary to the Ambushers'.
  Ambushing Piercing Shot's *"on the round in which it deploys via this rule"* is the unit condition `ambushed this
  round`, set on arrival and cleared as a round starts. Re-Deployment's units are picked at the Scouts phase's deploy
  Ask beside the Scouts, up to two per side, each removed to the shelf and placed again within the zone (p.6, not a
  Scout's 12"). Ambush Re-Deployment is offered in the activation-end window of a unit all of whose models hold it,
  once a game; the unit goes to the shelf set aside and is one of the next round's Ambushers. Fanatic and Vanguard are
  `place 9` rows at a point, `after-deploy`, whose window opens when a deployment turn's unit is fully placed (a Scout's
  too; an Ambusher's arrival is a round boundary, not a deployment turn, and opens none). Surprise Attack is a pick
  entry offered only in the unit's first activation and once a game for the unit: it rolls X dice and its one target
  takes one hit per 2+ (`rule-hits`, blocked under AP(1)). Takedown Shot and Takedown Strike are not built: each is
  an attack with a profile no weapon carries (Quality 2+, AP(2), Deadly(3), Takedown); the build path is the import
  adding that profile as a weapon of the carrying model, used once a game.
- **Batch 4.** Bounding and Dash are `place` rows of *"D3+1"* (`amountDice d3+1`); Dash's *"at the end of this unit's
  activation"* is a point, `activation-end`, whose window opens after the activation's own work. Hit & Run is Harassing's
  text and rows. A *"pick one effect"* rule (Versatile Attack, Watchborn, Versatile Defense, Versatile Reach) and Speed
  Feat's *"you may use this rule"* are `set_condition` rows at `activation-start`: the player picks one, its condition is
  set on the unit, and the effect rows read it; the unit's choice clears as its next activation starts, which is
  Versatile Defense's *"until the units' next activation"* and outlasts the others' activation only while they cannot act
  (*"when shooting or charging"*, *"when this unit moves"*). Versatile Defense's choice *"when … deployed"* is not
  offered: deployment opens no point. Re-Position Artillery is a pick entry (a friendly model within 6" with Artillery,
  used before attacking as every pick is) whose `move 9` row moves the picked model's unit under its own move rules.
  Coordinate runs after the activation's morale tests: the team may activate another of its units within 12" that has
  not activated and is not Shaken (its activation is idle, p.10); that activation is followed by its own end-of-
  activation window and morale tests, and offers no Coordinate. Delayed Action is offered as the activation option
  `Delay` while an opponent has more units on the table left to activate, once a round; the turn ends and the unit stays
  unactivated. Inquisitorial Agent and Martial Prowess offer a unit that has activated this round its actions again, once
  a game, at most ⌈n/3⌉ such units a round (n: the army's units holding the rule), clearing fatigue. Instinctive is
  read when the activation starts: a unit that can charge is offered only Charge, one that can shoot may not Rush and
  takes its shot without being asked; its only target is the closest valid one, and its shooting or charging attacks
  that activation get +1 to hit.
- **Batch 5.** *"The attacking unit takes X hits"* and *"the attacker takes X hits per wound taken"* are hits with no roll to
  hit (`rule-hits`), blocked as melee hits, dealt by the wounded model's unit; only wounds from a weapon or Impact attack in a
  melee set them off, so they set off nothing in turn. Deathstrike and Self-Destruct count a model killed by those wounds;
  Retaliate counts each wound a carrier took. Self-Destruct's survivors are removed after the Unwieldy strike, *"after both
  sides have finished attacking"*, then each side's hits are dealt. Ravage rolls at its unit's turn to attack: the
  charger's before its strikes, the defender's after it struck back (or struck first with Counter); each 6 is a wound with
  no block (`rule-wounds`). Crossing Attack and Strafing read *"moves through enemy units"* as an action's move segment
  crossing an enemy model's base; after the move the player picks one unit it crossed (once: one move per activation);
  Strafing's weapon is in no shooting group (*"may only be used in this way"*) and makes its attack as a shot.
  Protection Feat is a roll to ignore wounds at 5+, *"once per game"* read as every wound of the first attack it is
  rolled against (it is used automatically, the first time). Regenerative Strength moves to batch 6 (a marker on a
  model); Takedown Shot and Takedown Strike are under batch 3.
- **Batch 6.** A marker is a grant of the rule's marker row (duration `marker`, kept for the game, or `spent_marker`, spent
  with its fellows the first time one applies), so *"for each marker"* is the sum of its grants; a carried rule's marker
  rows act only through those grants. *"For every two markers"* is two marker rows the markers cycle through, the first
  of each pair never acting. Steadfast, Battleborn and Honor Code roll at the round boundary, before the Ambushers. The
  Frenzies and Vengeance read *"fully destroys an enemy unit"* as an attack's wounds leaving the unit with no model in
  play (a unit Routed by a morale test is not counted); Vengeance places one marker per model that carried it at the start
  of the game, for its own side. The Growths place their marker as the round starts. Spotters and Tags are pick entries
  (used before attacking); *"friendly units may remove markers … before rolling"* removes them all at the attack that
  reads them. A marker its holder's own side placed (Fortified Growth's *"enemies attacking this unit get AP(-1)"*)
  reaches every opponent. Regenerative Strength's markers are the wounds its model ignored, and its *"+X attacks"* go to
  the model's first melee weapon.
- **Batch 7.** Caster Group's caster is the group's first model in play (the pick made for the player), Caster(X) with X its
  models in play; it gets X tokens at the round start in place of what it held, and a killed caster's tokens pass to the
  next. Spell Accumulator's tokens pay a friendly caster's spell value after the caster's own, and count in its spend for
  the roll, from a model within 12" of the caster in another unit that is not Shaken. Spell Conduit adds a `cast_from`
  choice after the spell is picked; a cast from a conduit measures its targets from the conduit and rolls +1. Extended
  Buff Range adds to a Hero's Buff picks (range alone, unit grain) the friendly units holding it within 24" of the Hero's
  unit, when that unit holds it too. Reanimation rolls as its activation starts, one die per killed model and per wound
  its models carry; each 5+ restores a killed model first (placed by the engine within 1" of a model in play, on no base),
  then a wound. Any X or count from an imported army is bounded where it becomes grants or dice: 20 markers of one rule
  on a unit, 60 hits or dice for one use. Spawn, Split and Reinforcement are not built: each places a unit the army
  snapshot does not hold (*"a new unit of X"*, *"a new copy of it"*); the build path is the import adding the units X
  names, from the book's units, as reserve units of the army, which the rule then places.
- **Batch 8.** Armor is a `replace_stat` of Defense from the parameter. *"To a min. of 6""* is a `modify_stat` row's
  `floor`: the stat goes no lower than 6, or than itself when already lower. A charge's distance is read at its target
  (`GfMelee.ChargeDistance`, the charge move's allowance), so the target's Melee Shrouding and Darkborn take from it.
  Grounded Precision reads the attacker's own terrain (`Interaction.AttackerTerrain`). Entrenched and Mobile Artillery
  read *"hasn't moved during the round"* as the unit condition `moved this round`, set by its moves and cleared as a round
  starts. Quick Readjustment is a `suppress_property` naming a roll: Indirect's modifiers of the hit roll go, Indirect
  stays. Heavy Impact grants Impact with its own X (a `grant_property` from the parameter) and its Impact hits are
  blocked under AP(1). No Retreat turns the failed test into a pass, rolls one die per wound left to the unit and deals
  a wound on each 1-3 through deal-wounds with no roll to ignore it.
- **Order.** Batch 3 is taken after batches 4–8, on the Ambush placement code another branch wrote.

Banner and Musician have no description in the books; Army Forge's common rules give it (`GET /api/rules/common/2`, the
same rule ids as the books' upgrade gains): Banner *"This model and its unit get +1 to morale test rolls."*, Musician
*"This model and its unit moves +1" when using move actions."*

## Generator

`scripts/ArmyBooks` `generate` writes one more file, `server/game/Content/grimdark-future-army-book-rules.part.json`, never
edited by hand. The server embeds and composes every `*.part.json` already (`server/game/Tabletop.GameServer.csproj:21`,
`PropertiesSeeder.cs:13`).

- **Input.** Each book's `specialRules` (name, description), read beside the spells it reads now. Two books giving one name
  different descriptions fail the run and write nothing, as spells do.
- **Names in scope.** Every rule name on a unit or an upgrade in the official books. A name core or the hand-authored part
  defines is skipped: hand-authoring wins. Names come from a unit's rules, its weapons' rules and its items' rules, and
  from every upgrade gain (a rule, a weapon's rules, an item's rules).
- **A wrapper is generated** when its name ends in Aura, Buff, Mark or Debuff, its description is that pattern's template
  with X equal to the name less the suffix, and X is a catalog entry. The entry's rows point at X.
- **Every other name is a text-only entry**: kind `model` (`weapon` when the books print the name only on weapons, so its
  authored entry keeps the kind), aggregation `any` on a model entry, the description as `ruleText`, no effects,
  `played: false`.
- **Report.** The run prints the counts of the table in *The gap*: generated, text-only by group.

Authoring a base and re-running `generate` turns its template wrappers from text-only into generated entries.

## Played flag

`CatalogEntry` gains `Played` (default true; `played` in JSON, omitted when true). The generator writes `false` on text-only
entries; nothing else does. `PropertiesJson.Validate` refuses `played: false` on an entry with effects.

- **Server.** `Gather` skips an entry with `Played == false` as it skips an absent name today. Nothing else reads it.
- **Delivery.** `GET /v1/properties/{hash}/rules` (`GameRepository.RuleTexts`) returns `Played` with each text.
- **Client.** `RuleTexts.NotAutomated(name)` (`client/src/Core/RuleTexts.cs:24`) is true for an absent name or an entry with
  `Played == false`. `StatSheet` shows the rule dimmed with " (not automated)" and the Army Forge text as its tooltip, so the
  text is readable while the rule is unplayed.
- **Schema.** `docs/schema.md` gains the `played` column on the entry table.

## Coverage

`sim/Suite/Coverage.cs` counts every catalog entry. It changes to:

- **Text-only entries** (`Played == false`) are listed as not automated and not counted. The report prints the count and the
  names. They never fail a run.
- **Generated wrappers** are covered by pattern: covered when their pattern's game passes and X is covered. The report shows
  them as "covered by pattern".
- **Hand-authored entries** keep their own game.

Four suite games, one per pattern, each with an exact check quoting the template:

- **Aura**: a unit whose one model carries Stealth Aura is shot from over 9"; the attacker's hit rolls carry Stealth's -1.
- **Buff**: before attacking, the Buff is offered and used on a friendly unit within 12"; that unit gets X, and X is spent at
  its next application.
- **Mark**: a marked enemy unit; the first friendly attack against it gets X; the second does not.
- **Debuff**: a debuffed enemy unit suffers the effect once.

## Done

The generator reports no text-only entry, the suite's not-automated list is empty, and the suite passes on both drivers.

## Documents this changes

`docs/schema.md` (played), `docs/design/interpreter/effect-grammar.md` (targeting and `once` on a `model` entry),
`docs/superpowers/specs/2026-10-02-army-rule-hooks-design.md` (the `use` reaction at `before-attacking`),
`docs/superpowers/specs/2026-10-09-rules-suite-design.md` (coverage), `scripts/ArmyBooks/AGENTS.md` (the new output),
`docs/handoff.md` (*Known rule gaps*).
