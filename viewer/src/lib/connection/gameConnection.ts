// viewer/src/lib/connection/gameConnection.ts
// The participant socket: Session's socket half (client/src/Core/Session.cs) as a plain TypeScript module. It sends edits and
// commands, settles each on its Refused or on the next frame of its kind, queues sends while reconnecting, reconnects with backoff,
// reauthenticates every minute and rebuilds from the server on every connect. It judges nothing: the server refuses.

import type {
  Board, Command, Connect, Edit, GameEvent, GameInfo, Refused, RulesOptionCatalog, SightAnswer, SocketMessage, TableState, ZoneLegs,
} from '../protocol/wire.generated';
import { PROTOCOL_VERSION } from '../protocol/wire.generated';
import { Applier, ProjectionDesyncError, type Projection } from '../live/apply';
import type { SocketLike } from '../feed/liveFeed';
import { closedText, refusalText, rejectedText } from './feedback';

export type ConnectionPhase = 'connecting' | 'connected' | 'reconnecting' | 'closed';

export interface ConnectionState {
  readonly phase: ConnectionPhase;
  readonly game: GameInfo | null;
  /** Welcome's participant row; the all-zero id for an admin watcher. */
  readonly participantId: string | null;
  readonly options: RulesOptionCatalog;
  /** Welcome's Side Battle legs, which every zone shape resolves with; null before the welcome. */
  readonly zones: ZoneLegs | null;
  /** Setup: the last board's table. */
  readonly draft: TableState | null;
  /** From playing on: the snapshot the log applies to. */
  readonly board: Board | null;
  /** Every event applied, in order. */
  readonly log: readonly GameEvent[];
  /** Applier(board, game) after `log`. */
  readonly projection: Projection | null;
  readonly maxWounds: ReadonlyMap<string, number>;
  readonly deployInside: ReadonlyMap<string, readonly string[]>;
  /** The units that may be set aside before deployment now, or returned to it (ModelFacts.maySetAside). */
  readonly maySetAside: ReadonlySet<string>;
  /** From a board until a model_facts frame or an empty events page (Session.CatchingUp). */
  readonly catchingUp: boolean;
  /** Player text for the last close or rejection (feedback.ts); null while nothing went wrong. */
  readonly closeReason: string | null;
}

/**
 * How a send ended: its Refused, or null once the next frame of its kind arrived. `id` is the message id its Refused names. A
 * broadcast from another player can settle a send as accepted before its own Refused arrives, so a caller that needs the true
 * result treats a later onRefused with this id as the result, as Session matches Refused by id.
 */
export interface SendOutcome { readonly id: string; readonly refused: Refused | null }

export class SendError extends Error {
  constructor(message: string, readonly why: 'not_connected' | 'queue_full' | 'disconnected' | 'closed') {
    super(message);
    this.name = 'SendError';
  }
}

export interface GameConnection {
  readonly gameId: string;
  readonly state: ConnectionState;
  /** Called at once with the current state, then on every change. */
  subscribe(listener: (state: ConnectionState) => void): () => void;
  /** Each events batch after it is applied and published. */
  onEvents(listener: (events: readonly GameEvent[]) => void): () => void;
  /** Every Refused, with Feedback.Refusal's text (null for a watcher's refused presence), even one for a send that already settled. */
  onRefused(listener: (refused: Refused, text: string | null) => void): () => void;
  /** Sends now when welcomed, queues while reconnecting (16 at most), else rejects with SendError('not_connected'). */
  sendEdit(edit: Edit): Promise<SendOutcome>;
  sendCommand(command: Command): Promise<SendOutcome>;
  /** Resolves on the SightAnswer with this query's id. */
  querySight(observerUnitId: string, targetUnitId: string): Promise<SightAnswer>;
  /** The player leaves the page: closes for good, rejects what is pending with SendError('closed'). */
  close(): void;
}

export interface ConnectionOptions {
  readonly url: string;
  readonly gameId: string;
  /** A fresh JWT; called for every Connect and Reauth. */
  readonly jwt: () => Promise<string>;
  readonly open?: (url: string) => SocketLike;
  /** Default 1000, 2000, 5000, 10000, then 10000. */
  readonly backoffMs?: (attempt: number) => number;
  /** Default 60000. */
  readonly reauthEveryMs?: number;
}

const WELCOME_TIMEOUT_MS = 10_000;
const QUEUE_LIMIT = 16;
const SETTLED_LIMIT = 64;
const BACKOFF = [1000, 2000, 5000, 10000];
const NO_OPTIONS: RulesOptionCatalog = { options: [], presets: [] };
/** The edits the server answers with a `game` frame; every other edit is a setup edit and is answered with a `board` frame. */
const LOBBY_EDITS = new Set([
  'create_team', 'pick_team', 'pick_army', 'leave_team', 'set_configuration', 'start_setup', 'end_setup',
]);

type Settle = 'game' | 'board' | 'at_rest' | 'sight';
type Send = {
  readonly id: string; readonly kind: Settle; readonly frame: SocketMessage; readonly isCommand: boolean;
  readonly resolve: (value: never) => void; readonly reject: (error: SendError) => void;
};

const INITIAL: ConnectionState = {
  phase: 'connecting', game: null, participantId: null, options: NO_OPTIONS, zones: null, draft: null, board: null, log: [], projection: null,
  maxWounds: new Map(), deployInside: new Map(), maySetAside: new Set(), catchingUp: true, closeReason: null,
};

export function connectGame(options: ConnectionOptions): GameConnection {
  const open = options.open ?? ((url: string) => new WebSocket(url) as unknown as SocketLike);
  const backoff = options.backoffMs ?? ((attempt: number) => BACKOFF[Math.min(attempt, BACKOFF.length - 1)]!);
  const reauthEvery = options.reauthEveryMs ?? 60_000;

  let state: ConnectionState = INITIAL;
  const stateListeners = new Set<(state: ConnectionState) => void>();
  const eventListeners = new Set<(events: readonly GameEvent[]) => void>();
  const refusedListeners = new Set<(refused: Refused, text: string | null) => void>();

  let socket: SocketLike | null = null;
  let welcomed = false;
  let userClosed = false;
  let errored = false;
  let attempt = 0;
  let desyncFloor: number | null = null;
  let applier: Applier | null = null;
  let swallowAtRest = 0;
  let welcomeTimer: ReturnType<typeof setTimeout> | undefined;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let reauthTimer: ReturnType<typeof setInterval> | undefined;
  const queue: Send[] = [];
  let pending: Send[] = [];
  /** The last sends that settled as accepted: a Refused can still name one. */
  const settled = new Map<string, boolean>();

  function publish(patch: Partial<ConnectionState>): void {
    state = { ...state, ...patch };
    for (const listener of [...stateListeners]) listener(state);
  }

  function rejectAll(why: 'disconnected' | 'closed', includeQueue: boolean): void {
    const dead = [...pending, ...(includeQueue ? queue.splice(0) : [])];
    pending = [];
    for (const send of dead) send.reject(new SendError(why === 'closed' ? 'The connection was closed.' : 'The connection dropped before the server answered.', why));
  }

  /** Back to what a fresh connect rebuilds from (Session.Reset). */
  function reset(): void {
    applier = null;
    swallowAtRest = 0;
    settled.clear();
    publish({
      game: null, participantId: null, draft: null, board: null, log: [], projection: null,
      maxWounds: new Map(), deployInside: new Map(), maySetAside: new Set(), catchingUp: true,
    });
  }

  function stopTimers(): void {
    clearTimeout(welcomeTimer);
    clearInterval(reauthTimer);
    welcomeTimer = undefined;
    reauthTimer = undefined;
  }

  /** Lets the current socket go without its close being handled as a drop. */
  function detach(): void {
    const old = socket;
    socket = null;
    stopTimers();
    welcomed = false;
    old?.close();
  }

  function connect(phase: ConnectionPhase): void {
    if (userClosed) return;
    welcomed = false;
    errored = false;
    publish({ phase });
    const next = open(options.url);
    socket = next;
    const current = () => socket === next;
    welcomeTimer = setTimeout(() => { if (current()) dropped(null, null); }, WELCOME_TIMEOUT_MS);

    next.addEventListener('open', () => {
      if (!current()) return;
      options.jwt().then((sessionJwt) => {
        if (!current()) return;
        const frame: Connect = { kind: 'connect', sessionJwt, gameId: options.gameId, clientVersion: PROTOCOL_VERSION };
        next.send(JSON.stringify(frame));
      }).catch(() => { if (current()) closeForGood(closedText(1008, null, false)); });
    });
    next.addEventListener('error', () => { if (current()) errored = true; });
    next.addEventListener('close', (event: never) => {
      if (!current()) return;
      const { code, reason } = event as unknown as { code?: number; reason?: string };
      // 1005 and 1006 are the browser's "no code": an abnormal close.
      dropped(code === undefined || code === 1005 || code === 1006 ? null : code, reason ?? null);
    });
    next.addEventListener('message', (event: never) => {
      if (!current()) return;
      let parsed: unknown;
      try { parsed = JSON.parse((event as unknown as { data: string }).data); } catch { return; }
      if (typeof parsed !== 'object' || parsed === null) return;
      handle(parsed as SocketMessage);
    });
  }

  /** The socket closed or never welcomed: reconnect with backoff, or close for good (Session.OnClosed). */
  function dropped(code: number | null, reason: string | null): void {
    const was = welcomed;
    const sawError = errored;
    detach();
    rejectAll('disconnected', false);
    reset();
    if (userClosed) return;
    if (was && code === 1000) return closeForGood(closedText(code, reason, false));
    publish({ phase: 'reconnecting', closeReason: closedText(code, reason, true, false, sawError) });
    const wait = backoff(attempt++);
    retryTimer = setTimeout(() => connect('reconnecting'), wait);
  }

  function closeForGood(text: string | null): void {
    userClosed = true;
    clearTimeout(retryTimer);
    detach();
    rejectAll('disconnected', true);
    reset();
    publish({ phase: 'closed', closeReason: text });
  }

  /** An applier fault: this copy of the game is unusable. One rebuild per log position, at once (Session.OnDesync). */
  function desynced(error: ProjectionDesyncError): void {
    const reached = applier?.projection.lastSeq ?? 0;
    if (desyncFloor !== null && reached <= desyncFloor) return closeForGood(closedText(null, error.message, false, true));
    desyncFloor = reached;
    detach();
    rejectAll('disconnected', false);
    reset();
    publish({ phase: 'reconnecting', closeReason: closedText(null, error.message, true, true) });
    clearTimeout(retryTimer);
    retryTimer = setTimeout(() => connect('reconnecting'), 0);
  }

  /** A frame of this kind settles the oldest send waiting on it. */
  function settle(kind: Settle, key?: string): void {
    const index = pending.findIndex((p) => p.kind === kind && (key === undefined || p.id === key));
    if (index < 0) return;
    const [send] = pending.splice(index, 1);
    remember(send!);
    (send!.resolve as (v: SendOutcome) => void)({ id: send!.id, refused: null });
  }

  function remember(send: Send): void {
    if (send.kind === 'sight') return;
    settled.set(send.id, send.isCommand);
    if (settled.size > SETTLED_LIMIT) settled.delete(settled.keys().next().value!);
  }

  function handle(frame: SocketMessage): void {
    switch (frame.kind) {
      case 'welcome':
        welcomed = true;
        attempt = 0;
        clearTimeout(welcomeTimer);
        publish({ phase: 'connected', participantId: frame.participantId, game: frame.game, options: frame.options ?? NO_OPTIONS, zones: frame.zones ?? null, closeReason: null });
        reauthTimer = setInterval(() => void reauth(), reauthEvery);
        flush();
        return;
      case 'game':
        publish({ game: frame.game });
        settle('game');
        return;
      case 'board': {
        const game = state.game;
        if (game === null) return;
        if (game.status === 'playing' || game.status === 'ended') {
          applier = new Applier(frame.board, game);
          publish({ board: frame.board, draft: null, log: [], projection: applier.projection, catchingUp: true });
        } else {
          applier = null;
          publish({ draft: frame.board.table, board: null, log: [], projection: null, catchingUp: true });
        }
        settle('board');
        return;
      }
      case 'events': {
        if (applier === null) return;
        try {
          for (const event of frame.events) applier.apply(event);
        } catch (error) {
          if (error instanceof ProjectionDesyncError) return desynced(error);
          throw error;
        }
        publish({ log: [...applier.log], projection: applier.projection, catchingUp: frame.events.length === 0 ? false : state.catchingUp });
        for (const listener of [...eventListeners]) listener(frame.events);
        return;
      }
      case 'model_facts':
        publish({
          maxWounds: new Map(Object.entries(frame.maxWounds)),
          deployInside: new Map(Object.entries(frame.deployInside ?? {})),
          maySetAside: new Set(frame.maySetAside ?? []),
          catchingUp: false,
        });
        return;
      case 'at_rest':
        if (swallowAtRest > 0) swallowAtRest--; else settle('at_rest');
        return;
      case 'refused': {
        const index = pending.findIndex((p) => p.id === frame.messageId);
        if (index >= 0) {
          const [send] = pending.splice(index, 1);
          // The command's own at_rest follows its Refused and settles nothing.
          if (send!.isCommand) swallowAtRest++;
          (send!.resolve as (v: SendOutcome) => void)({ id: send!.id, refused: frame });
        } else if (settled.has(frame.messageId)) {
          // A broadcast settled this send first; the command's own at_rest still follows its Refused.
          if (settled.get(frame.messageId)) swallowAtRest++;
          settled.delete(frame.messageId);
        }
        const text = refusalText(frame);
        for (const listener of [...refusedListeners]) listener(frame, text);
        return;
      }
      case 'sight_answer': {
        const index = pending.findIndex((p) => p.kind === 'sight' && p.id === frame.queryId);
        if (index < 0) return;
        const [send] = pending.splice(index, 1);
        (send!.resolve as (v: SightAnswer) => void)(frame);
        return;
      }
      case 'rejected':
        return closeForGood(rejectedText(frame.reason));
      default:
        return;                                   // presence, ping, presence_ended and kinds a later protocol adds
    }
  }

  async function reauth(): Promise<void> {
    const live = socket;
    if (live === null || !welcomed) return;
    try {
      const sessionJwt = await options.jwt();
      if (socket === live && welcomed) live.send(JSON.stringify({ kind: 'reauth', reauthId: crypto.randomUUID(), sessionJwt }));
    } catch { /* the next tick tries again; the socket's own expiry closes it with 1008 */ }
  }

  function flush(): void {
    const live = socket;
    if (live === null) return;
    for (const send of queue.splice(0)) {
      pending.push(send);
      live.send(JSON.stringify(send.frame));
    }
  }

  function submit<T>(kind: Settle, id: string, frame: SocketMessage, isCommand: boolean): Promise<T> {
    if (state.phase === 'closed') return Promise.reject(new SendError('The connection is closed.', 'closed'));
    return new Promise<T>((resolve, reject) => {
      const send: Send = { id, kind, frame, isCommand, resolve: resolve as (v: never) => void, reject };
      if (state.phase === 'connected' && welcomed && socket !== null) {
        pending.push(send);
        socket.send(JSON.stringify(frame));
      } else if (state.phase === 'reconnecting' && kind !== 'sight') {
        if (queue.length >= QUEUE_LIMIT) return reject(new SendError('Too many changes are waiting for the connection to return.', 'queue_full'));
        queue.push(send);
      } else reject(new SendError('Not connected to the game yet.', 'not_connected'));
    });
  }

  connect('connecting');

  return {
    gameId: options.gameId,
    get state() { return state; },
    subscribe(listener) {
      stateListeners.add(listener);
      listener(state);
      return () => { stateListeners.delete(listener); };
    },
    onEvents(listener) { eventListeners.add(listener); return () => { eventListeners.delete(listener); }; },
    onRefused(listener) { refusedListeners.add(listener); return () => { refusedListeners.delete(listener); }; },
    sendEdit(edit) {
      const editId = crypto.randomUUID();
      return submit(LOBBY_EDITS.has(edit.kind) ? 'game' : 'board', editId, { kind: 'edit', editId, edit }, false);
    },
    sendCommand(command) {
      const commandId = crypto.randomUUID();
      return submit('at_rest', commandId, { kind: 'command', commandId, command }, true);
    },
    querySight(observerUnitId, targetUnitId) {
      const queryId = crypto.randomUUID();
      return submit('sight', queryId, { kind: 'sight_query', queryId, observerUnitId, targetUnitId }, false);
    },
    close() {
      if (state.phase === 'closed' && userClosed) return;
      userClosed = true;
      clearTimeout(retryTimer);
      detach();
      rejectAll('closed', true);
      reset();
      publish({ phase: 'closed' });
    },
  };
}
