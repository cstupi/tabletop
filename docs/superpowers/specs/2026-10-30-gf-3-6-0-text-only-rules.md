# Grimdark Future 3.6.0: the text-only rules played

The 3.6.0 set ships 43 entries the engine plays nothing for (`played: false`; the card says "not automated"): 19 rules
in `grimdark-future-3.6/army-rules.part.json` and `army-book-rules.part.json`, and 24 spells that grant them. This spec
designs the grammar and engine additions each needs, so the suite's 3.6 coverage line reads `0 not automated`, and fixes
three half-plays found with them: Thrust in Melee Buff's AP(+1), the Marks' second half (Bane Mark, Unstoppable Mark), and
Predator's "may". The plan is `docs/superpowers/plans/2026-10-30-gf-3-6-0-text-only/index.md`.

Printed text is the authority. Every quote below is the 3.6 Army Forge dump's (`.superpowers/armyforge/3.6/`) or the
3.6.0 rulebook's (`3.6.0 p.N`). The 3.5.1 content stays byte-identical, except where it departs from its printed text (the AP rows that print "to a
min. of AP(0)", and Guardian Boost's, which counts as Guardian's, carry `floor: 0`), and its suite stays 545 of 545 with no pin; an engine or content change that reaches a 3.5.1
game is a rule fix, which moves every game of the edition.

## Brace and Parry have text

The books carry both as weapon rules with no description: Daemons of Change, Lust, Plague and War (uids
`04z57ua0bwth37zh`, `9qvy1oufoangt2gl`, `fmuqw5lr5l6dq0mq`, `prsajf5ot936qznk`) print `Spear (A1, Counter, Brace(1))`,
`Hand Weapon (A1, Parry(1))`, `Hand Weapon (A3, Parry(3))` and `Master Hand Weapon (A3, AP(1), Parry(3))`. No 3.6.0
rulebook page names either (`.claude/skills/licensed/skills/grimdark-future-rules/v3.6.0/pages/`, pp.1–74, searched),
and no book's `specialRules` describes them. Army Forge's common-rules endpoint does:
`GET https://army-forge.onepagerules.com/api/rules/common/2` (game system 2, Grimdark Future; 39 rules, `coreType: 1`)
returns

- **Brace**, id `0bcGY4Xr4NE0`: *"Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued."*
- **Parry**, id `_T07iXWo4VwJ`: *"When this unit takes hits in melee, roll X dice per model with this rule in it, up to
  as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)."*

The same two texts come back for systems 3, 9 and 10. Both are authored from this text (below). `./scripts/army-books
fetch` saves the endpoint's reply beside the books as `.superpowers/armyforge/<edition>/common-rules.json`, and
`generate` reads a name's description from it when no book describes the name, so the generator's "no description" list
empties and `ArmyRules36Tests` accepts a hand text that matches the common-rules file.

## The additions

Each line of the handoff's list of missing pieces becomes one grammar point or one engine read. Grammar names are
OPR-free (`server/game/Effects/`); the rule names stay in `Rules/GrimdarkFuture/` and the content files.

| Missing piece | Addition | Rules |
| --- | --- | --- |
| a range test against the weapon's own range | predicate `weapon_range` | Pierce, Smash |
| a positive-Defense-modifier class | `suppress_modifiers positive_defense` | Pierce |
| remaining size against starting size | predicate `remaining_size` | Destructive, Decimate |
| a melee and morale wound-count adder | kind `count_wounds` | Scourge |
| a per-round usage per target across users | `targeting.oncePerRound` | Mind Control |
| tokens with pick counts, spending one | `targeting.countFromParameter`, duration `one_marker` | Piercing Tag, Precision Tag |
| a defender's choice per attack | point `attacked` for `set_condition` rows | Versatile Defense |
| a dice move on "this model" | the move row's dice count reads the entry's live parameter | Enhanced Wolfborn, Enhanced Rapid Blink |
| a Ravage value from grants | `GfStrikeRolls36` reads Ravage off the live set | Ravage Aura |
| Indirect without line of sight off Hold | `GfAttack.IndirectApplies` reads Quick Throw by name | Quick Throw |
| removing spell tokens | kind `remove_spell_tokens` | Spell Drain, Burn the Heretic, Shifting Form |
| a once grant spent at one step | once grants released at the attack's end | Thrust in Melee Buff, Bane Mark, Unstoppable Mark |
| "may" on an extra attack | `add_hit` `optional` | Predator |
| Impact rolls taken by the defender's weapon | `GfMelee.ImpactStep` reads Brace by name | Brace |
| hits ignored before the block | `parry` step in `resolve-hits` | Parry |
| an empty attack type in the dump | the translator writes no `attack_kind` condition | Triangulation Bots |

New enumeration values reach Postgres through one migration, `027_text_only_rules_grammar.sql`: `effect_kind_check` gains
`count_wounds` and `remove_spell_tokens`; `effect_condition_kind_check` gains `weapon_range` and `remaining_size`;
`effect_target_modifier_check` gains `positive_defense`; the duration check gains `one_marker`; `effect` gains `optional
boolean not null default false` (checked `optional = false or kind = 'add_hit'`). `PropertySetDocument.Validate`,
`PostgresPropertiesStore`, `PropertySetDocument` and `docs/schema.md` learn the same values. Docker is down while this is
planned, so the migration is reviewed and its `MigrationTests` assertion written, and both run once the stack is up.

### `weapon_range` (predicate)

Relational, no subject, `comparison` and `number` a percentage of the source weapon's printed range: holds when
`ix.RangeInches × 100 cmp number × range`, where `range` is the `Range` of the weapon the source instance carries
(`LiveProperty.WeaponId`; `StatReader.WeaponRange` of the live set, so a granted or suppressed range reads as it plays).
The range compared is the one the attack measured (`determine-attacks`, `range_hundredths`, p.5 base to base, never
rounded), which is the unit-to-unit distance, so "units over half of this weapon's range away" reads the distance to the
target unit. Pierce and Smash write `gt 50`.

A spell's printed range is "this weapon's range" (owner ruling): a source with no weapon reads `Interaction.SpellReach`,
holding when `Distance × 100 cmp number × Range`. `GfAttack.AttackInteraction` sets it on a spell's hit interaction
(`AttackKind.Spell`, no weapons) from `GfCasting.Reach`: `Range` is the entry's `targeting.range` (the number after
"within" in its text) and `Distance` the distance from where the cast is measured (the conduit origin or the caster model;
a pick entry's carrying model) to the target's nearest living model, or the model a model-grain pick named, base to base
and never rounded, as `GfCasting.Targets` measures a spell's range. Cerebral Trauma (24", AP(1) and Smash) re-rolls
unmodified Defense 6s on a target over 12" from the caster; Mind Wound (18", AP(1) and Pierce) gets AP(+1) over 9";
Pierce's positive-Defense half acts at any range. A source with no weapon outside a spell's hits, or an interaction with
no range, fails it.

### `remaining_size` (predicate)

Subject-bearing; `values: [<stat or rule>]` names the rule whose parameter is a single model's size (Grimdark Future:
`Tough`), so `Effects/` names no OPR rule. Holds when `remaining × 100 cmp number × starting`: for a unit that started
with more than one model, `remaining` is its living models (`Lookup.AliveModels`) and `starting` its definition's model
count (3.6.0 p.10 *"starting size is counted at the beginning of the game"*); for a single-model unit, `starting` is the
named rule's parameter (1 without it) and `remaining` that less the model's wounds, 0 on the shelf. `GfHooks.AtHalfOrLess`
moves onto the same `Lookup.RemainingSize(state, unitId, sizeRule)` so p.10 and this predicate cannot disagree.
Destructive and Decimate write `subject opponent, gt 50`: a 10-model unit with 6 alive qualifies, with 5 it does not; a
Tough(6) model with 3 wounds does not.

### `positive_defense` (`TargetModifier`)

`RollEffects.Attributed` takes the ruleset's defense roll name beside `hitRollName`; `positive_defense` suppressed on the
defender removes every positive modifier of that roll, base (`cover`) and rule rows alike, each dropped modifier listed in
`RollPlan.Ignored` by the suppressor. It leaves negative modifiers (AP) and the target number alone. Pierce writes
`suppress_modifiers positive_defense` on the opponent, unconditional.

### `count_wounds` (kind)

Pass two, per die: an `add_hit`-shaped row with a `roll_result` condition on the defense roll, `amount` N, subject
`opponent`. `roll-to-block` sums it over the defender's dice into `counted_wounds` in its Values, with a per-face map as
`ExtraWoundsKey` keeps, so a die edited after the roll counts on the face it then shows. Two readers:

- `GfMelee.WoundsTaken` adds to a unit's wounds the `counted_wounds` of every `resolve-hits` frame under this melee whose
  defender is that unit (3.6.0 p.10 *"compare the number of wounds each unit took during this activation"*).
- The general morale test (`GfActivationEnd`, 3.6.0 p.10 *"takes wounds that leave it with half or less"*): the counted
  wounds of the shooting attack-sequences and of the spells' hits (`spell-hits`) on a unit since the activation began; a
  spell's hits count as shooting's do (owner ruling). A unit tests when `WoundsThisActivation + counted > 0` and
  `GfHooks.AtHalfOrLess(state, props, unit, extraWounds: counted)` holds, where the counted wounds are laid on the unit as
  `deal-wounds` would lay them (Tough order) before the half test. Dwarf Guilds' Smiting Rune (4 hits with Scourge) on a
  five-model unit: one unmodified 1 that wounds leaves 4 models, less 2 counted is 2, and the unit tests.

Scourge writes `count_wounds 2` on an unmodified defense 1 (*"For each unmodified result of 1 to block hits, enemies count as
having taken 2 extra wounds when checking who won melee, or if shooting wounds would trigger Morale Tests"*). The counted
wounds change no model and no `WoundsThisActivation`.

### `targeting.oncePerRound` and `countFromParameter`

`Targeting` gains two fields. `oncePerRound: true` leaves out of the eligible targets (`GfCasting.Targets`, read by
`GfPick.Offers` and `rule-target`) every unit a use of this entry, by any carrying model of any unit, moved since
`round_started` (a `move` step change under a `pick-rule` frame of the entry naming the unit); a unit picked whose test
passed was not moved and stays eligible. Mind Control: *"Units may only be moved once per round in this way."*

`countFromParameter: true` makes the pick's `PieceSelection` 1 to X units (`count` is the floor when X is absent). For a
marker entry (`GfMarkers.Rows`), one unit picked takes X markers and several take one each, which is the Tags' *"place X
tokens on one enemy unit …, or one token on X enemy units"*; every shipped X is 1, so both shapes are one token on one unit.

### `one_marker` (`EffectDuration`)

A `one_marker` row is one token a rule places; its grants stack as `marker` grants do, but `LiveProperties.Gather` keeps one
instance of the entry per side (the first grant), so one token acts on an attack, and `LiveSet.Spend` spends that grant
alone: *"may remove one token from their target before rolling to block to get AP(+1)"*; it is spent whenever its row
applies, since nothing in its text makes saving it a choice. `spent_marker` (3.5.1 Piercing Tag, Precision Tag, both
editions' Spotters: *"friendly units may remove tokens from their target … where X is the number of removed tokens"*) is
the attacking team's choice of how many: `attack-sequence`'s `spotter-tokens` step asks `remove_tokens` before the hit roll,
once per target per shooting or melee strike of the unit, removes that many and gives every attack of that strike at that
target X equal to the count (grants.md → A marker is a grant).

### `attacked` (a point for the defender's choice)

A `set_condition` row at `at_point attacked` is a choice the defending unit's team makes at the start of each attack at it.
`attack-sequence` gains a `defender-choice` step after `determine-attacks` (the range is recorded) and before
`pick-target-model`: it settles the attack's interaction with `Point = attacked`, and for each entry whose `set_condition`
rows at that point act on the Defender side with their other conditions holding, asks the defender's team `choose_effect`
(`Options`, one per condition, pass allowed; `GfChoice` shape), sets the chosen condition on the defending unit and clears
the entry's others. The attack's closing step (`attack-end`, below) clears every `attacked`-point condition on the
defender, so *"you may pick a different effect each time the unit is attacked"* holds per attack-sequence. The rule's
effect rows read the condition through `actor_state`, as Watchborn's do. Impact's hits resolve from the melee frame with
no attack-sequence and take neither effect; the choice is made for the charger's attacks.

### A dice move's count reads the entry's parameter

`reposition`'s `RollDistance` rolls `row.EffectiveAmount(null) + (source.Parameter ?? 0)` dice for a `move` or `place` row
with `amountDice`, and `GfHooks.Label` shows the same count. Enhanced Wolfborn is then `modify_parameter Wolfborn +1`, self,
conditioned `holds_property self Wolfborn`: the one Wolfborn instance on the unit reads parameter 1 and rolls 2D3 (*"may
move by up to 2D3" from Wolfborn (instead of only D3")"*), and its movers stay `Holders(unit, Wolfborn)`, the models that
carry Wolfborn. Enhanced Rapid Blink is the same over Rapid Blink. Every shipped grant of an Enhanced rule reaches the whole
unit (the Aura's *"This model and its unit"*, the spells' *"friendly units"*), so a unit-wide parameter is exact for every
shipped case.

### Ravage from grants

`GfStrikeRolls36.Dice` settles the strike's interaction (striker, target, melee) and sums Ravage over the striker's living
models: each model's own parameter, plus every granted Ravage instance's amount, counted once per living model when the
instance names no model (an Aura's or a spell's grant on the unit) and once when it names one. Ravage Aura (*"This model and
its unit get Ravage(+1)"*) is a `grant_property Ravage amount 1` wrapper, written by the generator's Aura template once the
base plays, or by hand if the template declines a `(+X)` grant. The 3.5.1 `RavageStep` is untouched.

### Quick Throw

A hand entry with no rows, read by name: `GfAttack.IndirectApplies` holds when the attacker has Indirect and either the
edition needs no Hold, the action is Hold, or an attacking model of the group holds Quick Throw; Quick Throw is recorded as
applied wherever Indirect is recorded on an action other than Hold. *"This model may always target enemies that are not in
line of sight when shooting with Indirect weapons (instead of only when using Hold actions)."*

### `remove_spell_tokens` (kind)

Pass two, `amount` N. As a spell's row (`GfCasting.EffectsStep`): the picked unit loses N tokens, taken from its models
with the most tokens first, to zero (`SpellTokensSet`). As a `take_test` child (`spell-test`'s `consequence`, Spell Drain:
*"pick one enemy model within 18" in line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens"*):
the picked model loses N. *"Note that the target may not spend those spell tokens to prevent this effect"* needs nothing:
no spend window opens on a spell's effect. The generator translates Army Forge's effect `Urj6dU6Tua8B` (*"loses N spell
tokens"*) to the kind, which makes Burn the Heretic (Blessed Sisters) and Shifting Form (Change Disciples, Daemons of
Change, Titan Lords Change Disciples) played.

### Once grants released at the attack's end

Today a step that reads a once grant emits `GrantRemoved` for every grant of that entry on the unit as it completes
(`RollEffects.Consume`), so Thrust in Melee Buff's grant, read at `roll-to-hit` for its +1, is gone when `roll-to-block`
would read its AP(+1), and a Bane Mark read for its re-roll is gone when `assign` would read its Regeneration suppression.
*"Next time the effect would apply"* is the attack. `StepContext.Consume(live)` replaces the direct call in every step: under
an attack root (`attack-sequence`, `spell-hits`, `rule-hits`) it emits nothing and holds each read grant as
`held_<unit>_<grant>` in the step's Values, as `GfMorale.TestStep` holds for `override`; an `attack-end` step closes each of
the three sequences and emits `GrantRemoved` for every grant held by any step of the frame or its descendants that is still
on its unit, with the other grants of the same entry on that unit (Consume's rule). Outside an attack root (a morale test,
a reposition, a casting roll, a move) consumption stays immediate. A grant held and never released (an attack abandoned)
stays on the unit, as a grant that never applied does.

### `optional` on `add_hit` (Predator's "may")

An `add_hit` row on `attacks` may carry `optional: true`. `roll-to-hit` rolls the pool, and when optional rows set off N
extra attacks it asks the roller's team `extra_attacks` ("Predator: roll N extra attacks?", Options Yes and No, pass is No)
before adding the dice; Yes adds them as today. Predator and Primal share the text; both carry it. The answer is a player
decision, not a dice edit.

### Brace

Hand entry, kind weapon, takes X, no rows; `GfMelee.ImpactStep` (3.6 path, `ImpactBeforeCounter`) builds the defender's
party with its melee weapons, reads `live.Parameter(Side.Defender, Brace)` and takes it off the charger's total Impact dice,
floor 0, unless the defender is Fatigued or Shaken (3.6.0 p.10: Shaken strikes back counting as Fatigued). Instances of one
weapon type collapse as every weapon rule's do, so five Spears with Brace(1) take one die: the text says *"-X total"*.
Recorded as applied when it took a die.

### Parry

Hand entry, kind weapon, takes X, no rows; a `parry` step opens `resolve-hits` under a melee (an attack-sequence pushed by
the melee, and Impact's resolve-hits): dice = min(Σ over the defender's living models of the highest Parry X among that
model's weapons, hits); each 6+ ignores one hit, taken first from the hits carrying the most per-die grants (the hits worst
for the defender; identical hits need no pick); `roll-to-block` reads the remaining hits. No Ask: a pick among hits that
differ only by their grants is settled for the defender, as the Hero's wound order is.

### Triangulation Bots

The dump's structured field leaves `attackType` empty for its grant of Indirect. `SpellTranslator` writes a grant with no
`attack_kind` condition when the type is empty (Indirect is a shooting rule; a melee attack reads it as nothing), and reports
the spell as translated rather than text-only.

## Rulings taken here

- On a weapon's attack, the range Pierce and Smash test is the attack's measured unit-to-unit range (p.5), not a per-model
  distance.
- A spell's printed range is "this weapon's range": Smash and Pierce on a spell's hits test the caster's distance to the target,
  measured as the spell's range is, against half of it.
- Destructive, Decimate and p.10's half read one `RemainingSize`; a single-model unit's size is its Tough.
- Scourge's counted wounds never wound a model; they count only for the melee result and the morale trigger of shooting
  wounds, and a spell's hits count as shooting's do.
- Mind Control's "once per round" is per unit moved, across every user of the rule; a passed test leaves the unit eligible.
  Its 6" move is the spell-test forced move (any direction, as Deep Hypnosis's); "in a straight line" is not enforced.
- The Tags spend their token automatically; Predator asks. A modifier is never a choice; whether to roll more dice is one.
- Versatile Defense is chosen per attack-sequence, by the defender's team, before the roll to hit; Impact's hits take neither effect.
- Enhanced Wolfborn and Enhanced Rapid Blink add a die unit-wide; every shipped grant is unit-wide.
- Ravage(+1) from a grant adds one die per living model of the unit.
- Brace is "-X total", with weapon-type collapse; Shaken counts as Fatigued for it.
- Parry's ignored hits are the hits with the most per-die grants; no Ask.
- Once grants last the attack they first apply in and go at its end.

## Suite

One `army:3.6:<uid>` game per rule in `sim/Suite/SuiteCatalog.Army36M.cs` (Pierce, Smash, Destructive, Decimate,
Scourge, Brace and Parry), `SuiteCatalog.Army36N.cs` (the picks, choices, moves and Auras) and `SuiteCatalog.Army36P.cs` (once grants and
Predator's Ask inside one attack), with their checks in `Checks/Army36M.cs`, `Checks/Army36N.cs` and `Checks/Army36P.cs`,
each check quoting the 3.6 text; the 24 spells and the three authored by hand get rows in `SpellTables36` so `spells:3.6` casts them; the Auras
are covered by pattern once their bases fire. The full run must read `PASSED`, 3.5 `545 of 545 … 0 not automated`, 3.6
`N of N … 0 not automated`.
