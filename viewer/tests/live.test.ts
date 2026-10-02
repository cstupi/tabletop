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

  it('keeps the first error when later socket errors follow', () => {
    const { socket, last } = attach();
    socket.deliver({ kind: 'rejected', reason: 'no seat' });
    expect(last().detail).toBe('rejected: no seat');
    socket.fire('error', {});
    expect(last().detail).toBe('rejected: no seat');
  });

  it('reports any other fault while applying events as an error with its detail', () => {
    const { game, board } = loadFixture('skirmish');
    const { socket, last } = attach();
    socket.fire('open', {});
    socket.deliver({ kind: 'welcome', participantId: 'p9', game });
    socket.deliver({ kind: 'board', board });
    // models_moved with no `moves`: a TypeError, not a desync.
    expect(() => socket.deliver({ kind: 'events', events: [{ seq: 1, actor: null, warning: null, payload: {
      kind: 'models_moved' } }] })).not.toThrow();
    expect(last().status).toBe('error');
    expect(last().detail).toContain('TypeError');
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
    try {
      const { socket, last } = attach();
      socket.fire('open', {});
      vi.advanceTimersByTime(10_000);
      expect(last()).toMatchObject({ status: 'error', detail: 'no welcome within 10 s' });
      expect(socket.closed).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });

  it('stays in error when a welcome and a board arrive after the timeout', () => {
    const { game, board } = loadFixture('skirmish');
    vi.useFakeTimers();
    try {
      const { socket, last } = attach();
      socket.fire('open', {});
      vi.advanceTimersByTime(10_000);
      socket.deliver({ kind: 'welcome', participantId: 'p9', game });
      socket.deliver({ kind: 'board', board });
      expect(last()).toMatchObject({ status: 'error', detail: 'no welcome within 10 s' });
      expect(last().board).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('emits closed once when close() is followed by the socket close event', () => {
    const { socket, feed, seen } = attach();
    socket.fire('open', {});
    feed.close();
    socket.fire('close', {});
    expect(seen.filter((s) => s.status === 'closed')).toHaveLength(1);
  });
});
