import { describe, expect, it } from 'vitest';
import { apiOf } from '../src/lib/api/client';
import { ManagementError } from '../src/lib/live/management';
import { PROTOCOL_VERSION } from '../src/lib/protocol/wire.generated';

type Call = { url: string; method: string; headers: Headers; body: unknown };

function fake(answer: (call: Call) => Response, calls: Call[] = []): typeof fetch {
  return (async (input: RequestInfo | URL, init?: RequestInit) => {
    const call = { url: String(input), method: init?.method ?? 'GET', headers: new Headers(init?.headers),
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined };
    calls.push(call);
    return answer(call);
  }) as typeof fetch;
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
const jwt = async () => 'tok';

describe('the management api', () => {
  it('createGame posts the request with the bearer and the client version', async () => {
    const calls: Call[] = [];
    const api = apiOf(jwt, '', fake(() => json({ id: 'g1' }), calls));
    const request = { name: 'N', propertiesHash: 'h', schedulerShape: 'alternating-activation', enforcement: 'warn', waitsBetweenSteps: false } as const;
    expect(await api.createGame(request)).toEqual({ id: 'g1' });
    expect(calls[0]).toMatchObject({ url: '/v1/games', method: 'POST', body: request });
    expect(calls[0]!.headers.get('authorization')).toBe('Bearer tok');
    expect(calls[0]!.headers.get('tabletop-client-version')).toBe(String(PROTOCOL_VERSION));
  });

  it('listArmies follows next until it is absent', async () => {
    const calls: Call[] = [];
    const api = apiOf(jwt, '', fake((c) => c.url.endsWith('after=a1')
      ? json({ items: [{ id: 'a2' }] }) : json({ items: [{ id: 'a1' }], next: 'a1' }), calls));
    expect((await api.listArmies()).map((a) => a.id)).toEqual(['a1', 'a2']);
    expect(calls.map((c) => c.url)).toEqual(['/v1/armies?limit=200', '/v1/armies?limit=200&after=a1']);
  });

  it('a 422 problem throws its detail with the status', async () => {
    const api = apiOf(jwt, '', fake(() => json({ title: 'Unprocessable', detail: 'A game name is required.' }, 422)));
    const error = await api.createGame({} as never).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ManagementError);
    expect((error as ManagementError).message).toBe('A game name is required.');
    expect((error as ManagementError).status).toBe(422);
  });

  it('leaveGame accepts 204, join and end and delete use their verbs', async () => {
    const calls: Call[] = [];
    const api = apiOf(jwt, '', fake((c) => c.url.endsWith('/join') ? json({ id: 'g' }) : new Response(null, { status: 204 }), calls));
    await api.leaveGame('g'); await api.endGame('g'); await api.deleteGame('g'); await api.joinGame('g');
    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual([
      'POST /v1/games/g/leave', 'POST /v1/games/g/end', 'DELETE /v1/games/g', 'POST /v1/games/g/join']);
  });

  it('listProperties reads the list', async () => {
    const api = apiOf(jwt, '', fake(() => json([{ contentHash: 'h', name: 'GF', gameRules: 'gf' }])));
    expect(await api.listProperties()).toEqual([{ contentHash: 'h', name: 'GF', gameRules: 'gf' }]);
  });
});
