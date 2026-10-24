import { describe, expect, it } from 'vitest';
import { rowActionsOf } from '../src/lib/games/rowActions';
import type { GameSummary } from '../src/lib/live/games';

const row = (over: Partial<GameSummary> = {}): GameSummary => ({
  id: 'g', name: 'G', status: 'lobby', createdBy: 'me', createdAt: '2026-10-09T12:34:00Z',
  participants: 1, participantNames: ['Me'], joined: false, ...over,
});
const none = { canEnd: false, canDelete: false, canReplay: false };

describe('GameRowActions', () => {
  it('nothing without a row or an account', () => {
    expect(rowActionsOf(null, 'me')).toEqual(none);
    expect(rowActionsOf(row(), null)).toEqual(none);
  });

  it('the creator ends a game that has not ended and deletes any', () => {
    expect(rowActionsOf(row({ status: 'playing' }), 'me')).toMatchObject({ canEnd: true, canDelete: true });
    expect(rowActionsOf(row({ status: 'ended' }), 'me')).toMatchObject({ canEnd: false, canDelete: true });
  });

  it('a non-creator neither ends nor deletes', () => {
    expect(rowActionsOf(row({ createdBy: 'other', status: 'playing' }), 'me')).toMatchObject({ canEnd: false, canDelete: false });
  });

  it('replay needs a joined game that is playing or ended', () => {
    expect(rowActionsOf(row({ joined: true, status: 'lobby' }), 'me').canReplay).toBe(false);
    expect(rowActionsOf(row({ joined: false, status: 'playing' }), 'me').canReplay).toBe(false);
    expect(rowActionsOf(row({ joined: true, status: 'ended' }), 'me').canReplay).toBe(true);
    expect(rowActionsOf(row({ joined: true, status: 'playing', createdBy: 'other' }), 'me').canReplay).toBe(true);
  });
});
