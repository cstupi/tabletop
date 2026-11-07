import { describe, expect, it } from 'vitest';
import { createDefaults } from '../src/lib/games/listModel';

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
