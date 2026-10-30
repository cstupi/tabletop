import { describe, expect, it } from 'vitest';
import { createDefaults, listsOf } from '../src/lib/games/listModel';
import type { GameSummary } from '../src/lib/live/games';

const g = (id: string, over: Partial<GameSummary> = {}): GameSummary => ({
  id, name: id, status: 'lobby', createdBy: 'c', createdAt: '2026-10-09T12:00:00Z',
  participants: 1, participantNames: ['A'], joined: false, ...over,
});

describe('listsOf', () => {
  it('splits joined from open lobby and setup games', () => {
    const all = [
      g('mine-old', { joined: true, createdAt: '2026-10-01T00:00:00Z' }),
      g('mine-ended', { joined: true, status: 'ended', createdAt: '2026-10-05T00:00:00Z' }),
      g('open-lobby'), g('open-setup', { status: 'setup' }),
      g('theirs-playing', { status: 'playing' }),
      g('mine-new', { joined: true, status: 'playing', createdAt: '2026-10-08T00:00:00Z' }),
    ];
    const lists = listsOf(all);
    expect(lists.mine.map((x) => x.id)).toEqual(['mine-new', 'mine-ended', 'mine-old']);
    expect(lists.open.map((x) => x.id)).toEqual(['open-lobby', 'open-setup']);
  });
});

describe('createDefaults', () => {
  const packs = [{ contentHash: 'h1', name: 'GF', gameRules: 'gf', default: false }, { contentHash: 'h2', name: 'W40k', gameRules: 'w40k', default: false }];
  it('takes the first pack, alternating activation and warn', () => {
    expect(createDefaults(packs)).toEqual({
      name: '', propertiesHash: 'h1', schedulerShape: 'alternating-activation', enforcement: 'warn', waitsBetweenSteps: false,
    });
  });
  it('takes the flagged pack when it is not first', () => {
    const flagged = [packs[0]!, { ...packs[1]!, default: true }];
    expect(createDefaults(flagged)?.propertiesHash).toBe('h2');
  });
  it('is null without packs', () => {
    expect(createDefaults([])).toBeNull();
  });
});
