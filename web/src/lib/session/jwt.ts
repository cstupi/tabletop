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
