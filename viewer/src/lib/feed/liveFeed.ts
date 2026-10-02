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
