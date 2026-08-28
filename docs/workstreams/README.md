# Workstreams

Each file in this directory is a **self-contained prompt** for one Claude session. Sessions run
independently and in parallel, so a prompt must carry everything its session needs and must not
depend on knowledge from any other session.

This file is the index and the rules. **Every session reads this file and
`docs/architecture.md` before doing anything else.**

## Status

| # | Workstream | Owns paths | Depends on | Status |
| --- | --- | --- | --- | --- |
| 01 | [Contracts](01-contracts.md) | `libs/` | — | **ready** |
| 02 | [Geometry](02-geometry.md) | `libs/Tabletop.Geometry/` | 01 (API surface only) | **ready** |
| 03 | [Management: identity](03-management-identity.md) | `server/management/`, `web/` | — | **ready** |
| 04 | [Game server skeleton](04-game-server-skeleton.md) | `server/game/` | 01 | **ready** |
| 05 | [Client shell](05-client-shell.md) | `client/` | 01, 02 | **ready** |
| 06 | Setup phase (vertical slice) | `client/`, `server/game/` | 04, 05 | not scoped |
| 07 | Scheduler + OPR skeleton | `server/game/` | 06 | not scoped |
| 08 | Resolution pipelines | `server/game/` | 07 | not scoped |
| 09 | [Management: collections](09-management-collections.md) | `server/management/`, `web/` | 03 | **ready** |
| 10 | Management: assets | `server/management/`, `web/` | 09 | not scoped |
| 11 | [Mesh prep tool](11-mesh-prep-tool.md) | `tools/mesh-prep/` | — | **ready** |

**Do not start a workstream marked "not scoped."** It has no prompt file because the design
decisions behind it have not been made. Scoping happens in the main planning session.

## Dependency shape

```
01 Contracts ──┬──> 02 Geometry ──> 05 Client shell ──┐
               │                                      ├──> 06 Vertical slice
               └──> 04 Game server ───────────────────┘

03 Management P1 ──> 09 Collections ──> 10 Assets
```

Two roots. `01`+`02` and `03` share no code, no database, and no contract — they can run
simultaneously from a cold start.

## Rules for every session

**Read first, in order:** this file, then `docs/architecture.md`, then `CLAUDE.md`, then your
own workstream file.

**Stay inside your paths.** The "Owns paths" column is exclusive. If your work seems to require
editing a path another workstream owns, stop and report it — that is a design problem, not a
coding problem, and it means a contract is wrong.

**Work on your own branch**, named `ws/<nn>-<slug>` (e.g. `ws/02-geometry`). Never commit to
`main`. Parallel sessions on one working tree will collide; use a git worktree if running
locally alongside another session.

**Do not scaffold ahead of your prompt.** `CLAUDE.md` is explicit about this: no speculative
folders, interfaces, or placeholder systems for work that has not been designed. If your prompt
does not ask for it, do not build it.

**Contracts are frozen.** If you are consuming a type from `libs/`, you may not change it. If it
is wrong, stop and report. Workstream 01 is the only one that may edit contract shapes.

**Record decisions you had to make.** Anything you decided that the prompt did not specify goes
in a `## Decisions` section appended to your own workstream file, with one line of reasoning.
The planning session folds these back into `docs/architecture.md`.

**Stop rather than guess.** If a decision would change what other workstreams build, it is not
yours to make. Report and wait.

## Definition of done, for every workstream

1. The deliverable in your prompt works, and you have run the command that proves it.
2. Tests pass. `dotnet test` for .NET, the project's runner for `web/`.
3. `dotnet build` succeeds from the repo root.
4. Nothing outside your owned paths is modified.
5. Decisions recorded, as above.

Claims of completion require evidence — paste the command and its output. "It should work" is
not done.
