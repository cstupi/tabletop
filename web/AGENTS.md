# web

The Svelte SPA for account, library and armies: SvelteKit 2 + Svelte 5, `adapter-static`, `ssr = false`, `prerender = false`. Talks only to the management API's `/v1`. Players use it to upload meshes and import armies for the Godot client.

## Layout

- `src/routes/` pages: `login`, `reset-password`, `account`, `library` (+ `[id]`), `armies` (+ `[id]`); `+layout.ts` sets `ssr`/`prerender`.
- `src/lib/api/` typed client: `client.ts` (`api`, one function per endpoint), `http.ts` (`http`, `ApiError`, `API_BASE`, `setTokenProvider`), `types.ts` (DTOs).
- `src/lib/session/` `session.svelte.ts` (token and account in `$state`, persisted under `tabletop.session`), `jwt.ts` (expiry and refresh delay), `messages.ts`.
- `src/lib/mesh/` STL to GLB: `stl.ts` (parse, `mergeSoups`), `weld.ts`, `decimate.ts` (`BUDGETS`), `normals.ts` (creased), `glb.ts`, `supports.ts`, `bounds.ts` (`terrainVolume`), `guess.ts`, `three-frame.ts` (`eulerFor`), `convert.ts` (`convert`, `convertInWorker`, `describeGlb`), `convert.worker.ts`.
- `src/lib/units/` `units.ts` (`mmToInches`, `inchesToMm`), `bases.ts` (`BASE_PRESETS`), `volumes.ts` (six declarations, `defaultVolume`).
- `src/lib/upload/` `intake.ts` (`classifyFiles`), `sha256.ts`, `upload.ts` (presigned sequence).
- `src/lib/library/` `folders.ts`, `volume-edit.ts` (`toDraft`, `fromDraft`).
- `src/lib/armies/` `import.ts` (`normaliseSource`), `patches.ts` (`PatchArmyRequest` builders).
- `src/lib/components/` `MeshPreview` (three.js), `UploadDialog`, `VolumeEditor`, `BasePicker`, `AssetPicker`, `FolderTree`.
- `tests/` Vitest; `tests/fixtures/` STL files and `make-fixtures.ts`.

## How it works

- Upload: `classifyFiles` sorts the drop into STLs, one GLB or one image. `UploadDialog` runs `convertInWorker` (a module Web Worker over `convert`).
- `convert` order: parse, merge, weld, recentre on the footprint, decimate to a triangle budget, creased normals, export GLB, then analysis (bounds, supports, `Proposal`, terrain `Volume`).
- `guess.ts` proposes millimetres (`scale = mmToInches(1)`) and an up axis: `y` when the model is tallest along Y, else `z`. Y-up gets `pitch: 90`.
- `upload.ts` stages: `hash`, `create`, `put` (presigned), `confirm`, `volumes`. An existing hash returns `deduplicated`.
- Orientation is `{ pitch, yaw, roll }` in degrees, stored on the asset with the scale; `eulerFor` maps it to a three.js `ZYX` Euler.
- `http.ts` sends requests to `API_BASE + path`; `API_BASE` is `PUBLIC_API_BASE` or empty, so dev is same-origin through the Vite proxy.
- `session.svelte.ts` refreshes the JWT before expiry through `/v1/auth/session/refresh`.

## Commands

- `npm install` once in `web/`; Node 24 comes from mise in WSL.
- `./scripts/web`: dev server on `http://localhost:5173`, `/v1` proxied to management on `:5100` (needs management running).
- `cd web && npm test`: Vitest (`tests/**/*.test.ts`, node environment, 60 s timeout).
- `cd web && npm run check`: `svelte-kit sync` and `svelte-check`.
- `cd web && npm run build`: static output to `web/build`, SPA fallback `index.html`.
- `cd web && npm run fixtures`: regenerates the STL fixtures.

## Conventions and pitfalls

- The API speaks inches. Millimetres exist only in the UI; convert at the edge with `src/lib/units/`.
- The client calls `/v1/auth`, `/v1/account`, `/v1/assets`, `/v1/folders`, `/v1/armies`. It never calls `/v1/games*`, `/v1/properties` or `/v1/layouts`.
- `npm` with exact versions: `.npmrc` sets `save-exact=true` and `engine-strict=true`; `package.json` pins every dependency.
- The mesh pipeline is headless: no DOM in `src/lib/mesh/` outside `convertInWorker` and the worker, so Vitest runs it against `tests/fixtures`.
- `vite.config.ts` excludes `meshoptimizer` from `optimizeDeps` and sets `worker.format: 'es'`.
- Vite exposes only `VITE_*` and `PUBLIC_*` on `import.meta.env` (`envPrefix`); `PUBLIC_API_BASE` is read there, not through `$env`.
- `session.svelte.ts` wraps `localStorage` in try/catch; a blocked store leaves the session in memory.
- Table frame: Z up, origin at the corner; mesh space is converted by `Proposal.orientation`, not by the page.

## Docs

- `docs/design/web-app/design.md`
- `docs/superpowers/plans/2026-09-05-web-app/README.md` (tasks 01 to 17)
- `docs/superpowers/specs/2026-08-29-web-app-design.md`
- `docs/superpowers/specs/2026-09-04-mesh-conversion-design.md`
- `docs/superpowers/specs/2026-10-01-starter-meshes-design.md`
