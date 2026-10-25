// viewer/src/lib/shell/auth.ts
// One sign-in for the whole shell: the website's session, read from `tabletop.session` on the website's origin. Development serves
// the viewer through the website's dev server at /viewer/ (web/vite.config.ts), so both builds sign in the same way.

import { hostedSession, signInHref, accessOf, expOf, readWebSession, type Access, type StorageLike } from '../hosted/hosted';
import { ManagementError, type Session } from '../live/management';

export { signInHref };

export interface Auth {
  readonly access: Access | 'checking';
  /** A JWT for one use: the stored one while it has over a minute left, else one shared refresh written back to `tabletop.session`.
   *  Throws signed out. */
  jwt(): Promise<string>;
  /** The account (`GET /v1/account`), fetched once after sign-in. */
  account(): Promise<{ readonly id: string; readonly name: string }>;
}

const defaultBase = (): string => import.meta.env.VITE_API_BASE ?? '';
const SIGNED_OUT = 'signed out of Tabletop';

function accountOf(jwt: () => Promise<string>, fetchFn: typeof fetch): () => Promise<{ readonly id: string; readonly name: string }> {
  let cached: Promise<{ readonly id: string; readonly name: string }> | null = null;
  return () => {
    cached ??= (async () => {
      const response = await fetchFn(`${defaultBase()}/v1/account`, { headers: { authorization: `Bearer ${await jwt()}` } });
      if (!response.ok) throw new ManagementError(`/v1/account answered ${response.status}`, response.status);
      const body = (await response.json()) as { id: string; name: string };
      return { id: body.id, name: body.name };
    })().catch((e: unknown) => { cached = null; throw e; });
    return cached;
  };
}

/** A stored JWT serves until this long before its `exp` (the website refreshes 30 s before it). */
export const REUSE_MARGIN_MS = 60_000;

export function hostedAuth(
  storage: StorageLike, refresh: (jwt: string) => Promise<Session>, fetchFn: typeof fetch = fetch, now: () => number = Date.now,
): Auth {
  let access: Access | 'checking' = 'checking';
  let inFlight: Promise<string> | null = null;
  const jwt = (): Promise<string> => {
    // The stored token (this page's last refresh, or the website's) serves every call until it nears its exp.
    const current = inFlight === null ? readWebSession(storage) : null;
    const exp = current === null ? null : expOf(current);
    if (current !== null && exp !== null && exp * 1000 - now() > REUSE_MARGIN_MS) {
      access = accessOf(current);
      return Promise.resolve(current);
    }
    // Concurrent callers share one refresh: each would otherwise read and write the same stored token.
    inFlight ??= (async () => {
      try {
        const result = await hostedSession(storage, refresh);
        access = result.access;
        if (result.sessionJwt === null) throw new Error(SIGNED_OUT);
        return result.sessionJwt;
      } finally { inFlight = null; }
    })();
    return inFlight;
  };
  return { get access() { return access; }, jwt, account: accountOf(jwt, fetchFn) };
}
