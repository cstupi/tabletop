# Handoff — 2026-08-24

Where the project stands at the end of the first planning session, and what to pick up next.

## Nothing is committed

The entire session's work is sitting in the working tree, unstaged. `git status` shows the
restructure as renames (so history follows), plus modified docs and new directories.

Committing is the first thing to do tomorrow, unless you want to review it first.

## What happened this session

Two things: the repo was restructured, and several load-bearing architecture decisions were
reversed. The reversals matter more.

### Repo restructure (done, verified)

The Godot project moved from the repo root into `client/`. Verified: headless Godot import
exits 0, `dotnet build` from the root succeeds, assembly lands at
`client/.godot/mono/temp/bin/Debug/Tabletop.dll`, per-OS `obj/` split still resolves.

```
client/            Godot game client — the only Godot dependency
server/game/       live sessions, rules engine, event log   (empty)
server/management/ ASP.NET Core CRUD API                    (empty)
libs/              Geometry, Protocol, Content              (empty)
docs/  scripts/    repo-level support
```

Godot did **not** demand a solution beside `project.godot`, so `Tabletop.sln` stays at the root
and `dotnet/project/solution_directory` was not needed.

`web/` and `tools/` do not exist yet; they are created by their workstreams.

### Decisions reversed from the original architecture

| Was | Now |
| --- | --- |
| Rules engine shared, runs on client and server | **Thin client.** Rules live only in `server/game/` |
| One `Tabletop.Server` with module folders | **Two servers**, split on CRUD vs live sessions |
| In-process authority gives local hotseat free | **No offline mode.** The client always connects |
| Self-hosted, filesystem blob store | **AWS**: Fargate, RDS, S3 |
| Networking deliberately late (step 7) | **Step 3.** It is the only transport |
| Assets → art profiles → army loadouts | **Two layers**: Asset (physical) + Binding (representational) |
| Server converts uploaded meshes | **glTF only.** Conversion is an offline tool |

All of this is written into `docs/architecture.md`. That document is now correct and is the
shared context every workstream session reads.

### Two things I got wrong mid-session, now corrected

- I claimed `Tabletop.Geometry` needed bit-exact cross-platform determinism. It does not — the
  server is authoritative, so client geometry is a **prediction for responsiveness only**, and
  divergence cannot compound. Corrected in the doc and in workstream 02.
- I described a Stytch redirect URL as "production". There is no production environment; both
  redirect URLs are in the **test** project.

## Workstream prompts

`docs/workstreams/README.md` is the master index and carries the rules every session follows:
read order, exclusive path ownership, branch naming `ws/<nn>-<slug>`, contracts frozen, decisions
recorded back into the workstream file, evidence required for completion claims.

**Ready:** 01 contracts, 02 geometry, 03 management identity, 04 game server skeleton,
05 client shell, 09 management collections, 11 mesh prep tool.

**Not scoped, marked do-not-start:** 06 vertical slice, 07 scheduler + OPR, 08 resolution
pipelines, 10 management assets.

Genuinely startable with nothing frozen first: **01**, **03**, **11**.

## Open — pick up here

**01-contracts is the weakest prompt and blocks the most.** It currently says "design an API
surface" without specifying what goes in it, which means 04 and 05 would build against different
guesses. The conclusion reached but not yet written up:

- `Tabletop.Geometry` — fully specifiable now, no game concepts in it
- `Tabletop.Content` — specifiable now, stats are a *named* store, never typed OPR fields
- `Tabletop.Protocol` — **cannot** be fully frozen. Freeze the *envelope* (game_id, actor, kind,
  payload, override flag, sequence, caused_by); let the command set grow per vertical slice.
  `DeclareShoot` cannot be designed before resolution pipelines exist.

**Not scoped yet, in the order originally agreed:** game server beyond the skeleton, then army
management detail, then the client. Workstreams 06–08 and 10 need scoping before they can run.

**Parked, not blocking:** who instantiates a game session when a lobby room starts. Management
phase 1 has no lobby and the game server skeleton stubs session creation, so neither waits.

**Unresolved smaller items:**

- Binding granularity — unit-level, model-level, or unit default with per-model override. Third
  option recommended, not decided.
- `Layout` and `Mission` — Layout is drawn as a user-owned table, but a mission may be rule-pack
  data rather than a row. Depends whether users author missions.
- Triangle-count guardrail at upload — agreed in principle, threshold not chosen.

## Environment notes

Stytch MCP is authenticated and works. Project `TabletopWar`, vertical `CONSUMER`, test project
`project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. The user updated the test environment's
redirect config during the session, so re-read it rather than trusting values quoted in older
messages.

The available MCP tools are project-configuration only — redirect URLs, SDK config, tokens,
email templates. There are no user-management tools, so anything about the Consumer user schema
has to come from the docs.
