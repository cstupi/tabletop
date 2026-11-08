# Mesh picker

Spec: `docs/superpowers/specs/2026-11-07-mesh-picker-design.md`.

`GET /v1/assets` pages by keyset (at most 200 a page) and the army page already reads every miniature once, so search and filters
stay in the browser. The one server change is `GET /v1/armies/meshes`: the cross-army suggestions would otherwise need one
`GET /v1/armies/{id}` per army (up to 200).

## Tasks

1. Management, tests first: `ArmyMeshUse(UnitName, ModelName, AssetId, Slots)`; `IArmyRepository.MeshUses(ownerId)` (owner's slots
   with a mesh, grouped, most slots first); `ArmyRepository` (one grouped query), `InMemoryArmyRepository`; contract test in
   `RepositoryContract.Content.cs`; `GET /v1/armies/meshes` with an endpoint test in `ArmyTests.cs` (owner only). `server/management/AGENTS.md`,
   `docs/design/management-api/design.md` endpoint table.
2. `web/src/lib/meshpicker/search.ts` with `tests/meshpicker-search.test.ts`: word splitting (camelCase, digits, `_`, `-`), stemming,
   word scores (exact, prefix, inside, edits), folder words, AND over query words, ordering; a timing test over 1000 assets.
3. `suggest.ts` with tests: this unit's other slots, cross-army uses, name words, the current mesh left out, limits.
4. `scope.ts` with tests: scopes for a single slot, a type in a unit, the shared row, deduplication, default, the patch (one
   `SlotPatch` per slot, `clearAsset` for none), the change line.
5. `nav.ts` with tests: arrows across the suggested row and the grid, clamping, PageUp/PageDown, Home/End.
6. `memory.ts` with tests: recent list (dedupe, cap 30, per account), last folder (deleted folder → All), blocked storage.
7. `MeshPicker.svelte` (dialog: search, chips, tree or folder chips at 390, suggested row, virtual grid, scope, keyboard) and
   `MeshButton.svelte`; the army page uses them; the page's Mesh folder select, `AssetPicker.svelte`, `lib/armies/folder.ts` and its
   test go. Icons `search`, `recent`.
8. `web/AGENTS.md`, `docs/design/web-app/design.md`, the UX redesign part 3 Army page section to the current behaviour.

## Validation

| Check | Command | Pass |
| --- | --- | --- |
| Build | `dotnet build` | 0 errors |
| Management unit | `dotnet test tests/Tabletop.Management.Tests` | all pass |
| Postgres contract | `dotnet test tests/Tabletop.Management.Tests --settings tests/e2e.runsettings --filter "FullyQualifiedName~PostgresRepositoryContract"` | all pass |
| Web | `cd web && npm test && npm run check` | all pass, 0 errors 0 warnings |
| Timing | the search timing test, and in the browser a keystroke to a drawn grid over 1000 assets | search < 5 ms; keystroke < 50 ms |
| Live | own management from the worktree on a free port, own web dev server proxying `/v1` to it; dev session; 300+ seeded miniatures with thumbnails in folders; an imported army | each screen below works |
| Screens | Playwright at 1280, 1920 and 390: picker open with suggestions; a search narrowing hundreds to a few; folder browsing; the scope choice; the army page after a pick | matches the spec's sketches; no horizontal scroll at 390 |
| Clean-up | delete the seeded assets, folders and army | the account's library as before |

### Results (2026-11-07)

- `dotnet build`: 0 errors. `dotnet test tests/Tabletop.Management.Tests`: 356 passed. Postgres contract (filter above): 65 passed.
- `cd web && npm test`: 31 files, 168 passed, 1 skipped (`tests/meshpicker.test.ts`: 22). `npm run check`: 0 errors, 0 warnings.
- Timing: `search` over 1000 assets, 1.0–1.6 ms a query in Vitest, 0.42–0.65 ms in Chromium (index build 4.4–6.9 ms). In the page
  over 388 assets, a keystroke to the updated grid took 2–21 ms at 1280 and 390 and up to 40 ms at 1920 (headless, software
  raster; the following painted frame adds the headless renderer's frame time).
- Live: own management on :5191 and web on :5195; a dev account with 382 seeded miniatures (thumbnails from the starter meshes'
  renders) in seven factions of nested folders, a Shared folder and a copies folder, and two imported orc armies, the second with
  meshes. `GET /v1/armies/meshes` grouped `Orc Boyz 1`…`10` as one model name. Picks over the army scope set 20 slots in two
  units in one `PATCH`; Enter picked the highlighted tile after arrows. The account was deleted afterwards.
- Screenshots at 1280, 1920 (Fantasy) and 390: `meshpicker-shots/` in the session scratchpad (army before, picker with
  Suggested, search, folder, folder search with **Search all**, scope, army after). No horizontal scroll at any width.
