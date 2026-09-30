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
