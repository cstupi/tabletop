import { describe, expect, it } from 'vitest';
import { fetchSuite, recordingUrl, runEvents, spectate, startRun, SuiteError, type RunEvent, type SuiteGameResult } from '../src/lib/suite/client';
import { applyEvent, canRun, counts, coverageCell, groupsOf, initial, requestOf, setDriver, setKeep, started, toggleGame, toggleGroup } from '../src/lib/suite/model';

const games = [
  { name: 'core-a', group: 'core', covers: ['Rending'] },
  { name: 'core-b', group: 'core', covers: [] },
  { name: 'spell-a', group: 'spells', covers: ['Fireball'] },
];
const result = (name: string, passed = true): SuiteGameResult =>
  ({ name, group: 'core', passed, failures: passed ? [] : ['no'], seed: 1, replay: 'r', recordingPath: 'p', gameId: null, spectator: null });
const report = { runId: 'x', games: [result('core-a')], coverage: [{ name: 'Rending', kind: 'rule', games: ['core-a'], fired: true, exact: [], state: 'own' as const }], passed: true };

const stream = (chunks: string[]) => new Response(new ReadableStream({
  start(c) { for (const s of chunks) c.enqueue(new TextEncoder().encode(s)); c.close(); },
}));
const collect = async (it: AsyncGenerator<RunEvent>) => { const out: RunEvent[] = []; for await (const e of it) out.push(e); return out; };

describe('suite client', () => {
  it('parses /suite', async () => {
    const f = (async () => Response.json({ games })) as typeof fetch;
    expect(await fetchSuite('', f)).toEqual(games);
  });
  it('rejects a /suite failure', async () => {
    await expect(fetchSuite('', (async () => new Response('', { status: 502 })) as typeof fetch)).rejects.toThrow(SuiteError);
  });
  it('posts an explicit JSON body and returns the run id', async () => {
    let seen: RequestInit | undefined;
    const f = (async (_u: string, init?: RequestInit) => { seen = init; return Response.json({ runId: 'abc' }, { status: 202 }); }) as typeof fetch;
    expect(await startRun({ only: ['core-a'], driver: 'socket', keep: true }, '', f)).toBe('abc');
    expect(JSON.parse(seen!.body as string)).toEqual({ only: ['core-a'], driver: 'socket', keep: true });
  });
  it('reports a 409 with the server message', async () => {
    const f = (async () => Response.json({ error: 'busy', runId: 'r' }, { status: 409 })) as typeof fetch;
    await expect(startRun({ only: [], driver: 'in-process', keep: false }, '', f)).rejects.toThrow('busy');
  });
  it('reads game events then done, across arbitrary chunk boundaries', async () => {
    const text = `event: game\ndata: ${JSON.stringify(result('core-a'))}\n\nevent: done\ndata: ${JSON.stringify(report)}\n\n`;
    const cut = [text.slice(0, 7), text.slice(7, 60), text.slice(60)];
    const events = await collect(runEvents('r', '', (async () => stream(cut)) as typeof fetch));
    expect(events.map((e) => e.type)).toEqual(['game', 'done']);
  });
  it('turns an error event into one error and stops', async () => {
    const f = (async () => stream(['event: error\ndata: {"message":"db down"}\n\n'])) as typeof fetch;
    expect(await collect(runEvents('r', '', f))).toEqual([{ type: 'error', message: 'db down' }]);
  });
  it('builds a recording url', () => {
    expect(recordingUrl('r1', 'a b')).toBe('/runs/r1/recordings/a%20b');
  });
});

describe('spectate', () => {
  it('posts to the game and returns the game id and jwt', async () => {
    let url = '', method = '';
    const f = (async (u: string, init?: RequestInit) => { url = u; method = init?.method ?? ''; return Response.json({ gameId: 'g', jwt: 'j' }); }) as typeof fetch;
    expect(await spectate('r1', 'a b', '', f)).toEqual({ gameId: 'g', jwt: 'j' });
    expect([url, method]).toEqual(['/runs/r1/games/a%20b/spectate', 'POST']);
  });
  it('throws on a 404', async () => {
    await expect(spectate('r1', 'x', '', (async () => new Response('', { status: 404 })) as typeof fetch)).rejects.toThrow(SuiteError);
  });
});

describe('page model', () => {
  it('groups games in catalog order', () => {
    expect(groupsOf(games).map((g) => [g.name, g.games.length])).toEqual([['core', 2], ['spells', 1]]);
  });
  it('selects everything and sends an empty only for the full run', () => {
    expect(requestOf(initial(games))).toEqual({ only: [], driver: 'in-process', keep: false });
  });
  it('tracks selection, driver and keep', () => {
    let p = toggleGame(initial(games), 'core-b');
    p = setKeep(setDriver(p, 'socket'), true);
    expect(requestOf(p)).toEqual({ only: ['core-a', 'spell-a'], driver: 'socket', keep: true });
    p = toggleGroup(p, 'core');   // a partly selected group selects fully
    expect(requestOf(p).only).toEqual([]);
    p = toggleGroup(p, 'core');   // a fully selected group clears
    expect(requestOf(p).only).toEqual(['spell-a']);
  });
  it('cannot run with nothing selected or while running', () => {
    let p = initial(games);
    for (const g of groupsOf(games)) p = toggleGroup(p, g.name);
    expect(canRun(p)).toBe(false);
    expect(canRun(started(initial(games), 'r'))).toBe(false);
  });
  it('folds events into results, coverage and the verdict', () => {
    let p = started(initial(games), 'r');
    p = applyEvent(p, { type: 'game', result: result('core-a', false) });
    expect(counts(p)).toEqual({ passed: 0, failed: 1 });
    p = applyEvent(p, { type: 'done', report });
    expect(p.status).toBe('done');
    expect(p.coverage).toHaveLength(1);
    expect(p.passed).toBe(true);
  });
  it('keeps partial results when the run errors', () => {
    let p = applyEvent(started(initial(games), 'r'), { type: 'game', result: result('core-a') });
    p = applyEvent(p, { type: 'error', message: 'boom' });
    expect([p.status, p.message, p.results.size]).toEqual(['error', 'boom', 1]);
  });
});

describe('coverage cell', () => {
  const entry = (state: 'own' | 'pattern' | 'not_automated', fired: boolean) => ({ name: 'X', kind: 'rule', games: [], fired, exact: [], state });
  it('shows a text-only entry as muted, not as a failure', () => {
    expect(coverageCell(entry('not_automated', false))).toEqual({ text: 'not automated', tone: 'muted' });
  });
  it('shows a wrapper as covered by pattern', () => {
    expect(coverageCell(entry('pattern', true))).toEqual({ text: 'by pattern', tone: 'ok' });
  });
  it('shows an own entry by whether it fired', () => {
    expect(coverageCell(entry('own', false))).toEqual({ text: 'no', tone: 'error' });
    expect(coverageCell(entry('own', true))).toEqual({ text: 'yes', tone: 'ok' });
  });
});
