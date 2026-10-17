# Schema

The physical Postgres schema. [`docs/data-model.md`](data-model.md) is the conceptual view —
entities and relationships; this is tables, columns and constraints.

Where the two disagree, the conceptual model is the intent and this is the expression.

The tables and their columns are drawn in [`schema-diagram.md`](schema-diagram.md).

## Decisions

### Normalised, not documents

An earlier draft of this session put the army tree, the table state and the asset profile in
`jsonb` columns, on the grounds that each is loaded whole and never queried inside.

**That was wrong, and for a reason this project already had a name for.** `jsonb` is the
magic-string move at the storage layer: the database can check nothing, and the typed, closed,
predefined structure that every design session insisted on stops being enforced exactly where
it would be cheapest to enforce it. The supporting argument — that fewer migrations suit a
moving model — does not apply either, since the point of the design sessions was to stop it
moving.

So the army is four tables plus six for stats and properties, and every constraint the design
argued for is a constraint the database holds: `base_shape` is checked, a weapon cannot exist
without a slot, the six terrain declarations are columns rather than strings in an array.

### `Properties` is ten tables, not a document

The schema pinned `game.properties_hash` and stored nothing to resolve it against. **The data
half of a ruleset had no home** — stat declarations and the three property catalogs were
designed in [content values](superpowers/specs/2026-08-29-content-values-design.md) and
[the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md) and never given
tables. These are those tables, with `book` and `book_spell` for the army books that list a set's spells.

A content-addressed document was the alternative, and it is what `blob` already does for asset
bytes. It was rejected for the reason this section opens with: the effect grammar is **closed**
— seventeen effect kinds, eleven predicate kinds, four stat kinds — so it is exactly the structure
a database can enforce, and putting it in `jsonb` would be the magic-string move at the one
place the enforcement is cheapest.

The cost is real and accepted: pinning by content hash now needs a canonical serialisation to
hash, because the set is spread across ten tables rather than being a run of bytes.

### An action is a row, because an effect can target it

`Properties` names the actions a ruleset has, and each carries **one number: a distance**.
Grimdark Future's are `Hold` at 0", `Advance` at 6", and `Rush` and `Charge` at 12".

The reason this is a table and not a hard-coded list is `Fast` and `Slow`:

> `Fast` — *models move +2" when using Advance and +4" when using Rush/Charge.*

[Content values](superpowers/specs/2026-08-29-content-values-design.md) found that **Grimdark
Future has no movement stat at all** — a unit entry is Quality and Defense, and movement comes
from the action. So the number `Fast` modifies belongs to the action, and without this table two
of the rulebook's thirty-two special rules have nothing to point at.

**That is also where the line falls between an action's data and its behaviour.** An action's
distance is a row because an effect targets it. *"May shoot after this action"*, *"must reach
base contact"*, and *"ends the activation"* are targeted by nothing, so they stay in
`GameRules` with the rest of the turn structure. **What an effect can target must be data**;
what only the engine reads need not be.

Actions are named per ruleset, so a ruleset with an action nobody anticipated needs content
rather than a code change — which is what
[game flow](superpowers/specs/2026-08-28-game-flow-design.md) decided, now with somewhere to
put it.

**The record of what a unit has done is not this.** That is the action record, cleared at the
round boundary, reconstructed from the event log and never a row.

### A roll is a row, for the action's reason

**Added 2026-09-03 by [action sequences](superpowers/specs/2026-09-03-action-sequences-design.md).**
`effect.target_roll` was a `text` check constraint of four OPR values — `hit | defense |
morale | casting` — so a ruleset with a wound or save roll would have needed a migration. That
contradicts the section above: rolls are the other thing effects target, and what an effect can
target must be data.

`roll` is a set's named rolls and nothing else. The dice convention, the threshold derivation
and the modifier cap stay in `GameRules`, exactly as an action's *"may shoot after"* does —
the row exists because an effect points at it.

### The catalog holds four kinds, and one of them is a spell

`catalog_entry` is every named rule in a ruleset, and `kind` says what sort:
`unit | model | weapon | spell`. The first three are the property kinds
[identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md)
set out — attachment points, with a name belonging to exactly one of them. `GET /v1/properties/{hash}/rules`
serves a set's entries with their `rule_text`, the client's hover text; a rule with no entry is tagged *not automated*.

**`played`** (boolean, default true) is false only on an entry `scripts/ArmyBooks` generated as text only: the Army Forge
description as `rule_text`, no effects. The engine plays it as an absent name, and `GET /v1/properties/{hash}/rules`
returns it so the unit card tags the rule *not automated* while showing its text. A spell is always played.

**A spell is the fourth, and it is not a property**, which is why this table is not called
`property_definition`. A property is attached to something and travels with an army; a spell is
cast, is listed by an army book, and is picked at the moment of casting.

A spell's effect is an `add_hit`, a `take_test`, or a row with `duration = once`. What it has that no
property has is a **cost**, which is why `cost` is on the entry — `Caster(X)` grants X spell tokens a
round and casting spends as many tokens as the spell's value, which the generator writes from Army
Forge's `threshold`. Spell entries also carry **`target_side`, `target_grain`, `target_count`,
`target_range`**, from which the casting sequence's target Ask computes its legal set.

`faction` is null on every entry and nothing reads it: one spell entry may be listed by six books, and
`book_spell` is what says which.

A set holds every official book's spells, generated by `scripts/ArmyBooks` into
`server/game/Content/army-books/` and composed with the core document before hashing
([the spell library](superpowers/specs/2026-10-02-spell-library-design.md)). A spell name two books use for
different spells is qualified with the alphabetically first book holding it, in parentheses.

### One `effect` table for every kind

The seventeen kinds share most of their shape — a subject, an ordering, a thing being acted on,
and usually one number. `entry_name` is the catalog entry it belongs to. `amount` carries the number, and
`amount_from_parameter` says the number is the entry's own `X`, which is what makes `Impact(X)`
and `Blast(X)` one row rather than one row per value of X.

**What an effect acts on is nine different things, so it is nine columns rather than one**, ten
counting the level a stat reference needs. A
single `target` string would have been a magic string with no defined contents — the database
could not check it and nothing said what it may contain:

| Column | Vocabulary | Used by |
| --- | --- | --- |
| `target_roll` | **FK** to `roll` in the same set | Modify a roll, Force a reroll, Take a test, a spell's Add a hit that rolls its hits |
| `target_stat`, `target_stat_level` | **FK** to `stat_definition` in the same set. That table's key is `(properties_hash, level, name)`, so the level travels with the name and the FK is two columns | Modify a stat, Replace a stat |
| `target_action` | **FK** to `action` in the same set | Modify a stat, where the stat is a move distance |
| `target_entry` | **FK** to `catalog_entry` in the same set | Grant, Suppress, Modify a parameter |
| `target_quantity` | `hits \| wounds \| attacks` | Multiply (`hits \| wounds`), Add a hit (`wounds`, `attacks`; null is `hits`) |
| `target_result` | `wound \| morale_test` | Override a result |
| `target_modifier` | `cover \| to_hit_penalties \| range_penalties` | Suppress modifiers |
| `target_terrain` | `difficult \| dangerous` | Count as terrain |
| `target_condition` | a condition name (`fatigued` in the corpus); no check | Set a condition |

`Roll`, `Move` and `Place` act on nothing and leave all nine null.

The three foreign keys are the point. `Bane` suppresses `Regeneration`, and it must not be able
to name a rule that does not exist in the set it belongs to; `Reliable` replaces `Quality`, which
must be a stat that set declares. As a string, neither could be checked.

**`amount` is signed, and keeps its sign when the magnitude comes from the parameter.**
`AP(X)` is *−X to Defense rolls* and `Fear(X)` is *+X wounds*: both take their size from `X`
and differ only in direction. So `amount` holds `-1` or `+1` when
`amount_from_parameter` is set, and the effective value is that sign applied to `X`.

Four columns exist for one kind each, beside `target_terrain` and `target_condition` above, which is the honest price of collapsing seventeen kinds into one table:

- **`threshold`** — `Roll` only. `Impact(X)` is *X dice at 2+*, so it needs a count and a
  score, and the count is `amount`.
- **`cap_kind`** — `Blast(X)` only, and an enumeration rather than prose. *"Up to as many hits
  as models in the target unit"* is part of the effect, per the effect grammar, not a separate
  rule — but written as text the engine could not read it. Grimdark Future has exactly one cap,
  `models_in_target_unit`; a second ruleset needing another adds a value, which is a code
  change like every other closed set here.
- **`replacement`** — `Replace a stat` only. `Reliable` is *attacks at Quality 2+*.
- **`amount_dice`** — `Place` only, checked to `d3`: a placement whose distance is a die roll.

Per-kind `CHECK` constraints keep `threshold`, `cap_kind` and `replacement` honest: a `Multiply` row with a
`threshold` is rejected. `target_terrain` and `amount_dice` carry only a value check and `target_condition` none;
`PropertiesJson.Validate` holds all three to their kinds.

**`parent_effect_id` is how `Roll` nests.** *X dice at 2+, each success adds a hit* is a `Roll`
with an `Add a hit` child, so kind 6 is reused rather than special-cased. The column is
self-referencing and nullable; one level covers the whole rulebook and nothing forbids deeper.

**`duration` is `interaction | once`**, default `interaction`. Per
[spell effects](superpowers/specs/2026-09-04-spell-effects-design.md): a `once` effect, when it
applies, writes a grant onto its target — a fourth in-play store on the unit, rebuilt from the
log and never a row — consumed the first time that grant's effect applies. Every core rule is
`interaction`.

**`ordinal` is not decoration.** The rulebook orders effects by hand — `Deadly` is *"resolved
first"*, `Blast` applies *"after resolving other special rules"* — and four of the seventeen kinds
act on other properties rather than on game state, which does not commute.

### Ordered child rows record their position

`action.position`, `effect.position`, `effect_condition.position` and `condition_value.position` are each row's index among
its siblings in the document (an effect's siblings share its entry and parent), arriving as management's
`007_properties_positions.sql`. The game server reads every one of those lists in that order and nothing else orders them:
ids are v7 uuids minted in one loop, random within a millisecond. The order is evaluated, not cosmetic: sibling effects that
share an `ordinal` apply in document order, conditions are judged in order (`Slayer`'s `attack_kind`, judged first, keeps its
`most_models` from reading a defender an interaction without an attack does not have), a `roll_result` condition's values are its roll, then `raw | modified`, then the optional `success | fail`,
and a turn's options list actions in document order.

`position` is null on rows seeded before the column existed. Their order is not in the rows, so the game server's seeder,
finding a set already present with a null position, takes the positions from the document its hash names and updates the
rows in place, matching each document sibling to a stored sibling with the same content; ids are kept, because a game's
grants name effect ids. A set whose document is no longer embedded keeps null positions and reads in id order.

### A condition is a row, and its values are rows

`effect_condition` carries the predicate kind, its subject, and a comparison — `Stealth` is
*range, no subject, greater than, 9*. Relational predicates take no subject, so `subject` is
nullable by design rather than by accident.

**`comparison` includes `neq`**, because `Impact(X)` is *unless fatigued*. The effect grammar
found no *or* and no *not* in the rulebook's properties, and that holds for the structure —
there is no expression tree — but a single predicate still has to be able to test a state's
absence.

Values live in `condition_value` rather than in a column because **one predicate can name a
set**. `Slow` applies *when using Advance*; `Fast` applies to *Rush/Charge*, which the effect
grammar found is set membership over action types and not an `or`. One row is a single value,
several rows are a set, and there is no boolean expression tree anywhere — the rulebook has no
*or* and no *not* in its properties.

A `roll_result` condition has two or three `condition_value` rows: the roll it reads (`hit`,
`defense`), either `raw` or `modified`, and optionally `success` or `fail`, which the die's
success must match. `effect_condition.number` is the face compared. No roll may be named `raw`,
`modified`, `success` or `fail`.

Conditions hang off the effect, never off the property. `Indirect` is why: its three effects
have different conditions, one of them conditional and two unconditional.

### Worked against the book

The shape was checked by writing real Grimdark Future rules into these columns rather than by
inspection. Four that between them exercise every unusual column:

| Rule | Rows |
| --- | --- |
| `AP(X)` | one effect: `subject=opponent`, `kind=modify_roll`, `target_roll=defense`, `amount=-1`, `amount_from_parameter`. No conditions. |
| `Stealth` | `catalog_entry.aggregation=all`; one effect: `subject=opponent`, `modify_roll`, `target_roll=hit`, `amount=-1`. Two conditions — attack kind is shooting, and range `gt` 9 with no subject. |
| `Impact(X)` | one effect: `kind=roll`, `amount_from_parameter`, `threshold=2`, with a **child** effect `kind=add_hit`, `amount=1`. Conditions: action taken is charge, actor state `neq` fatigued. |
| `Bane` | **two** effects on one property — `force_reroll` with `target_roll=defense` on the opponent, conditioned on a raw roll result of 6; and `suppress_property` with `target_entry=Regeneration`, unconditional. |

`Bane` is the case that proves conditions belong to the effect and not to the property, and
`Impact(X)` is the one that proves nesting is needed rather than a kind of its own.

### Stats are rows because stats are declared

`Quality` and `Defense` cannot be columns on `unit` when Warhammer declares M/T/Sv/W/Ld/OC
instead. A declared stat set forces a key-value child table at each level — `unit_stat`,
`slot_stat`, `weapon_stat`.

This is the one unavoidable smell in the schema, and it is **forced by a decision that is
correct**: [content values](superpowers/specs/2026-08-29-content-values-design.md) settled
that stats are declared by `Properties` rather than typed into the engine.

`value` is `text` because the declaration carries the kind — target number, distance, count, or
dice expression — and `D6` is not a number. The column holds what was written; `Properties`
says how to read it.

### Where `jsonb` survives, and why

Two columns, for opposite reasons to the one originally given.

**`game_event.payload`** — genuinely heterogeneous by event kind. `architecture.md` settled
this: adding an event type must not be a migration.

**`participant.army_snapshot`** — because it **must not** be reasoned about. It is written when
setup ends — the moment the armies freeze, per [the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md)
— and read only to rebuild that game. It is **nullable**, as is `participant.team_id`: a
participant who joins after deployment has no army, and one who has not yet picked a team in the
lobby has no team (ruled 09-04, [the whole-design review](superpowers/specs/2026-09-04-whole-design-review.md)). Normalised, a later schema change would migrate
historical snapshots and silently alter what a past game was played with. Being opaque is the
requirement here, not a convenience.

### Enumerations are `text` with a check constraint

Not Postgres `enum` types. The values are closed and extending them is a code change, which is
accepted — but `ALTER TYPE ADD VALUE` has transaction restrictions that bite in migrations,
while altering a check constraint is ordinary DDL. Text also reads directly in queries.

### Lengths, coordinates and angles are `double precision`

Every length, coordinate, yaw and scale column is `double precision`, which Postgres also names
`float8` and the diagram writes that way. `Tabletop.Geometry` and `Tabletop.Content` carry these
values as `double`, so the round trip is exact; a `real` column would lose digits on every write.

### Every foreign-key column is indexed

Postgres indexes a primary key and a unique constraint, not a foreign key. Each foreign-key column
that does not already lead one of those has its own index in `001_schema.sql`, so the tree reads
(`where army_id = …`, `where table_state_id = …`) and the delete cascades are index lookups.

### `table_state` has two nullable owners

`owner_id` is set when it is an account's saved layout; `game_id` is set when it is a game's
setup result. Exactly one, enforced by a check constraint. A game's row **exists from the moment
setup starts and is written on every edit** by the game server, then frozen from `playing` on —
enforced in the game server, not the database ([the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md)).

**Built-in or asset, exactly one.** `table_state.texture_builtin | texture_asset_id` and
`marker.visual_builtin | visual_asset_id` each carry a check constraint that one and only one is
set — stated here since 09-04, because the terrain placement section below leans on it.

Nullable foreign keys are a smell, and the alternative is two tables with identical columns,
which is a worse one. [Setup content](superpowers/specs/2026-08-29-setup-content-design.md)
decided these are **one shape with two owners**, and this is what that looks like.

### A terrain placement names a built-in or an asset

**Added 2026-09-04 by [the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md).**
Built-in terrain ships with the client, the way board textures and marker visuals do, and the
terrain generators place it. So `terrain_placement` carries `builtin` beside a now-nullable
`asset_id`, exactly one set, with the same check constraint `texture_builtin | texture_asset_id`
and `visual_builtin | visual_asset_id` already use. The placement's own `terrain_volume` rows
carry its footprint and declarations either way, which is what lets a user swap the mesh and
keep the piece. A placement's `terrain_volume` rows are always written on add, copied from the
built-in's shipped volumes or from the asset's `asset_volume` rows. That makes the draft and the
snapshot self-contained. The game server never reads `asset_volume`. Editing those rows in place
is the override, and clearing them re-copies the source's volumes.

### A game has a name, an enforcement setting and a wait setting

**Added 2026-09-04 by [the whole-design review](superpowers/specs/2026-09-04-whole-design-review.md).** `name` is what the game list
shows. `enforcement` is `silent | warn | strict`, replacing `warns boolean`: the review reopened
identity and enumerations' boolean and kept the refusing position two later designs had written.
`waits_between_steps` is resolution's second setting, which had no column; its first, animate, is
a per-player client setting and not here. Neither the client's create-game form nor its lobby panel offers `waits_between_steps`;
both send `false`, and the column serves development.

The game server writes `game.status = 'ended'` from `playing` on a strict end, and management writes it on end, leave and delete.

### An army book is a row, and lists its spells

`book (properties_hash, uid, name, version)`, keyed `(properties_hash, uid)` and cascading from `properties`, is one
Army Forge army book in a pinned set: its `uid`, its name, and the `version` its record carries, null for the book that
publishes none. `book_spell (properties_hash, book_uid, entry_name, ordinal)`, keyed `(properties_hash, book_uid,
entry_name)`, lists the book's spells in its own order and references `book` and `catalog_entry`. A book's version is
a property of the set, not of the game: regenerating one book makes a new set and a new hash. Both arrive as
management's `005_spell_library.sql`.

`model_slot.army_book text null` is the `uid` of the book the slot's Army Forge entry came from (the TTS export's
`armyId`), written at import. A caster model may cast exactly the spells the pinned set lists under its slot's book; a
slot with no book, or a book the set does not list, casts nothing. It is the slot's and not the unit's because every
entry, a joined one included, carries its own `armyId`, and `Caster(X)` is a model rule.

### The catalog's per-kind check

`cost` is non-null only when `kind = spell`; `target_side`, `target_grain`, `target_count` and `target_range` are non-null
on a spell and on a pick entry (a `model` entry an army-book Buff, Mark or Debuff writes); `target_line_of_sight` is null
(in sight) or false (range alone, a Buff), and set only with a target; `aggregation` only when `kind = model`;
`takes_parameter` is false for a spell. `PropertiesJson.Validate` holds a pick entry to count one and `once` rows at
`before-attacking`.

### A game records its creator

**Added 2026-09-04 by [the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md).**
`game.created_by` is the account that called `POST /v1/games`. It is read for two socket
commands — start setup, end setup — and nothing else; the creator is not a host. Account
deletion already destroys every game the account played in, so a deleted creator leaves no
orphan.

### A game stores its dice seed

`game.rng_seed bigint not null`, with no default. The RNG
is counter-based: the seed and the count of dice drawn in the whole log, undone events included, give every die, so an
undone roll's dice stay spent and no RNG state is stored anywhere. Management writes the
seed once, at creation, and never again. It is not derived from the game id, because a simulator
run then has no way to choose one. The absent default is deliberate,
so a caller that omits the seed fails loudly instead of playing a game nobody can reproduce.

### `participant` links to its game, and holds the army it picked

`team_id` is nullable, because a participant with no team is what a spectator is, so the link to
the game cannot travel through the team. `participant.game_id` is its own column, and
`unique (game_id, account_id)` is what stops an account joining twice. `army_id` is the lobby's
pick, nullable and `on delete set null`. It stands until `army_snapshot` is written at the start of
setup, after which the snapshot is what plays, and the army may then change or be deleted without
touching the game.

### `blob` records whether its bytes arrived, and what they are

`uploaded boolean not null default false` and `content_type text not null`, checked against
`model/gltf-binary | image/png | image/jpeg | image/webp`. The row is written when `POST /v1/assets` hands out a
presigned PUT and flipped by `/confirm`, so the gap between the two is visible rather than
indistinguishable from a finished upload. These two columns arrive as management's
`002_blob_uploaded.sql` rather than in `001_schema.sql`. The init SQL, `scripts/postgres/init/001_schema.sql`, is
management's `001_schema.sql`; every table and column from `002` to `009` reaches the database through management's
migrations only.

### `account` records whether its starter meshes were seeded

`starter_seeded_at timestamptz null`, arriving as management's `003_account_starter.sql`. Null means the
account has not been given the starter miniature set; management's `Sessions.Complete` seeds it at login and
sets the column. Nothing else reads it, and deleting a starter asset does not clear it
([starter meshes](superpowers/specs/2026-10-01-starter-meshes-design.md)).

### `properties` records when it was last seeded

`seeded_at timestamptz not null default now()`, arriving as management's `004_properties_seeded_at.sql`. The game
server's seeder inserts a new set and, for one already present, sets `seeded_at = now()`, so the sets the running
server ships are the newest. `GET /v1/properties` lists one set per `(game_rules, name)`, the latest by `seeded_at`;
editing a document under the same name supersedes the listing entry, and a game that pinned the older hash keeps it.

### A deployment zone names a team ordinal, not a team

A saved layout has deployment zones but no game, so no `team` rows exist to point at. The zone
records `team_ordinal`, and a game maps ordinals onto its actual teams when the layout is
loaded.

`shape` holds a zone shape id from `Tabletop.Geometry.ZoneStyles` (`'frontline/1'`), namespaced by
style, and is the same value as `Tabletop.Content.DeploymentZone.Shape`. The style name is not stored.

### Deleting an account is application logic

Its armies, assets, blobs, folders and layouts cascade downward normally. But
[accounts](superpowers/specs/2026-08-29-accounts-design.md) requires deleting **every game it
participated in**, which cascades *upward* — participant to team to game — and then back down
to other people's participants and into the game schema's event log.

Postgres cascades do not run that way. It is a deliberate multi-step operation in the delete
path rather than a constraint or a trigger: a trigger would hide a destructive cross-schema
action behind an ordinary `DELETE`.

### Identifiers

UUIDv7 throughout, generated in application code with `Guid.CreateVersion7()`, stored as
`uuid`. Per `architecture.md`: v7 so inserts append near the end of the index, and
application-generated because ids must exist before external systems are told about them.

`blob`'s primary key is `(owner_id, hash)` rather than a surrogate id, which makes per-user
dedup a property of the schema rather than a rule in code.

## Still open

- ~~How the game server reads game metadata.~~ **Settled** by
  [the management API](superpowers/specs/2026-08-29-management-api-design.md): the client
  opens its socket with a Stytch session JWT and a game id, and the game server **reads the
  game's row directly** — and, since 09-04, writes the game's own rows while its status is
  `lobby` or `setup`, and `game.status = 'ended'` from `playing` when its own log ends the game ([the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md)). The
  two services share a database, so a management migration can break the game server.
- ~~Whether the two schemas are two Postgres schemas in one database or two databases.~~
  **Settled** for local development by the contracts plan: two Postgres schemas in one database,
  created by `create schema management; create schema game;` at the top of `001_schema.sql`. The
  game server reads and, before play, writes management's rows, which is one connection when the
  two share a database. Hosting is still free to split them.
- Indexes beyond primary and foreign keys. None are needed at this scale, but the game
  browser's listing query is the first candidate.
- Ordering of an army's children. `weapon`, `unit_stat`, `unit_property`, `slot_stat`,
  `slot_property`, `weapon_stat` and `weapon_property` carry no ordering column; they are written
  once at import and `ArmyRepository.Get` reads them `order by ctid`, which reproduces insertion
  order until a `VACUUM FULL` or an `UPDATE` moves a row. The durable fix is an `ordinal` column on
  all seven, as a new migration (`001` is never edited after the fact).
- ~~Presence has never been designed.~~ **Settled** by
  [presence](superpowers/specs/2026-08-30-presence-design.md), which changes nothing here:
  cursors, pings, measurements, selection and live placements are relayed and never written,
  so presence has no table and no column.
