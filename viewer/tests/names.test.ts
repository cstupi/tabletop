import { describe, expect, it } from 'vitest';
import { NAME, nameProblem } from '../src/lib/api/names';

describe('nameProblem', () => {
  it('a blank name is required', () => {
    expect(nameProblem('   ')).toBe('A name is required.');
    expect(nameProblem(null, 'A game name')).toBe('A game name is required.');
  });
  it('65 characters is too long', () => {
    expect(NAME).toBe(64);
    expect(nameProblem('x'.repeat(64))).toBeNull();
    expect(nameProblem('x'.repeat(65))).toBe('A name is at most 64 characters.');
  });
  it('a bidi mark is refused', () => {
    expect(nameProblem('a‮b')).toBe('A name contains a control or bidirectional formatting character.');
    expect(nameProblem('a\tb')).toBe('A name contains a control or bidirectional formatting character.');
  });
  it('a good name passes, trimmed', () => {
    expect(nameProblem('  Skirmish at dawn  ')).toBeNull();
  });
});
