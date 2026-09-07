# Workstreams — superseded 2026-09-06

> **Do not execute anything in this directory.** The ten implementation plans in
> `docs/superpowers/plans/` replace these prompts entirely. Every file here was written on
> 2026-08-24 (04 corrected on 2026-08-30), before the first design session, and describes a data
> model that no longer exists — type names, protocol messages, tables and deliverables that were
> designed away between 2026-08-27 and 2026-09-06. **No file here is ready, and no session should
> read this directory first.** The entry point is `docs/handoff.md`, which gives the reading order
> and names the plan to start from.
>
> The directory is kept, not deleted, because `docs/architecture.md`, four plans, three design
> documents and two specs each cite these files by path as the stale sketch they departed from.
> Deleting them would break those citations and erase the record of how the project was first
> scoped. It is history, and nothing more.

## What replaced each file

Each fate below was checked against the file itself and against the plan that names it, not
against memory.

| File | Subject | Fate | Now covered by |
| --- | --- | --- | --- |
| [01-contracts.md](01-contracts.md) | `libs/` type surface, signatures only | Superseded. Names `EndActivation` and `DiceRolled`, neither of which exists, and "terrain multipliers", an invention. | `plans/2026-09-05-contracts-and-local-dev.md` |
| [02-geometry.md](02-geometry.md) | Geometry implementation and its tests | Superseded | `plans/2026-09-05-geometry.md` |
| [03-management-identity.md](03-management-identity.md) | Accounts, Stytch login, web shell | Superseded | `plans/2026-09-05-management-api.md`, `plans/2026-09-05-web-app.md` |
| [04-game-server-skeleton.md](04-game-server-skeleton.md) | Socket, event log, reconnect | Superseded. Its auth section was corrected in place on 2026-08-30 (commits `7502926`, `f1686db`) when our own session table was dropped for Stytch's JWT; that correction is the only edit any file here ever received, and it does not make the file current. | `plans/2026-09-05-game-server-skeleton.md` |
| [05-client-shell.md](05-client-shell.md) | Table, camera, mouse, no server | Superseded outright by `specs/2026-09-04-client-shell-design.md`, which records that this file scoped the shell to table, camera and mouse with no server. | `plans/2026-09-05-client-shell.md` |
| [09-management-collections.md](09-management-collections.md) | Army Forge import, art profiles | Superseded | `plans/2026-09-05-management-api.md`, `plans/2026-09-05-web-app.md` |
| [11-mesh-prep-tool.md](11-mesh-prep-tool.md) | `tools/mesh-prep/` STL-to-glTF CLI | Superseded outright, and the deliverable itself is gone. `specs/2026-09-04-mesh-conversion-design.md` moved STL conversion into the web app's upload flow, in the browser. There is no `tools/mesh-prep/`. | `plans/2026-09-05-web-app.md` |
| This file | The index, the status table, and the rules for parallel sessions | Superseded. The status column claimed 01–05 and 09, 11 were **ready**; they were not, and the markers have been removed. | `docs/handoff.md` |

The rows this index left unscoped were designed later and are covered too: setup phase (06) by
the game server and client shell plans over `specs/2026-09-04-setup-phase-design.md`, the
scheduler (07) by `plans/2026-09-05-scheduler.md`, resolution (08) by
`plans/2026-09-05-interpreter-and-effects.md`, and assets (10) by the management API and web app
plans. Two plans have no ancestor here at all —
`plans/2026-09-06-simulation.md` and `plans/2026-09-06-viewer.md`, for deliverables ruled on
2026-09-06.

## What this directory was, for the record

Each file was a self-contained prompt for one Claude session, meant to run in parallel with the
others on its own `ws/<nn>-<slug>` branch, owning an exclusive set of paths. The dependency shape
was two independent roots:

```
01 Contracts ──┬──> 02 Geometry ──> 05 Client shell ──┐
               │                                      ├──> 06 Vertical slice
               └──> 04 Game server ───────────────────┘

03 Management P1 ──> 09 Collections ──> 10 Assets
```

That scheme was abandoned when the project moved to designing every module first. What survived
it are the habits now recorded elsewhere: stay inside the paths your plan owns, do not scaffold
ahead of a plan (`CLAUDE.md`), stop rather than guess when a decision would change what another
module builds, and back a claim of completion with the command and its output
(`superpowers:verification-before-completion`). None of that needs this directory.
