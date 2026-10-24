import { describe, expect, it, vi } from 'vitest';
import { devAuth, hostedAuth } from '../src/lib/shell/auth';
import { WEB_SESSION_KEY, readWebSession } from '../src/lib/hosted/hosted';

const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64url');
const jwt = (claims: Record<string, unknown>) => `${b64({ alg: 'RS256' })}.${b64({ sub: 'u', exp: 1, ...claims })}.sig`;
const admin = jwt({ roles: ['admin'] });
const player = jwt({ roles: [] });

function store(initial: Record<string, string> = {}) {
  const items = new Map(Object.entries(initial));
  return { items, getItem: (k: string) => items.get(k) ?? null, setItem: (k: string, v: string) => { items.set(k, v); } };
}
const webSession = (sessionJwt: string) => JSON.stringify({ sessionJwt, account: { id: 'a', name: 'Ann' } });

describe('hostedAuth', () => {
  it('refreshes on each jwt() and writes the token back', async () => {
    const s = store({ [WEB_SESSION_KEY]: webSession(player) });
    const seen: string[] = [];
    const auth = hostedAuth(s, async (j) => { seen.push(j); return { sessionJwt: j === player ? admin : player }; });
    expect(auth.access).toBe('checking');
    expect(await auth.jwt()).toBe(admin);
    expect(auth.access).toBe('admin');
    expect(await auth.jwt()).toBe(player);
    expect(auth.access).toBe('player');
    expect(seen).toEqual([player, admin]);
    expect(readWebSession(s)).toBe(player);
  });

  it('is signed out with nothing stored', async () => {
    const auth = hostedAuth(store(), async () => { throw new Error('not called'); });
    await expect(auth.jwt()).rejects.toThrow('signed out');
    expect(auth.access).toBe('signed-out');
  });

  it('account() fetches once', async () => {
    const s = store({ [WEB_SESSION_KEY]: webSession(player) });
    const fetchFn = vi.fn(async () => new Response(JSON.stringify({ id: 'a', name: 'Ann' }), { status: 200 }));
    const auth = hostedAuth(s, async () => ({ sessionJwt: player }), fetchFn as unknown as typeof fetch);
    expect(await auth.account()).toEqual({ id: 'a', name: 'Ann' });
    expect(await auth.account()).toEqual({ id: 'a', name: 'Ann' });
    expect(fetchFn).toHaveBeenCalledTimes(1);
    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/v1/account');
    expect((init.headers as Record<string, string>).authorization).toBe(`Bearer ${player}`);
  });
});

describe('devAuth', () => {
  it('throws before signIn and reads admin from the roles claim', async () => {
    const auth = devAuth(async (j) => ({ sessionJwt: j }));
    await expect(auth.jwt()).rejects.toThrow('signed out');
    expect(auth.access).toBe('signed-out');
    auth.usePasted(admin);
    expect(auth.access).toBe('admin');
    expect(await auth.jwt()).toBe(admin);
    auth.usePasted(player);
    expect(auth.access).toBe('player');
  });
});
