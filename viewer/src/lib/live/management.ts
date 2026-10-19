// viewer/src/lib/live/management.ts
// The session JWT for Connect and for the game list (games.ts): POST /v1/auth/password and
// POST /v1/auth/session/refresh. The viewer never touches /v1/armies or /v1/assets.

/** A refusal from management; `status` is the HTTP status when there was an answer. */
export class ManagementError extends Error {
  constructor(message: string, readonly status: number | null = null) { super(message); }
}

export type Session = { readonly sessionJwt: string };

const defaultBase = (): string => import.meta.env.VITE_API_BASE ?? '';

async function post(base: string, path: string, body: unknown): Promise<Session> {
  const response = await fetch(`${base}${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    // Management answers application/problem+json (management plan Global Constraints).
    const problem = (await response.json().catch(() => ({}))) as { title?: string; detail?: string };
    throw new ManagementError(problem.detail ?? problem.title ?? `${path} answered ${response.status}`, response.status);
  }
  const session = (await response.json()) as { sessionJwt?: string };
  if (typeof session.sessionJwt !== 'string') throw new ManagementError(`${path} returned no sessionJwt`);
  return { sessionJwt: session.sessionJwt };
}

export const logIn = (email: string, password: string, base: string = defaultBase()): Promise<Session> =>
  post(base, '/v1/auth/password', { email, password });

/**
 * The JWT lasts five minutes and is verified once, at connect (game server plan → Client shell plan:
 * "an open socket is never re-authenticated"), so this matters only for the next connect.
 */
export const refreshSession = (sessionJwt: string, base: string = defaultBase()): Promise<Session> =>
  post(base, '/v1/auth/session/refresh', { sessionJwt });
