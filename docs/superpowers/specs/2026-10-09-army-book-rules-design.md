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
  `at_point` of `before-attacking`, a `usage_count` of one per `activation`, and its effect rows with duration `once`.
  `PropertiesJson.Validate` (`server/game/Properties/PropertySetDocument.cs:73`, `:77`) admits `targeting` and `once` rows
  on a `model` entry that has a `before-attacking` `at_point`; today only spells may carry them.
- **Where it is offered.** `GfHooks.ReactionsAt` (`server/game/Rules/GrimdarkFuture/GfHooks.cs:79-94`) offers at
  `before-attacking` the cast reaction and the live Move/Place rows. It also offers one `use` reaction per carrying model
  whose pick entry has not been used this activation. A Shaken unit is offered none (`GfHooks.cs:88`). The point runs only
  when the unit attacks (`GfMoves.cs:258`, `:321`), which is *"before attacking"*.
- **What it does.** A new sequence `pick-rule`: an Ask `rule_target` with `PieceSelection` over the eligible units (the
  spell-targeting reading, `GfCasting.SpellTargets`, measured from the carrying model), pass allowed; then one `GrantAdded`
  per `once` row on the picked unit (`Grant(Id, EffectId, EntryName, CasterUnitId, 1)`, as `GfCasting.cs:227`), and the use
  counted.
- **How it is spent.** By the existing grant reading and consumption (`LiveProperties.cs:188-196`, `RollEffects.Consume`),
  which is *"once (next time the effect would apply)"* (spell-library spec, *A grant is its effect row*). Nothing new.
- **Per model, optional.** Each carrying model has its own use per activation (*"this model"*). The pick may be passed, as
  a cast may.

## Authored rules

Authored entries go in `grimdark-future-army-rules.part.json` (hand-authored, as the 73 there now) with the Army Forge text
as `ruleText`. Each needs a suite game, as every hand-authored entry does (*Coverage*). They are built in batches, each its
own plan, ordered so that each batch's mechanisms are in place before it:

1. **Grammar only.** Rules the current effect kinds express: Agile, Armor, Bane in Melee, Fortified, Good Shot, Grounded
   Stealth, Heavy Impact, Ignores Cover, Ignores Cover when Shooting, Ignores Regeneration, Ignores Regeneration in Melee,
   Increased Shooting Range, Melee Shrouding, Melee Slayer, Point-Blank Surge, Precise, Predator Fighter, Protected, Rapid
   Charge, Rending in Melee, Sergeant, Shred in Melee, Shred when Shooting, Steadfast, Surge when Shooting, Swift,
   Unstoppable in Melee; the bases that only template wrappers name (Precision Shooter, Piercing Fighter, Ranged Shrouding,
   Screened, Thrust in Melee, …); and the 21 written-out Auras. A rule that turns out to need a new effect kind moves to
   the batch that builds it.
2. **The pick.** The mechanism above, the 10 template Buffs and 9 template Marks, the 8 written-out Buffs and 6 written-out Marks, and the
   10 Debuffs; and the before-attacking actives that share it: Mend, Mind Control, Breath Attack, Storm of Change, Storm of
   Lust, Storm of Plague, Storm of War.
3. **Deployment.** Infiltrate, Rapid Ambush, Ambush Beacon, Repel Ambushers, Vanguard, Re-Deployment, Ambush Re-Deployment,
   Ambushing Piercing Shot, Surprise Attack.
4. **Activation.** Bounding, Dash, Coordinate, Delayed Action, Instinctive, Versatile Attack, Hit & Run, Re-Position
   Artillery.
5. **Wounds and melee.** Deathstrike, Retaliate, Self-Destruct, Ravage, Crossing Attack, Strafing, Takedown Shot, Takedown
   Strike, Protection Feat, Regenerative Strength.
6. **Unit markers.** One marker count on a unit, placed by a trigger and read by a row: Defensive Frenzy, Piercing Frenzy,
   Precision Frenzy, Fortified Growth, Piercing Growth, Precision Growth, Piercing Spotter, Precision Spotter, Piercing Tag,
   Precision Tag, Piercing Target, Precision Target, Vengeance.
7. **New units and casting.** Spawn, Split, Reinforcement, Spell Accumulator, Spell Conduit, Extended Buff Range.

Banner and Musician have no description in the dump. Their text comes from the books' upgrade entries, found while
planning batch 1.

## Generator

`scripts/ArmyBooks` `generate` writes one more file, `server/game/Content/grimdark-future-army-book-rules.part.json`, never
edited by hand. The server embeds and composes every `*.part.json` already (`server/game/Tabletop.GameServer.csproj:21`,
`PropertiesSeeder.cs:13`).

- **Input.** Each book's `specialRules` (name, description), read beside the spells it reads now. Two books giving one name
  different descriptions fail the run and write nothing, as spells do.
- **Names in scope.** Every rule name on a unit or an upgrade in the official books. A name core or the hand-authored part
  defines is skipped: hand-authoring wins.
- **A wrapper is generated** when its name ends in Aura, Buff, Mark or Debuff, its description is that pattern's template
  with X equal to the name less the suffix, and X is a catalog entry. The entry's rows point at X.
- **Every other name is a text-only entry**: kind `model`, aggregation `any`, the description as `ruleText`, no effects,
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
