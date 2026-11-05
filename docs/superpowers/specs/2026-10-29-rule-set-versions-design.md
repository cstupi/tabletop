# Rule-set editions: Grimdark Future 3.5.1 and 3.6.0 side by side

Owner ruling (2026-10-29): both rulebooks stay playable. 3.6.0 is a second rule set beside 3.5.1, built by copying the
existing rules and changing what differs; each game chooses one; existing and unfinished games stay on 3.5.1; new games
default to 3.6.0. Army books are versioned by edition too. Within an edition the ruling "a rule fix moves every game"
still holds; the edition choice is the one exception.

The rule differences are `2026-10-29-gf-3-6-0-rules-diff.md` (core, special rules) and
`2026-10-29-gf-3-6-0-rules-diff-modules.md` (modules, army books); ids such as C4 and S5 refer to them.

## How the engine is organised today

### Rules identity

- `GrimdarkFutureRules.RulesId = "grimdark-future"` (`server/game/Rules/GrimdarkFuture/GfRules.cs`), one singleton
  `Instance`. `GameRulesRegistry.Resolve(id)` (`server/game/Rules/IGameRules.cs`) is one equality test, else
  `UnknownGameRulesException`.
- `Hydrator.HydrateAsync` resolves `info.GameRules`, then `RulesEngineFactory.Create(rules, info)` loads the
  `PropertySet` by `game.PropertiesHash` and `SchedulerFactory` picks the scheduler by `SchedulerShape`.
- The id is stored in `management.game.game_rules` and `management.properties.game_rules` (both `text not null`). A game
  never names it directly: `POST /v1/games` takes `CreateGameRequest(Name, PropertiesHash, SchedulerShape, Enforcement,
  WaitsBetweenSteps)` and `GameRepository.Create` copies `game_rules` from the chosen set's row;
  `PostgresGameRows.SetConfigurationAsync` does the same in the lobby. On the wire it is `GameInfo.GameRules` (in
  `Welcome`, `GameInfoMessage`) and `PropertiesSummary.GameRules` (`GET /v1/properties`).
- The only version anywhere is the core document's display name, `"Grimdark Future v3.5.1"`.

### Property sets

- `PropertiesSeeder.EmbeddedDocuments()` composes one document per embedded `*.properties.json`, folding in every
  `*.part.json` and `*.book.json` whose `gameRules` field equals the core's. File names are convention only, but three
  places name files: `PropertiesSeeder.GeneratedPart`, `scripts/ArmyBooks` (core and hand part by name), and
  `ScenarioCompiler.CoreDocument` (`.Single(d => d.GameRules == GrimdarkFutureRules.RulesId)`).
- Seeding is by content hash. `CurrentProperties` maps game-rules id → current hash; `Hydrator` repins a game that has not
  ended to its id's current hash (`IGameRows.RepinPropertiesAsync`). `EffectAliases` aliases effect rows across the sets
  of one id, so grants minted under an older hash still act. `PostgresPropertiesStore` refuses a catalog-entry kind clash
  across the sets of one id.
- Consequence: a game-rules id is exactly the unit "rule fixes move every game" applies to. A second core file under the
  same id would fight the first for `CurrentProperties`, repin 3.5.1 games onto 3.6.0 and alias across editions. An
  edition is therefore a game-rules id.

### Per-game configuration (the precedent)

- Optional rules (spec `2026-10-15-optional-rules-design.md`): `game.optional_rules jsonb` (migration 010),
  `GameInfo.OptionalRules`, `IGameRules.OptionCatalog` sent as `Welcome.Options` (`RulesOptionCatalog` with page strings),
  `SetConfiguration(Name, PropertiesHash, SchedulerShape, Enforcement, WaitsBetweenSteps, OptionalRules)` checked by
  `GameSession.Lobby` (creator only, `PropertiesExistAsync`, `OptionProblems`), refused `WrongStatus` from `playing` on.
  The rules read it through `GfOptions.Of(state)`, parsed where it decides.
- `SchedulerShape` is the same pattern: chosen at create, changeable in the lobby, a checked column, picked by
  `SchedulerFactory`.
- The Godot client's create dialog (`HomeScreen.CreateDialog`) already lists rule sets from `GET /v1/properties` as
  `"{Name} ({GameRules})"`; the viewer's `CreateGameDialog.svelte` shows its select only when there is more than one set and
  defaults to `packs[0]`, the newest seeded. Both lobbies send `SetConfiguration` with the game's current hash, so the
  lobby cannot change the set. `GameSession.Rules` is resolved at hydration only, so a lobby change of `game_rules` would
  not reach the live session until the next hydration.

### Army books and armies

- `scripts/ArmyBooks fetch` reads Army Forge (`/api/army-books?filters=official&gameSystemSlug=grimdark-future`, then
  `/api/army-books/{uid}?gameSystem=2`) into `.superpowers/armyforge/` (git-ignored); `generate` writes
  `server/game/Content/army-books/<uid>.book.json` (`gameRules: "grimdark-future"` hard-coded), the generated rules part,
  and `server/management/Content/Armies/army-book-units.json`.
- Army Forge now serves only 3.6.0 books; the 3.5 dump on disk is the only 3.5 source (modules diff, Army books).
- Import (`ArmyForgeClient`: `/api/tts?id=<list>`) returns the list re-resolved against the current books; the payload
  carries `gameSystem`, each unit's `armyId` (book uid) and `id`, and no edition. `ArmyForgeImport.GameRules` maps `gf` and
  `aof` to `grimdark-future`. The `army` row is `(id, owner_id, name, game_system, source)`; the game holds an
  `army_snapshot` per participant from `EndSetup`.

### Rulebook reference

`.claude/skills/licensed/` (tracked, plugin `licensed`): `skills/grimdark-future-rules/SKILL.md` (v3.5.1, page index,
method) and `pages/p01–p71.md`. Code and docs cite bare `p.N`, meaning 3.5.1.

## Design

### Editions are game-rules ids

- `grimdark-future` stays the 3.5.1 id: every existing game, property set and alias keeps working with no migration.
- `grimdark-future-3.6` is the 3.6.x id. Its fixes (and a later 3.6.1 if OPR publishes one) move every 3.6 game; 3.5.1
  games never see them. A 3.7 would be a third id.
- The edition's display name is the core document's `name` (`Grimdark Future v3.5.1`, `Grimdark Future v3.6.0`); UI
  shows the name, never the id.
- `GameRulesRegistry.Resolve` maps both ids; `CurrentProperties`, repin, `EffectAliases` and the kind-clash check are
  already per id and need no change.

### Where the rules live: copy the data, share the code that does not change

Copy (each edition owns its file; nothing is shared across ids by the seeder anyway):

- `server/game/Content/grimdark-future-3.6/` holds `core.properties.json` (copied from 3.5.1, then S1–S16's text, effects
  and aggregations), `army-rules.part.json` (the hand part, rewritten for the 3.6.0 books), `relic-wargear.part.json`,
  the generated `army-book-rules.part.json` and `army-books/*.book.json`, all with `"gameRules": "grimdark-future-3.6"`.
  The 3.5.1 files move to `Content/grimdark-future-3.5.1/` only if their composed hash is unchanged by the move (a test
  asserts it); otherwise they stay where they are. The csproj globs become recursive.
- `PropertiesSeeder.GeneratedPart`, `scripts/ArmyBooks` and `ScenarioCompiler` take a content folder per edition instead
  of fixed file names.

Share (one C# implementation, edition read from state):

- `GfEdition.Of(state)` reads `state.Info.GameRules`, exactly as `GfOptions.Of(state)` reads `OptionalRules`, and
  returns a record of what differs: melee reach (C1), Scout reach and clearance (S13), dangerous test mode (C4), fatigued
  6s trigger rules or not (C2), Impact before Counter and Counter's reduction (S5, S11), Counter optional, caster target
  sight and helper sight (S4), Indirect needs Hold (S12), Artillery Hold-only (S2), Cumbersome (S6), Aircraft transport
  reachable (S15), melee loss by wounds taken (C3), the Hero aura (S10), Side Battle legs (M1), terrain flag meanings
  (below), option catalog page strings (p.56 vs p.58).
- Most of `Rules/GrimdarkFuture/` (44 files) is unchanged between the editions: sequences, scheduler hooks, army-rule
  hooks, objectives, sight, containers, moves. Copying the folder would double every future fix to shared behaviour, which
  the owner's fix ruling makes frequent; a value in `GfEdition` is cleaner wherever 3.6.0 changes a number or a yes/no.
- Where a step's logic changes, the step is copied, not branched: `GfMoves.DangerousTerrain()` gets a 3.6 sequence
  (unit-level test, C4) beside the 3.5.1 one, chosen in `GrimdarkFutureRules.Sequences` by edition; the melee sequence list
  is built per edition (Impact before Counter, an optional Counter Ask); the casting eligibility in `GfCasting` gets a
  3.6 predicate. The 3.5.1 code paths are not edited.
- `GrimdarkFutureRules` keeps one class with two instances (`V351`, `V360`), each with its `Id`, `OptionCatalog` and
  sequence catalog; statics that read a number read it from `GfEdition.Of(state)`.
- Rule text the engine names (`GfRules.ActivationChoices` notes such as `ArtilleryNote`) comes from the edition.

### Terrain flags across editions

Built-in pieces and saved layouts carry `TerrainDeclarations`, shared by both editions. The editions read them:

| Flag | 3.5.1 reading | 3.6.0 reading |
| --- | --- | --- |
| `BlocksLineOfSight` | Blocking | Sight Blocking |
| `SeeInAndOut` | forest "see into and out of" (p.12) | Dense (p.12) |
| `DangerousOnRushCharge` | Dangerous for a Rush or Charge (p.12) | Unstable: Dangerous when a model moves over 6" into or through it |
| `Steep` (new flag) | Hills "Difficult when moving up" (p.12) | Steep: Difficult moving onto, open moving off |

The same pieces carry these meanings in both books (Ruins, deep Rivers, Hills), so no piece needs two declaration sets.
Labels in `TerrainLabel` and the viewer come from the game's edition. `Steep` is direction-dependent, so the Ask's
`MoveRegion` lists it apart from plain difficult volumes and `Tabletop.Geometry`'s movement reads it, on server and both
clients alike.

### How a game picks its edition

- Creation: the client's and viewer's create dialogs already list property sets. `GET /v1/properties` returns the newest
  set per id; each `PropertiesSummary` gains `Default` (true for `grimdark-future-3.6`), and both dialogs preselect it
  rather than relying on seed order. `CreateGameRequest` is unchanged: the hash names the edition.
- Lobby: the creator may change the set (both lobbies get the dialog's rule-set picker; `SetConfiguration` already carries
  `PropertiesHash`). On a change of `game_rules`, `GameSession.Lobby` re-resolves `Rules`, sends the new
  `OptionCatalog` to every client (a `Welcome.Options`-shaped message), drops option keys the new catalog refuses, and
  re-checks every picked army's edition (below). From `playing` on the edition is frozen with the rest of the
  configuration.
- Existing and unfinished games: their `game_rules` is `grimdark-future`, so they stay on 3.5.1 and keep following 3.5.1
  fixes. Nothing is migrated.
- Replay: a game's log replays against its hash; the hash's id is its edition. Ended games keep their hash as today.
- `GET /v1/games`' `GameSummary` gains `RuleSet` (the set's name) so the games list shows each game's edition.

### Army books per edition

- Dumps: `.superpowers/armyforge/3.5/` (today's dump, moved, never refetched: Army Forge no longer serves it) and
  `.superpowers/armyforge/3.6/`. `army-books fetch` writes only the edition Army Forge currently serves, read from the
  index's `versionString` (`3.6.0` → `3.6/`), and refuses to overwrite a dump of another edition. The 3.5 dump is backed up
  outside the working tree before the move; it cannot be fetched again.
- Generation: `army-books generate --edition 3.5|3.6` reads that dump and that edition's core and hand part, and writes
  that edition's content folder (`gameRules` from the edition, not hard-coded) and
  `server/management/Content/Armies/army-book-units.<edition>.json`. 3.5.1's generated output must come out byte-identical
  to today's (the generator test).
- The hand part for 3.6 (`army-rules.part.json`) starts as a copy of 3.5.1's, keeps the 113 rules whose text is
  unchanged, renames the same-effect renames (Anti-Regen, Anti-Cover, Bravery Aura, Crossing Strike, Piercing Charge,
  Counter in Melee, Unique Hero), and authors the changed and new ones (154 changed texts, 132 new names); the generator's
  report lists every name a 3.6 book uses that neither part covers.
- Management: `ArmyBookAdditions`/`ArmyBookReserves` read the edition's units file.

### Armies know their edition

- Migration: `army.edition text not null` (`'3.5'` | `'3.6'`, checked), existing rows `'3.5'`. Import stamps the edition
  Army Forge serves, which management reads from its newest units file's edition (today `'3.6'`). The TTS payload carries
  no edition, so the stamp is the import time's edition. Armies imported between Army Forge's switch (2026-10-29T23:48Z)
  and the migration are 3.6 lists marked 3.5; the migration marks them by their rule names (any name only 3.6 books use,
  e.g. Bravery Aura, Anti-Cover, Unique Hero, Armor).
- `ArmySnapshot` gains `Edition`; the game snapshot keeps it.
- Army pages (web, client army list) show the edition beside the game system.

### A 3.5 army in a 3.6 game, and the reverse

Recommendation: refuse, and offer re-import.

- `PickArmy` and `EndSetup` refuse an army whose edition differs from the game's (`ArmyLimits` gains the check; a refusal
  names both editions). The army pickers list only the game's edition, the rest greyed with the reason.
- Converting by ids is not reliable: 962 of 1,058 unit ids survive, but 913 of those change cost and 45% of upgrade-option
  ids are gone, so a converted army would carry wrong points and lose upgrades without saying which.
- A 3.5 army brought to a 3.6 game: "Re-import from Army Forge" re-reads the same list id (`army.source`) and Army Forge
  returns its 3.6.0 version; it becomes a new army stamped `'3.6'`, the 3.5 army kept.
- A 3.6 army brought to a 3.5 game: refused; Army Forge cannot produce a 3.5 list any more.
- Until 3.6.0 is playable, new imports are 3.6 lists that 3.5.1 games accept today with their new rule names inert. Step 2
  stamps them and shows the edition in the lobby; the refusal switches on with step 6.

### Sim rules suite

- `TestGame` gains `Edition` (default `grimdark-future`, so every existing game keeps running against 3.5.1 unchanged).
  `ScenarioCompiler` picks the core document by the game's edition; snapshots carry it.
- Coverage is per property set: a full run checks every catalog entry of each edition's shipped set, so 3.6.0's new and
  changed entries fail coverage until a 3.6 game covers them.
- Core and special games: games whose rules did not change run under both editions (`Editions: [both]`, run twice, report
  rows keyed by edition); games whose checks quote a changed rule (C1–C5, S1–S16) get a 3.6 copy with 3.6.0 quotes and
  pages, the 3.5.1 one untouched.
- Army and spell games: groups become `army:<edition>:<uid>` and `spells:<edition>:<uid>`; today's are the 3.5 ones. 3.6
  games are written against the 3.6 books (different unit ids, options and rules), batch by batch as the hand part grows.
- Lifecycle runs (`LifecycleGame.Lobby`) pick the set by id explicitly, once per edition.
- The viewer's Tests page shows the edition column and filters by it.

### Client and viewer

- No UI encodes an edition. What changes: the create dialogs' default and lobby picker; the games list's `RuleSet`; the
  lobby and in-game header show the set's name; option page strings come from the edition's catalog; terrain labels read
  the edition's words; `PlaybackView` shows the set name rather than the id.
- Rule text, activation notes and step labels already come from the server's property set and rules, so they follow the
  edition with no client change.

### The rulebook skill

One skill, two references: `.claude/skills/licensed/skills/grimdark-future-rules/` keeps its name, `SKILL.md` gains a
first step "Which edition: the game's `game_rules` (`grimdark-future` = 3.5.1, `grimdark-future-3.6` = 3.6.0)", and the
pages move to `v3.5.1/pages/p01–p71.md` and `v3.6.0/pages/p01–p74.md`, each with its own page index and special-rules
list (3.6.0: Assault, Cumbersome, Shred, Spell Amplifier added; terrain types on pp.11–12; modules renumbered). One skill
keeps one trigger and one method; two folders keep the page numbers apart. Citations: existing bare `p.N` means 3.5.1;
new citations name the edition (`3.6.0 p.9`).

## Build order

| # | Step | Size |
| --- | --- | --- |
| 1 | Back up the 3.5 dump outside the tree; move it to `.superpowers/armyforge/3.5/`; fetch 3.6.0 into `3.6/` | S |
| 2 | Armies: `army.edition` migration with the rule-name backfill, import stamping, `ArmySnapshot.Edition`, edition shown on army pages and in the lobby | S |
| 3 | Edition plumbing: id `grimdark-future-3.6`, registry, `GfEdition.Of(state)`, two `GrimdarkFutureRules` instances, per-edition content folders (3.5.1 hash unchanged), seeder/ArmyBooks/ScenarioCompiler by folder; 3.6 content a copy of 3.5.1 under the new id | M |
| 4 | Game choice: `PropertiesSummary.Default`, create-dialog default in client and viewer, lobby rule-set picker, session re-resolves rules and resends options, `GameSummary.RuleSet`, headers | M |
| 5 | Skill: `v3.5.1/` move, `v3.6.0/pages` transcribed (74 pages, deployment diagrams and mission cards), SKILL.md edition step and index | M |
| 6 | 3.6.0 core and special rules in the 3.6 set and `GfEdition`: C1, C2, C3, S1, S2, S3, S5, S6, S8, S11, S12, S13, S14, S15, S16 (S each); C4 dangerous test, C5 and the terrain flags with `Steep` in geometry, S4 casting, S9 Flying, S10 Hero aura (M each); M1 Side Battle (S). Armies of the other edition refused from here | L |
| 7 | Suite per edition: `TestGame.Edition`, coverage per set, run-twice for unchanged core/special games, 3.6 copies of the changed ones | M |
| 8 | 3.6 army books: generator `--edition`, generated part and books, the 3.6 hand part (renames, 154 changed, 132 new), units file per edition, `army:3.6:<uid>` and `spells:3.6:<uid>` suite games batch by batch | L |
| 9 | Docs: AGENTS.md and module AGENTS, design docs naming the edition, handoff; the 35 files listed in the modules diff | S |

Steps 1–2 are independent of the rest and stop the silent mixing of 3.6 lists into 3.5.1 games from growing unnoticed.
Steps 3–5 make an empty 3.6.0 (a 3.5.1 copy) selectable; 6–8 make it 3.6.0.
