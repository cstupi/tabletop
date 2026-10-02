# Task 11: The live feed — a spectator socket to the game server

**Files:**
- Create: `viewer/src/lib/live/management.ts`, `viewer/src/lib/feed/liveFeed.ts`
- Modify: `viewer/src/lib/components/SourcePicker.svelte` (add the connect form), `viewer/vite.config.ts` (`VITE_GAME_SOCKET` default)
- Test: `viewer/tests/live.test.ts`

**Interfaces:**
- Consumes: `SocketMessage`, `Connect`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `Refused`, `Rejected`, `GameInfo`, `Board`, `GameEvent` (Task 2); `Emitter`, `IDLE`, `Feed`, `FeedState`, `ControlChannel` (Task 8); `FrameProjector` and `ProjectionDesyncError` (Task 9); `loadFixture` (Task 10, in the test).
- Produces (`management.ts`): `class ManagementError extends Error`; `type Session = { readonly sessionJwt: string }`; `async function logIn(email: string, password: string, base?: string): Promise<Session>`; `async function refreshSession(sessionJwt: string, base?: string): Promise<Session>`.
- Produces (`liveFeed.ts`): `const CLIENT_VERSION: Connect['clientVersion'] = 1`; `type SocketLike = { send(data: string): void; close(): void; addEventListener(type: string, handler: (event: never) => void): void }`; `type LiveOptions = { readonly url: string; readonly gameId: string; readonly sessionJwt: string; readonly label?: string }`; `function liveFeed(options: LiveOptions, open?: (url: string) => SocketLike): Feed`; `function isServerFrame(value: unknown): value is SocketMessage`.
- Preconditions the viewer does not arrange: the account whose JWT this is must already be a **participant with no team** on that game — "attached as a participant with no team. Presence, the scheduler and the interpreter already treat that participant as a spectator who is never Asked" (spec → *Two feeds*). Joining is `POST /v1/games/{id}/join` on the management API and is out of scope; a JWT for an account that is not a participant gets `Rejected(not_a_participant)`, which the viewer reports verbatim.

Spec: spec → *Two feeds*; *Interactive debugging is a door, not a room* — "The viewer sends no command and answers no Ask." Game server plan → *Interfaces needed → Client shell plan* — the connect sequence, the frame order and every rejection case, followed exactly.

**The connect sequence, as the game server plan states it.** Open `ws://localhost:5200/ws`; send `Connect` as the first frame within 10 s; expect `Welcome`, then `BoardMessage` from `setup` on, then at `playing`/`ended` one or more `EventsMessage`s ending with the whole log delivered — an empty `EventsMessage` when the log is empty — then live frames. A game ended before setup ever ran has no `table_state` row and no log, so `Welcome` is the whole sequence and no board arrives. A `Rejected` frame is followed by a close. A second socket for the same participant closes the first with `1000`. The JWT is verified once, so nothing refreshes it on an open socket — `refreshSession` exists only for the next connect.

**Frames the viewer does not read, `at_rest` among them.** The socket also carries `presence`, `ping`, `presence_ended` and `at_rest(seq)` — the frame the server sends when it has finished processing and has nothing further to emit. The viewer acts on none of them and must ignore each without faulting, which is what `HANDLED` and the `switch`'s `default` arm are for. `at_rest` is pruned out of `wire.generated.ts` (Task 2), so it arrives as a kind the generated union does not declare, and that is exactly the case the ignore path exists to survive: a viewer that threw on an unrecognised kind would die the next time the protocol grew one. Nothing about the timing matters here either — the viewer republishes on every `EventsMessage`, so it is never waiting to hear that the server has stopped, and `at_rest` arriving after a run of events with nothing appended is a frame it never looks at.

**A desync stops the feed.** `FrameProjector.add` throws `ProjectionDesyncError` on an event naming an id the projection does not hold (Task 9). `drain` catches exactly that type, drops the projector and the pending events, and reports `status: 'error'` with the event kind and the id. It does not skip the event and continue, because a viewer silently diverging from the server is worse than one that visibly stops, and the whole point of this tool is to show what the server did. It also does not reconnect on its own: reconnecting is the source picker's connect form, a fresh `liveFeed` whose connect sequence redelivers the whole log and rebuilds the projection from scratch. Doing it by hand is deliberate for a developer tool — an automatic retry loop would repair the symptom repeatedly and hide the fault being debugged. If the server's own log genuinely names an id nothing created, reconnecting faults again at the same event, which is a server bug and belongs to the game server, not here; the game server faults the session rather than resyncing.

- [ ] **Step 1: Write `viewer/tests/live.test.ts`**

```ts
import { describe, expect, it, vi } from 'vitest';
import { liveFeed, type SocketLike } from '../src/lib/feed/liveFeed';
import { attachFeed, IDLE, type FeedState } from '../src/lib/feed/feed';
import { recordingFeed } from '../src/lib/feed/recordingFeed';
import { loadFixture } from './fixture';

class FakeSocket implements SocketLike {
  readonly sent: string[] = [];
  readonly #handlers = new Map<string, (event: never) => void>();
  closed = false;
  send(data: string) { this.sent.push(data); }
  close() { this.closed = true; this.fire('close', {}); }
  addEventListener(type: string, handler: (event: never) => void) { this.#handlers.set(type, handler); }
  fire(type: string, event: unknown) { this.#handlers.get(type)?.(event as never); }
  deliver(message: unknown) { this.fire('message', { data: JSON.stringify(message) }); }
}

function attach() {
  const socket = new FakeSocket();
  const seen: FeedState[] = [];
  const feed = liveFeed({ url: 'ws://localhost:5200/ws', gameId: 'g1', sessionJwt: 'jwt' }, () => socket);
  feed.subscribe((s) => seen.push(s));
  return { socket, seen, feed, last: () => seen[seen.length - 1]! };
}

describe('the live feed', () => {
  it('sends Connect as its first and only frame', () => {
    const { socket } = attach();
    socket.fire('open', {});
    expect(JSON.parse(socket.sent[0]!)).toEqual({ kind: 'connect', sessionJwt: 'jwt', gameId: 'g1', clientVersion: 1 });
    expect(socket.sent).toHaveLength(1);
  });

  it('leaves the control channel empty, so nothing else can be sent', () => {
    expect(attach().feed.control).toBeNull();
  });

  it('follows welcome, board and events into frames the renderer already draws', () => {
    const { game, board, events, recording } = loadFixture('skirmish');
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    expect(last()).toMatchObject({ status: 'open', frames: [] });
    expect(last().meta).toMatchObject({ driver: 'live', outcome: 'live' });
    socket.deliver({ kind: 'board', board });
    expect(last().board!.models).toHaveLength(recording.board.models.length);
    socket.deliver({ kind: 'events', events: events.slice(0, 5) });
    expect(last().frames).toHaveLength(5);
    expect(last().frames[0]!.line).toBe(recording.frames[0]!.line);
    socket.deliver({ kind: 'events', events: events.slice(5, 8) });
    expect(last().frames).toHaveLength(8);
    expect(last().frames[7]!.seq).toBe(recording.frames[7]!.seq);
    expect(last().meta!.lastSeq).toBe(recording.frames[7]!.seq);
  });

  it('holds events that arrive before the board, then applies them in order', () => {
    const { game, board, events } = loadFixture('skirmish');
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    socket.deliver({ kind: 'events', events: events.slice(0, 3) });
    expect(last().frames).toHaveLength(0);
    socket.deliver({ kind: 'board', board });
    expect(last().frames).toHaveLength(3);
  });

  it('accepts an empty events frame — a game with no log', () => {
    const { game, board } = loadFixture('skirmish');
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    socket.deliver({ kind: 'board', board });
    socket.deliver({ kind: 'events', events: [] });
    expect(last()).toMatchObject({ status: 'open', frames: [] });
  });

  it('reports a rejection verbatim and goes to error, then closed', () => {
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'rejected', reason: 'not_a_participant' });
    expect(last()).toMatchObject({ status: 'error', detail: 'rejected: not_a_participant' });
    socket.close();
    expect(last().status).toBe('error');       // an error survives the close that follows a rejection
  });

  it('reports a refusal without losing the feed', () => {
    const { game } = loadFixture('skirmish');
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    socket.deliver({ kind: 'refused', messageId: 'x', reason: 'not_permitted', detail: 'spectator' });
    expect(last()).toMatchObject({ status: 'open', detail: 'refused: not_permitted — spectator' });
  });

  it('ignores presence, pings and anything else it does not read', () => {
    const { game } = loadFixture('skirmish');
    const { socket, last, seen } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    const before = seen.length;
    socket.deliver({ kind: 'presence', presence: { participantId: 'p1' } });
    socket.deliver({ kind: 'ping', ping: { sender: 'p1', point: { x: 1, y: 1, z: 0 } } });
    socket.deliver({ kind: 'presence_ended', participantId: 'p1' });
    // at_rest is the thirteenth SocketMessage kind and is pruned out of
    // wire.generated.ts, so it arrives as a kind the viewer has no declaration for. It is dropped like
    // any other: no fault, no emission, and no edit here when the protocol grows a fourteenth.
    socket.deliver({ kind: 'at_rest', seq: 42 });
    socket.deliver({ kind: 'a_kind_that_does_not_exist_yet', whatever: true });
    socket.fire('message', { data: 'not json' });
    expect(seen).toHaveLength(before);
    expect(last().status).toBe('open');
  });

  it('reports a desync and stops, rather than carrying a diverged projection', () => {
    const { game, board } = loadFixture('skirmish');
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    socket.deliver({ kind: 'board', board });
    // A marker id nothing placed: a desync, which the ported applier throws on.
    socket.deliver({ kind: 'events', events: [{ seq: 1, actor: null, warning: null, payload: {
      kind: 'marker_moved', markerId: '0198b7ac-0000-7000-8000-0000000000ff',
      position: { x: 5, y: 5, z: 0 }, yaw: { degrees: 90 } } }] });
    expect(last().status).toBe('error');
    expect(last().detail).toContain('desync');
    // The diverged copy is gone rather than drawn: a later events frame changes nothing.
    socket.deliver({ kind: 'events', events: [] });
    expect(last().status).toBe('error');
  });

  it('closes on request and on the socket closing', () => {
    const { socket, feed, last } = attach();
    socket.fire('open', {});
    feed.close();
    expect(socket.closed).toBe(true);
    expect(last().status).toBe('closed');
  });

  it('reports a socket error', () => {
    const { socket, last } = attach();
    socket.fire('error', {});
    expect(last()).toMatchObject({ status: 'error' });
  });

  it('cannot clobber the feed that replaced it, however late its last frames arrive', () => {
    const { game, board, events, recording } = loadFixture('skirmish');
    const socket = new FakeSocket();
    let state: FeedState = IDLE;
    const listener = (s: FeedState) => { state = s; };

    // attachFeed is the function App.svelte calls, not a copy of what it does: the order under test
    // lives in one place, so a component that got it wrong could not make this pass.
    const live = liveFeed({ url: 'ws://localhost:5200/ws', gameId: 'g1', sessionJwt: 'jwt' }, () => socket);
    let stop = attachFeed(null, null, live, listener);
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    socket.deliver({ kind: 'board', board });
    socket.deliver({ kind: 'events', events: events.slice(0, 5) });
    expect(state.frames).toHaveLength(5);

    stop = attachFeed(live, stop, recordingFeed({ label: 'run.json', recording }), listener);
    expect(state.label).toBe('run.json');

    // A socket close is asynchronous and frames can still be behind it. Neither reaches the new feed.
    socket.deliver({ kind: 'events', events: events.slice(5, 8) });
    socket.fire('close', {});
    expect(state.label).toBe('run.json');
    expect(state.frames).toEqual(recording.frames);
  });

  it('gives up if the server has not answered the connect', () => {
    vi.useFakeTimers();
    const { socket, last } = attach();
    socket.fire('open', {});
    vi.advanceTimersByTime(10_000);
    expect(last()).toMatchObject({ status: 'error', detail: 'no welcome within 10 s' });
    vi.useRealTimers();
  });
});
```

- [ ] **Step 2: Run; expect the import to fail. Write `management.ts`**

```ts
// viewer/src/lib/live/management.ts
// The only thing the viewer wants from the management API: a session JWT to put in Connect.
// POST /v1/auth/password and POST /v1/auth/session/refresh (management plan Task 5). Nothing else
// is called — the viewer never touches /v1/games, /v1/armies or /v1/assets.

export class ManagementError extends Error {}

export type Session = { readonly sessionJwt: string };

const base = (): string => import.meta.env.VITE_API_BASE ?? '';

async function post(path: string, body: unknown): Promise<Session> {
  const response = await fetch(`${base()}${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    // Management answers application/problem+json (management plan Global Constraints).
    const problem = (await response.json().catch(() => ({}))) as { title?: string; detail?: string };
    throw new ManagementError(problem.detail ?? problem.title ?? `${path} answered ${response.status}`);
  }
  const session = (await response.json()) as { sessionJwt?: string };
  if (typeof session.sessionJwt !== 'string') throw new ManagementError(`${path} returned no sessionJwt`);
  return { sessionJwt: session.sessionJwt };
}

export const logIn = (email: string, password: string): Promise<Session> =>
  post('/v1/auth/password', { email, password });

/**
 * The JWT lasts five minutes and is verified once, at connect (game server plan → Client shell plan:
 * "an open socket is never re-authenticated"), so this matters only for the next connect.
 */
export const refreshSession = (sessionJwt: string): Promise<Session> =>
  post('/v1/auth/session/refresh', { sessionJwt });
```

- [ ] **Step 3: Write `liveFeed.ts`**

```ts
// viewer/src/lib/feed/liveFeed.ts
import type {
  Board, Connect, GameEvent, GameInfo, SocketMessage,
} from '../protocol/wire.generated';
import type { RecordedTeam, RecordingMeta } from '../protocol/recording.generated';
import { ProjectionDesyncError } from '../live/apply';
import { FrameProjector } from '../live/project';
import { Emitter, IDLE, type ControlChannel, type Feed, type FeedState } from './feed';

/**
 * The number is hard-coded (Interfaces needed, item 6); its type is not — it comes from the wire.
 * `ProtocolVersion.Current` is 1, and the thirteenth `SocketMessage` kind, `at_rest`, does not move
 * it: adding a kind is additive, and a client that ignores it stays compatible. So the literal
 * is 1 and nothing about the handshake changes.
 */
export const CLIENT_VERSION: Connect['clientVersion'] = 1;
const WELCOME_TIMEOUT_MS = 10_000;

/** Just enough of WebSocket to be replaceable in a test. */
export type SocketLike = {
  send(data: string): void;
  close(): void;
  addEventListener(type: string, handler: (event: never) => void): void;
};

export type LiveOptions = {
  readonly url: string;
  readonly gameId: string;
  readonly sessionJwt: string;
  readonly label?: string;
};

/**
 * The six server kinds this feed acts on. Everything else the socket carries is dropped: `presence`,
 * `ping`, `presence_ended`, `at_rest`, and whatever kind the protocol grows next. Membership is tested
 * rather than switched on, so an unknown kind costs one lookup and never a fault — `wire.generated.ts`
 * declares seven members of `SocketMessage` and the wire has thirteen, so this set is the authority on
 * what is read, not the generated union.
 */
const HANDLED = new Set(['welcome', 'game', 'board', 'events', 'refused', 'rejected']);

export function isServerFrame(value: unknown): value is SocketMessage {
  return typeof value === 'object' && value !== null && HANDLED.has((value as { kind?: unknown }).kind as string);
}

/**
 * A live game, over the socket, attached as a participant with no team — a spectator who is never Asked
 * (spec → Two feeds). It sends Connect and nothing else, ever.
 */
export function liveFeed(
  options: LiveOptions,
  open: (url: string) => SocketLike = (url) => new WebSocket(url) as unknown as SocketLike,
): Feed {
  const started = Date.now();
  const emitter = new Emitter({ ...IDLE, label: options.label ?? `${options.url} · ${options.gameId}`, status: 'loading' });
  const socket = open(options.url);
  let game: GameInfo | null = null;
  let projector: FrameProjector | null = null;
  const pending: GameEvent[] = [];
  let welcomed = false;
  let failed = false;

  const timeout = setTimeout(() => {
    if (!welcomed) fail('no welcome within 10 s');
  }, WELCOME_TIMEOUT_MS);

  function fail(detail: string): void {
    failed = true;
    clearTimeout(timeout);
    emitter.emit({ status: 'error', detail });
  }

  function metaOf(info: GameInfo, lastSeq: number, rounds: number): RecordingMeta {
    const teams: RecordedTeam[] = info.teams.map((team) => ({
      id: team.id, ordinal: team.ordinal, name: `Team ${team.ordinal}`,
      participants: info.participants.filter((p) => p.teamId === team.id).map((p) => p.name),
    }));
    return {
      scenario: info.name, seed: 0, driver: 'live', gameRules: info.gameRules,
      propertiesHash: info.propertiesHash, schedulerShape: info.schedulerShape,
      enforcement: info.enforcement, teams, outcome: 'live',
      rounds, lastSeq, startedAt: new Date(started).toISOString(), durationMs: Date.now() - started,
    } as RecordingMeta;
  }

  function publish(): void {
    if (game === null || projector === null) return;
    const frames = projector.frames;
    const last = frames.length === 0 ? null : frames[frames.length - 1]!;
    emitter.emit({
      status: 'open', board: projector.board, frames: [...frames],
      meta: metaOf(game, last?.seq ?? 0, last?.round ?? 0),
    });
  }

  function drain(): void {
    if (projector === null) return;
    try {
      for (const event of pending.splice(0)) projector.add(event);
    } catch (error) {
      if (!(error instanceof ProjectionDesyncError)) throw error;
      // A desync: an event named an id this projection does not hold, so this copy of the game
      // has diverged from the server's. The projection is dropped rather than carried on with. The
      // viewer does not resync by itself: it is the tool a desync is meant to be visible in, and an
      // automatic reconnect would repair the symptom in a loop and hide it. Reconnecting is the source
      // picker's connect form, and the connect sequence redelivers the whole log, so a fresh feed
      // rebuilds from scratch — which is the ruling's drop, reconnect, rebuild, driven by hand.
      projector = null;
      pending.length = 0;
      fail(`desync: ${error.kind} names ${error.unknownId}, which this projection does not hold`);
    }
  }

  socket.addEventListener('open', () => {
    // The first frame, within 10 s of opening (game server plan → Client shell plan). It is built as
    // the generated `Connect` rather than as an object literal, so the one message the viewer sends is
    // checked against the C# record exactly like the ones it reads.
    const connect: Connect = {
      kind: 'connect',
      sessionJwt: options.sessionJwt,
      gameId: options.gameId,
      clientVersion: CLIENT_VERSION,
    };
    socket.send(JSON.stringify(connect));
  });

  socket.addEventListener('message', (event: never) => {
    let parsed: unknown;
    try {
      parsed = JSON.parse((event as unknown as { data: string }).data);
    } catch {
      return;                                   // not our frame; the socket carries nothing else we read
    }
    if (!isServerFrame(parsed)) return;         // presence, ping, presence_ended, at_rest: not read here
    const frame = parsed;
    switch (frame.kind) {
      case 'welcome':
        welcomed = true;
        clearTimeout(timeout);
        game = frame.game;
        // A game ended before setup ever ran sends no board, so status stays open with nothing to draw.
        emitter.emit({ status: 'open', meta: metaOf(frame.game, 0, 0) });
        return;
      case 'game':
        game = frame.game;
        publish();
        return;
      case 'board':
        if (game === null) return fail('a board arrived before the welcome');
        projector = new FrameProjector(frame.board as Board, game);
        drain();
        publish();
        return;
      case 'events':
        pending.push(...frame.events);
        drain();
        publish();
        return;
      case 'refused':
        emitter.emit({ detail: `refused: ${frame.reason} — ${frame.detail}` });
        return;
      case 'rejected':
        return fail(`rejected: ${frame.reason}`);
      default:
        return;                                 // `connect` is in the union and never arrives; nothing else reaches here
    }
  });

  socket.addEventListener('error', () => fail('the socket failed'));
  socket.addEventListener('close', () => {
    clearTimeout(timeout);
    if (!failed) emitter.emit({ status: 'closed' });
  });

  return {
    kind: 'live',
    control: null as ControlChannel | null,
    subscribe: (listener: (state: FeedState) => void) => emitter.subscribe(listener),
    close: () => {
      clearTimeout(timeout);
      socket.close();
      if (!failed) emitter.emit({ status: 'closed' });
    },
  };
}
```

`RecordingMeta.outcome` is set to `live`, the value the simulation plan's `RecordingMeta` doc comment reserves for a feed that is still running; `sim/` writes only the four finished-run values. Nothing about the shape changes — `outcome` is a `string`.

A `game` frame after teams change rebuilds nothing: `recordedBoardOf`'s team ordinals were resolved when the board arrived, so a team created mid-game does not recolour existing models. Teams are created only while the game is `lobby` or `setup`, and the team list is frozen at the flip to `playing` (game server plan), so this cannot happen during a live game being watched.

**Cost per live frame — an accepted limit, stated rather than fixed.** `publish()` copies the whole frame array and rebuilds `RecordingMeta` on every `EventsMessage`, and `App.svelte` then recomputes `seqs` across every frame, so a socket that delivers `n` frames one at a time does two walks of the whole feed per frame: quadratic in `n`. It is kept for two reasons. The copy is what makes a `FeedState` a snapshot — `FrameProjector.frames` is the projector's own array and grows in place, so handing it out uncopied would let a state a subscriber is still holding change under it — and the primary case never pays: the recording feed publishes once. The number: an eight-round game is 800 to 1,000 frames, so each publish walks at most about 1,000 elements, which is microseconds. The 5 to 6 MB of a recorded eight-round game is parsed once by the recording feed and never republished. **It starts to hurt at about ten thousand frames on one socket** — twenty games' worth without reconnecting — where every frame walks 10,000 elements twice and allocates an 80 KB array, and the run has churned a few hundred megabytes of short-lived arrays. If a socket is ever held that long the fix is two changes and no new concepts: have `FrameProjector` keep a running `seqs` array beside `frames` and put it on `FeedState` so `App.svelte` stops mapping, and coalesce publishes onto one animation frame. Neither is worth writing for a developer tool watching one game.

- [ ] **Step 4: Extend `SourcePicker.svelte` with the connect form**

```svelte
<!-- add to viewer/src/lib/components/SourcePicker.svelte's <script> -->
  import { liveFeed } from '../feed/liveFeed';
  import { logIn } from '../live/management';

  let gameId = $state('');
  let email = $state('');
  let password = $state('');
  let jwt = $state('');
  const socketUrl = import.meta.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws';

  async function connect() {
    const token = jwt.length > 0 ? jwt : (await logIn(email, password)).sessionJwt;
    jwt = token;
    onFeed(liveFeed({ url: socketUrl, gameId, sessionJwt: token }));
  }
```

```svelte
<!-- add to the markup, after the recording inputs -->
<details class="live">
  <summary>Watch a live game</summary>
  <p class="muted">
    A spectator socket to {socketUrl}. The account must already be a participant with no team on that
    game; the viewer only watches — it sends no command and answers no Ask.
  </p>
  <input type="text" placeholder="game id" bind:value={gameId} style="width: 22rem" />
  <input type="text" placeholder="session JWT (or log in below)" bind:value={jwt} style="width: 22rem" />
  <input type="email" placeholder="email" bind:value={email} />
  <input type="password" placeholder="password" bind:value={password} />
  <button disabled={gameId.length === 0 || (jwt.length === 0 && email.length === 0)}
    onclick={tryConnect}>Connect</button>
</details>
```

`connect` calls `onFeed` itself rather than going through `load`, because it is asynchronous in two stages — log in, then open the socket — so it gets its own handler:

```svelte
  async function tryConnect() {
    try { await connect(); } catch (error) { onError((error as Error).message); }
  }
```

Add to `viewer/vite.config.ts`'s `defineConfig`:

```ts
  define: { 'import.meta.env.VITE_GAME_SOCKET': JSON.stringify(process.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws') },
```

- [ ] **Step 5: Run the tests, then watch a real game**

```bash
cd viewer && npm test && npm run check && npm run build
```
Expected: 13 more tests passed. The thirteenth is the desync; the ignored frames are two deliveries inside one test rather than a test of their own.

Then, once the game server runs: start it, create a game and join with a third account through management so that account is a participant with `team_id = null`, run `dotnet run --project sim -- skirmish --driver socket`, and connect the viewer with that account's game id and JWT. Expected: the board appears, frames arrive as the simulator plays, the timeline follows the end until you step, and the log fills.

- [ ] **Step 6: Commit**

```bash
cd /home/cstupi/src/tabletop
git add viewer/src viewer/vite.config.ts viewer/tests/live.test.ts
git commit -m "Watch a live game as a spectator with no team, over the socket"
```
