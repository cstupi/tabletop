# Developer tooling and test defaults: 3.6 first

Owner, 2026-11-07: *"We want future changes to be ready for 3.6."* Grimdark Future v3.6.0 (`RuleSets.Gf360`, armies of edition
`'3.6'`) is the edition new work targets; 3.5.1 stays supported (unfinished games keep it, the rules suite plays both). Production
already defaults to 3.6.0 (`RuleSets.Default`, `ArmyEditions.Served`); nothing here changes a server, a game or a recorded fixture.

## Tasks

1. **Scenarios.** `sim/Scenarios/Scenario.cs` `Rules` and `ScenarioBuilder._rules` default to `RuleSets.Gf360`, so `sim skirmish`,
   `objectives` and every `ScenarioCatalog` scenario play 3.6.0 with 3.6 army snapshots. `army-rules` fields 3.6.0 Grounded Defense
   in place of 3.5.1's Grounded Reinforcement (the near-terrain bonus; 3.6 has no Grounded Reinforcement). `casting` keeps Human
   Defense Force (`z65fgu0l29i4lnlu`), whose 3.6 book has the same six spells.
2. **Rules suite unchanged.** `TestGame.Edition` defaults to `RuleSets.Gf351` itself instead of reading `Scenario.Rules`; a 3.6 game
   names its edition (`.In36()`, `.BothEditions()`, `Army36` `Kept`, `SpellGames.Game` sets `Edition = set.GameRules`). Check:
   `sim suite --list` is identical before and after.
3. **Lifecycle.** `LifecycleShape.Rules` defaults to `Gf360`; `two-players-3.6` becomes `two-players` (3.6) and `two-players-3.5` is
   added (`Gf351`, 3.5 lists), the one 3.5.1 shape. `sim/Lifecycle/Armies/3.6/` holds all four books, three units each at their
   default loadout, cut from `.superpowers/armyforge/3.6/books/` (book 0 Daemons of War: Blood Warriors, Blood Hounds, Blood Beast
   Riders). `LifecycleArmy.List` defaults to edition 3.6; `AmbushUnit(edition)` names book 0's riders per edition.
   `LifecycleScenario.From`'s `rules` defaults to `Gf360`. `LifecycleTests` (E2E) is a theory over `LifecycleShapes.All`, so it
   follows the shapes.
4. **Test infrastructure.** Sim tests that serve the game over the socket seed every shipped core set (`RuleSets.All`), in memory and
   in Postgres (`SocketRowsContract`). Tests that pinned the 3.5.1 default (`ScenarioTests`, `ScenarioCompilerTests`, `ProgramTests`,
   `LifecycleArmyTests`, `LifecycleRunnerTests`, `LifecycleAmbushTests`) assert 3.6 by default and 3.5.1 when named.
5. **Viewer e2e harness.** `viewer/tests/e2e/harness.ts` imports `sim/Lifecycle/Armies/3.6/<book>.json` with `edition: '3.6'` and
   creates games on the default set (`defaultSet`), as does `setup.e2e.test.ts`. Before, it imported the 3.5 cuts unstamped (stamped
   `'3.6'` by the server) onto the first listed set, and `ambush.e2e`'s one-account case failed at `pick_army`. `ambush.e2e.test.ts`:
   the refusal match drops 3.5.1's `(p.13)`, and the one-account case waits for team 1's open `deploy` Ask before Set aside (a
   roll-off answer leaves team 1 acting for a moment before the deployment order arrives).
6. **Army-book generator.** `./scripts/army-books generate` defaults to `--edition 3.6`.
7. **Docs.** Root `AGENTS.md` (Rules for every module): "New work targets Grimdark Future v3.6.0: build, test and check it on 3.6
   games with 3.6 armies first; 3.5.1 stays supported." `sim/AGENTS.md`, `viewer/AGENTS.md`, `scripts/ArmyBooks/AGENTS.md`,
   `server/game/AGENTS.md`, `docs/design/simulation/design.md`.

Not changed: `InMemorySeeder.SeedPropertiesAsync()` and the game server's rules tests (each pins its edition), `client/src/PerfProbe.cs`
(enters an existing game; no edition), `viewer/tests/fixtures/*` (recorded games, not regenerated).

## Validation

- `dotnet build`
- `dotnet test` (the whole unit suite)
- `dotnet run --project sim -- suite`: PASSED
- Each catalog scenario in process: `--seeds 1..10` (`objectives` `--seeds 1..20`)
- `dotnet run --project sim -- lifecycle` against the compose stack: every shape passed
- `cd viewer && npm test`; `npm run test:e2e` against the stack

### Results

2026-11-07, on this branch:

- `dotnet build`: 0 errors.
- `dotnet test`: every project passed (GameServer 1832, Client 956, Sim 348, Management 337, Geometry 303, TerrainMeshes 114,
  Content 71, Sounds 65, Protocol 65, ArmyBooks 60, Geometry.Wasm 44, TableTextures 29, Viewer 16).
- `sim suite` (in process): PASSED, 685 of 685 games, 1122 of 1122 entries covered; `suite --list` identical to before the change.
- Catalog scenarios on 3.6.0, in process: `army-rules`, `casting`, `final-round`, `final-round-silent`, `final-round-warn`,
  `free-for-all`, `player-turn`, `skirmish`, `stalemate`, `terrain` 10 of 10 each (`--seeds 1..10`); `objectives` 20 of 20
  (`--seeds 1..20`). None needed 3.5.1.
- `sim lifecycle` against the compose stack (management through the :5173 proxy): 8 of 8 shapes passed (`two-players`,
  `two-players-3.5`, `self-play`, `team-game`, `late-joiner`, `ambusher`, `four-teams`, `leave-and-delete`).
- E2E `LifecycleTests` and `PostgresSocketRowsTests` (`--settings tests/e2e.runsettings`): 15 of 15 passed.
- `cd viewer && npm test`: 1690 passed, 6 skipped. `npm run test:e2e` against the stack: 6 of 6, three runs in a row.
