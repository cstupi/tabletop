import { describe, expect, it } from 'vitest';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import { nameOfOption, ofOption, split, tooltip } from '../src/lib/play/spellText';

const catalog = RuleTexts.from([
  { name: 'Hex', kind: 'spell', takesParameter: false, ruleText: 'Pick one enemy unit within 18", which takes 2 hits.' },
  { name: 'Blast | Ruin (Wizards)', kind: 'spell', takesParameter: false, ruleText: 'Pick up to 3 units.' },
]);

describe('SpellText', () => {
  it('an option id names its spell before the last bar', () => {
    expect(nameOfOption('Hex|0198d1a0-0000-7000-8000-000000000001')).toBe('Hex');
    expect(nameOfOption('Blast | Ruin (Wizards)|m')).toBe('Blast | Ruin (Wizards)');
    expect(nameOfOption('Hex')).toBe('Hex');
  });

  it('a label splits into name and casting value', () => {
    expect(split('Hex (3)')).toEqual({ name: 'Hex', cost: 3 });
    expect(split('Blast | Ruin (Wizards) (2)')).toEqual({ name: 'Blast | Ruin (Wizards)', cost: 2 });
    expect(split('Blast | Ruin (Wizards)')).toEqual({ name: 'Blast | Ruin (Wizards)', cost: null });
  });

  it('the tooltip is name, value, then catalog text', () => {
    expect(ofOption(catalog, 'Hex|m', 'Hex (3)')).toBe('Hex\nCasting value 3\nPick one enemy unit within 18", which takes 2 hits.');
  });

  it('a spell with no entry or no catalog still names itself and its value', () => {
    expect(tooltip(catalog, 'Bolt', 1)).toBe('Bolt\nCasting value 1');
    expect(tooltip(RuleTexts.empty, 'Hex')).toBe('Hex');
    expect(tooltip(catalog, '')).toBeNull();
  });
});
