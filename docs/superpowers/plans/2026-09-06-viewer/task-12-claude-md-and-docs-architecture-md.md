# Task 12: `CLAUDE.md` and `docs/architecture.md`

**Files:**
- Modify: `CLAUDE.md` (Layout, Commands, Conventions)
- Modify: `docs/architecture.md` (Deliverables)

**Interfaces:**
- Consumes: everything Tasks 1–11 produced, as the facts these two documents now record.
- Produces: no code. Step 5's whole-tree check is this plan's exit gate.

Spec: spec → *`viewer/` is a developer web app, separate from `web/`* — "A static page, its own top-level directory, sharing no code and no build with `web/`."

**Ownership rule for `CLAUDE.md`**, so that plans can execute in parallel: a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

Both documents carry a `viewer/` placeholder — one bullet in `CLAUDE.md`'s Layout that says `viewer/` does not exist yet, one row in `architecture.md`'s Deliverables table and one line in its Repository layout block. This task replaces the placeholder bullet with what was built, extends `architecture.md`'s ruling paragraph, and appends Commands and Conventions lines; the table row and the layout line already read correctly and are left alone.

- [ ] **Step 1: Edit `CLAUDE.md` → Layout, the `viewer/` bullet**

Replace the `viewer/` bullet (the line beginning "- `viewer/` —" and ending "Does not exist yet.") with:

```markdown
- `viewer/` — a static developer web app that draws a `sim/` recording top-down on a 2D canvas: a
  grid in inches, each model's base outline (circle, oval or rect) coloured by team with its facing, terrain as outlines
  labelled with their declarations, and a timeline beside the log. Plain Vite and Svelte 5, its
  own `package.json`, **no SvelteKit and no code shared with `web/`**. Two feeds: a recording file,
  which needs no server, and a live socket as a spectator with no team. Playback only — it sends
  no command and answers no Ask.
```

- [ ] **Step 2: `CLAUDE.md` → the opening paragraph and the closing note — nothing to change**

There is no edit here, and this step exists to say so. The contracts plan wrote both in a form that never needs another plan's edit: the paragraph says the Layout bullets are the record of what exists, and the note reads "Each module is designed before it is built." Do not add a sentence about what is empty or absent.

- [ ] **Step 3: Edit `CLAUDE.md` → Commands and Conventions**

In **Commands**, append two lines at the end of the block, changing no existing line:

```bash
cd viewer && npm install && npm test   # the viewer's Vitest suite
cd viewer && npm run dev               # the viewer on :5174, /v1 proxied to management on :5100
```

In **Conventions**, append two bullets:

```markdown
- `viewer/`'s TypeScript protocol types are **generated, not written**:
  `viewer/src/lib/protocol/{recording,wire}.generated.ts` come from the C# JSON contracts and are
  checked in. Regenerate with `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`;
  a plain `dotnet test` fails if they are stale. Never hand-edit them.
- `viewer/tests/fixtures/` holds one recording plus the three socket frames of the same run, per
  scenario. They are checked in and regenerated deliberately with
  `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests` — ids differ between runs, so
  they are never diffed against a previous version.
```

- [ ] **Step 4: Edit `docs/architecture.md` → Deliverables**

The table row and the Repository layout line are already there from the simulation plan Task 11 Step 3. Extend that task's ruling paragraph — before, its last sentence:

```markdown
of the rules. `viewer/` draws a run's recording top-down and is separate from `web/`, which owns
preparation for players.
```

After:

```markdown
of the rules. `viewer/` draws a run's recording top-down and is separate from `web/`, which owns
preparation for players — a static page with its own `package.json`, plain Vite and Svelte 5,
sharing no code and no build with `web/`.

The recording is `viewer/`'s only input shape. A recording file is read directly; a live game over
the socket is *converted* into the same frames in the browser, so the renderer, the timeline and
the panels have one shape to draw and neither feed is privileged. That second path is the one place
in the project where `Tabletop.Protocol` and the event applier exist twice, and the duplication is
generated away rather than maintained: the TypeScript types come from the C# JSON contracts, and
the ported applier is checked against the C# one by replaying a recording through both and
comparing every frame (`tests/Tabletop.Viewer.Tests`). Playback is the whole of it — the viewer
sends no command and answers no Ask, and the seam where it could is deliberately empty.
```

- [ ] **Step 5: Run the whole-tree check**

```bash
cd "$(git rev-parse --show-toplevel)"
docker compose up -d
dotnet build && dotnet test
cd viewer && npm ci && npm test && npm run check && npm run build && cd ..
find viewer -maxdepth 2 -name obj -o -maxdepth 2 -name bin | head
git status --short
```

Expected: build clean, every test green in both languages, `svelte-check` at 0 errors, `viewer/dist/index.html` built, no `obj/` or `bin/` anywhere under `viewer/` (it is not in `Tabletop.sln`), and nothing unexpected in `git status`.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md docs/architecture.md
git commit -m "Record the developer viewer as built: two feeds, one input shape, generated types"
```
