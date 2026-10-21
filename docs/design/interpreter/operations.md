# State, failure modes and testing

Part of [the interpreter design](design.md).

## State and data

### Persisted

One database, two schemas. This module reads both and has one write path of its own: the ten
`Properties` tables, once per hash, through `PropertiesSeeder` at startup. Every payload it
produces reaches `game.game_event` through the skeleton's session, never through this module.

| Table | Columns this module reads or writes | Who writes |
| --- | --- | --- |
| `management.properties` | `content_hash` PK, `name`, `game_rules` | `PropertiesSeeder` at startup, once per hash |
| `management.action` | `properties_hash`, `name`, `distance`, `position` | the same |
| `management.roll` | `properties_hash`, `name` | the same |
| `management.stat_definition` | `properties_hash`, `level`, `name`, `kind` | the same |
| `management.catalog_entry` | `properties_hash`, `name`, `kind`, `takes_parameter`, `aggregation`, `cost`, `faction`, `rule_text`, `target_side`, `target_grain`, `target_count`, `target_range`, `played`, `target_line_of_sight` | the same |
| `management.effect` | `id`, `properties_hash`, `entry_name`, `parent_effect_id`, `subject`, `ordinal`, `kind`, `target_roll`, `target_stat`, `target_entry`, `target_action`, `target_quantity`, `target_result`, `target_modifier`, `amount`, `amount_from_parameter`, `threshold`, `cap_kind`, `replacement`, `duration`, `target_terrain`, `target_condition`, `amount_dice`, `position` | the same |
| `management.effect_condition` | `id`, `effect_id`, `kind`, `subject`, `comparison`, `number`, `position` | the same |
| `management.condition_value` | `condition_id`, `value`, `position` | the same |
| `management.book` | `properties_hash`, `uid`, `name`, `version` | the same |
| `management.book_spell` | `properties_hash`, `book_uid`, `entry_name`, `ordinal` | the same |
| `management.model_slot` | `army_book`, read by `PostgresGameRows` into the army snapshot as `ModelSlot.ArmyBook` | management's Army Forge import |
| `management.game` | `properties_hash` and `rng_seed` read at hydration; nothing written | management, which writes `rng_seed` once at creation, then the game server while `lobby`/`setup` |
| `game.game_event` | `(game_id, seq, kind, payload)`: every payload this module returns | the skeleton's `PostgresEventLog` |

`PostgresFixture.DeletePropertiesAsync(hash)` deletes the `properties` row and lets the nine
others cascade. The embedded core set is left in a developer's database on purpose.

### In memory

| Thing | Held where | Lifetime |
| --- | --- | --- |
| `PropertySet` | `RulesEngineFactory._cache`, keyed by hash | process |
| `GameState.RngSeed` | `unchecked((ulong)Info.RngSeed)` on `GameState` construction, the value stored on the game row | session |
| The frame stack | `Projection.Stack`, applied from the four sequence events | rebuilt by the applier; **never a row** |
| A frame's position, values and history | nothing; computed by `FrameReader.Read` on every `Advance` | one call |
| The dice cursor | nothing; `DiceCursor.DrawnSoFar` sums over the whole log | one call |
| A unit's grants, conditions, action record, wounds-this-activation, spell tokens | `UnitInPlay`/`ModelInPlay` in the projection | rebuilt from the log |
| An open Ask and its answers | `Projection.OpenAsks` | rebuilt from the log |

Nothing here stores a position, a wound, a token count or whose activation it is. A table growing
a *current* column means the event spine has been broken.

## Failure modes

### Ordering and lifecycle

| Hazard | What happens | What the design does |
| --- | --- | --- |
| A step returns a non-empty batch forever | The session's `RunToRestAsync` loop never terminates and the game hangs | Nothing generic. Every repeating step must advance its own `Values`; no loop bound exists. **The most likely way to hang a session.** |
| A step opens an Ask it already opened | Same hang, one call later | Every asking step tests `ctx.History.LastClosed()` first. `WindowTests` and `InterpreterTests` pin the pattern for the two generic cases |
| A sequence lists one step name twice in a row | `FrameReader` would read the second completion as a repeat and the position would stall | `Sequence`'s constructor throws `ArgumentException`, at first call of `GfRules.Sequence` |
| A sealed Ask never closes because a team is disconnected | The frame is stuck and every other team waits | Not handled. A menu edit or an abandon is the only exit, and nothing times out |
| A team disconnects mid-window | `Windows.Run` keeps returning the same `OpenAsks` | Same. Muting is client-side, so a muted window is still an unanswered Ask unless the client auto-passes |
| `GameRules` changes between deploys | An old log replays against a different step list or a different threshold function | Accepted. Replay applies recorded results, so thresholds and dice are faithful. A *step-list* change is detectable because `step_completed` names its step. A game that has not ended also moves to the current `Properties` set at hydration; grants follow through effect aliases |
| A menu edit puts state the rules call impossible under a running frame | A step reads a dead model on the table, a unit with no models, a container that is not a model | *Nothing is an invariant.* Steps read state defensively: `Lookup.AliveModels(...).Count == 0` finishes, and an `Inside` whose container is not on the table is dropped. This is a per-step obligation with no type-level help |
| An out-of-set Ask answer under `warn`/`silent` | A step receives an answer its legal set did not offer | Applied as given. Steps use `is not Chosen`/`?? []` guards, and `AssignStep` applies whatever allocation arrived |

### Reentrancy and containment

- **`disgorge` beneath `transport-destroyed`.** The one reentrancy in the module. An ancestor
  walk guards it rather than a stack scan, so the outer `disgorge` still works while its child
  runs. The two `GfContainersTests` cases cover the ordering: occupants tested, shaken, placed,
  wreck last. Neither asserts the guard itself. A third case would.
- **A model-grain spell cast from inside a container reaches only the models inside with it.**
  `GfCasting.Targets` asks containment before range and sight, so the same box answers every alive
  model of a unit inside it and no spell crosses the wall either way (p.15, the plan's ruling 10).
- **A grant on a model-grain target lands on the model's unit,** because the grants store is the
  unit's. If two models of one unit are granted the same rule, the instances collapse unless the
  entry takes a parameter. That is the intended behaviour, not a bug.

### Cost and accepted gaps

- **Every `Advance` is O(log length).** `FrameReader.Read` walks the effective log once per
  frame, `DiceCursor.DrawnSoFar` walks it again, `Conditions.Usage` walks it for a `Limited`
  weapon, and `GfCasting.TriedThisActivation` walks it for a caster. With the wait setting on and
  four players over several hours the log is thousands of events and `Advance` runs after every
  appended batch. No index, no memoisation, no incremental reader. The scan is the price of
  statelessness. `architecture.md` prices the workload as *turn-based, small state, no simulation
  tick*.
- **Content grows monotonically.** Editing an authored document seeds a new set. Nothing deletes
  the old rows, and no tool reports which hashes are unreferenced.
- **Effect ids are database-local.** A `Grant.EffectId` in an event log does not survive being
  restored into a differently-seeded database. Nothing detects it.
- **`GameRules` is unpinned**, so an ended game restarted after a deploy may replay differently.
  Accepted in identity and enumerations: the alternative makes every deploy a wall old games
  cannot cross.
- **Capacity is never enforced.** `Transport(X)`'s parameter is carried by the catalog entry and
  read by nothing. Game flow rules it explicitly.
- **Unit coherency is never enforced.** Nothing judges it. The 1-inch separation, base overlap and a path through a model are
  judged by `GfMoves.ContactBreach` and `OverlapBreach` under the enforcement setting; `MoveRegion.Separation` and
  `PassesModels` carry the Ask's terms (Separation 1 except the Charge move, close-in, consolidation and an Aircraft's
  move (move step, forced move, reposition); PassesModels from the Flying/Aircraft pass-impassable rule, including the terrain reposition Ask;
  IgnoresTerrain on an Aircraft's move, which exempts its landing, p.13 *"ignore all units and terrain when moving and stopping"*).
- **Missions beyond the core one do not exist here.** Objective seizing and the round-4 winner are the ruleset's `OnRoundEnded` and `JudgeAdvance`, not interpreter steps; players keep their own counters.

### Concurrency

One point of shared mutable state exists: `RulesEngineFactory._cache`, guarded by a `lock`.
Everything else is per-session, and the skeleton serialises a session's work through
`GameSession.RunAsync`, so no step ever runs concurrently with another step of the same game.
`PropertiesSeeder` races another process starting at the same moment. `SeedAsync` returning
`false` for an existing hash makes the loser a no-op, and the insert is by primary key, so the
worst case is a duplicate-key error on one of the two.

## Testing

Test-driven throughout, in `tests/Tabletop.GameServer.Tests`. Every task starts with a failing
xUnit test. Every OPR sequence has either the rulebook's own worked example or a constructed case
with scripted dice. 80 new tests are expected, which is the figure the plan's final step checks.

| Suite | Covers | Needs Docker |
| --- | --- | --- |
| `PropertySetTests`, `PropertySetDocumentTests` | lookups, `Comparison` over six operators, enum wire spellings, canonical hashing, validation, a spell's three admissible effect shapes, book listings | no |
| `CoreContentTests` | the authored OPR core set parses, validates and builds | no |
| `SpellLibraryTests`, `GrantRowTests`, `SpellShapeTests` | the composed set validates and holds 48 books; its hash is stable; every shipped spell is cast by a caster of a book listing it; a grant holds one row; the spell library's kinds and predicates | no |
| `PropertiesStoreTests` | ten tables in, ten tables out, books included; `SeedAsync` idempotent by hash | **yes**: `[Collection("Postgres")]`, `PostgresFixture` |
| `DiceTests` | counter-based determinism, the stored seed reinterpreted whole including a negative one, cursor arithmetic, `RequiredRaw` versus threshold, forced rerolls, an undone roll's dice staying counted | no |
| `LivePropertiesTests`, `RollEffectsTests` | aggregation, a `unit`-kind entry on a model slot read the same way by both readers, collapse-unless-parameter, the four pass-one kinds in ordinal order, plan modifiers, suppression, per-die grants, capped multipliers | no |
| `InterpreterTests`, `WindowTests` | the five outcomes, position and history from the log, confirm gates, reaction order, the simultaneous spend | no |
| `RulesEngineTests` | the three-line `Advance` split, the three dice commands, `PropertiesNotFoundException` | no |
| `GfRollsTests` and the seven `Gf*Tests` | every OPR sequence against the rulebook, including p.8's and p.9's worked examples | no |
| `InterfaceConstraintTests` | seven of the nine constraints, by source scan and reflection | no |

The harness is two classes. `Armies` builds a two-team playing game around one sequence.
`Sim.Gf(state, params int[] faces)` wraps it over an `InMemoryEventLog` with `ScriptedDice`,
exposes `Start`, `AskFor`, `Answer`, `Steps` and `Alive`, and drives the `Advance`/answer loop. A
whole engagement becomes an array of faces in draw order and a handful of assertions on
`step_completed.Values`. That works because the dice are counter-based and the log is the whole
state.

**What cannot be tested headlessly:** nothing in this module. It is Godot-free and has no client
dependency. The Postgres-backed store tests need
`docker compose up -d`. Every other test runs on an in-memory log and an
`InMemoryPropertiesStore`.

Notable gaps:

- **The constraint tests are source-text scans.** They will pass a violation written with string
  concatenation and fail on an innocent comment mentioning `Tough`. Constraints 5 and 8 have no
  assertion at all.
- **No test bounds a repeating step.** The hang described above is not covered.
- **No test covers a disconnect during a simultaneous spend or an open window.**
- **The two-module integration is covered only by the scheduler module's `SchedulerHarness`,**
  which runs the real `RulesEngine`. There is no test in this module that exercises the
  scheduler-to-interpreter handover.
- **Log-length cost is unmeasured.** No benchmark exists for `Advance` over a long game.
- **`DiceSource`'s distribution is unexamined** beyond uniformity in `1..sides`. `% sides` on a
  64-bit value has a modulo bias for six too small to matter, and nothing tests it.
