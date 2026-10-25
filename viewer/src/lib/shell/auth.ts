// viewer/src/lib/shell/auth.ts
// One sign-in for the whole shell: the website's session, read from `tabletop.session` on the website's origin. Development serves
// the viewer through the website's dev server at /viewer/ (web/vite.config.ts), so both builds sign in the same way.

import { hostedSession, signInHref, accessOf, type Access, type StorageLike } from '../hosted/hosted';
import { ManagementError, type Session } from '../live/management';

export { signInHref };

export interface Auth {
  readonly access: Access | 'checking';
  /** A fresh JWT for one use (each call refreshes; the hosted build writes it back to `tabletop.session`). Throws signed out. */
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

export function hostedAuth(storage: StorageLike, refresh: (jwt: string) => Promise<Session>, fetchFn: typeof fetch = fetch): Auth {
  let access: Access | 'checking' = 'checking';
  let inFlight: Promise<string> | null = null;
  const jwt = (): Promise<string> => {
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
