# viewer

A static web app that draws a `sim/` recording or a game's log top-down on a 2D canvas. Plain Vite + Svelte 5 + Vitest, its own `package.json`. Two builds: the developer viewer (`npm run dev`, `npm run build`) and the hosted build (`npm run build:hosted`), which production serves at `/viewer/` on the website's origin as the admin-only Replays tool.

## Layout

- `src/main.ts`, `src/App.svelte`: entry and shell.
- `src/lib/protocol/` `recording.generated.ts`, `wire.generated.ts`: TypeScript types generated from the C# JSON contracts.
- `src/lib/recording/` `read.ts` (`parseRecording`, `RecordingError`), `source.ts` (`fromFile`, `fromUrl`).
- `src/lib/feed/` `feed.ts` (`FeedState`, `Feed`, `Emitter`), `recordingFeed.ts`, `liveFeed.ts` (socket as spectator).
- `src/lib/live/` browser ports for the live feed: `apply.ts` (`Applier`, `ProjectionDesyncError`), `project.ts` (`FrameProjector`, `recordedBoardOf`), `logLines.ts` (`lineOf`), `management.ts` (session JWT), `games.ts` (`listGames`, `rowOf`: the game list).
- `src/lib/board/` `frame.ts` (shapes, `OVAL_VERTICES`), `draw.ts`, `pick.ts`, `transform.ts`, `palette.ts` (declaration colours).
- `src/lib/timeline/timeline.ts` (`reduce`, `frameOf`), `src/lib/asks/describe.ts`.
- `src/lib/components/` `SourcePicker`, `Board`, `Timeline`, `LogPanel`, `AskPanel`, `RollPanel`, `Scoreboard`.
- `src/lib/hosted/hosted.ts` the hosted build: `configOf` (which build, socket URL, runner), `hostedSession` (the website's session from `localStorage`), `accessOf`, `signInHref`.
- `src/lib/suite/` `client.ts` (runner API: `fetchSuite`, `startRun`, `runEvents`), `model.ts` (Tests page model); `components/TestsPage.svelte`.
- `tests/` Vitest; `tests/fixtures/` per-scenario files (`skirmish`, `terrain`): `.recording.json`, `.events.json`, `.board.json`, `.welcome.json`.

## How it works

- Every feed produces `FeedState` in the recording's own schema, so the renderer cannot tell the sources apart.
- Recording feed: a file or URL read by `parseRecording`; needs no server.
- Live feed: in the developer build the source picker's Games panel logs in once per page session (`management.ts`: `/v1/auth/password`, or a pasted JWT), refreshes the JWT (`/v1/auth/session/refresh`) before each use, and lists `GET /v1/games` (`games.ts`): name, status, players, created (UTC). Clicking a game opens `liveFeed.ts`, which sends `Connect` on the game socket (`VITE_GAME_SOCKET`, default `ws://localhost:5200/ws`). The account watches as an admin (Stytch role `admin` in the session's `roles` claim); a non-admin is rejected unless it plays in the game.
- Live frames come from `Applier` (port of `Tabletop.Protocol.EventApplier`) and `FrameProjector` (port of `Tabletop.Sim.Recording.FrameProjector`); `lineOf` ports `Tabletop.Sim.Recording.LogLines`.
- The board is a grid in inches: each model's base outline coloured by team with facing, terrain volumes as outlines labelled with declarations. A timeline sits beside the log.
- The Tests page (header tab) drives the runner service (`sim serve`): `/suite` and `/runs` are proxied to `TABLETOP_SUITE_PROXY_TARGET` (default `http://localhost:5180`; compose `http://sim-runner:5180`). Watch loads a kept recording; Open live asks the runner for a spectator session (`POST /runs/{id}/games/{game}/spectate`) and opens the live feed on that kept socket game. The page groups the games, runs a selection with Keep and a driver, lists each result
  as it arrives (a game pinned to an engine defect shows failures prefixed `known defect <id>: `) and the coverage table at the end.
- Hosted build (Vite mode `hosted`, base `/viewer/`): `web/Dockerfile` builds it into the web image at `/srv/viewer`; `web/Caddyfile` serves it with its own CSP header. It has no login form: it reads the website's session from `localStorage` `tabletop.session` (`{ sessionJwt, account }`, written by `web/src/lib/session/session.svelte.ts`), refreshes it before each use and writes the new JWT back. Signed out (nothing stored, or a refresh refused with a 4xx but 429) shows a sign-in link to `/login?next=/viewer/`; a JWT without the `admin` role shows *Not authorised*. The socket is `wss://<page host>/ws`. No Tests tab (`config.runner` is false). The Games panel opens loaded; `GET /v1/games` lists ended games too, and the live feed replays a game's log from the start.
- Playback only. It sends no command and answers no Ask; the server refuses a watcher anything that changes the game (`refused`, reason `watcher`). The calls to management are the two auth endpoints and `GET /v1/games`.

## Commands

- `docker compose up -d` runs the dev server as the `viewer` service on `http://127.0.0.1:5174`, `/v1` proxied to
  `http://management:5100`, `VITE_GAME_SOCKET=ws://127.0.0.1:5200/ws`. It shares `viewer/node_modules` with the host and
  runs `npm ci` on start when `package-lock.json` differs from the hash in `node_modules/.tabletop-lock.sha256`.
- `cd viewer && npm install`
- `cd viewer && npm run dev` (after `docker compose stop viewer`): `:5174`, `/v1` proxied to `TABLETOP_API_PROXY_TARGET`,
  default `http://localhost:5100`.
- `cd viewer && npm test`: Vitest (`tests/**/*.test.ts`).
- `cd viewer && npm run check`: `svelte-check`.
- `cd viewer && npm run build` (developer) and `npm run build:hosted` (production, `dist/` with base `/viewer/`).
- Regenerate types: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`.
- Regenerate fixtures: `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests`.
- Record a run: `dotnet run --project sim -- skirmish --seeds 1..20` (files in `./recordings`).

## Conventions and pitfalls

- No SvelteKit and no code shared with `web/` (the hosted build shares only the `tabletop.session` storage entry).
- `src/lib/protocol/*.generated.ts` are never hand-edited. A plain `dotnet test` fails when they are stale.
- Fixtures are checked in and regenerated deliberately; ids differ between runs, so never diff them against a previous version.
- A change to `EventApplier`, `FrameProjector` or `LogLines` regenerates the fixtures and ports the same change to `src/lib/live/` until `tests/agreement.test.ts` passes.
- `agreement.test.ts` replays the fixture events through the TypeScript `Applier` and `FrameProjector` and compares with the C# recording after `tests/normalise.ts`.
- `src/lib/live/` holds no rules: every name it prints is a string the event already carries.
- `VITE_API_BASE` (default empty, same-origin) sets the management base for the live feed; `VITE_GAME_SOCKET` sets the developer build's socket URL (the hosted build ignores it).
- The hosted build depends on the web app's storage key and shape (`tabletop.session`); a change there changes `src/lib/hosted/hosted.ts`.
- Node 24 via mise; `.npmrc` pins exact versions.

## Docs

- `docs/design/viewer/design.md`
- `docs/superpowers/specs/2026-09-06-simulation-design.md`, `2026-10-09-admin-watch-design.md`
