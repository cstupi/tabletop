# Browser play C1: the shell, the games list, the connection and the board Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** The hosted viewer opens to every signed-in player: a games list (join, open, replay, leave, create, end, delete), a
participant connection that sends edits and commands with refusals and reconnects as Godot's `Session` does, a board with an
interaction API the lobby and setup (C2) and play (D) plans drive.

**Architecture:** `App.svelte` becomes a shell over hash routes (`#/games`, `#/game/<id>`, `#/replay/<id>`, `#/watch`,
`#/tests`). Playback (`PlaybackView`, a `Feed`) and play (`GamePage`, a `GameConnection`) are siblings that share `Board`
and its `View`. Godot's decisions port as plain TS modules with Vitest tests: `GameRowActions` → `games/rowActions.ts`,
`Feedback` → `connection/feedback.ts`, `Session`'s socket half →
`connection/gameConnection.ts`. Svelte components only bind them.

**Tech Stack:** Vite 8, Svelte 5 (runes), TypeScript 5.9, Vitest 5, Node 24; .NET 10 xUnit for the type generator; SvelteKit 2
for the one web change.

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (Goals; Constraints; Decided → Who, Where, Setup without
Godot; Coordinator's calls → Replays). Godot sources ported: `client/src/Core/Session.cs`, `ManagementClient.cs`,
`GameRowActions.cs`, `Feedback.cs`; `client/src/Screens/HomeScreen.cs`. Continued by `2026-10-24-browser-play-c2-setup.md` (the game page, lobby and setup); plan D (play) consumes the
`GameConnection` (Task 6) and board (Task 7) interfaces.

## Findings that shape the plan

- **The game server already admits players.** `ConnectionHandshake` admits a participant on `/ws` (plays) or `/ws?watch=1`
  (a watcher, read-only), an admin anywhere, and rejects anyone else `not_a_participant`. A replay must use `?watch=1`: a
  participant's plain `/ws` would replace that account's play socket (close 1000 "replaced") in another tab.
- **Management needs no change.** `GET /v1/games?joined=true` lists the account's games, each `GameSummary` carries `joined`;
  join, leave, end, delete, `POST /v1/games`, `GET /v1/properties`, `GET /v1/armies`, `GET /v1/account` exist. The `/v1/games`
  group is `RequireAuthorization()`; the viewer always sends the bearer.
- **No acknowledgement frame.** A refused edit or command gets `Refused(messageId)`. An accepted setup edit broadcasts
  `board`, an accepted lobby edit `game`; every command ends with one `at_rest` (broadcast when it appended, else to the
  sender alone), after its `Refused` if refused. The game's mailbox is serial and each socket's outbound queue is FIFO, so a
  send settles on its `Refused` or on the next frame of its kind; another player's frame can settle an accepted send early,
  never a refused one late (its `Refused` still reaches `onRefused`).
- **The JWT lasts five minutes and the socket closes 1008 at its expiry** unless a `Reauth` with a refreshed JWT moves it on.
  Godot refreshes every 60 s and sends `Reauth` on a welcomed socket; the browser does the same.
- **Generated wire types hold only what the live feed reads** (`GeneratedTypesTests.WireModule`): no `Command`, `Edit`,
  `CommandMessage`, `EditMessage`, `AtRest`, `Reauth`, `SightQuery`, `SightAnswer`, `ModelFacts`. Task 1 adds them.
- **Management DTOs are not generated** (`live/games.ts` types `GameSummary` by hand); C1 follows that pattern in
  `src/lib/api/types.ts`, matching `client/src/Core/ManagementDtos.cs` and the server records.
- **`lib/geometry` is setup and play only** (plan B's `geometry.lazy.test.ts` forbids a `geometry` import under
  `src/lib/{feed,live,board,timeline,recording,asks,hosted,suite}`, `src/lib/components`, `App.svelte`, `main.ts`). C1 imports
  none; C2's setup lives in `src/lib/setup/`.

## Global Constraints

- The browser judges nothing: every lobby change is an `Edit`, every play action a `Command`; the server refuses.
- No new endpoint, edit or command. `/v1` and the socket exactly as Godot speaks them.
- `src/lib/protocol/*.generated.ts` are never hand-edited; regenerate with `TABLETOP_WRITE_GENERATED=1 dotnet test
  tests/Tabletop.Viewer.Tests`.
- Svelte 5 runes; no SvelteKit and no code shared with `web/` beyond the `tabletop.session` entry.
- Logic with a decision in it is a `.ts` module with a Vitest test; components bind.
- Copy is Godot's own where Godot has it (`Feedback`, Home's tooltips and confirmations).
- New ids are `crypto.randomUUID()`.
- Files under 500 lines; `App.svelte` ends under 120.
- Commands: `cd viewer && npm test`, `cd viewer && npm run check`, `cd viewer && npm run build:hosted`, `cd web && npm test &&
  npm run check`, `dotnet test tests/Tabletop.Viewer.Tests`.

## Review Focus

- A reconnect mid-lobby: edits sent while `reconnecting` queue (at most 16) and go after the next `welcome`; the board and
  game rebuild from what the server resends. Test: `queued edits flush after the next welcome, in order` (Task 6).
- The same game open in two tabs: the older closes 1000 "replaced" and must not reconnect (it would steal the socket back in a
  loop). Test: `a replaced socket closes for good with Godot's words` (Task 6).
- A player who is no participant opening `#/game/<id>` by URL: `rejected not_a_participant` ends with Godot's
  `Feedback.Rejected` line and a link back to the list, no retry. Test: `rejected ends the connection without a retry` (Task 6).
- A JWT that expires while a tab sits in the lobby: the 60 s `Reauth` keeps the socket; a 1008 refreshes before reconnecting.
  Test: `a 1008 close asks for a fresh JWT before reconnecting` (Task 6).
- A non-admin's list never offers Replay on a game it is not in: `GameRowActions` gates it on `joined`. Test:
  `replay needs a joined game that is playing or ended` (Task 4).

---
### Task 1: Wire types for a sending client

**Files:** modify `tests/Tabletop.Viewer.Tests/GeneratedTypesTests.cs` (`WireModule`, the wire-module test),
`tests/Tabletop.Viewer.Tests/TypeScriptEmitter.cs` only if it cannot emit an abstract intermediate; regenerate
`viewer/src/lib/protocol/wire.generated.ts`.

**Interfaces:**

- Produces, in `wire.generated.ts`: `Command` (the 20 kinds of `libs/Tabletop.Protocol/Commands.cs`, e.g. `AnswerAsk`,
  `MoveModels`, `Undo`), `Edit` (the 21 kinds of `Edits.cs`, e.g. `CreateTeam`, `PickTeam`, `SetConfiguration`,
  `SetTableSize`, `ReplaceDraft`), `CommandMessage { kind: 'command'; commandId; command }`, `EditMessage { kind: 'edit';
  editId; edit }`, `AtRest { kind: 'at_rest'; seq }`, `Reauth { kind: 'reauth'; reauthId; sessionJwt }`, `SightQuery`,
  `SightAnswer`, `SightLine`, `ModelFacts`, all in `SocketMessage`.
- Still absent: `PresenceMessage`, `PingMessage`, `PresenceEnded`, `Presence`, `Ping`, `Cursor`, `Measurement` (presence is
  not ported, spec → What play shows).

- [ ] Change the test first: `The_wire_module_carries_the_frames_a_playing_client_sends_and_reads` asserts the module
  contains `export type Command =`, `export type Edit =`, `export interface CommandMessage {\n  readonly kind: 'command';`,
  `export interface EditMessage {\n  readonly kind: 'edit';`, `export interface AtRest {\n  readonly kind: 'at_rest';`,
  `export interface PickTeam {\n  readonly kind: 'pick_team';`, `export interface ReplaceDraft {\n  readonly kind:
  'replace_draft';`, `export interface Reauth`, `export interface ModelFacts`, and none of `PresenceMessage`, `PingMessage`,
  `presence_ended`, `Cursor`, `Measurement`. `Every_union_member_is_discriminated_by_a_literal_kind` raises its floor to 8
  (`Command`, `Edit` join the six).
- [ ] `dotnet test tests/Tabletop.Viewer.Tests --filter GeneratedTypesTests` fails.
- [ ] Add `CommandMessage`, `EditMessage`, `AtRest`, `Reauth`, `SightQuery`, `SightAnswer`, `ModelFacts` to the
  `SocketMessage` subtype list. If the emitter does not flatten `LobbyEdit`/`SetupEdit`, map `Edit` to its 21 concrete types
  in the same dictionary (the kinds in `[JsonDerivedType]` order). Run `TABLETOP_WRITE_GENERATED=1 dotnet test
  tests/Tabletop.Viewer.Tests`.
- [ ] `dotnet test tests/Tabletop.Viewer.Tests` passes; `cd viewer && npm test && npm run check` pass (the live feed's
  `HANDLED` set still drops every kind it does not read).
- [ ] Commit: `Viewer: generated wire types for commands, edits, at_rest, reauth and sight`.

### Task 2: Every signed-in player opens the hosted viewer

**Files:** modify `viewer/src/lib/hosted/hosted.ts`, `viewer/src/App.svelte` (the gate), `viewer/tests/hosted.test.ts`,
`web/src/routes/+layout.svelte`, `web/src/lib/replays.ts` (comment), `web/src/lib/session/jwt.ts` (drop `isAdmin`),
`web/tests/jwt.test.ts`.

**Interfaces:**

```ts
// hosted.ts
export type Access = 'signed-out' | 'player' | 'admin';
export const accessOf = (jwt: string | null): Access;   // null → signed-out; roles has 'admin' → admin; else player
```

- [ ] Tests first (`hosted.test.ts`): `accessOf gives a signed-in non-admin player access` (`accessOf(jwtWithRoles([]))` →
  `'player'`, `['admin']` → `'admin'`, null → `'signed-out'`); `hostedSession returns player for a refreshed non-admin`.
  Remove the `not-admin` expectations.
- [ ] `cd viewer && npx vitest run tests/hosted.test.ts` fails.
- [ ] Implement; `App.svelte` drops the *Not authorised* block and `hostedJwt` throws only when `sessionJwt` is null. In
  `web`, `+layout.svelte` shows the Replays link whenever `session.token` is set; `isAdmin` and its four assertions go
  (no other caller).
- [ ] `cd viewer && npm test && npm run check`; `cd web && npm test && npm run check`.
- [ ] Commit: `Viewer and web: the hosted viewer and its header link open to every signed-in player`.

### Task 3: The shell — routes, one sign-in, playback as a view

**Files:** create `viewer/src/lib/shell/route.ts`, `viewer/src/lib/shell/auth.ts`,
`viewer/src/lib/components/PlaybackView.svelte` (the panes, log tabs and timeline moved out of `App.svelte`),
`viewer/src/lib/components/ReplayPage.svelte`; modify `viewer/src/App.svelte`, `viewer/src/lib/components/SourcePicker.svelte`
(takes `auth` in place of `hostedJwt`, keeps its login form for the developer build); test `viewer/tests/route.test.ts`,
`viewer/tests/auth.test.ts`.

**Interfaces:**

```ts
// route.ts
export type Route =
  | { readonly page: 'games' } | { readonly page: 'game'; readonly gameId: string }
  | { readonly page: 'replay'; readonly gameId: string } | { readonly page: 'watch' } | { readonly page: 'tests' };
/** `#/games`, `#/game/<uuid>`, `#/replay/<uuid>`, `#/watch`, `#/tests`; anything else is the build's home. */
export function parseRoute(hash: string, home: Route): Route;
export function hrefOf(route: Route): string;            // '#/game/<id>' …
/** The tabs a viewer sees: players games; admins games and watch; the developer build adds tests. */
export function tabsOf(access: Access, runner: boolean): readonly Route['page'][];
/** The socket a replay opens: the same URL with `watch=1` (ConnectionHandshake: read-only, never replaces a play socket). */
export function watchUrl(socketUrl: string): string;
// auth.ts
export interface Auth {
  readonly access: Access | 'checking';
  /** A fresh JWT for one use (each call refreshes; the hosted build writes it back to `tabletop.session`). Throws signed out. */
  jwt(): Promise<string>;
  /** The account (`GET /v1/account`), fetched once after sign-in. */
  account(): Promise<{ readonly id: string; readonly name: string }>;
}
export function hostedAuth(storage: StorageLike, refresh: (jwt: string) => Promise<Session>, fetchFn?: typeof fetch): Auth;
/** The developer build: `signIn` (password or a pasted JWT) once per page; `access` from the JWT's roles. */
export function devAuth(refresh: (jwt: string) => Promise<Session>, fetchFn?: typeof fetch): Auth & {
  signIn(email: string, password: string): Promise<void>; usePasted(jwt: string): void;
};
```

Home: hosted `games`; developer `watch` (its recording flow and `?recording=` stay as they are). `App.svelte` holds `auth`,
listens to `hashchange`, renders the gate (checking / signed out with `signInHref`), the header tabs from `tabsOf`, and one of
`GamesPage` (Task 5), `GamePage` (C2 Task 1, which adds that branch), `ReplayPage`, the watch view (`SourcePicker` + `PlaybackView`), `TestsPage`.
`ReplayPage` opens `liveFeed({ url: watchUrl(socketUrl), gameId, sessionJwt: await auth.jwt() })` into `PlaybackView`.

- [ ] Tests first. `route.test.ts`: `parses each page and falls back to home` (`#/game/0190…` → game with that id;
  `#/game/notauuid` → home; `''` → home); `hrefOf round-trips through parseRoute`; `players see games, admins games and watch,
  the developer build adds tests`; `watchUrl adds watch=1` (`wss://h/ws` → `wss://h/ws?watch=1`; `ws://h:5200/ws?x=1` →
  `…?x=1&watch=1`). `auth.test.ts`: `hostedAuth refreshes on each jwt() and writes the token back`; `hostedAuth is signed out
  with nothing stored`; `devAuth throws before signIn and reads admin from the roles claim`; `account() fetches once`
  (two calls, one `GET /v1/account` with the bearer).
- [ ] `cd viewer && npx vitest run tests/route.test.ts tests/auth.test.ts` fails.
- [ ] Implement; move the playback markup verbatim into `PlaybackView.svelte` (`feed: Feed | null` prop; it owns
  `attachFeed`/`detachFeed`, the timeline, tabs and toggles).
- [ ] `npm test && npm run check && npm run build:hosted`; `npm run dev` and open `http://127.0.0.1:5174/#/watch`: a fixture
  recording still plays.
- [ ] Commit: `Viewer: a shell over hash routes, one sign-in, playback as a view, replays on watch sockets`.

### Task 4: Management calls for the games list, and who may do what

**Files:** create `viewer/src/lib/api/types.ts`, `viewer/src/lib/api/client.ts`, `viewer/src/lib/api/names.ts`,
`viewer/src/lib/games/rowActions.ts`;
modify `viewer/src/lib/live/games.ts` (`GameSummary.joined`, `listGames(jwt, { joined })`); test `viewer/tests/api.test.ts`,
`viewer/tests/rowActions.test.ts`, `viewer/tests/games.test.ts`, `viewer/tests/names.test.ts`.

**Interfaces:**

```ts
// types.ts (hand-typed, from client/src/Core/ManagementDtos.cs and the server records)
export type GameStatus = import('../protocol/wire.generated').GameStatus;
export interface CreateGameRequest { readonly name: string; readonly propertiesHash: string;
  readonly schedulerShape: SchedulerShape; readonly enforcement: Enforcement; readonly waitsBetweenSteps: false }
export interface PropertiesSummary { readonly contentHash: string; readonly name: string; readonly gameRules: string }
export interface ArmySummary { readonly id: string; readonly name: string; readonly gameSystem: string; readonly source: string;
  readonly units: number; readonly models: number }
export interface Page<T> { readonly items: readonly T[]; readonly next?: string | null }
// client.ts — one function per endpoint; problem+json → ManagementError(detail ?? title, status)
export interface Api {
  createGame(r: CreateGameRequest): Promise<GameInfo>;  joinGame(id: string): Promise<GameInfo>;
  leaveGame(id: string): Promise<void>;  endGame(id: string): Promise<void>;  deleteGame(id: string): Promise<void>;
  listProperties(): Promise<PropertiesSummary[]>;
  listArmies(): Promise<ArmySummary[]>;                    // every page of /v1/armies?limit=200
}
export function apiOf(jwt: () => Promise<string>, base?: string, fetchFn?: typeof fetch): Api;
// rowActions.ts — GameRowActions.Of, ported
export interface RowActions { readonly canEnd: boolean; readonly canDelete: boolean; readonly canReplay: boolean }
export function rowActionsOf(row: GameSummary | null, me: string | null): RowActions;
// names.ts — TextLimits.Name and NameProblem (management's Limits.Name), word for word
export const NAME = 64;
export function nameProblem(value: string | null, field?: string): string | null;   // field default 'A name'
```

C2 adds the layout, built-in and asset calls to `Api`.

- [ ] Tests first. `rowActions.test.ts` (Godot's cases): `nothing without a row or an account`; `the creator ends a game that
  has not ended and deletes any`; `a non-creator neither ends nor deletes`; `replay needs a joined game that is playing or
  ended` (joined lobby → false; not joined playing → false; joined ended → true). `api.test.ts` with a fake fetch:
  `createGame posts the request with the bearer`; `listArmies follows next until it is absent`; `a 422 problem throws its
  detail with the status`; `leaveGame accepts 204`. `games.test.ts`: `listGames with joined asks for joined=true`. `names.test.ts`: `a blank name
  is required`, `65 characters is too long`, `a bidi mark is refused`, `a good name passes` (Godot's texts).
- [ ] `cd viewer && npx vitest run tests/rowActions.test.ts tests/api.test.ts tests/games.test.ts tests/names.test.ts`
  fails.
- [ ] Implement; every request sends `Tabletop-Client-Version: <PROTOCOL_VERSION>` (`ProtocolVersion.Header`) as `ManagementClient` does.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: management calls for games and armies; GameRowActions ported`.

### Task 5: The games page

**Files:** create `viewer/src/lib/games/GamesPage.svelte`, `viewer/src/lib/games/CreateGameDialog.svelte`,
`viewer/src/lib/games/listModel.ts`; test `viewer/tests/listModel.test.ts`.

**Interfaces:**

```ts
// listModel.ts
export interface GameLists { readonly mine: readonly GameSummary[]; readonly open: readonly GameSummary[] }
/** Mine: joined, newest first. Open: not joined and in lobby or setup (teams still editable). */
export function listsOf(all: readonly GameSummary[]): GameLists;
/** HomeScreen's Create: the defaults the dialog opens with (first rule set, alternating activation, Enforcement 'warn'). */
export function createDefaults(packs: readonly PropertiesSummary[]): CreateGameRequest | null;   // null: no rule set loaded
```

Mine rows: name, status, players, created; **Open** (`#/game/<id>`; `joinGame` first when the row is not joined), **Replay**
(`#/replay/<id>`, when `canReplay`), **Leave** (`leaveGame`, asked first: "Leave <name>? Your seat and the army you brought
go."), **End** / **Delete** (creator, asked first with Godot's texts: "<name> ends for every player." / "<name> and its whole log
are deleted for every player. This cannot be undone."), each disabled with Godot's tooltip when not allowed. Open rows:
**Join** (`joinGame`, then `#/game/<id>`). Admins also get **All games** (every game, Replay on each: an admin watches any).
**Create game** opens `CreateGameDialog`: name (`nameProblem`, shown as typed;
Create waits until it is null), rule set, activation (Alternating activation /
Player turn), enforcement (Silent / Warn / Strict), an army (optional; "No army (pick one in the lobby)"); Create posts
`createGame`, navigates to `#/game/<id>` and hands the army to `GamePage` as `?army=<id>` in the hash, which sends
`PickArmy(armyId)` once welcomed (HomeScreen's `PickWhenConnected`). Lists reload after every action.

- [ ] Tests first: `listsOf splits joined from open lobby and setup games` (a joined ended game in mine; an unjoined playing
  game in neither); `createDefaults takes the first pack, alternating activation and warn`; `createDefaults is null without
  packs`. Add to `route.test.ts`: `a game route carries an optional army` (`#/game/<id>?army=<id2>` → `{ page: 'game', gameId,
  army: id2 }`; extend `Route` with `army?: string`).
- [ ] `cd viewer && npx vitest run tests/listModel.test.ts tests/route.test.ts` fails.
- [ ] Implement the model and the two components (`<dialog>` with `showModal`, `role="alert"` for failures).
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the games page — mine, open, create, join, replay, leave, end, delete`.

### Task 6: `GameConnection` — the participant socket

**Files:** create `viewer/src/lib/connection/gameConnection.ts`, `viewer/src/lib/connection/feedback.ts`; test
`viewer/tests/gameConnection.test.ts`, `viewer/tests/feedback.test.ts`.

**Interfaces** (plan D consumes exactly this):

```ts
// gameConnection.ts
import type { Board, Command, Edit, GameEvent, GameInfo, Refused, RulesOptionCatalog, SightAnswer, TableState }
  from '../protocol/wire.generated';
import type { Projection } from '../live/apply';
import type { SocketLike } from '../feed/liveFeed';

export type ConnectionPhase = 'connecting' | 'connected' | 'reconnecting' | 'closed';
export interface ConnectionState {
  readonly phase: ConnectionPhase;
  readonly game: GameInfo | null;
  /** Welcome's participant row; the all-zero id for an admin watcher. */
  readonly participantId: string | null;
  readonly options: RulesOptionCatalog;              // Welcome.options, empty until welcomed
  readonly draft: TableState | null;                 // setup: the last board's table
  readonly board: Board | null;                      // from playing on: the snapshot the log applies to
  readonly log: readonly GameEvent[];                // every event applied, in order
  readonly projection: Projection | null;            // Applier(board, game) after `log`
  readonly maxWounds: ReadonlyMap<string, number>;   // ModelFacts
  readonly deployInside: ReadonlyMap<string, readonly string[]>;
  /** From a board until a model_facts frame or an empty events page (Session.CatchingUp). */
  readonly catchingUp: boolean;
  /** Player text for the last close or rejection (feedback.ts); null while nothing went wrong. */
  readonly closeReason: string | null;
}
/** How a send ended: its Refused, or null once the next frame of its kind arrived (`board` for a setup edit, `game` for a
 *  lobby edit, `at_rest` for a command). */
export interface SendOutcome { readonly refused: Refused | null }
export class SendError extends Error {
  constructor(message: string, readonly why: 'not_connected' | 'queue_full' | 'disconnected' | 'closed');
}
export interface GameConnection {
  readonly gameId: string;
  readonly state: ConnectionState;
  /** Called at once with the current state, then on every change. */
  subscribe(listener: (state: ConnectionState) => void): () => void;
  /** Each events batch after it is applied and published (D's log, dice and highlights). */
  onEvents(listener: (events: readonly GameEvent[]) => void): () => void;
  /** Every Refused, with Feedback.Refusal's text (null for a watcher's refused presence). */
  onRefused(listener: (refused: Refused, text: string | null) => void): () => void;
  /** Sends now when welcomed, queues while reconnecting (16 at most), else throws SendError('not_connected'). */
  sendEdit(edit: Edit): Promise<SendOutcome>;
  sendCommand(command: Command): Promise<SendOutcome>;
  /** Resolves on the SightAnswer with this query's id. */
  querySight(observerUnitId: string, targetUnitId: string): Promise<SightAnswer>;
  /** The player leaves the page: closes for good, rejects what is pending with SendError('closed'). */
  close(): void;
}
export interface ConnectionOptions {
  readonly url: string; readonly gameId: string;
  /** A fresh JWT; called for every Connect and Reauth. */
  readonly jwt: () => Promise<string>;
  readonly open?: (url: string) => SocketLike;
  readonly backoffMs?: (attempt: number) => number;   // default 1000, 2000, 5000, 10000, then 10000
  readonly reauthEveryMs?: number;                    // default 60000
}
export function connectGame(options: ConnectionOptions): GameConnection;
// feedback.ts — Feedback.Refusal, Closed, Rejected, word for word
export function refusalText(r: Refused): string | null;
export function closedText(code: number | null, reason: string | null, willReconnect: boolean, desync?: boolean): string;
export function rejectedText(reason: RejectReason): string;
```

Behaviour, Session's: `connect` with `jwt()` on open; no `welcome` within 10 s counts as a dropped connection. `welcome` sets
phase `connected`, resets the attempt count, flushes the queue in order. `game` replaces `game`; `board` while the game is
`setup` replaces `draft`, from `playing` builds a new `Applier` (`live/apply.ts`) and clears `log`; `events` apply one by one
(an empty page ends `catchingUp`). A `ProjectionDesyncError` reconnects at once, once per log position (`_desyncFloor`); the
same position again closes with `closedText(…, desync)`. `rejected` closes for good with `rejectedText`. A close: by the
player, or `1000` after a welcome (replaced, too many windows) → `closed`; else `reconnecting` after `backoffMs(attempt)`, a
`1008` calling `jwt()` first (each Connect does anyway). Leaving `reconnecting` for `closed` rejects the queue with
`SendError('disconnected')`. While `connected`, every `reauthEveryMs` sends `Reauth { reauthId, sessionJwt: await jwt() }`.
`presence`, `ping`, `presence_ended` are dropped.

- [ ] Tests first, on `liveFeed.test`'s `FakeSocket` and `vi.useFakeTimers()`: `sends connect with a fresh jwt on open`;
  `a welcome publishes game, participant and options`; `setup boards replace the draft`; `a playing board starts the
  projection and events apply in order` (skirmish fixture: `projection.lastSeq` equals the last event's seq);
  `an edit settles null on the next board` and `a lobby edit settles null on the next game`; `a refused edit settles with its
  Refused and reaches onRefused with Godot's text` ("Not at this point of the game: …"); `a command settles null on at_rest`;
  `a refused command settles on the Refused, not the at_rest after it`; `querySight resolves on its answer`;
  `queued edits flush after the next welcome, in order`; `the seventeenth queued send throws queue_full`; `a dropped socket
  reconnects after 1, 2, 5 and 10 s`; `a 1008 close asks for a fresh JWT before reconnecting`; `a replaced socket closes
  for good with Godot's words` ("This game was opened in another window, which took over."); `rejected ends the connection
  without a retry`; `a desync reconnects once per log position, then closes`; `reauth goes every 60 s while connected`;
  `close rejects pending sends with closed`. `feedback.test.ts`: one case per `Feedback` arm (reasons, codes 1000 ×2, 1001,
  1008 with and without reconnect, 1009, 1011, 1013, unreachable, dropped, other; the three rejections).
- [ ] `cd viewer && npx vitest run tests/gameConnection.test.ts tests/feedback.test.ts` fails.
- [ ] Implement. Pending sends: a map by message id (`crypto.randomUUID()`) holding the settle kind and the resolver; a frame
  of a kind settles the oldest pending send of that kind (each accepted send yields one such frame to its sender);
  a command's `Refused` also consumes the `at_rest` that follows it, so that frame settles nothing.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: GameConnection — edits and commands with refusals, queueing, reconnects, reauth and resync`.

### Task 7: The board's interaction API, circles and a shared view

**Files:** create `viewer/src/lib/board/interaction.ts`, `viewer/src/lib/board/pointer.ts`; modify
`viewer/src/lib/board/frame.ts` (`ModelShape.circle`, `ModelShape.z`), `viewer/src/lib/board/draw.ts`,
`viewer/src/lib/board/pick.ts` (markers), `viewer/src/lib/board/transform.ts` (`fitBox`),
`viewer/src/lib/components/Board.svelte`; test `viewer/tests/pointer.test.ts`, `viewer/tests/frame.test.ts`,
`viewer/tests/pick.test.ts`, `viewer/tests/transform.test.ts`.

**Interfaces** (C2's setup and D's play drive the board only through these):

```ts
// interaction.ts
import type { Point, View } from './transform';
export type BoardHit =
  | { readonly kind: 'model'; readonly id: string }
  | { readonly kind: 'terrain'; readonly id: string; readonly volumeId: string }
  | { readonly kind: 'marker'; readonly id: string };
export interface Modifiers { readonly shift: boolean; readonly ctrl: boolean; readonly alt: boolean }
/** Table inches throughout, never pixels. Every handler is optional; without onDragStart a press pans. */
export interface BoardInteraction {
  /** Hit testing; default pickAt(scene, at): models, then markers, then terrain, last drawn first. */
  pick?(at: Point): BoardHit | null;
  onHover?(hit: BoardHit | null, at: Point): void;
  /** A press and release under the drag threshold (3 px). */
  onSelect?(hit: BoardHit | null, mods: Modifiers): void;
  /** Left press on a hit, moved past the threshold: true takes the drag, false pans. */
  onDragStart?(hit: BoardHit, at: Point, mods: Modifiers): boolean;
  onDragMove?(at: Point, mods: Modifiers): void;
  onDragEnd?(at: Point, mods: Modifiers): void;
  /** Escape, pointer cancel or the canvas losing the pointer mid-drag. */
  onDragCancel?(): void;
  /** Right click; `screen` is canvas pixels, for a menu. */
  onContextMenu?(hit: BoardHit | null, at: Point, screen: Point): void;
  /** Keys while the canvas has focus (it is focusable); true when handled. Escape cancels a drag before this is asked. */
  onKey?(key: string, mods: Modifiers): boolean;
}
/** Drawn after the scene, in table inches: ghosts, landing rings, rulers, rings. */
export type Overlay = (ctx: CanvasRenderingContext2D, project: (p: Point) => Point, pixelsPerInch: number) => void;
// pointer.ts — the press/drag/pan state machine Board.svelte runs, DOM-free
export type PointerInput =
  | { readonly type: 'down'; readonly screen: Point; readonly at: Point; readonly button: 0 | 2; readonly mods: Modifiers }
  | { readonly type: 'move'; readonly screen: Point; readonly at: Point; readonly mods: Modifiers }
  | { readonly type: 'up'; readonly screen: Point; readonly at: Point; readonly mods: Modifiers }
  | { readonly type: 'cancel' };
export type PointerEffect =
  | { readonly type: 'hover'; readonly hit: BoardHit | null; readonly at: Point }
  | { readonly type: 'select'; readonly hit: BoardHit | null; readonly mods: Modifiers }
  | { readonly type: 'dragStart' | 'dragMove' | 'dragEnd'; readonly at: Point; readonly mods: Modifiers }
  | { readonly type: 'dragCancel' } | { readonly type: 'pan'; readonly dx: number; readonly dy: number }
  | { readonly type: 'context'; readonly hit: BoardHit | null; readonly at: Point; readonly screen: Point };
export type PointerState = { readonly kind: 'idle' }
  | { readonly kind: 'pressed'; readonly screen: Point; readonly at: Point; readonly hit: BoardHit | null; readonly mods: Modifiers }
  | { readonly kind: 'dragging'; readonly last: Point } | { readonly kind: 'panning'; readonly last: Point };
export const DRAG_THRESHOLD_PX = 3;
export function step(state: PointerState, input: PointerInput, pick: (at: Point) => BoardHit | null,
  takesDrag: (hit: BoardHit, at: Point, mods: Modifiers) => boolean): { state: PointerState; effects: PointerEffect[] };
// transform.ts
/** Fits a box in table inches (the table, widened to hold terrain set beside it) into the viewport. */
export function fitBox(box: { minX: number; minY: number; maxX: number; maxY: number }, table: Table, viewport: Viewport): View;
// frame.ts: ModelShape gains `circle: Shape` (centre, radius = the base's longer side / 2) and `z: number`.
// draw.ts: DrawOptions gains `models: 'bases' | 'circles'` and `selected: ReadonlySet<string>` (was one id).
```

`Board.svelte` props: `scene`, `view = $bindable(null)` (the parent holds it, so setup and play share one pan and zoom),
`extent?` (a box for `fitBox`; default the table), `interaction?`, `overlays?: readonly Overlay[]`, `selected`, `models`,
`showGrid`, `showZones`, `showLabels`. Circles draw in the team colour with the facing tick; a model with `z > 0.01` gets a
small height label (`3"`), spec → Heights. `PlaybackView` passes `onSelect` (the old `onPick`) and `models: 'bases'` on the
watch page, `'circles'` on a replay.

- [ ] Tests first. `pointer.test.ts`: `a still click selects what is under it`; `a press on empty table moved 3 px pans`;
  `a press on a hit that takesDrag starts a drag and streams moves in inches`; `takesDrag false pans instead`; `up ends the
  drag with the last point`; `cancel mid-drag emits dragCancel`; `a right press emits context and no drag`; `moving with no
  press hovers`. `frame.test.ts`: `a model's circle is as wide as its base's longer side` (oval 60×35 mm in inches → radius
  = baseA/2; rect 1×2 → 1). `pick.test.ts`: `markers pick after models and before terrain`. `transform.test.ts`:
  `fitBox keeps a beside strip in view` (72×48 table, box maxX 100 → the point (100, 24) on screen).
- [ ] `cd viewer && npx vitest run tests/pointer.test.ts tests/frame.test.ts tests/pick.test.ts tests/transform.test.ts` fails.
- [ ] Implement; `Board.svelte` turns DOM events into `PointerInput` and effects into handler calls and pans.
- [ ] `npm test && npm run check`; a recording on `#/watch` still selects, pans and zooms.
- [ ] Commit: `Viewer: the board's interaction API — select, drag, hover, context, keys, overlays; models as circles`.

