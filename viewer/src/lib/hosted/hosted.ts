// viewer/src/lib/hosted/hosted.ts
// The hosted build (`npm run build:hosted`, Vite mode `hosted`): the viewer served under /viewer/ on the website's origin
// (web/Dockerfile). It reuses the website's session, which web/src/lib/session/session.svelte.ts keeps in localStorage
// under `tabletop.session` as { sessionJwt, account }, opens the game socket on the page's own host, and has no Tests page
// (no runner behind it). The developer build is served the same way through the website's dev server (web/vite.config.ts) and
// keeps VITE_GAME_SOCKET and the Tests page.

import { ManagementError, type Session } from '../live/management';

export const WEB_SESSION_KEY = 'tabletop.session';
const DEV_SOCKET = 'ws://127.0.0.1:5200/ws';   // never localhost: Windows tries ::1 first, which does not reach WSL

export type ViewerConfig = { readonly hosted: boolean; readonly socketUrl: string; readonly runner: boolean };
type Env = { readonly MODE?: string; readonly VITE_GAME_SOCKET?: string };
type PageLocation = { readonly protocol: string; readonly host: string };

export function configOf(env: Env, location: PageLocation): ViewerConfig {
  if (env.MODE === 'hosted') {
    return { hosted: true, socketUrl: `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`, runner: false };
  }
  return { hosted: false, socketUrl: env.VITE_GAME_SOCKET ?? DEV_SOCKET, runner: true };
}

export type StorageLike = { getItem(key: string): string | null; setItem(key: string, value: string): void };

function stored(storage: StorageLike): Record<string, unknown> | null {
  try {
    const raw = storage.getItem(WEB_SESSION_KEY);
    const value: unknown = raw === null ? null : JSON.parse(raw);
    return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
  } catch { return null; }
}

export function readWebSession(storage: StorageLike): string | null {
  const jwt = stored(storage)?.sessionJwt;
  return typeof jwt === 'string' ? jwt : null;
}

/** Keeps the website's copy current, so its own refresh loop starts from the newest token. */
export function writeWebSession(storage: StorageLike, sessionJwt: string): void {
  try { storage.setItem(WEB_SESSION_KEY, JSON.stringify({ ...stored(storage), sessionJwt })); }
  catch { /* storage blocked or full: the viewer keeps the token in memory */ }
}

/** The Stytch role ids management stamps in the `roles` claim (admin watch spec). Read, never verified: the servers verify. */
export function rolesOf(jwt: string): string[] {
  const roles = claimsOf(jwt)?.roles;
  if (typeof roles === 'string') return [roles];
  return Array.isArray(roles) ? roles.filter((r): r is string => typeof r === 'string') : [];
}

/** The JWT's `exp` in seconds since the epoch, or null. Read, never verified. */
export function expOf(jwt: string): number | null {
  const exp = claimsOf(jwt)?.exp;
  return typeof exp === 'number' ? exp : null;
}

function claimsOf(jwt: string): Record<string, unknown> | null {
  const parts = jwt.split('.');
  if (parts.length !== 3) return null;
  try {
    const claims: unknown = JSON.parse(atob(parts[1]!.replace(/-/g, '+').replace(/_/g, '/')));
    return typeof claims === 'object' && claims !== null ? (claims as Record<string, unknown>) : null;
  } catch { return null; }
}

export type Access = 'signed-out' | 'player' | 'admin';

/** Every signed-in account plays; the `admin` role adds watching any game. The servers check the role themselves. */
export const accessOf = (jwt: string | null): Access =>
  jwt === null ? 'signed-out' : rolesOf(jwt).includes('admin') ? 'admin' : 'player';

/** The website's sign-in page, which returns to `path` afterwards (web/src/lib/session/redirect.ts `safeNext`). */
export const signInHref = (path: string): string => `/login?${new URLSearchParams({ next: path })}`;

/** Management refused the session for good: any 4xx but 429 (the web app's rule). */
const refused = (e: unknown): boolean =>
  e instanceof ManagementError && e.status !== null && e.status >= 400 && e.status < 500 && e.status !== 429;

/**
 * A fresh JWT from the website's session and the access it grants. Signed out with no stored session or a refused
 * refresh; any other failure is thrown, since retrying may succeed.
 */
export async function hostedSession(
  storage: StorageLike,
  refresh: (sessionJwt: string) => Promise<Session>,
): Promise<{ access: Access; sessionJwt: string | null }> {
  const current = readWebSession(storage);
  if (current === null) return { access: 'signed-out', sessionJwt: null };
  let next: string;
  try { next = (await refresh(current)).sessionJwt; }
  catch (e) { if (refused(e)) return { access: 'signed-out', sessionJwt: null }; throw e; }
  writeWebSession(storage, next);
  return { access: accessOf(next), sessionJwt: next };
}
