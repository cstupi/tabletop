# web

The Svelte SPA for account, library and armies: SvelteKit 2 + Svelte 5, `adapter-static`, `ssr = false`, `prerender = false`. Talks only to the management API's `/v1`. Players use it to upload meshes and import armies for the Godot client.

## Layout

- `src/routes/` pages: `login`, `reset-password`, `account`, `library` (+ `[id]`), `armies` (+ `[id]`), `download`; `+layout.ts` sets `ssr`/`prerender`.
- `src/lib/api/` typed client: `client.ts` (`api`, one function per endpoint), `http.ts` (`http`, `ApiError` with `retryAfter`, `API_BASE`, `setTokenProvider`), `types.ts` (DTOs), `errors.ts` (`errorMessage(e, context)`: every failure as one sentence).
- `src/lib/session/` `session.svelte.ts` (token and account in `$state`, persisted under `tabletop.session`), `jwt.ts` (expiry, refresh delay, `decodeRoles`, `isAdmin`), `messages.ts` (`loginErrorMessage`), `redirect.ts` (`safeNext`, `loginHref`).
- `src/lib/replays.ts` `REPLAYS_URL`: the admin Replays link, `PUBLIC_VIEWER_URL` or `/viewer/`.
- `src/lib/validation.ts` `nameProblem`, `sizeProblem`, `MAX_NAME`, `MAX_BYTES`, `ASSET_QUOTA`, `libraryUsage`, `formatBytes`: management's limits, checked before sending.
- `src/lib/mesh/` STL to GLB: `stl.ts` (parse, `mergeSoups`), `weld.ts`, `decimate.ts` (`BUDGETS`), `normals.ts` (creased), `glb.ts`, `supports.ts`, `bounds.ts` (`terrainVolume`), `guess.ts`, `size.ts` (`GLB_DEFAULTS`, `suggestUnit`, `describeSize`, `toggleUpright`), `three-frame.ts` (`eulerFor`), `convert.ts` (`convert`, `convertInWorker`, `describeGlb`), `convert.worker.ts`.
- `src/lib/units/` `units.ts` (`mmToInches`, `inchesToMm`, `UNIT_SCALES`), `bases.ts` (`BASE_PRESETS`), `volumes.ts` (seven declarations, `defaultVolume`).
- `src/lib/upload/` `intake.ts` (`classifyFiles`), `sha256.ts`, `upload.ts` (presigned sequence, `STAGE_LABELS`, byte progress).
- `src/lib/library/` `folders.ts` (`moveTargets`), `selection.ts` (multi-select: `click`, `selectAll`, `keepOnly`), `bulk.ts` (`uploadTarget`,
  `folderOptions`, `holdersIn`, `runBulk` in chunks of `BULK_MAX` = 200, `ASSET_DRAG`), `volume-edit.ts` (`toDraft`, `fromDraft`).
- `src/lib/download/` `platforms.ts` (`detectPlatform`, `orderDownloads`, `FIRST_RUN`): the Download page's OS pick and unsigned-build steps.
- `src/lib/armies/` `import.ts` (`normaliseSource`, `reimport`), `patches.ts` (`PatchArmyRequest` builders), `folder.ts` (`pickerAssets`,
  `loadFolder`, `saveFolder`: the army page's mesh pickers list one library folder and its sub-folders, plus the mesh a picker already
  holds; the choice is the army's, shared by every picker on the page and kept in `localStorage` under `tabletop.armyFolder.<armyId>`).
- `src/lib/components/` `MeshPreview` (three.js), `UploadDialog`, `ScaleOrientation` (Scale and Orientation fieldsets, shared by the dialog and the asset page), `VolumeEditor`, `BasePicker`, `AssetPicker`, `FolderTree`, `Icon`
  (paths in `icons.ts`), `ThemeSwitch`.
- `src/lib/theme/theme.ts` `THEMES`, `resolveTheme`, `loadTheme`, `saveTheme`, `applyTheme` (`tabletop.theme` in `localStorage`).
- `src/themes.css` the client's two themes as tokens and `@font-face`; `src/app.css` base, shell, panels, controls, states.
- `static/` `theme-init.js` (sets `data-theme` before first paint), `themes/<id>/fonts/` (the client's OFL fonts, `LICENSES.md`),
  `themes/fantasy/paper.svg`.
- `tests/` Vitest; `tests/fixtures/` STL files and `make-fixtures.ts`.

## How it works

- Library: an upload is filed into the folder open in the tree (the dialog's "Adds to", changeable); tiles multi-select (checkbox,
  Ctrl/Cmd-click, Shift range, Space, long-press) for add/move to a folder (picker or drag onto the tree), remove from folder and delete,
  through management's bulk endpoints.
- Upload: `classifyFiles` sorts the drop into STLs, one GLB or one image. `UploadDialog` runs `convertInWorker` (a module Web Worker over `convert`).
- `convert` order: parse, merge, weld, recentre on the footprint, decimate to a triangle budget, creased normals, export GLB, then analysis (bounds, supports, `Proposal`, terrain `Volume`).
- `guess.ts` proposes millimetres (`scale = mmToInches(1)`) and an up axis: `y` when the model is tallest along Y, else `z`. Y-up gets `pitch: 90`.
- A dropped GLB takes `GLB_DEFAULTS` (glTF: metres, Y up, front +Z → scale 39.37, pitch 90, yaw 180); `suggestUnit` offers mm or cm
  when the size is implausible (outside 2 mm–1 m), never switching by itself.
- `MeshPreview` orbit: polar 0–85° about table Z (never under the table), starting at 45° from the front-right, fitted to
  model + ring; code-built `RoomEnvironment`, hemisphere fill, camera-following key and rim lights.
- `upload.ts` stages: `hash`, `create`, `put` (presigned), `confirm`, `volumes`. An existing hash returns `deduplicated`. The `PUT` uses
  `XMLHttpRequest` for upload progress where it exists, `fetch` otherwise (Node tests).
- Errors: pages show `errorMessage(e, context)`, never `e.message` of an `ApiError`; the context (`auth`, `import`, `upload`) decides
  what a `401` and a `502` say. Names go through `nameProblem` and files through `sizeProblem` before a request.
- Orientation is `{ pitch, yaw, roll }` in degrees, stored on the asset with the scale; `eulerFor` maps it to a three.js `ZYX` Euler.
- `http.ts` sends requests to `API_BASE + path`; `API_BASE` is `PUBLIC_API_BASE` or empty, so dev is same-origin through the Vite proxy.
- Replays: the header shows a Replays link (`data-sveltekit-reload`, a full page load) when the session JWT's `roles` claim holds `admin`.
  It opens the viewer's hosted build at `/viewer/` (`viewer/AGENTS.md`), which reads this app's `tabletop.session` entry and sends
  a signed-out reader to `/login?next=/viewer/`; changing that entry's key or shape changes `viewer/src/lib/hosted/hosted.ts`.
- `session.svelte.ts` refreshes the JWT before expiry through `/v1/auth/session/refresh`. A bearer `401`, or a refresh refused with any
  4xx but 429, clears the session with `expired` set; the layout sends the reader to `/login?next=<path>&expired=1`, and sign-in returns
  to `safeNext(next)`.

## Commands

- `docker compose up -d` runs the dev server as the `web` service on `http://127.0.0.1:5173`, `/v1` proxied to
  `http://management:5100`. It shares `web/node_modules` with the host and runs `npm ci` on start when
  `package-lock.json` differs from the hash in `node_modules/.tabletop-lock.sha256`.
- Host run: `npm install` once in `web/` (Node 24 from mise), `docker compose stop web`, then `./scripts/web`; `/v1` goes to
  `TABLETOP_API_PROXY_TARGET`, default `http://localhost:5100`.
- `cd web && npm test`: Vitest (`tests/**/*.test.ts`, node environment, 60 s timeout).
- `cd web && npm run check`: `svelte-kit sync` and `svelte-check`.
- `cd web && npm run build`: static output to `web/build`, SPA fallback `index.html`. Production builds set `TABLETOP_BLOB_ORIGIN`
  (the presigned URLs' origin) for the CSP; Caddy serves `web/build` (`deploy/README.md`).
- `web/Dockerfile` (context: repo root, build arg `TABLETOP_BLOB_ORIGIN`): the same build served by Caddy on :8080 with SPA
  fallback and the `frame-ancestors` header (`web/Caddyfile`), plus the viewer's hosted build (`npm run build:hosted` in `viewer/`)
  at `/viewer/` with its own CSP header; the home-server stack's `web` service.
- `docker compose` sets `PUBLIC_VIEWER_URL=http://127.0.0.1:5174/` so the dev Replays link opens the dev viewer (its own login).
- `cd web && npm run fixtures`: regenerates the STL fixtures.

## Conventions and pitfalls

- The API speaks inches. Millimetres exist only in the UI; convert at the edge with `src/lib/units/`.
- The client calls `/v1/auth`, `/v1/account`, `/v1/assets`, `/v1/folders`, `/v1/armies`, `/v1/client/latest`. It never calls `/v1/games*`, `/v1/properties` or `/v1/layouts`.
- `npm` with exact versions: `.npmrc` sets `save-exact=true` and `engine-strict=true`; `package.json` pins every dependency.
- The mesh pipeline is headless: no DOM in `src/lib/mesh/` outside `convertInWorker` and the worker, so Vitest runs it against `tests/fixtures`.
- `vite.config.ts` excludes `meshoptimizer` from `optimizeDeps` and sets `worker.format: 'es'`.
- Vite exposes only `VITE_*` and `PUBLIC_*` on `import.meta.env` (`envPrefix`); `PUBLIC_API_BASE` is read there, not through `$env`.
- `session.svelte.ts` wraps `localStorage` in try/catch; a blocked store leaves the session in memory.
- Accessibility: forms submit on Enter (`<form onsubmit>`), every control has a label or `aria-label`, progress and confirmations are
  `role="status"`, failures `role="alert"`, each page sets `<title>`. `UploadDialog` is a modal `<dialog>` (`showModal`); its `close`
  event is the one exit path. Layout helpers in `app.css`: `.stack`, `.actions`, `.scroll-x`, `.visually-hidden`, `.skip`.
- Svelte trims whitespace at the start of an `{#if}` block: write `{' · '}` rather than a leading space inside it.
- MinIO's CORS (`docker-compose.yml`) admits only the `5173` origins; a preview on another port cannot `PUT` presigned uploads.
- Table frame: Z up, origin at the corner; mesh space is converted by `Proposal.orientation`, not by the page.
- Themes mirror the client's `UiThemes` (Sci-fi default, Fantasy): change a colour or font in both. Theme-wide control rules use
  `:where()` so scoped component styles win; buttons take `.primary`, `.danger`, `.quiet`; blocks are `.panel`, `.seg`, `.empty`,
  `.loading`, `.notice`; spacing `--s1`…`--s7`. Sci-fi controls are clipped (`clip-path`), so their focus is the frame turning
  light, not an outline. Text pairs meet WCAG AA on their panel; Fantasy's `--dim` is AA only on parchment, not the page background.
- `static/theme-init.js` duplicates `loadTheme`'s read; `tests/theme.test.ts` runs it to keep them in step. It loads after
  `%sveltekit.head%` so the CSP meta covers it.
- `svelte.config.js` sets `kit.csp` (hash mode): `script-src 'self' 'wasm-unsafe-eval'` (meshoptimizer's WASM), `worker-src 'self' blob:`,
  `connect-src` and `img-src` add the blob origin (`TABLETOP_BLOB_ORIGIN`, default `http://127.0.0.1:9000`) and `PUBLIC_API_BASE`'s origin. A new
  outside origin goes there. `font-src 'self'`: fonts are self-hosted, never a CDN.

## Docs

- `docs/design/web-app/design.md`
- `docs/superpowers/specs/2026-08-29-web-app-design.md`
- `docs/superpowers/specs/2026-09-04-mesh-conversion-design.md`
- `docs/superpowers/specs/2026-10-01-starter-meshes-design.md`
- `docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md` (Themes)
