# Task 11: `CLAUDE.md`

**Files:**
- Modify: `CLAUDE.md` (Layout, Commands)
- Verify only: `docs/architecture.md`. It already records the deliverables, so Step 3 checks it
  rather than editing it.

**Interfaces:**
- Consumes: everything Tasks 1–10 produced, as the facts this document records.
- Produces: no code. The whole-tree check in Step 4 is this plan's exit gate.

Spec: spec → *`sim/` is a fourth deliverable* — "The layout rule in `CLAUDE.md` had three shipping directories, `client/`, `server/` and `web/`… `sim/` and `viewer/` are a fourth and fifth"; "They ship to developers, not to players. Neither is in the client's export, neither is referenced by a server, and a release that omits both is complete."

`viewer/` is another plan's, so it keeps the stub line it already has and nothing more.

- [ ] **Step 1: `CLAUDE.md` → Layout, the kinds list — already correct, verify only**

**Ownership rule for `CLAUDE.md`**, so that plans can execute in parallel: a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

**There is no edit here, and this step exists to say so.** The first bullet already lists five deliverables:

```markdown
- **Deliverables** — `client/`, `server/`, `web/`, `sim/`, `viewer/`. These ship. The first three
  ship to players; `sim/` and `viewer/` ship to developers, so neither is in the client's export,
  neither is referenced by a server, and a release that omits both is complete.
```

- [ ] **Step 2: Edit `CLAUDE.md` → Layout, the `sim/` bullet**

Replace the `sim/` bullet (the line beginning "- `sim/` —" and ending "Does not exist yet.") with:

```markdown
- `sim/` — `Tabletop.Sim`, a library plus a console entry point that plays a whole game with no
  Godot client: a scenario, a seeded answer policy over the server's legal sets, two drivers
  (in process and over the socket), the invariant checks, and one recording file per run.
  It reads game state freely but holds no second implementation of the rules: it answers from the
  server's legal sets without narrowing them, and asserts invariants.
```

Leave the `viewer/` bullet alone; the viewer plan replaces its own. Touch neither the opening paragraph nor the closing note.

- [ ] **Step 3: Edit `CLAUDE.md` → Commands, and verify `docs/architecture.md`**

In **Commands**, append two lines at the end of the block, changing no existing line:

```bash
dotnet run --project sim -- --list                 # the scenarios
dotnet run --project sim -- skirmish --seeds 1..20 # twenty games, recordings in ./recordings
```

**`docs/architecture.md` needs nothing from this task.** Its Deliverables table carries six rows and an Audience column, the deliverables paragraph sits above the client's platform note, and the Repository layout block lists `sim/` and `viewer/`. Confirm the table's client row reads *Windows first, other desktop platforms possible (Godot export)*.

- [ ] **Step 4: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d
dotnet build && dotnet test
dotnet run --project sim -- skirmish --seeds 1..50 --no-recording
find sim tests/Tabletop.Sim.Tests -path '*/obj/*' -maxdepth 3 -type d | grep -v Unix
git status --short
```

Expected: build clean, every test green, fifty runs with no failures, no `obj/` outside `obj/Unix/`, and nothing unexpected in `git status`.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "Record what sim/ is in CLAUDE.md, and how to run it"
```
