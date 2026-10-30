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

  it('ruleTexts and grantableEffects get the pinned set\'s rows with the bearer', async () => {
    const calls: Call[] = [];
    const rules = [{ name: 'Tough', kind: 'unit', takesParameter: true, ruleText: 'T.' }];
    const grants = [{ entryName: 'Hex', effectId: 'e', duration: 'once' }];
    const api = apiOf(jwt, '', fake((c) => json(c.url.endsWith('/rules') ? rules : grants), calls));
    expect(await api.ruleTexts('sha256:abc')).toEqual(rules);
    expect(await api.grantableEffects('sha256:abc')).toEqual(grants);
    expect(calls.map((c) => [c.method, c.url])).toEqual([['GET', '/v1/properties/sha256:abc/rules'], ['GET', '/v1/properties/sha256:abc/grants']]);
    expect(calls[0]!.headers.get('authorization')).toBe('Bearer tok');
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
    const api = apiOf(jwt, '', fake(() => json([{ contentHash: 'h', name: 'GF', gameRules: 'gf', default: true }])));
    expect(await api.listProperties()).toEqual([{ contentHash: 'h', name: 'GF', gameRules: 'gf', default: true }]);
  });

  it('randomTable posts the request and returns the terrain', async () => {
    const calls: Call[] = [];
    const answer = { seed: 7, terrain: [], unplaced: [], unlisted: [] };
    const api = apiOf(jwt, '', fake(() => json(answer), calls));
    const request = { size: { width: 72, length: 48 }, planet: 'hospitable', seed: 7 } as const;
    expect(await api.randomTable(request)).toEqual(answer);
    expect(calls[0]).toMatchObject({ url: '/v1/layouts/random', method: 'POST', body: request });
  });

  it('builtinCatalog is null on 404', async () => {
    const api = apiOf(jwt, '', fake(() => json({ title: 'Not found' }, 404)));
    expect(await api.builtinCatalog()).toBeNull();
    const broken = apiOf(jwt, '', fake(() => json({ title: 'Bad gateway' }, 502)));
    await expect(broken.builtinCatalog()).rejects.toBeInstanceOf(ManagementError);
  });

  it('terrainAssets keeps uploaded terrain only', async () => {
    const calls: Call[] = [];
    const api = apiOf(jwt, '', fake((c) => c.url.endsWith('after=a1')
      ? json({ items: [{ id: 'a2', uploaded: true }] })
      : json({ items: [{ id: 'a1', uploaded: true }, { id: 'a0', uploaded: false }], next: 'a1' }), calls));
    expect((await api.terrainAssets()).map((a) => a.id)).toEqual(['a1', 'a2']);
    expect(calls[0]!.url).toBe('/v1/assets?kind=terrain&limit=200');
  });

  it('deleteLayout accepts 204', async () => {
    const calls: Call[] = [];
    const api = apiOf(jwt, '', fake(() => new Response(null, { status: 204 }), calls));
    await api.deleteLayout('l1');
    expect(calls[0]).toMatchObject({ url: '/v1/layouts/l1', method: 'DELETE' });
  });
});
