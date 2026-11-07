import { describe, expect, it } from 'vitest';
import { ago } from '../src/lib/games/present';

describe('the games list words', () => {
  it('says when a game was made in words', () => {
    const now = new Date('2026-10-24T12:00:00Z');
    expect(ago('2026-10-24T11:59:40Z', now)).toBe('just now');
    expect(ago('2026-10-24T11:55:00Z', now)).toBe('5 minutes ago');
    expect(ago('2026-10-24T09:00:00Z', now)).toBe('3 hours ago');
    expect(ago('2026-10-23T10:00:00Z', now)).toBe('yesterday');
    expect(ago('2026-10-20T12:00:00Z', now)).toBe('4 days ago');
  });
});
