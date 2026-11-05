import { describe, expect, it } from 'vitest';
import { ago, mainActionOf, playersLine, ruleSetLine } from '../src/lib/games/present';
import type { GameSummary } from '../src/lib/live/games';

const game = (status: GameSummary['status']) => ({ status } as GameSummary);

describe('the games list words', () => {
  it('offers the way back into a game where it stands, and a replay once it ended', () => {
    expect(mainActionOf(game('lobby'))).toEqual({ label: 'Open lobby', replay: false });
    expect(mainActionOf(game('setup'))).toEqual({ label: 'Continue setup', replay: false });
    expect(mainActionOf(game('playing'))).toEqual({ label: 'Continue', replay: false });
    expect(mainActionOf(game('ended'))).toEqual({ label: 'Replay', replay: true });
  });
  it('says when a game was made in words', () => {
    const now = new Date('2026-10-24T12:00:00Z');
    expect(ago('2026-10-24T11:59:40Z', now)).toBe('just now');
    expect(ago('2026-10-24T11:55:00Z', now)).toBe('5 minutes ago');
    expect(ago('2026-10-24T09:00:00Z', now)).toBe('3 hours ago');
    expect(ago('2026-10-23T10:00:00Z', now)).toBe('yesterday');
    expect(ago('2026-10-20T12:00:00Z', now)).toBe('4 days ago');
  });
  it('names up to three players, then counts the rest', () => {
    expect(playersLine([])).toBe('No players yet');
    expect(playersLine(['Ann'])).toBe('Ann');
    expect(playersLine(['Ann', 'Ben', 'Cy'])).toBe('Ann, Ben and Cy');
    expect(playersLine(['Ann', 'Ben', 'Cy', 'Di'])).toBe('Ann, Ben and 2 more');
  });
});

describe('the games list rule set chip', () => {
  it('names the rule set, and has none for a row without it', () => {
    expect(ruleSetLine({ ruleSet: 'Grimdark Future v3.6.0' } as GameSummary)).toBe('Grimdark Future v3.6.0');
    expect(ruleSetLine({} as GameSummary)).toBeNull();
  });
});
