# Army-rule hooks

Design session, 2026-10-02. The fifteen army-book rules that [the spell library](2026-10-02-spell-library-design.md)
authors as names with no effect rows, because each needs a hook the engine does not have. The owner ruled on
2026-10-02 that all fifteen are built this round, by the tree that builds the library, in a second plan after it.

Each rule is written as data where the effect grammar can carry it, on the library's additions (`move`,
`most_models`, `holds_property`, `add_hit` with `target_quantity`) and on the core's `usage_count` and
`position_terrain`, and as a name check at a Grimdark Future hook only where the rule changes what an action is
allowed to do. The [effect grammar](2026-08-29-effect-grammar-design.md)'s *What `GameRules` handles instead* is
the precedent for the second kind.

## Read, not reasoned

The texts, from the books' `specialRules` (one description per name across all 48 books):

| Rule | Books | Text |
|---|---|---|
| *Hit & Run Fighter* | 3 | *"Once per round, units where all models have this rule may move by up to 3" after being in melee."* |
| *Hit & Run Shooter* | 4 | *"Once per round, units where all models have this rule may move by up to 3" after shooting."* |
| *Harassing* | 1 | *"Once per round, units where all models have this rule may move by up to 3" after shooting or being in melee."* |
| *Harassing Boost* | 1 | *"If most models in this unit have Harassing, they may move by up to 6" from Harassing (instead of only 3")."* |
| *Quick Shot* | 5 | *"This model may shoot after using Rush actions."* |
| *Grounded Reinforcement* | 1 | *"If a unit where all models have this rule has most of them within 1" of terrain, they get +1 to defense rolls."* |
| *Teleport* | 9 | *"Once per activation, before attacking, you may place this model anywhere fully within 3" of its position when using Advance/Charge actions, or fully within 6" of its position when using Rush actions."* |
| *Rapid Blink* | 1 | *"When this unit is activated, you may place all models with this rule in it anywhere fully within D3" of their position."* |
| *Rapid Blink Boost* | 1 | *"If this model has Rapid Blink, it may be placed within 2D3" from Rapid Blink (instead of only within D3")."* |
| *Unpredictable Fighter* | 17 | *"When in melee, roll one die and apply one effect to all models with this rule: on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead."* |
| *Unpredictable Shooter* | 6 | the same, *"When shooting"* |
| *Primal*, *Clan Warrior* | 1 each | *"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn't apply to newly generated attacks."* |
| *Primal Boost*, *Clan Warrior Boost* | 1 each | *"If this model has Primal, it gets extra attacks on successful unmodified hit results of 5-6 from Primal (instead of only on 6)."* |

The rulebook pieces they lean on: p.7, *"Models may never be within 1" of models from other units, unless they are
taking a Charge action"* and *"may never move outside of the battlefield … or be placed in physically impossible
locations"*; p.7, *"Rush - Moves 12" but can't shoot"*; p.4, *"D3: To use these dice, simply roll a D6 and halve
the result"*; p.10, Shaken units *"can't take any actions, or use any active special rules, such as casting
spells, using buffs/debuffs on units, re-positioning, etc."*; p.11, *"Open terrain does not have any special
rules, and any rules that affect terrain do not apply to open terrain"*.

Fifteen spells, counting reprints, grant one of these rules (spell library → *Army-book rules spells name*).
*Combat Ecstasy* grants *Quick Shot* in the *against* form: friendly units may shoot after Rush, at the marked unit.

## Hook points: the `at_point` predicate

Five of the rules happen at a moment rather than in an interaction: after shooting, after being in melee, before
attacking, when activated, and at the start of an attack. The grammar's windows (action sequences, 09-03) are
general, and casting already reacts at `before-attacking`. So **predicate `at_point`**, values `[point]`, no
subject, holds only when the engine asks for the effects available at that point. The points:

| Point | Where the engine asks | Who |
|---|---|---|
| `activation-start` | a new `activation-start` step right after `activation-terrain`, in every action sequence and in `idle` | the activating unit |
| `before-attacking` | the built checkpoint (`CastingWindowStep`, each `MoveStep`'s window phase) | the acting unit |
| `after-shooting` | a new last step of the `shooting` sequence | the shooting unit |
| `after-melee` | a new last step of the `melee` sequence, after `consolidate` | each unit of the melee still on the table, charger first |
| `attack-start` | a new first step of `shooting`, and `melee-start` for each side | each attacking side |

A row with `at_point` is never returned by the interaction queries, because no interaction asks for a point; a
row without it is never returned at a point. Asking at a point is `GfHooks.ReactionsAt` for the first four and the
attack-start step for the fifth. **A Shaken unit is offered nothing at any point**, p.10's *"re-positioning"* and
*"active special rules"*; `ReactionsAt` already returns nothing for a Shaken actor and keeps that for every point.

**A pick is offered at `before-attacking` too.** An army-book Buff, Mark or Debuff is a pick entry (army-book rules
spec → the pick): each carrying model offers a `use` reaction (`GfPick.Offers`) while it has not used it this
activation and has a target, and the reaction pushes `pick-rule`, whose `rule_target` Ask picks the unit that gets
the grants. The use is counted per model by `GfPick.Uses`, not by `usage_count`, which counts per unit.

**`usage_count` gains a scope.** Today it counts the sequences that used the holding weapon, over the game, which
is `Limited`'s *"once per game"*. A `usage_count` condition's first value may now be `round` or `activation`, and a
use of a non-weapon rule is a sequence started by it, whose `SequenceContext.Action` is the entry's name. *"Once
per round"* is `usage_count lt 1`, values `[round]`: uses since the last `round_started`. *"Once per activation"*
is values `[activation]`: uses since the unit's activation began, read as `GfCasting.TriedPicks` reads it. The
count is per entry, so *Harassing*'s two rows share one use.

## A move after attacking: *Hit & Run* and *Harassing*

The library's kind 16, `move`, already says *"a team may move a unit up to `amount` inches"*, with a `MoveRegion`
over the unit's alive models, the impassable and difficult volumes and cap the moving unit's own move has (`GfMoves.TerrainFor`), and a pass. On a spell the team
is the caster's and the unit the target's. **On a property, the team is the holder's and the unit is the
holder's own.** The three rules are `move` rows on a model rule with aggregation `all` (*"units where all models
have this rule"*):

| Entry | Rows |
|---|---|
| *Hit & Run Fighter* | `move` 3, `at_point after-melee`, `usage_count lt 1 [round]` |
| *Hit & Run Shooter* | `move` 3, `at_point after-shooting`, `usage_count lt 1 [round]` |
| *Harassing* | the two rows above, under its own name |
| *Harassing Boost* | `suppress_property` Harassing; *Harassing*'s two rows at 6; each `holds_property` Harassing |

*Harassing Boost*'s *"If most models in this unit have Harassing"* is `holds_property`, which reads the base under
its own aggregation (`all`); a unit where most but not all models have it holds no live *Harassing* to boost, so the
narrower reading changes nothing. The Boost's rows count the Boost's own uses; the base is suppressed while the
Boost is live, so a boosted unit still moves once per round.

**The hook.** At `after-shooting` and `after-melee`, `GfHooks.ReactionsAt` settles a lone interaction for each
eligible unit and collects its live `move` rows whose conditions hold at that point. Each becomes a `Reaction` to
the holder's team, *"{entry}: move up to {amount}""*, pushing a new one-step `reposition` sequence whose context
names the unit and carries the entry's name as `Action`. Its step asks the `move` and applies it with
`GfMoves.MoveStep`'s own validation, so the 1" rule, impassable volumes, difficult terrain and dangerous terrain
are the move's as for any move. `Windows.Run` already asks one team at a time in reaction order, so a melee whose
two units both hold *Hit & Run Fighter* asks the charger's team and then the defender's. A unit may move away from
a melee it was in; p.7's 1" rule does not stop a move that ends outside 1".

## A placement: *Teleport* and *Rapid Blink*

**Kind 17, `place`**, with `amount` in inches and a new nullable column `amount_dice` (`d3`): the holder's team may
place the holder's models that carry the entry *"anywhere fully within"* the distance of their own positions.
With `amount_dice` the distance is rolled when the placement is chosen: `amount` dice, each a D6 halved and
rounded up (p.4), summed, recorded as a `RollRecord` named for the entry. A placement is not a move: nothing is
swept, so it crosses impassable terrain and units, and only the landing is checked: each model's displacement is
at most the distance, and its base touches no impassable volume (p.11: units *"may not end their move partially
overhanging gaps or terrain"*; `GfMoves.LandsInImpassable`). Under strict, dispatch also refuses a landing off the
table (`GfMoves.PathBreach`, p.7); p.7's 1" from other units is judged by `GfMoves.ContactBreach` (`move_separation`, the region's `Separation`), as it is for every move the engine measures. It is offered as a `MoveRegion` whose allowance is the distance and whose impassable and
difficult lists are empty, which is how the client is told nothing will be swept; `GfMoves` validates it with a
landing check instead of `Movement.Check`. No wire type changes.

*"This model"* and *"all models with this rule in it"* are per model, so both are aggregation `none`, and the
`MoveRegion`'s `ModelIds` are the unit's alive models that hold the entry, or all of them when the unit holds it
by a grant.

| Entry | Rows |
|---|---|
| *Teleport* | `place` 3, `at_point before-attacking`, `action_taken` in `{Advance, Charge}`, `usage_count lt 1 [activation]`; `place` 6, the same with `action_taken` `{Rush}` |
| *Rapid Blink* | `place` 1 `d3`, `at_point activation-start` |
| *Rapid Blink Boost* | `suppress_property` Rapid Blink; `place` 2 `d3`, `at_point activation-start`, `holds_property` Rapid Blink |

**The hooks.** At `before-attacking`, `GfHooks.ReactionsAt` adds a `Reaction` per live `place` row beside the cast
reaction, so *Teleport* and casting share the built checkpoint; after either child the window reopens, as it does
after a cast today (`MoveStep`'s window phase and `CastingWindowStep` repeat on any child the window pushed, not
only a casting one). At `activation-start`, a new step after `activation-terrain` runs `Windows.Run` for the point,
so *Rapid Blink* is offered as the first thing the unit does after p.11's test. It is a step of its own because
`Windows.Run` counts its own children, and the terrain step's dangerous terrain child would be counted among them. Choosing either
pushes the same `reposition` sequence as a move after attacking; its step re-settles the unit's live set at the
point, takes the one `place` or `move` row of that entry whose conditions hold, rolls the distance if the row has
`amount_dice`, and only then asks. Declining rolls nothing. *"When this unit is activated"* is every activation, Idle included, but p.10
gives a Shaken unit no re-positioning, so a Shaken unit's Idle offers nothing.

## Shooting after Rush: *Quick Shot*

*Quick Shot* changes what an action is allowed to do, so it is a name check, as `Immobile` and `Aircraft` are
(effect grammar → *What `GameRules` handles instead*): a model rule, aggregation `all`, no rows, and
`GfNames.QuickShot`. The `rush` sequence gains `GfMoves.ShootOrNotStep` as its last step, which today ends `hold`
and `advance`. `GfShooting.ValidTargets`, whose interaction already carries the action
(`ctx.Frame.Context.Action`), admits a target after a Rush only when *Quick Shot* is live on the attacking side of
that target's interaction (`LiveSet.Has`). So an innate *Quick Shot* opens every target, and *Combat Ecstasy*'s
*against* grant, a subject-`opponent` row on the marked unit, opens that unit alone, which is what *"friendly
units get Quick Shot against"* says. With no such target the step asks nothing, as for a unit with nothing in
range. The shooting is ordinary: the unit has `moved`, so *Indirect*'s *"when shooting after moving"* applies.

## Near terrain: *Grounded Reinforcement*

`modify_roll` defense +1, subject `self`, on a model rule with aggregation `all`, conditioned on
`position_terrain` with the new value `near`. `Battlefield.TerrainUnder`, which lists the terrain declarations
under a unit and feeds `Interaction.DefenderTerrain`, adds `near` when more than half the unit's living models are
within 1" (`Distance.EdgeToEdge(PlacedModel, WorldVolume)`) of a terrain volume that declares at least one terrain
type. A volume with no declaration is open terrain, and p.11 says *"any rules that affect terrain do not apply to
open terrain"*. `position_terrain` already reads the defender, or the lone party, so the +1 applies when the unit
blocks hits, as *"they get +1 to defense rolls"* says.

## A die at the start of the attack: *Unpredictable*

*"Roll one die and apply one effect to all models with this rule"* is the grammar's kind 5, `roll`, with one die,
whose children are chosen by the face:

| Entry | Rows |
|---|---|
| *Unpredictable Fighter* | `roll` 1 die at threshold 1, `at_point attack-start`, `attack_kind melee`; children `grant_property` AP 1 when its own roll's raw face `lte 3`, `modify_roll` hit +1 when `gte 4` |
| *Unpredictable Shooter* | the same with `attack_kind shooting` |

A property's own roll is a `RollRecord` named for the entry (effect grammar → *`Roll` nests*), and the children's
`roll_result` condition names that roll, which the validator admits for a child of a `roll` row naming its own
entry. **The attack-start step** (new first step of `shooting`; for melee, `melee-start` for each side) settles
each attacking side's lone interaction, takes its live `roll` rows at `attack-start` whose conditions hold, rolls
each once, and records the face as `face_<unit>_<entry>` in its values (both sides of a melee may hold the same rule). `GfAttack.AttackInteraction` reads the faces
recorded on the nearest `shooting` or `melee` ancestor frame into a new `Interaction.Faces` (side → entry → raw),
and `LiveProperties.Settle` treats a live `roll` row with a recorded face as its children whose `roll_result`
holds against that face, a pass-one child in pass one and the rest in pass two. So the AP(+1) or the +1 to hit
lasts for every weapon group of that shooting and both strikes of that melee, and the die is rolled once, as
*"roll one die"* says. Aggregation `all` follows the core document's reading of *"models with this rule"* (`Fast`,
`Slow`).

## Extra attacks: *Primal* and *Clan Warrior*

**`add_hit` takes `target_quantity = attacks`**: each die meeting the row's conditions adds one attack, rolled at
once with the same plan as the die that produced it. *"With that weapon"* is the same weapon group and Quality
pool; *"This rule doesn't apply to newly generated attacks"* is that an added attack's die is read for every
per-die effect except `add_hit` attacks. `RollEffects.PerDie` returns the added attacks apart from extra hits and
wounds, and `GfAttack`'s roll-to-hit rolls them after the pool, in the same step, as one more `RollRecord` of the
`hit` roll, so `HitsFrom` counts their successes.

| Entry | Rows |
|---|---|
| *Primal*, *Clan Warrior* | `add_hit` attacks 1, subject `self`, `roll_result hit raw eq 6`; model rule, aggregation `all` |
| *Primal Boost*, *Clan Warrior Boost* | `add_hit` attacks 1, `roll_result hit raw eq 5`, `holds_property` the base |

The Boosts are the library's *face widened* form: the base keeps the 6, the Boost adds the 5. Roll-to-hit reads
per-die effects on successful dice only, which is *"successful unmodified hit results of 5-6"*; p.5: *"a roll of 6
always counts as a success"*.

## The entries

Every entry is in `server/game/Content/grimdark-future-army-rules.part.json`, where the library authors it as a
name with its book's text. This spec gives each its rows:

| Entry | Kind, aggregation | Hook |
|---|---|---|
| *Hit & Run Fighter*, *Hit & Run Shooter*, *Harassing* | model, `all` | `move` at `after-melee` / `after-shooting`, once per round |
| *Harassing Boost* | model, `all` | the same at 6", suppressing *Harassing* |
| *Teleport* | model, `none` | `place` at `before-attacking`, 3" or 6" by action, once per activation |
| *Rapid Blink*, *Rapid Blink Boost* | model, `none` | `place` D3" or 2D3" at `activation-start` |
| *Quick Shot* | model, `all` | name check on the `rush` sequence's shooting |
| *Grounded Reinforcement* | model, `all` | defense +1 at `position_terrain near` |
| *Unpredictable Fighter*, *Unpredictable Shooter* | model, `all` | `roll` at `attack-start`, children by face |
| *Primal*, *Clan Warrior*, and their Boosts | model, `all` | `add_hit` attacks on a 6, the Boosts on a 5 |

A granted instance of any of them works as an innate one, through the library's grant-is-its-row reading, and is
consumed by the library's rule: the first step that would apply it. For a hook that is the `reposition` step that
makes the move or the placement. Offering it is reading it for a legal set, which the library rules spends nothing,
so a declined *Hit & Run* grant waits for the next `after-melee` point.

## Grammar, schema and wire

Additions, on top of the library's sixteen kinds and ten predicates: **kind 17 `place`**; **predicate `at_point`**,
the eleventh; **`usage_count`'s scope value** (`round`, `activation`); **`position_terrain`'s value `near`**;
**`target_quantity = attacks`** on `add_hit`; and the validator admitting a `roll_result` that names its own
entry's roll on a child of a `roll` row.

Management migration `006_army_rule_hooks.sql`: `effect.kind`'s check admits `place`; `effect.target_quantity`'s
check admits `attacks`; `effect` gains `amount_dice text check (amount_dice in ('d3'))`, set only on `place`;
`effect_condition.kind`'s check admits `at_point`. `PostgresPropertiesStore` reads and writes `amount_dice`.
`EffectDocument` and `Effect` gain `AmountDice` as a trailing optional.

**No wire kind is added and `ProtocolVersion` stays.** The hooks travel as built shapes: a `react` `Options` Ask
at the new points (its `WindowKind` the point's name), a `move` `MoveRegion`, `ModelsMoved`, `RollRecord`s named
for the entry, and `step_completed` values. The viewer and client already render a `react` Ask by its options
and a `MoveRegion` by its fields. `wire.generated.ts` does not change.

## Testing

Each hook has tests beside the built ones, in `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`, against the
shipped set:

- *Hit & Run Fighter* offers a 3" move to the defender's team after a melee it survived, and not a second time in
  the round; *Hit & Run Shooter* after shooting; *Harassing* at both points but once per round; *Harassing Boost*
  at 6"; a Shaken unit is offered nothing; the move is refused across impassable terrain.
- *Teleport* is offered at the before-attacking checkpoint of Advance and Charge at 3" and of Rush at 6", not on
  Hold, once per activation, beside a cast; a placement across an impassable volume is accepted and one landing
  inside it refused; the window reopens after it.
- *Rapid Blink* rolls one die halved up when chosen and nothing when declined; the Boost rolls two; only the
  models holding it are in the `MoveRegion`; a Shaken unit's Idle offers nothing.
- *Quick Shot*: a Rush is followed by the shoot Ask only with the rule, and *Combat Ecstasy*'s grant opens only
  the marked unit.
- *Grounded Reinforcement*: +1 to defense with most models within 1" of a declared volume, none beside an
  undeclared one.
- *Unpredictable*: one die per shooting and per side per melee; a 3 gives AP(+1) to every group, a 4 gives +1 to
  hit; the face is read back on replay.
- *Primal*: each 6 adds an attack rolled in the same step whose own 6 adds none; the Boost adds on a 5.
- `usage_count` with `round` and `activation`; `at_point` rows absent from interaction queries; migration 006
  round-trips `amount_dice` in `PropertiesStoreTests`.
- The library's every-spell theory gains, for the fifteen grants, an assertion that the granted rule is offered or
  applied at its point.

**The simulator** gains a catalog scenario, `army-rules`: two armies whose units carry *Hit & Run Fighter*,
*Hit & Run Shooter*, *Teleport*, *Rapid Blink*, *Quick Shot*, *Unpredictable Fighter*, *Primal* and *Grounded
Reinforcement*, on the terrain board, with `AskAsked("react")` among its expectations, under the uniform policy.
It runs on both drivers, and the seven invariants hold on it.

## Files touched

Created:

- `server/management/Persistence/Migrations/006_army_rule_hooks.sql`
- `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

Modified:

- `server/game/Content/grimdark-future-army-rules.part.json` (the fifteen entries' rows)
- `server/game/Properties/PropertySet.cs`, `PropertySetDocument.cs`, `PostgresPropertiesStore.cs`
- `server/game/Effects/Conditions.cs`, `Interaction.cs`, `LiveProperties.cs`, `RollEffects.cs`
- `server/game/Interpreter/Battlefield.cs` (`TerrainUnder`'s `near`)
- `server/game/Rules/GrimdarkFuture/GfHooks.cs`, `GfMoves.cs`, `GfShooting.cs`, `GfMelee.cs`, `GfAttack.cs`, `GfNames.cs`,
  `GfRules.Sequences.cs`
- `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`, `PropertiesStoreTests.cs`, `SpellLibraryTests.cs`,
  `GfShootingTests.cs`, `GfMeleeTests.cs` (the new first and last steps shift step indexes some tests read)
- `tests/Tabletop.Management.Tests/MigrationTests.cs`
- `sim/Scenarios/ScenarioCatalog.cs`; `tests/Tabletop.Sim.Tests/ScenarioTests.cs`

**Against the enforcement tree.** This plan runs after the library plan on the same tree, which has by then taken
the enforcement branch in. It touches none of the enforcement tree's files; `ScenarioCatalog.cs` and `GfNames.cs`
are textual overlaps only, already merged by then.

## Open for the owner

1. ***Primal* and *Clan Warrior* say a model *"may"* roll the extra attack.** The engine rolls it every time,
   without asking. Declining only helps on a weapon that hurts its own unit on a 1, which is *Hazardous*, and
   *Hazardous* appears nowhere in Saurian Starhost or Eternal Dynasty, the two books with *Primal* and *Clan
   Warrior*; only a menu edit could combine them. The recommendation is to roll every time. If the owner wants the
   choice, it is one Ask per hit roll that produced a 6 on such a weapon, at the cost of a pause in every attack
   by those two armies.
