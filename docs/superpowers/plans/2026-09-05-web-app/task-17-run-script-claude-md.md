# Task 17: Run script, CLAUDE.md, and the whole-tree check

**Files:**
- Create: `scripts/web` (shell)
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything Tasks 1–16 produced, as the facts `CLAUDE.md` records. Every edit is to a line this plan owns or an append, so no other plan need have landed.
- Produces: no code. Step 3's whole-tree check is this plan's exit gate.

- [ ] **Step 1: Write `scripts/web`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# Run the web app's dev server from WSL on http://localhost:5173, proxying /v1 to the management API on :5100.
# Windows reaches it at the same address over forwarded localhost. Needs `npm install` once in web/.
set -euo pipefail
cd "$(dirname "$0")/../web"
exec npm run dev -- "$@"
```

- [ ] **Step 2: Edit `CLAUDE.md`**

**Ownership rule for `CLAUDE.md`** (plans execute in parallel): a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

In **Environment**, append: "Node 24 for `web/` comes from mise (`~/.local/share/mise`) in WSL; `npm`, exact versions (`web/.npmrc`)."

In **Commands**, append at the end of the block:

```bash
./scripts/web                # the web app on http://localhost:5173 (Vite; /v1 proxied to management on :5100)
cd web && npm test           # Vitest: the API client, session, units, the mesh pipeline against tests/fixtures
```

In **Layout**, replace the `web/` bullet (the line beginning "- `web/` —", currently the scaffold's stub) with: "`web/` — the Svelte SPA (SvelteKit 2 + Svelte 5, `adapter-static`, `ssr = false`). `src/lib/api/` is the typed client over management's `/v1`; `src/lib/mesh/` is the STL → GLB pipeline (`meshoptimizer`, `@gltf-transform/core`), headless under Vitest and in a Web Worker in the page; `src/lib/components/` holds the three.js preview, the upload dialog and the volume editor; `src/routes/` are the pages. Node 24 via mise; `npm`, exact versions (`.npmrc`)."

In **Conventions**, append: "- The web app speaks inches to the API; millimetres exist only in its UI (`src/lib/units/`). It never calls `/v1/games*`, `/v1/properties` or `/v1/layouts`."

- [ ] **Step 3: Run the whole check**

```bash
docker compose up -d && dotnet build && dotnet test
cd web && npm ci && npm test && npm run check && npm run build && cd ..
./scripts/management & ./scripts/web &   # then, from the Windows browser: http://localhost:5173/login
git status --short
```

Expected: .NET build and tests green; `npm test` 62 passed and 1 skipped (the skip is the real-STL test, when `tests/fixtures/real/` is absent); `svelte-check` 0 errors; `web/build/index.html` exists; the full flow works end to end from Windows — sign in, import `c2-6YsWRH3py`, upload `dense-binary.stl`, bind it to a unit, open the asset and see it on its ring; `git status` shows no `node_modules/`, `build/` or `.svelte-kit/`.

- [ ] **Step 4: Commit**

```bash
git add scripts/web CLAUDE.md
git commit -m "Record the web app in CLAUDE.md and add its run script"
```
