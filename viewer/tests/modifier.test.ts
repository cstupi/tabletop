import { describe, expect, it } from 'vitest';
import { modifierText } from '../src/lib/recording/modifier';

describe('modifierText', () => {
  it('writes AP as p.13 does', () => {
    expect(modifierText({ source: 'AP', amount: -1 })).toBe('AP(1)');
    expect(modifierText({ source: 'AP', amount: -4 })).toBe('AP(4)');
  });

  it('signs any other modifier', () => {
    expect(modifierText({ source: 'cover', amount: 1 })).toBe('cover +1');
    expect(modifierText({ source: 'Spell', amount: -1 })).toBe('Spell -1');
  });
});
