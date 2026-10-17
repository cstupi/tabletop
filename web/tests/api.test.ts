import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, http, setTokenProvider } from '../src/lib/api/http';
import { api } from '../src/lib/api/client';

function fakeFetch(status: number, body: unknown, contentType = 'application/json') {
  const f = vi.fn(async () => new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'content-type': contentType } }));
  vi.stubGlobal('fetch', f);
  return f;
}
afterEach(() => { vi.unstubAllGlobals(); setTokenProvider(() => null); http.onUnauthorized = () => {}; });

describe('http', () => {
  it('sends the bearer token and JSON, and parses the body', async () => {
    setTokenProvider(() => 'jwt-1');
    const f = fakeFetch(200, { id: 'a', name: 'Clay' });
    expect(await http.get('/v1/account')).toEqual({ id: 'a', name: 'Clay' });
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/v1/account');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer jwt-1');
  });

  it('turns problem+json into ApiError and 204 into undefined', async () => {
    fakeFetch(422, { title: 'Unprocessable', detail: 'name is required', status: 422 }, 'application/problem+json');
    await expect(http.post('/v1/folders', { name: '' })).rejects.toMatchObject(new ApiError(422, 'Unprocessable', 'name is required'));
    fakeFetch(204, undefined);
    expect(await http.del('/v1/folders/x')).toBeUndefined();
  });

  it('calls onUnauthorized on a 401 to a bearer request but not to /v1/auth', async () => {
    const seen: string[] = [];
    http.onUnauthorized = () => seen.push('401');
    setTokenProvider(() => 'stale');
    fakeFetch(401, { title: 'Unauthorized' }, 'application/problem+json');
    await expect(http.get('/v1/account')).rejects.toBeInstanceOf(ApiError);
    await expect(http.post('/v1/auth/password', { email: 'a', password: 'b' })).rejects.toBeInstanceOf(ApiError);
    expect(seen).toEqual(['401']);
  });
});

describe('client', () => {
  it('builds keyset query strings and the documented paths', async () => {
    const f = fakeFetch(200, { items: [], next: undefined });
    await api.assets.list({ limit: 20, after: 'abc', kind: 'terrain' });
    await api.armies.list({});
    await api.folders.addAsset('f1', 'a1');
    const urls = f.mock.calls.map((c) => (c as unknown as [string, RequestInit])[0]);
    expect(urls).toEqual(['/v1/assets?limit=20&after=abc&kind=terrain', '/v1/armies', '/v1/folders/f1/assets/a1']);
    expect((f.mock.calls[2] as unknown as [string, RequestInit])[1].method).toBe('PUT');
  });

  it('sends the chosen base set with an import, round when none is given', async () => {
    const f = fakeFetch(201, { id: 'a', name: 'A', gameSystem: 'grimdark-future', source: 's', units: [] });
    await api.armies.import('c2-6YsWRH3py', 'square');
    await api.armies.import('c2-6YsWRH3py');
    const bodies = f.mock.calls.map((c) => JSON.parse((c as unknown as [string, RequestInit])[1].body as string));
    expect(bodies).toEqual([{ source: 'c2-6YsWRH3py', bases: 'square' }, { source: 'c2-6YsWRH3py', bases: 'round' }]);
  });
});
