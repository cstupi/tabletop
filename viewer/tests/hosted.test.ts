import { describe, expect, it, vi } from 'vitest';
import { ManagementError, refreshSession } from '../src/lib/live/management';
import {
  WEB_SESSION_KEY, accessOf, configOf, hostedSession, readWebSession, rolesOf, signInHref, writeWebSession,
} from '../src/lib/hosted/hosted';

const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64url');
const jwt = (claims: Record<string, unknown>) => `${b64({ alg: 'RS256' })}.${b64({ sub: 'u', exp: 1, ...claims })}.sig`;
const admin = jwt({ roles: ['admin'] });
const player = jwt({ roles: [] });

function store(initial: Record<string, string> = {}) {
  const items = new Map(Object.entries(initial));
  return {
    items,
    getItem: (k: string) => items.get(k) ?? null,
    setItem: (k: string, v: string) => { items.set(k, v); },
  };
}

const webSession = (sessionJwt: string) => JSON.stringify({ sessionJwt, account: { id: 'a', name: 'Ann' } });

describe('the hosted viewer', () => {
  it('is hosted in the hosted build mode: socket from the page origin, no Tests runner', () => {
    expect(configOf({ MODE: 'hosted' }, { protocol: 'https:', host: 'tabletop.example.org' }))
      .toEqual({ hosted: true, socketUrl: 'wss://tabletop.example.org/ws', runner: false });
    expect(configOf({ MODE: 'hosted' }, { protocol: 'http:', host: '127.0.0.1:8080' }).socketUrl).toBe('ws://127.0.0.1:8080/ws');
  });

  it('is the developer viewer otherwise: VITE_GAME_SOCKET, its own login, the Tests runner', () => {
    expect(configOf({ MODE: 'development', VITE_GAME_SOCKET: 'ws://127.0.0.1:5200/ws' }, { protocol: 'http:', host: 'x' }))
      .toEqual({ hosted: false, socketUrl: 'ws://127.0.0.1:5200/ws', runner: true });
    expect(configOf({ MODE: 'production' }, { protocol: 'http:', host: 'x' }).socketUrl).toBe('ws://localhost:5200/ws');
  });

  it('reads the website session the web app keeps under tabletop.session', () => {
    expect(WEB_SESSION_KEY).toBe('tabletop.session');
    expect(readWebSession(store({ [WEB_SESSION_KEY]: webSession('j1') }))).toBe('j1');
    expect(readWebSession(store())).toBeNull();
    expect(readWebSession(store({ [WEB_SESSION_KEY]: 'not json' }))).toBeNull();
    expect(readWebSession(store({ [WEB_SESSION_KEY]: '{"account":{}}' }))).toBeNull();
    expect(readWebSession({ getItem: () => { throw new Error('blocked'); }, setItem: () => {} })).toBeNull();
  });

  it('writes a refreshed token back, keeping the account the web app stored', () => {
    const s = store({ [WEB_SESSION_KEY]: webSession('old') });
    writeWebSession(s, 'new');
    expect(JSON.parse(s.items.get(WEB_SESSION_KEY)!)).toEqual({ sessionJwt: 'new', account: { id: 'a', name: 'Ann' } });
    expect(() => writeWebSession({ getItem: () => null, setItem: () => { throw new Error('full'); } }, 'x')).not.toThrow();
  });

  it('reads roles from the token and admits only the admin role', () => {
    expect(rolesOf(jwt({ roles: ['admin', 'x'] }))).toEqual(['admin', 'x']);
    expect(rolesOf(jwt({ roles: 'admin' }))).toEqual(['admin']);
    expect(rolesOf(jwt({}))).toEqual([]);
    expect(rolesOf('garbage')).toEqual([]);
    expect(accessOf(admin)).toBe('admin');
    expect(accessOf(player)).toBe('not-admin');
    expect(accessOf(null)).toBe('signed-out');
  });

  it('sends a signed-out reader to the website sign-in, returning to the viewer', () => {
    expect(signInHref('/viewer/')).toBe('/login?next=%2Fviewer%2F');
  });

  it('keeps the HTTP status of a refused refresh, which hostedSession judges', async () => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401 }));
    try {
      const error = await refreshSession('j', '').catch((e: unknown) => e);
      expect(error).toBeInstanceOf(ManagementError);
      expect((error as ManagementError).status).toBe(401);
    } finally { vi.unstubAllGlobals(); }
  });

  describe('hostedSession', () => {
    it('refreshes the stored token, stores the new one and judges access on it', async () => {
      const s = store({ [WEB_SESSION_KEY]: webSession(player) });
      const sent: string[] = [];
      const result = await hostedSession(s, async (j) => { sent.push(j); return { sessionJwt: admin }; });
      expect(sent).toEqual([player]);
      expect(result).toEqual({ access: 'admin', sessionJwt: admin });
      expect(readWebSession(s)).toBe(admin);
    });

    it('is signed out with no stored session, without calling management', async () => {
      const result = await hostedSession(store(), async () => { throw new Error('not called'); });
      expect(result).toEqual({ access: 'signed-out', sessionJwt: null });
    });

    it('is signed out when management refuses the refresh', async () => {
      const s = store({ [WEB_SESSION_KEY]: webSession(admin) });
      const result = await hostedSession(s, async () => { throw new ManagementError('unauthorized', 401); });
      expect(result).toEqual({ access: 'signed-out', sessionJwt: null });
    });

    it('passes on a failure worth retrying (a 5xx, 429 or no answer)', async () => {
      const s = store({ [WEB_SESSION_KEY]: webSession(admin) });
      await expect(hostedSession(s, async () => { throw new ManagementError('busy', 503); })).rejects.toThrow('busy');
      await expect(hostedSession(s, async () => { throw new ManagementError('slow down', 429); })).rejects.toThrow('slow down');
      await expect(hostedSession(s, async () => { throw new TypeError('network'); })).rejects.toThrow('network');
    });
  });
});
