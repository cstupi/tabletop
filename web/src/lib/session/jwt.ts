// web/src/lib/session/jwt.ts — the JWT is opaque to us except for when it expires and which roles it names. Nothing here
// verifies it; management and the game server do.
function payload(jwt: string): Record<string, unknown> | null {
  const parts = jwt.split('.');
  if (parts.length !== 3) return null;
  try {
    const value: unknown = JSON.parse(atob(parts[1]!.replace(/-/g, '+').replace(/_/g, '/')));
    return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
  } catch { return null; }
}

export function decodeExp(jwt: string): number | null {
  const exp = payload(jwt)?.exp;
  return typeof exp === 'number' ? exp : null;
}

/** The Stytch role ids management stamps in the `roles` claim (admin watch spec); a list, or one id as a string. */
export function decodeRoles(jwt: string): string[] {
  const roles = payload(jwt)?.roles;
  if (typeof roles === 'string') return [roles];
  return Array.isArray(roles) ? roles.filter((r): r is string => typeof r === 'string') : [];
}

/** Shows the admin-only nav entry; the servers check the role themselves. */
export const isAdmin = (jwt: string | null): boolean => jwt !== null && decodeRoles(jwt).includes('admin');

/** Once a minute, but never later than 30 s before expiry, and at once when already expired. */
export function refreshDelayMs(exp: number | null, nowMs = Date.now(), { intervalMs = 60_000, marginMs = 30_000 } = {}): number {
  if (exp === null) return intervalMs;
  const untilExpiry = exp * 1000 - nowMs;
  return Math.max(0, Math.min(intervalMs, untilExpiry - marginMs));
}
