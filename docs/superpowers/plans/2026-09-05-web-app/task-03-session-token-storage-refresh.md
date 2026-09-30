# Task 3: The session — token storage, refresh, logout

**Files:**
- Create: `web/src/lib/session/jwt.ts`, `web/src/lib/session/session.svelte.ts`
- Test: `web/tests/jwt.test.ts`, `web/tests/session.test.ts`

**Interfaces:**
- Consumes: `api.auth.refresh/logout`, `api.account.get`, `setTokenProvider`, `http.onUnauthorized`, `SessionResponse`.
- Produces: `decodeExp(jwt): number | null` (seconds since epoch); `refreshDelayMs(exp, now, { intervalMs = 60_000, marginMs = 30_000 })`; `session` (a runes store in a `.svelte.ts` module) with `token: string | null`, `account: AccountRow | null`, `ready: boolean`, `accept(SessionResponse)`, `restore(): Promise<void>` (reads `localStorage['tabletop.session']`, coalesces a wrong-shaped record to `null`, refreshes once and takes the account from the answer), `refresh(): Promise<boolean>`, `logout(): Promise<void>`, `clear()`, `startRefreshLoop(): () => void`; `STORAGE_KEY = 'tabletop.session'`.
- Decisions: the JWT is kept in `localStorage` so a reload keeps the user signed in (ruling 1); the refresh loop runs every 60 s while a token exists and also immediately on `restore`, because a five-minute JWT is stale after any tab sleep; a `401` from any bearer call clears the session and sends the user to `/login` (via `http.onUnauthorized`); `logout` calls `DELETE /v1/auth/session` and clears storage whatever the answer.
- Decisions, refresh failures: `refresh()` clears the session only on an `ApiError` with status 401. A `TypeError` (offline, or an answer without CORS headers), a 429 or a 5xx returns `false` and keeps the token, because management maps a Stytch outage to 502 or 429 and Vite's proxy answers 502 while management restarts. After a failed refresh the loop waits the full 60 s rather than recomputing from an expired token, which would retry at once. Overlapping `refresh()` calls share one in-flight promise, so `restore()` and the loop's first tick make one request and cannot sign out a user who has just signed in again. `accept()` and `clear()` write `localStorage` through a guarded `write()`, so a storage-blocked browser keeps the session in memory for the tab and the caller still sees its own error.

Spec: management API → We issue no tokens of our own ("a JWT good for five minutes, refreshed every minute"); management plan Task 5 (`/session/refresh` accepts an expired JWT while the Stytch session lives; `SessionMinutes` 43200); accounts → Auth is a module ("Nothing outside management holds a Stytch secret").

- [ ] **Step 1: Write the failing tests**

```ts
// web/tests/jwt.test.ts
import { describe, expect, it } from 'vitest';
import { decodeExp, refreshDelayMs } from '../src/lib/session/jwt';

const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64url');
const jwt = (exp: number) => `${b64({ alg: 'RS256' })}.${b64({ sub: 'u', exp })}.sig`;

describe('jwt', () => {
  it('reads exp from the payload without verifying anything', () => {
    expect(decodeExp(jwt(1_800_000_000))).toBe(1_800_000_000);
    expect(decodeExp('not a jwt')).toBeNull();
    expect(decodeExp(`${b64({})}.${b64({ sub: 'u' })}.x`)).toBeNull();
  });

  it('refreshes every minute, or sooner when expiry is near', () => {
    const now = 1_000_000_000_000;
    expect(refreshDelayMs(now / 1000 + 300, now)).toBe(60_000);       // five minutes left → one minute
    expect(refreshDelayMs(now / 1000 + 45, now)).toBe(15_000);        // 45 s left → 30 s before expiry
    expect(refreshDelayMs(now / 1000 - 10, now)).toBe(0);             // already expired → now
    expect(refreshDelayMs(null, now)).toBe(60_000);
  });
});
```

```ts
// web/tests/session.test.ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { STORAGE_KEY, session } from '../src/lib/session/session.svelte';
import { ApiError } from '../src/lib/api/http';
import { api } from '../src/lib/api/client';

const store = new Map<string, string>();
beforeEach(() => {
  vi.stubGlobal('localStorage', { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => store.set(k, v), removeItem: (k: string) => store.delete(k) });
  store.clear();
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('session', () => {
  it('accept stores the token and account; logout revokes and clears', async () => {
    const logout = vi.spyOn(api.auth, 'logout').mockResolvedValue(undefined);
    session.accept({ sessionJwt: 'jwt-a', account: { id: 'acc', name: 'Clay' } });
    expect(session.token).toBe('jwt-a');
    expect(JSON.parse(store.get(STORAGE_KEY)!)).toEqual({ sessionJwt: 'jwt-a', account: { id: 'acc', name: 'Clay' } });
    await session.logout();
    expect(logout).toHaveBeenCalled();
    expect(session.token).toBeNull();
    expect(store.has(STORAGE_KEY)).toBe(false);
  });

  it('restore refreshes the stored token and loads the account; a failed refresh signs out', async () => {
    store.set(STORAGE_KEY, JSON.stringify({ sessionJwt: 'old', account: { id: 'acc', name: 'Old' } }));
    vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'new', account: { id: 'acc', name: 'Clay' } });
    await session.restore();
    expect(session.token).toBe('new');
    expect(session.account?.name).toBe('Clay');
    expect(session.ready).toBe(true);
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiError(401, 'Unauthorized'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBeNull();
  });

  it('a network failure during refresh keeps the session', async () => {
    store.set(STORAGE_KEY, JSON.stringify({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } }));
    vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } });
    await session.restore();
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new TypeError('Failed to fetch'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBe('live');
    expect(session.account?.name).toBe('Clay');
  });

  it('a non-401 API failure during refresh keeps the session', async () => {
    store.set(STORAGE_KEY, JSON.stringify({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } }));
    vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } });
    await session.restore();
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiError(502, 'Upstream failure'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBe('live');
  });

  it('overlapping refreshes make one request', async () => {
    session.accept({ sessionJwt: 'old', account: { id: 'acc', name: 'Clay' } });
    const refresh = vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'new', account: { id: 'acc', name: 'Clay' } });
    const [a, b] = await Promise.all([session.refresh(), session.refresh()]);
    expect(a).toBe(true);
    expect(b).toBe(true);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(session.token).toBe('new');
  });
});
```

- [ ] **Step 2: Run; expect failures. Write `jwt.ts`:**

```ts
// web/src/lib/session/jwt.ts — the JWT is opaque to us except for when it expires. Nothing here verifies it; management does.
export function decodeExp(jwt: string): number | null {
  const parts = jwt.split('.');
  if (parts.length !== 3) return null;
  try {
    const json = atob(parts[1]!.replace(/-/g, '+').replace(/_/g, '/'));
    const exp = (JSON.parse(json) as { exp?: unknown }).exp;
    return typeof exp === 'number' ? exp : null;
  } catch { return null; }
}

/** Once a minute, but never later than 30 s before expiry, and at once when already expired. */
export function refreshDelayMs(exp: number | null, nowMs = Date.now(), { intervalMs = 60_000, marginMs = 30_000 } = {}): number {
  if (exp === null) return intervalMs;
  const untilExpiry = exp * 1000 - nowMs;
  return Math.max(0, Math.min(intervalMs, untilExpiry - marginMs));
}
```

- [ ] **Step 3: Write `session.svelte.ts`**

```ts
// web/src/lib/session/session.svelte.ts
import { api } from '$lib/api/client';
import { ApiError, http, setTokenProvider } from '$lib/api/http';
import type { AccountRow, SessionResponse } from '$lib/api/types';
import { decodeExp, refreshDelayMs } from './jwt';

export const STORAGE_KEY = 'tabletop.session';

function read(): SessionResponse | null {
  try { const raw = localStorage.getItem(STORAGE_KEY); return raw ? (JSON.parse(raw) as SessionResponse) : null; } catch { return null; }
}

function write(s: SessionResponse | null) {
  try { if (s) localStorage.setItem(STORAGE_KEY, JSON.stringify(s)); else localStorage.removeItem(STORAGE_KEY); }
  catch { /* storage blocked or full: the session lives in memory for this tab only */ }
}

class Session {
  token = $state<string | null>(null);
  account = $state<AccountRow | null>(null);
  ready = $state(false);
  private inFlight: Promise<boolean> | null = null;

  constructor() {
    setTokenProvider(() => this.token);
    http.onUnauthorized = () => this.clear();
  }

  accept(s: SessionResponse) {
    this.token = s.sessionJwt;
    this.account = s.account;
    write(s);
  }

  clear() {
    this.token = null;
    this.account = null;
    write(null);
  }

  async restore() {
    const stored = read();
    if (stored) { this.token = stored.sessionJwt ?? null; this.account = stored.account ?? null; await this.refresh(); }
    this.ready = true;
  }

  /** A fresh five-minute JWT from the long-lived Stytch session. False and signed out when management refuses;
   *  false but still signed in on any other failure (network, 429, 5xx), so those are not a logout.
   *  Overlapping callers share one request: restore() and the loop's first tick both fire on an expired token. */
  async refresh(): Promise<boolean> {
    const token = this.token;
    if (!token) return false;
    if (this.inFlight) return this.inFlight;
    const pending = this.run(token).finally(() => { if (this.inFlight === pending) this.inFlight = null; });
    this.inFlight = pending;
    return pending;
  }

  private async run(token: string): Promise<boolean> {
    try { this.accept(await api.auth.refresh(token)); return true; }
    catch (e) { if (e instanceof ApiError && e.status === 401) this.clear(); return false; }
  }

  async logout() {
    try { await api.auth.logout(); } catch { /* the token may already be dead; clearing is what matters */ }
    this.clear();
  }

  /** Schedules refreshes from the token's exp; returns a stop function for onDestroy. */
  startRefreshLoop(): () => void {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const tick = async () => {
      if (!this.token) { timer = setTimeout(tick, 60_000); return; }
      const ok = await this.refresh();
      timer = setTimeout(tick, ok && this.token ? refreshDelayMs(decodeExp(this.token)) : 60_000);
    };
    timer = setTimeout(tick, refreshDelayMs(this.token ? decodeExp(this.token) : null));
    return () => clearTimeout(timer);
  }
}

export const session = new Session();
```

- [ ] **Step 4: Run `npm test`; expect 12 passed (runes in `.svelte.ts` compile under the SvelteKit Vite plugin). Commit: `Keep the session JWT, refresh it every minute, and sign out on 401`.**
