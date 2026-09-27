# Task 3: The session — token storage, refresh, logout

**Files:**
- Create: `web/src/lib/session/jwt.ts`, `web/src/lib/session/session.svelte.ts`
- Test: `web/tests/jwt.test.ts`, `web/tests/session.test.ts`

**Interfaces:**
- Consumes: `api.auth.refresh/logout`, `api.account.get`, `setTokenProvider`, `http.onUnauthorized`, `SessionResponse`.
- Produces: `decodeExp(jwt): number | null` (seconds since epoch); `refreshDelayMs(exp, now, { intervalMs = 60_000, marginMs = 30_000 })`; `session` (a runes store in a `.svelte.ts` module) with `token: string | null`, `account: AccountRow | null`, `ready: boolean`, `accept(SessionResponse)`, `restore(): Promise<void>` (reads `localStorage['tabletop.session']`, refreshes once, loads the account), `refresh(): Promise<boolean>`, `logout(): Promise<void>`, `clear()`, `startRefreshLoop(): () => void`; `STORAGE_KEY = 'tabletop.session'`.
- Decisions: the JWT is kept in `localStorage` so a reload keeps the user signed in (ruling 1); the refresh loop runs every 60 s while a token exists and also immediately on `restore`, because a five-minute JWT is stale after any tab sleep; a `401` from any bearer call clears the session and sends the user to `/login` (via `http.onUnauthorized`); `logout` calls `DELETE /v1/auth/session` and clears storage whatever the answer.

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
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new Error('401'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBeNull();
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
import { http, setTokenProvider } from '$lib/api/http';
import type { AccountRow, SessionResponse } from '$lib/api/types';
import { decodeExp, refreshDelayMs } from './jwt';

export const STORAGE_KEY = 'tabletop.session';

function read(): SessionResponse | null {
  try { const raw = localStorage.getItem(STORAGE_KEY); return raw ? (JSON.parse(raw) as SessionResponse) : null; } catch { return null; }
}

class Session {
  token = $state<string | null>(null);
  account = $state<AccountRow | null>(null);
  ready = $state(false);

  constructor() {
    setTokenProvider(() => this.token);
    http.onUnauthorized = () => this.clear();
  }

  accept(s: SessionResponse) {
    this.token = s.sessionJwt;
    this.account = s.account;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
  }

  clear() {
    this.token = null;
    this.account = null;
    localStorage.removeItem(STORAGE_KEY);
  }

  async restore() {
    const stored = read();
    if (stored) { this.token = stored.sessionJwt; this.account = stored.account; await this.refresh(); }
    this.ready = true;
  }

  /** A fresh five-minute JWT from the long-lived Stytch session. False (and signed out) when management refuses. */
  async refresh(): Promise<boolean> {
    if (!this.token) return false;
    try { this.accept(await api.auth.refresh(this.token)); return true; }
    catch { this.clear(); return false; }
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
      await this.refresh();
      timer = setTimeout(tick, refreshDelayMs(this.token ? decodeExp(this.token) : null));
    };
    timer = setTimeout(tick, refreshDelayMs(this.token ? decodeExp(this.token) : null));
    return () => clearTimeout(timer);
  }
}

export const session = new Session();
```

- [ ] **Step 4: Run `npm test`; expect 9 passed (runes in `.svelte.ts` compile under the SvelteKit Vite plugin). Commit: `Keep the session JWT, refresh it every minute, and sign out on 401`.**
