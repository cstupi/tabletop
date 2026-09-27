# Web App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `web/` — the Svelte SPA that owns preparation: login by password or email code against the management API, the account page and total deletion, armies imported from Army Forge and edited per unit and per slot, the asset library with nested folders, and the upload flow that converts STL to GLB in the browser, previews the result on a ring of the modelled base, and uploads by presigned URL.

**Architecture:** SvelteKit 2 on Svelte 5, built by `@sveltejs/adapter-static` as a pure SPA (`ssr = false`, `index.html` fallback) so `/reset-password?token=…` and `/armies/[id]` are real paths without a server. Every HTTP call goes through one typed client (`src/lib/api/`) whose request and response shapes are the management API plan's DTOs verbatim in camelCase. The mesh pipeline (`src/lib/mesh/`) is plain TypeScript over typed arrays — STL parse, merge, weld, decimate with the `meshoptimizer` WebAssembly simplifier, creased normals, GLB export with `@gltf-transform/core` — and runs headless under Vitest as well as inside a Web Worker in the page. three.js is used only to draw the preview and the top-down volume editor. Pages are thin over tested modules.

**Tech Stack:** Node 24 (`~/.local/share/mise`), npm; `svelte` 5.57.0, `@sveltejs/kit` 2.70.3, `@sveltejs/adapter-static` 3.0.10, `@sveltejs/vite-plugin-svelte` 7.3.0, `vite` 8.2.2, `vitest` 5.0.0, `typescript` 5.9.3, `svelte-check` 4.7.6; `meshoptimizer` 1.2.0 (WASM simplifier), `@gltf-transform/core` 4.5.0 (GLB read and write), `three` 0.185.1 + `@types/three` 0.185.4 (preview only).

**Spec:** `docs/superpowers/specs/2026-08-29-web-app-design.md` (primary); `2026-09-04-mesh-conversion-design.md`; `2026-08-28-army-design.md`; `2026-08-28-asset-library-design.md`; `2026-08-29-accounts-design.md`; `2026-08-29-management-api-design.md`; `2026-08-29-geometry-values-design.md`; `2026-08-29-content-values-design.md`; `2026-09-04-whole-design-review.md` (wins where the others disagree); `docs/architecture.md` → Web frontend, Content and assets. Plans obeyed: `docs/superpowers/plans/2026-09-05-management-api/README.md` (its HTTP surface and DTO names are used verbatim), `2026-09-05-contracts-and-local-dev.md` (the `Tabletop.Content` army and `Tabletop.Geometry` value shapes the API serves as JSON), `2026-09-05-geometry.md` (the table frame: Z up, yaw 0 faces +Y).

**Requires landed:** the contracts plan and the management plan (every `/v1` route and DTO this SPA consumes).

## Global Constraints

- The web app owns preparation only: armies, mesh binding, base shape and height, assets, folders, the account, login. **No game concepts**: it never calls `/v1/games*`, `/v1/properties` or `/v1/layouts` (web app → The line is preparation versus play).
- Every request goes to the management API's `/v1` surface exactly as `2026-09-05-management-api/README.md` → *Interfaces → Produces* lists it; JSON is camelCase with snake_case string enums and nulls omitted (`ContentJson.Configure`); errors are `application/problem+json`; pages are `Page<T>(items, next)` with `?limit=&after=`; another owner's row is `404`. No `Tabletop-Client-Version` header is sent: the version check exists because the Godot client "is a downloaded desktop application and a user can be months behind" (management API → The version check) and an SPA fetched on every load cannot be, and management accepts a missing header (management plan).
- Auth is the Stytch session JWT management returns; the web app stores it, refreshes it with `POST /v1/auth/session/refresh` once a minute (management API → "a JWT good for five minutes, refreshed every minute"), sends it as `Authorization: Bearer`, and holds no Stytch identifier or secret (accounts → Auth is a module, not a vendor).
- Login is email + password or email one-time code; no social login, no magic link (`2026-09-04-client-shell-design.md` → No social login; web app → Login is the web app's). The `/reset-password` page reads `?token=` from Stytch's redirect and calls `POST /v1/auth/password/reset/complete` (management plan → Interfaces needed → Web-app plan).
- Units: the API speaks world units (inches); millimetres exist only in the UI — presets and the STL unit guess — and convert at the edge with `/ 25.4` (geometry values → Bases: "Millimetres never enter the model").
- Frame: Z is up, yaw 0 faces +Y, positive yaw is counter-clockwise from above, an oval's `a` (length) lies along the facing axis (geometry plan → Coordinate frame). glTF is Y-up, so the preview and the volume editor rotate the world, never the data.
- Mesh conversion runs in the browser and nothing uploads before the user has seen the preview; budgets 15,000 triangles (miniature) and 50,000 (terrain); supports are warned about and never removed; the converter proposes scale, orientation and modelled base and the user confirms (mesh conversion → The flow, What conversion does, What it writes).
- Upload sequence, exactly: SHA-256 in the browser → `POST /v1/assets` → if `uploadUrl` is present, `PUT` the bytes to it with the declared `Content-Type` → `POST /v1/assets/{id}/confirm` → show `warning` → for terrain, `PUT /v1/assets/{id}/volumes` with the bounding-box volume (management plan Tasks 6–7; mesh conversion → What it does not do).
- GLB is the only mesh format uploaded (`model/gltf-binary`); textures are `image/png`, `image/jpeg` or `image/webp`; STL is converted first and never sent (management API → GLB only).
- A changed list means a re-import: no reconciliation, per-slot bindings are lost and the UI says so (the army → A changed list means a re-import).
- TypeScript strict; Vitest for every module with logic (`src/lib/**`); Svelte pages are tested by running the app. Commit after every task (imperative one-line message, `Co-Authored-By` trailer naming the model).
- `web/` is a deliverable and contains only the SPA; nothing under `web/` is referenced by `Tabletop.sln`.

---

## File Structure

```
web/
  package.json, package-lock.json, svelte.config.js, vite.config.ts, tsconfig.json, .gitignore, .npmrc
  src/app.html, src/app.d.ts, src/app.css
  src/routes/+layout.ts                     ssr = false, prerender = false
  src/routes/+layout.svelte                 nav, session guard, refresh loop
  src/routes/+page.ts                       redirect / → /armies
  src/routes/login/+page.svelte             password and email-code forms, signup, forgot password
  src/routes/reset-password/+page.svelte    ?token= → new password
  src/routes/account/+page.svelte           rename, log out, delete account
  src/routes/armies/+page.svelte            list, import, delete
  src/routes/armies/[id]/+page.svelte       the army editor
  src/routes/library/+page.svelte           folders, asset grid, upload button
  src/routes/library/[id]/+page.svelte      asset detail, folders membership, volume editor
  src/lib/api/types.ts                      the DTOs, verbatim from the management plan
  src/lib/api/http.ts                       fetch wrapper: bearer, problem+json, Page<T>
  src/lib/api/client.ts                     one function per endpoint
  src/lib/session/jwt.ts                    decode exp, refresh scheduling (pure)
  src/lib/session/session.svelte.ts         the store: token, account, login/logout/refresh
  src/lib/session/messages.ts               login error wording by status
  src/lib/units/units.ts                    mm ↔ inch
  src/lib/units/bases.ts                    base presets, Base helpers
  src/lib/units/volumes.ts                  Volume helpers, declarations flags wire format
  src/lib/mesh/types.ts                     IndexedMesh, ConversionResult, Proposal
  src/lib/mesh/stl.ts                       parseStl (ASCII and binary), mergeMeshes
  src/lib/mesh/weld.ts                      weld positions into an indexed mesh
  src/lib/mesh/decimate.ts                  meshoptimizer simplifier to a triangle budget
  src/lib/mesh/normals.ts                   creased vertex normals with an angle threshold
  src/lib/mesh/glb.ts                       exportGlb, readGlb (positions + triangle count)
  src/lib/mesh/guess.ts                     units, up-axis, proposed scale and orientation
  src/lib/mesh/supports.ts                  pre-supported detection
  src/lib/mesh/bounds.ts                    bounding-box volume for terrain
  src/lib/mesh/three-frame.ts               Orientation → three.js Euler
  src/lib/mesh/convert.ts                   the pipeline: files → ConversionResult
  src/lib/mesh/convert.worker.ts            runs convert() off the main thread
  src/lib/upload/sha256.ts                  SubtleCrypto hash
  src/lib/upload/upload.ts                  create → PUT → confirm → volumes
  src/lib/upload/intake.ts                  which files were dropped: STLs, one GLB, one image
  src/lib/library/folders.ts                folder tree, subtree membership, grid filters
  src/lib/library/volume-edit.ts            VolumeDraft ↔ Volume, validation, pointer → table
  src/lib/armies/import.ts                  source normalisation, re-import
  src/lib/armies/patches.ts                 PATCH shapes for each edit, slot summary
  src/lib/components/MeshPreview.svelte     three.js viewer: mesh, base ring, facing arrow, top-down mode
  src/lib/components/UploadDialog.svelte    drop → convert → preview → confirm
  src/lib/components/BasePicker.svelte      preset dropdown + custom dimensions
  src/lib/components/AssetPicker.svelte     choose a library miniature for a unit or slot
  src/lib/components/FolderTree.svelte
  src/lib/components/VolumeEditor.svelte    footprint and two heights over the top-down preview
  tests/fixtures/make-fixtures.ts           writes the four synthetic STLs
  tests/fixtures/*.stl                      generated; real STLs may be dropped in tests/fixtures/real/
  tests/*.test.ts                           Vitest, Node environment
scripts/web                                 runs the dev server from WSL on 0.0.0.0:5173
CLAUDE.md                                   modified: commands, layout
```

Modules are split by responsibility: `api/` knows HTTP and nothing about meshes; `mesh/` knows typed arrays and nothing about HTTP or the DOM; `upload/` sequences the two; components render. Each `mesh/` file is one step of the pipeline so a step can be replaced (a different simplifier, a different exporter) without touching its neighbours.

## Tasks

One file per task, in plan order; each ends green and committed.

- [Task 1: Project scaffold — SvelteKit as a static SPA, Vitest, the dev proxy](task-01-project-scaffold-sveltekit-static.md)
- [Task 2: The API types and the HTTP client](task-02-api-types-http-client.md)
- [Task 3: The session — token storage, refresh, logout](task-03-session-token-storage-refresh.md)
- [Task 4: Login, the reset-password page, and the signed-in shell](task-04-login-reset-password-page.md)
- [Task 5: The account page — rename and total deletion](task-05-account-page-rename-total.md)
- [Task 6: Units, base presets and the volume wire helpers](task-06-units-base-presets-volume.md)
- [Task 7: Mesh pipeline I — STL parsing, merging, welding, and the test fixtures](task-07-mesh-pipeline-stl-parsing.md)
- [Task 8: Mesh pipeline II — decimation, creased normals, GLB out and in](task-08-mesh-pipeline-decimation-creased.md)
- [Task 9: Mesh pipeline III — the guesses, the support warning, the bounding-box volume, and `convert()`](task-09-mesh-pipeline-guesses-support.md)
- [Task 10: The upload sequence — hash, create, PUT, confirm, volumes](task-10-upload-sequence-hash-create.md)
- [Task 11: The preview and the base picker](task-11-preview-base-picker.md)
- [Task 12: The upload dialog — drop, convert, preview, confirm](task-12-upload-dialog-drop-convert.md)
- [Task 13: The library — folders, the asset grid, the asset page](task-13-library-folders-asset-grid.md)
- [Task 14: The terrain volume editor — a footprint and two heights over a top-down preview](task-14-terrain-volume-editor-footprint.md)
- [Task 15: Armies — the list, import from Army Forge, re-import, delete](task-15-armies-list-import-army.md)
- [Task 16: The army editor — unit default mesh, per-slot mesh, base and height](task-16-army-editor-unit-default.md)
- [Task 17: Run script, CLAUDE.md, and the whole-tree check](task-17-run-script-claude-md.md)

## Interfaces → Produces, whole project

Paths under `web/src/lib/`. Nothing here is consumed by a .NET project; the client shell plan may copy the login and refresh behaviour, not the code.

| Module | Members |
| --- | --- |
| `api/types.ts` | `Guid`, `Page<T>`, `Problem`; `Base`, `BaseShape`, `Orientation`, `Point2`, `Yaw`, `Footprint`, `FootprintKind`, `Volume`, `VolumeKind`, `Declaration`; `PropertyInstance`, `Weapon`, `ModelSlot`, `Unit`, `Army`, `TerrainVolume`; `AccountRow`, `SessionResponse`, `OtpSentResponse`; `AssetKind`, `ContentType`, `AssetDto`, `CreateAssetRequest`, `CreateAssetResponse`, `ConfirmResponse`, `PatchAssetRequest`, `DownloadResponse`; `FolderDto`, `FolderRequest`; `ArmySummary`, `ImportRequest`, `UnitPatch`, `SlotPatch`, `PatchArmyRequest` |
| `api/http.ts` | `API_BASE`, `ApiError(status, title, detail?)`, `setTokenProvider(fn)`, `http.{get,post,put,patch,del}<T>`, `http.onUnauthorized`, `query(params)` |
| `api/client.ts` | `api.auth.{sendOtp, verifyOtp, password, signup, startReset, completeReset, refresh, logout}`, `api.account.{get, rename, delete}`, `api.assets.{list, create, get, patch, delete, confirm, download, getVolumes, putVolumes}`, `api.folders.{list, create, patch, delete, addAsset, removeAsset}`, `api.armies.{import, list, get, patch, delete}` |
| `session/jwt.ts`, `session/session.svelte.ts`, `session/messages.ts` | `decodeExp`, `refreshDelayMs`; `session.{token, account, ready, accept, clear, restore, refresh, logout, startRefreshLoop}`, `STORAGE_KEY`; `loginErrorMessage` |
| `units/units.ts`, `units/bases.ts`, `units/volumes.ts` | `MM_PER_INCH`, `mmToInches`, `inchesToMm`, `round`; `BASE_PRESETS`, `BasePreset`, `circle`, `oval`, `rect`, `presetFor`, `baseLabel`; `DECLARATIONS`, `DECLARATION_LABELS`, `parseDeclarations`, `formatDeclarations`, `defaultVolume` |
| `mesh/types.ts`, `mesh/stl.ts`, `mesh/weld.ts` | `Vec3`, `IndexedMesh`, `Bounds`, `triangleCount`, `bounds`; `parseStl`, `mergeSoups`, `StlError`; `weld` |
| `mesh/decimate.ts`, `mesh/normals.ts`, `mesh/glb.ts` | `BUDGETS`, `decimate`, `compact`; `creasedNormals(mesh, angleDegrees = 60)`; `exportGlb(mesh, name)`, `readGlb(bytes)` |
| `mesh/guess.ts`, `mesh/supports.ts`, `mesh/bounds.ts`, `mesh/three-frame.ts` | `UpAxis`, `Proposal`, `guessUpAxis`, `propose`, `toWorld`, `worldExtent`, `recentre`; `SupportReport`, `detectSupports`; `terrainVolume`; `eulerFor` |
| `mesh/convert.ts`, `mesh/convert.worker.ts` | `ConversionInput`, `ConversionResult`, `Progress`, `convert`, `describeGlb`, `convertInWorker` |
| `upload/sha256.ts`, `upload/upload.ts`, `upload/intake.ts` | `sha256Hex`; `UploadStage`, `UploadPlan`, `UploadOutcome`, `UploadError`, `upload`; `Intake`, `classifyFiles` |
| `library/folders.ts`, `library/volume-edit.ts` | `FolderNode`, `buildTree`, `descendantIds`, `filterAssets`; `VolumeDraft`, `toDraft`, `fromDraft`, `validate`, `screenToTable` |
| `armies/import.ts`, `armies/patches.ts` | `normaliseSource`, `importErrorMessage`, `reimport`; `unitDefault`, `slotMesh`, `slotBase`, `slotHeight`, `rename`, `slotSummary` |
| `components/` | `MeshPreview` (+ `Measure`), `BasePicker`, `UploadDialog`, `FolderTree`, `VolumeEditor`, `AssetPicker` |
| Routes | `/login`, `/reset-password`, `/account`, `/armies`, `/armies/[id]`, `/library?folder=`, `/library/[id]` |
| Scripts | `./scripts/web`; `web/`: `npm run dev|build|preview|check|test|fixtures` |

Conventions later work relies on: the `Orientation` convention (pitch about table X, then roll about Y, then yaw about Z, degrees, right-handed; a Y-up mesh stands with `pitch = 90`); the converter emits the mesh in its own units and axes and writes scale and orientation to the asset row rather than baking them in; the converter centres the mesh on its footprint with its lowest point at the origin; the `[Flags]` wire format `"none"` / `"impassable, cover"`.

---

## Interfaces needed from other modules

- **Management API plan** — `AddCors` for `TABLETOP_WEB_URL` (origin `http://localhost:5173`), allowing `Authorization` and `Content-Type` headers and `GET, POST, PUT, PATCH, DELETE`: **provided** by management plan Task 3, with `Tabletop-Client-Version` also allowed and `UseCors` ahead of the version middleware so preflights are answered. Confirmed consumed as specified, not changed: every route in its *HTTP surface*; `Page<T>` as `{ items, next }`; `TerrainDeclarations` serialised by `JsonStringEnumConverter(SnakeCaseLower)` as `"none"` or a `", "`-joined list; `Base`, `Orientation`, `Volume`, `Army` shapes from `ContentJson`. **Renaming a unit or a model slot is not provided**: the army spec gives the list "composition and loadout" and the user "everything about appearance and physical presence"; a unit's or slot's name is the list's, so `UnitPatch` and `SlotPatch` have no `Name?` and the editor shows those names read-only (Task 16). Renaming would be `Name?` on both records and one `update` each in `ArmyRepository.Patch`.
- **MinIO (contracts plan Task 15)** — the presigned `PUT` from the browser is cross-origin to `http://localhost:9000`; MinIO's default `MINIO_API_CORS_ALLOW_ORIGIN=*` answers it. If that default is ever narrowed, `TABLETOP_WEB_URL` must be in the list.
- **Stytch dashboard** — `http://localhost:5173/reset-password` registered as a password-reset redirect URL (recorded by the management plan).
- **Client shell plan** — nothing consumed; it may reuse the login and refresh behaviour described in Tasks 3–4 (`/session/refresh` once a minute, sign out on `401`).

## Rulings

1. **The browser keeps the session JWT in `localStorage`**, under `tabletop.session`, so a reload and a new tab stay signed in for the Stytch session's 30 days: a thirty-day session means nothing if closing a tab ends it, which is what `sessionStorage` would do ([open questions design](../specs/2026-09-09-open-questions-design.md)). Revocation is "a matter of minutes rather than instant" either way (accounts). Task 3.

## Questions answered from the documents, and what they settled

Six points the documents settle; each is built into the tasks above.

- **The `Orientation` convention.** Fixed by the contracts plan: `Orientation(Pitch, Yaw, Roll)` in degrees, "raw mesh coordinates read as table coordinates, rotated Pitch about X, then Roll about Y, then Yaw about Z (right-handed, extrinsic), then scaled; a Y-up glTF stands at Pitch = 90 — the web app writes it, the client's `MeshLoader.Orient` applies it". `toWorld`, `eulerFor` and `propose` implement exactly that. Geometry values → *Asset scale and orientation* states the same convention.
- **Baking scale and orientation into the GLB.** No. Mesh conversion → *What it writes*: "the converter proposes, the user confirms in the preview, and **the fields hold the confirmed value**", and "a GLB dropped straight in gets no proposal and the user sets both" — fields that were always identity would hold nothing to confirm and would make a dropped GLB behave differently from a converted one. The GLB keeps the file's units and axes; the asset row carries scale and orientation.
- **Centring the mesh.** Yes: mesh conversion → *The flow* has the mesh **standing on a ring** of the modelled base, and the table domain model has the base as the hitbox the mesh only decorates. A mesh exported off-origin cannot stand on its base either in the preview or in play, so the footprint is centred on the origin with the lowest point at zero (mesh conversion → *What conversion does*, *Centre*).
- **`Tabletop-Client-Version` from the web app.** No. The management API spec puts the version check there "because the Godot client is a downloaded desktop application and a user can be months behind"; the SPA is fetched from the server on every load and cannot be behind. Management accepts a missing header (management plan), so no header is sent and `426` cannot reach this app.
- **Conversion in a Web Worker.** Yes: mesh conversion leaves "which parser, simplifier and exporter" to implementation and asks only that the pipeline run headless. `convert()` is the pipeline and knows nothing of the DOM; `convertInWorker` is a wrapper so a two-million-triangle STL does not freeze the page. Recorded in Task 9.
- **Paging the library grid.** Load-all, 200 per request, with folders and the base filter client-side. The asset library names the problem — "a user with five hundred meshes finding one, together with the modelled base as a filter" — and folders are membership lists, so filtering needs the whole set. The keyset pager stays available if a library ever outgrows this (Task 13).
