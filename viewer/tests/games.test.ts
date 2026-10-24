import { describe, expect, it } from 'vitest';
import { listGames, rowOf, type GameSummary } from '../src/lib/live/games';

const summary = (id: string, over: Partial<GameSummary> = {}): GameSummary => ({
  id, name: `game ${id}`, status: 'playing', createdBy: 'c', createdAt: '2026-10-09T12:34:00Z',
  participants: 2, participantNames: ['Ann', 'Bo'], joined: false, ...over,
});

type Call = { url: string; auth: string | null };

function fakeFetch(pages: Record<string, unknown>, calls: Call[], status = 200): typeof fetch {
  return (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, auth: new Headers(init?.headers).get('authorization') });
    const body = status === 200 ? pages[url] : { title: 'Unauthorized', detail: 'unauthorized_credentials' };
    return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
  }) as typeof fetch;
}

describe('the game list', () => {
  it('follows the keyset to the end, with the bearer on every page', async () => {
    const calls: Call[] = [];
    const games = await listGames('jwt-1', {}, '', fakeFetch({
      '/v1/games?limit=200': { items: [summary('b'), summary('a')], next: 'a' },
      '/v1/games?limit=200&after=a': { items: [summary('0')], next: null },
    }, calls));
    expect(games.map((g) => g.id)).toEqual(['b', 'a', '0']);
    expect(calls.map((c) => c.auth)).toEqual(['Bearer jwt-1', 'Bearer jwt-1']);
  });

  it('stops at a last page that leaves next out', async () => {
    const calls: Call[] = [];
    const games = await listGames('jwt-1', {}, '', fakeFetch({ '/v1/games?limit=200': { items: [summary('a')] } }, calls));
    expect(games.map((g) => g.id)).toEqual(['a']);
    expect(calls.map((c) => c.url)).toEqual(['/v1/games?limit=200']);
  });

  it('listGames with joined asks for joined=true', async () => {
    const calls: Call[] = [];
    await listGames('j', { joined: true }, '', fakeFetch({ '/v1/games?limit=200&joined=true': { items: [] } }, calls));
    expect(calls.map((c) => c.url)).toEqual(['/v1/games?limit=200&joined=true']);
  });

  it('reports the problem detail when management refuses', async () => {
    await expect(listGames('bad', {}, '', fakeFetch({}, [], 401))).rejects.toThrow('unauthorized_credentials');
  });

  it('shows name, status, players and the created time per row', () => {
    const row = rowOf(summary('g1', { status: 'ended', participantNames: ['Ann', 'Bo', 'Cy'] }));
    expect(row).toEqual({ id: 'g1', name: 'game g1', status: 'ended', players: 'Ann, Bo, Cy', created: '2026-10-09 12:34' });
    expect(rowOf(summary('g2', { participantNames: [] })).players).toBe('nobody');
  });
});
