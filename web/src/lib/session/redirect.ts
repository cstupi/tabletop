// web/src/lib/session/redirect.ts — where sign-in returns to: a path on this site, never another origin and never the
// sign-in pages themselves.
const PUBLIC = ['/login', '/reset-password'];
export const HOME = '/armies';

export function safeNext(raw: string | null | undefined): string {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//') || raw.startsWith('/\\')) return HOME;
  try {
    const url = new URL(raw, 'http://x');
    if (url.origin !== 'http://x' || PUBLIC.some((p) => url.pathname.startsWith(p))) return HOME;
    return url.pathname + url.search + url.hash;
  } catch { return HOME; }
}

/** The sign-in page, remembering where the reader was and whether a session just ended. */
export function loginHref(pathAndQuery: string, expired = false): string {
  const params = new URLSearchParams();
  const next = safeNext(pathAndQuery);
  if (next !== HOME) params.set('next', next);
  if (expired) params.set('expired', '1');
  const q = params.toString();
  return q ? `/login?${q}` : '/login';
}
