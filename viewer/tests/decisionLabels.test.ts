import { describe, expect, it } from 'vitest';
import { labelParts } from '../src/lib/play/decisionLabels';

describe('labelParts', () => {
  it('splits a leading number key', () => {
    expect(labelParts('2. Assault Brothers')).toEqual({ digit: '2', text: 'Assault Brothers', key: null });
  });
  it('splits a trailing key', () => {
    expect(labelParts('Commit move (Enter)')).toEqual({ digit: null, text: 'Commit move', key: 'Enter' });
    expect(labelParts('Stop here (C)')).toEqual({ digit: null, text: 'Stop here', key: 'C' });
  });
  it('leaves other brackets and plain labels alone', () => {
    expect(labelParts('Pass')).toEqual({ digit: null, text: 'Pass', key: null });
    expect(labelParts('Rifle (2 left)')).toEqual({ digit: null, text: 'Rifle (2 left)', key: null });
    expect(labelParts('10. Ten')).toEqual({ digit: null, text: '10. Ten', key: null });
  });
});
