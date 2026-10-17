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

/** Management refused this token for good (401: a dead session; or any other 4xx); a 429, a 5xx
 *  or no answer at all is worth retrying, so it keeps the session. */
const refused = (e: unknown) => e instanceof ApiError && e.status >= 400 && e.status < 500 && e.status !== 429;

class Session {
  token = $state<string | null>(null);
  account = $state<AccountRow | null>(null);
  ready = $state(false);
  /** True once a session ended under the reader (a 401, or a refused refresh) rather than by logging out; the sign-in page says so. */
  expired = $state(false);
  private inFlight: Promise<boolean> | null = null;

  constructor() {
    setTokenProvider(() => this.token);
    http.onUnauthorized = () => this.clear(true);
  }

  accept(s: SessionResponse) {
    this.token = s.sessionJwt;
    this.account = s.account;
    this.expired = false;
    write(s);
  }

  clear(expired = false) {
    if (this.token && expired) this.expired = true;
    this.token = null;
    this.account = null;
    write(null);
  }

  async restore() {
    const stored = read();
    if (stored) { this.token = stored.sessionJwt ?? null; this.account = stored.account ?? null; await this.refresh(); }
    this.ready = true;
  }

  /** A fresh five-minute JWT from the long-lived Stytch session. False and signed out when management refuses (any 4xx but 429);
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
    catch (e) { if (refused(e)) this.clear(true); return false; }
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
