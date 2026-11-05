import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { connectGame, SendError, type ConnectionState, type GameConnection } from '../src/lib/connection/gameConnection';
import type { SocketLike } from '../src/lib/feed/liveFeed';
import type { Command, Edit, GameEvent, Refused, SocketMessage } from '../src/lib/protocol/wire.generated';
import { loadFixture } from './fixture';

class FakeSocket implements SocketLike {
  readonly sent: string[] = [];
  readonly #handlers = new Map<string, (event: never) => void>();
  closed = false;
  send(data: string) { this.sent.push(data); }
  close() { this.closed = true; }
  addEventListener(type: string, handler: (event: never) => void) { this.#handlers.set(type, handler); }
  fire(type: string, event: unknown = {}) { this.#handlers.get(type)?.(event as never); }
  deliver(message: unknown) { this.fire('message', { data: JSON.stringify(message) }); }
  frames(): Array<Record<string, unknown>> { return this.sent.map((s) => JSON.parse(s) as Record<string, unknown>); }
}

const fixture = loadFixture('skirmish');
const welcome = fixture.frames[0]!;
const setupGame = { ...fixture.game, status: 'setup' as const };
const setupWelcome = { kind: 'welcome', participantId: 'p1', game: setupGame, options: { options: [], presets: [] } };
const lobbyGame = { ...fixture.game, status: 'lobby' as const };
const edit = (kind: string, rest: object = {}) => ({ kind, ...rest }) as unknown as Edit;
const command = (kind: string, rest: object = {}) => ({ kind, ...rest }) as unknown as Command;
const tick = () => vi.advanceTimersByTimeAsync(0);

function harness(over: Partial<Parameters<typeof connectGame>[0]> = {}) {
  const sockets: FakeSocket[] = [];
  let jwts = 0;
  const conn = connectGame({
    url: 'ws://h/ws', gameId: 'g1', jwt: async () => `jwt${++jwts}`,
    open: () => { const s = new FakeSocket(); sockets.push(s); return s; }, ...over,
  });
  const states: ConnectionState[] = [];
  conn.subscribe((s) => states.push(s));
  return { conn, sockets, states, jwtCount: () => jwts };
}

async function open(h: ReturnType<typeof harness>, index = h.sockets.length - 1): Promise<FakeSocket> {
  const s = h.sockets[index]!;
  s.fire('open');
  await tick();
  return s;
}
async function welcomed(h: ReturnType<typeof harness>, frame: unknown = welcome): Promise<FakeSocket> {
  const s = await open(h);
  s.deliver(frame);
  return s;
}
/** The pending promise of a send, with its rejection already handled so an unsettled one cannot fail the run. */
function track<T>(p: Promise<T>): Promise<T> { p.catch(() => {}); return p; }
const lastFrame = (s: FakeSocket) => s.frames().at(-1)!;

beforeEach(() => { vi.useFakeTimers(); });
afterEach(() => { vi.useRealTimers(); });

describe('GameConnection', () => {
  it('sends connect with a fresh jwt on open', async () => {
    const h = harness();
    expect(h.conn.state.phase).toBe('connecting');
    const s = await open(h);
    expect(s.frames()).toEqual([{ kind: 'connect', sessionJwt: 'jwt1', gameId: 'g1', clientVersion: expect.any(Number) }]);
  });

  it('a welcome publishes game, participant and options', async () => {
    const h = harness();
    await welcomed(h, setupWelcome);
    expect(h.conn.state).toMatchObject({ phase: 'connected', participantId: 'p1', game: setupGame, options: { options: [], presets: [] } });
  });

  it('rules_options replaces the catalog, zone legs and terrain words', async () => {
    const h = harness();
    const s = await welcomed(h, setupWelcome);
    const next = { options: [{ key: 'a', label: 'A', page: '1', default: 'off', values: [{ value: 'off', label: 'Off' }] }], presets: [] };
    const zones = { sideBattleLong: 2 / 3, sideBattleShort: 0.75 };
    s.deliver({ kind: 'rules_options', options: next, zones, terrainWords: { see_in_and_out: 'Dense' } });
    expect(h.conn.state).toMatchObject({ options: next, zones, terrainWords: { see_in_and_out: 'Dense' } });
  });

  it('a welcome publishes its zone legs', async () => {
    const h = harness();
    expect(h.conn.state.zones).toBeNull();
    await welcomed(h, { ...setupWelcome, zones: { sideBattleLong: 2 / 3, sideBattleShort: 0.75 } });
    expect(h.conn.state.zones).toEqual({ sideBattleLong: 2 / 3, sideBattleShort: 0.75 });
  });

  it('setup boards replace the draft', async () => {
    const h = harness();
    const s = await welcomed(h, setupWelcome);
    s.deliver({ kind: 'board', board: fixture.board });
    expect(h.conn.state.draft).toEqual(fixture.board.table);
    expect(h.conn.state.projection).toBeNull();
    expect(h.conn.state.catchingUp).toBe(true);
  });

  it('a playing board starts the projection and events apply in order', async () => {
    const h = harness();
    const batches: number[] = [];
    h.conn.onEvents((e) => batches.push(e.length));
    const s = await welcomed(h);
    s.deliver({ kind: 'board', board: fixture.board });
    expect(h.conn.state.board).toEqual(fixture.board);
    expect(h.conn.state.draft).toBeNull();
    s.deliver({ kind: 'events', events: fixture.events });
    expect(h.conn.state.log).toHaveLength(fixture.events.length);
    expect(h.conn.state.projection!.lastSeq).toBe(fixture.events.at(-1)!.seq);
    expect(batches).toEqual([fixture.events.length]);
    expect(h.conn.state.catchingUp).toBe(true);
    s.deliver({ kind: 'events', events: [] });
    expect(h.conn.state.catchingUp).toBe(false);
  });

  it('model facts end the catch-up and fill the facts', async () => {
    const h = harness();
    const s = await welcomed(h);
    s.deliver({ kind: 'board', board: fixture.board });
    s.deliver({ kind: 'model_facts', maxWounds: { m1: 3 }, deployInside: { u1: ['t1'] }, maySetAside: ['u2'] });
    expect(h.conn.state.maxWounds.get('m1')).toBe(3);
    expect(h.conn.state.deployInside.get('u1')).toEqual(['t1']);
    expect([...h.conn.state.maySetAside]).toEqual(['u2']);
    expect(h.conn.state.catchingUp).toBe(false);
  });

  it('an edit settles null on the next board and a lobby edit settles null on the next game', async () => {
    const h = harness();
    const s = await welcomed(h, setupWelcome);
    const setup = track(h.conn.sendEdit(edit('set_table_size', { size: { width: 72, length: 48 } })));
    const lobby = track(h.conn.sendEdit(edit('create_team')));
    expect(s.frames().slice(1).map((f) => (f.edit as { kind: string }).kind)).toEqual(['set_table_size', 'create_team']);
    s.deliver({ kind: 'game', game: lobbyGame });
    await expect(lobby).resolves.toMatchObject({ refused: null });
    s.deliver({ kind: 'board', board: fixture.board });
    await expect(setup).resolves.toMatchObject({ refused: null });
  });

  it('a refused edit settles with its Refused and reaches onRefused with Godot\'s text', async () => {
    const h = harness();
    const texts: Array<string | null> = [];
    h.conn.onRefused((_r: Refused, text) => texts.push(text));
    const s = await welcomed(h, setupWelcome);
    const sent = track(h.conn.sendEdit(edit('create_team')));
    const refused: SocketMessage = { kind: 'refused', messageId: lastFrame(s).editId as string, reason: 'wrong_status', detail: 'setup is over' };
    s.deliver(refused);
    const outcome = await sent;
    expect(outcome.refused).toEqual(refused);
    expect(texts).toEqual(['Not at this point of the game: setup is over.']);
  });

  it('a command settles null on at_rest', async () => {
    const h = harness();
    const s = await welcomed(h);
    const sent = track(h.conn.sendCommand(command('undo', { toSeq: 1 })));
    expect(lastFrame(s)).toMatchObject({ kind: 'command', command: { kind: 'undo' } });
    s.deliver({ kind: 'at_rest', seq: 4 });
    await expect(sent).resolves.toMatchObject({ refused: null });
  });

  it('a refused command settles on the Refused, not the at_rest after it', async () => {
    const h = harness();
    const s = await welcomed(h);
    const first = track(h.conn.sendCommand(command('undo', { toSeq: 1 })));
    s.deliver({ kind: 'refused', messageId: lastFrame(s).commandId, reason: 'illegal', detail: 'no' });
    const outcome = await first;
    expect(outcome.refused?.reason).toBe('illegal');
    const second = track(h.conn.sendCommand(command('undo', { toSeq: 2 })));
    let settled = false;
    void second.then(() => { settled = true; });
    s.deliver({ kind: 'at_rest', seq: 4 });          // the refused command's own at_rest: settles nothing
    await tick();
    expect(settled).toBe(false);
    s.deliver({ kind: 'at_rest', seq: 5 });
    await expect(second).resolves.toMatchObject({ refused: null });
  });

  it('a Refused after its send settled on another frame still reaches onRefused and names the send', async () => {
    const h = harness();
    const seen: Refused[] = [];
    h.conn.onRefused((r) => seen.push(r));
    const s = await welcomed(h, setupWelcome);
    const sent = track(h.conn.sendEdit(edit('set_table_size', { size: { width: 72, length: 48 } })));
    const id = lastFrame(s).editId as string;
    s.deliver({ kind: 'board', board: fixture.board });            // someone else's broadcast settles it as accepted
    const outcome = await sent;
    expect(outcome).toEqual({ id, refused: null });
    const refused: SocketMessage = { kind: 'refused', messageId: id, reason: 'illegal', detail: 'late' };
    s.deliver(refused);
    expect(seen).toEqual([refused]);
  });

  it('a late Refused for a command swallows the at_rest that follows it', async () => {
    const h = harness();
    const s = await welcomed(h);
    const first = track(h.conn.sendCommand(command('undo', { toSeq: 1 })));
    const id = lastFrame(s).commandId as string;
    s.deliver({ kind: 'at_rest', seq: 4 });                         // another player's command settles it
    await first;
    s.deliver({ kind: 'refused', messageId: id, reason: 'illegal', detail: 'no' });
    const second = track(h.conn.sendCommand(command('undo', { toSeq: 2 })));
    let settled = false;
    void second.then(() => { settled = true; });
    s.deliver({ kind: 'at_rest', seq: 5 });                         // the refused command's own at_rest
    await tick();
    expect(settled).toBe(false);
    s.deliver({ kind: 'at_rest', seq: 6 });
    await expect(second).resolves.toMatchObject({ refused: null });
  });

  it('querySight resolves on its answer', async () => {
    const h = harness();
    const s = await welcomed(h);
    const answer = track(h.conn.querySight('u1', 'u2'));
    const query = lastFrame(s);
    expect(query).toMatchObject({ kind: 'sight_query', observerUnitId: 'u1', targetUnitId: 'u2' });
    const reply = { kind: 'sight_answer', queryId: query.queryId, lines: [], canShoot: ['m1'] };
    s.deliver({ kind: 'sight_answer', queryId: 'other', lines: [], canShoot: [] });
    s.deliver(reply);
    await expect(answer).resolves.toEqual(reply);
  });

  it('sending before the welcome throws not_connected', async () => {
    const h = harness();
    await expect(h.conn.sendEdit(edit('create_team'))).rejects.toMatchObject({ why: 'not_connected' });
  });

  it('queued edits flush after the next welcome, in order', async () => {
    const h = harness();
    const first = await welcomed(h, setupWelcome);
    first.fire('close', { code: 1006, reason: '' });
    expect(h.conn.state.phase).toBe('reconnecting');
    const a = track(h.conn.sendEdit(edit('create_team')));
    const b = track(h.conn.sendEdit(edit('pick_team', { teamId: 't1' })));
    await vi.advanceTimersByTimeAsync(1000);
    const second = await welcomed(h, setupWelcome);
    expect(second.frames().map((f) => f.kind)).toEqual(['connect', 'edit', 'edit']);
    expect(second.frames().slice(1).map((f) => (f.edit as { kind: string }).kind)).toEqual(['create_team', 'pick_team']);
    second.deliver({ kind: 'game', game: lobbyGame });
    await expect(a).resolves.toMatchObject({ refused: null });
    second.deliver({ kind: 'game', game: lobbyGame });
    await expect(b).resolves.toMatchObject({ refused: null });
  });

  it('the seventeenth queued send throws queue_full', async () => {
    const h = harness();
    const s = await welcomed(h, setupWelcome);
    s.fire('close', { code: 1006 });
    for (let i = 0; i < 16; i++) track(h.conn.sendEdit(edit('create_team')));
    await expect(h.conn.sendEdit(edit('create_team'))).rejects.toMatchObject({ why: 'queue_full' });
  });

  it('a dropped socket reconnects after 1, 2, 5 and 10 s', async () => {
    const h = harness();
    let socket = await open(h);
    for (const [i, wait] of [1000, 2000, 5000, 10000, 10000].entries()) {
      socket.fire('close', { code: 1006 });
      expect(h.conn.state.phase).toBe('reconnecting');
      await vi.advanceTimersByTimeAsync(wait - 1);
      expect(h.sockets).toHaveLength(i + 1);
      await vi.advanceTimersByTimeAsync(1);
      expect(h.sockets).toHaveLength(i + 2);
      socket = h.sockets.at(-1)!;
    }
  });

  it('a 1008 close asks for a fresh JWT before reconnecting', async () => {
    const h = harness();
    const s = await welcomed(h);
    s.fire('close', { code: 1008, reason: '' });
    expect(h.conn.state.closeReason).toBe('Your sign-in on the game server expired; reconnecting with a fresh one…');
    await vi.advanceTimersByTimeAsync(1000);
    const next = await open(h);
    expect(next.frames()[0]).toMatchObject({ kind: 'connect', sessionJwt: 'jwt2' });
  });

  it('a replaced socket closes for good with Godot\'s words', async () => {
    const h = harness();
    const s = await welcomed(h);
    s.fire('close', { code: 1000, reason: 'replaced' });
    expect(h.conn.state.phase).toBe('closed');
    expect(h.conn.state.closeReason).toBe('This game was opened in another window, which took over.');
    await vi.advanceTimersByTimeAsync(60000);
    expect(h.sockets).toHaveLength(1);
  });

  it('rejected ends the connection without a retry', async () => {
    const h = harness();
    const s = await open(h);
    s.deliver({ kind: 'rejected', reason: 'not_a_participant' });
    s.fire('close', { code: 1008, reason: '' });
    expect(h.conn.state.phase).toBe('closed');
    expect(h.conn.state.closeReason).toBe('You are not in that game: join it from the list.');
    await vi.advanceTimersByTimeAsync(60000);
    expect(h.sockets).toHaveLength(1);
  });

  it('no welcome within 10 s counts as a dropped connection', async () => {
    const h = harness();
    await open(h);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(h.sockets[0]!.closed).toBe(true);
    expect(h.conn.state.phase).toBe('reconnecting');
    await vi.advanceTimersByTimeAsync(1000);
    expect(h.sockets).toHaveLength(2);
  });

  it('a desync reconnects once per log position, then closes', async () => {
    const h = harness();
    const bad: GameEvent = { seq: 999999, payload: { kind: 'wounds_set', modelId: 'nobody', wounds: 1 } };
    const run = async (s: FakeSocket) => {
      s.deliver(welcome); s.deliver({ kind: 'board', board: fixture.board });
      s.deliver({ kind: 'events', events: [...fixture.events, bad] });
    };
    const first = await open(h);
    await run(first);
    expect(first.closed).toBe(true);
    expect(h.conn.state.phase).toBe('reconnecting');
    expect(h.conn.state.closeReason).toBe('The table fell out of step with the server; rebuilding it from the log…');
    await vi.advanceTimersByTimeAsync(0);
    expect(h.sockets).toHaveLength(2);                // at once, no backoff
    const second = await open(h);
    await run(second);
    expect(h.conn.state.phase).toBe('closed');
    expect(h.conn.state.closeReason).toContain("This game's log cannot be replayed");
    await vi.advanceTimersByTimeAsync(60000);
    expect(h.sockets).toHaveLength(2);
  });

  it('reauth goes every 60 s while connected', async () => {
    const h = harness();
    const s = await welcomed(h);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(lastFrame(s)).toMatchObject({ kind: 'reauth', sessionJwt: 'jwt2' });
    expect(lastFrame(s).reauthId).toEqual(expect.any(String));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(s.frames().filter((f) => f.kind === 'reauth')).toHaveLength(2);
  });

  it('close rejects pending sends with closed', async () => {
    const h = harness();
    const s = await welcomed(h);
    const sent = track(h.conn.sendCommand(command('undo', { toSeq: 1 })));
    h.conn.close();
    expect(s.closed).toBe(true);
    expect(h.conn.state.phase).toBe('closed');
    await expect(sent).rejects.toBeInstanceOf(SendError);
    await expect(sent).rejects.toMatchObject({ why: 'closed' });
    await expect(h.conn.sendEdit(edit('create_team'))).rejects.toMatchObject({ why: 'closed' });
  });

  it('leaving reconnecting for closed rejects the queue with disconnected', async () => {
    const h = harness();
    const s = await welcomed(h, setupWelcome);
    s.fire('close', { code: 1006 });
    const queued = track(h.conn.sendEdit(edit('create_team')));
    await vi.advanceTimersByTimeAsync(1000);
    const next = await open(h);
    next.deliver({ kind: 'rejected', reason: 'not_a_participant' });
    await expect(queued).rejects.toMatchObject({ why: 'disconnected' });
  });

  it('presence, ping and unknown frames are dropped', async () => {
    const h = harness();
    const s = await welcomed(h);
    const before = h.conn.state;
    s.deliver({ kind: 'presence', presence: {} });
    s.deliver({ kind: 'ping', ping: {} });
    s.deliver({ kind: 'presence_ended', participantId: 'x' });
    s.deliver({ kind: 'from_the_future' });
    s.fire('message', { data: 'not json' });
    expect(h.conn.state).toBe(before);
  });
});

export type { GameConnection };
