import { describe, expect, it } from 'vitest';
import { Names } from '../src/lib/play/names';
import { RollWords, chain, collapsed, pip, rollEntryOf, type RollEntry } from '../src/lib/play/rollEntry';
import type { Die, RollModifier, RollRecord, SequenceContext, StepCompleted, ThresholdBasis } from '../src/lib/protocol/wire.generated';
import { FRAME, ev, gameOf, newId } from './play';

// RollLineTests: one line per roll, read off rollEntryOf directly.
const context: SequenceContext = { targetUnitIds: [], weaponIds: [] };
const N = new Names(gameOf(), null);

const D = (raw: number, success: boolean, ...triggered: string[]): Die =>
  ({ raw, modified: raw, success, triggered: triggered.length === 0 ? undefined : triggered });
const basis = (stat: string, printed: number): ThresholdBasis => ({ stat, printed, changes: [] });

function record(roll: string, threshold: number | undefined, dice: Die[], b?: ThresholdBasis, requiredRaw?: number, ...modifiers: RollModifier[]): RollRecord {
  return { id: newId(), roll, threshold, modifiers, dice, requiredRaw, basis: b };
}

function entry(r: RollRecord, sequence: string | null = 'shooting'): RollEntry {
  const step: StepCompleted = { kind: 'step_completed', frameId: FRAME, step: 'roll', rolls: [r], changes: [] };
  return rollEntryOf(ev(1, step), false, step, r, context, sequence, [], N);
}

const line = (r: RollRecord, sequence: string | null = 'shooting') => collapsed(entry(r, sequence), pip);

describe('RollLine', () => {
  it('a raw face roll reads unmodified', () => {
    expect(line(record('hit', 6, [D(6, true), D(5, false)], undefined, 6))).toBe('Hit · unmodified 6+ · 2 dice: ⚅ ⚄ · 1 hit');
  });

  it('a roll every face passes reads no target', () => {
    expect(line(record('hit', 1, [D(6, true), D(1, true)], undefined, 1))).toBe('Hit · 2 dice: ⚅ ⚀ · 2 hits');
  });

  it('a roll in a deal-wounds frame counts wounds ignored and standing', () => {
    expect(line(record('Regeneration', 5, [D(1, false), D(6, true), D(1, false)]), 'deal-wounds'))
      .toBe('Regeneration · 5+ · 3 wounds: ⚀ ⚅ ⚀ · 1 ignored, 2 wounds stand');
    expect(line(record('Regeneration', 5, [D(6, true), D(2, false)]), 'deal-wounds'))
      .toBe('Regeneration · 5+ · 2 wounds: ⚅ ⚁ · 1 ignored, 1 wound stands');
  });

  it('a single die test reads passed or failed then its consequence', () => {
    const morale = { ...entry(record('morale', 4, [D(1, false)], basis('Quality', 4)), 'morale-test'), consequence: 'Shaken' };
    expect(collapsed(morale, pip)).toBe('Morale · Quality 4+ · ⚀ · failed → Shaken');
    expect(chain(morale)).toBe('Morale failed → Shaken');
  });

  it('a passed casting roll reads cast', () => {
    expect(line(record('casting', 4, [D(6, true)], undefined, undefined, { source: 'tokens', amount: 1 }), 'casting'))
      .toBe('Casting · 4+ (tokens +1) → needs 3 · ⚅ · cast');
    expect(line(record('casting', 4, [D(1, false)]), 'casting')).toBe('Casting · 4+ · ⚀ · failed');
  });

  it('dangerous terrain fails on a one', () => {
    expect(line(record('dangerous-terrain', 2, [D(2, true), D(1, false)]), 'move')).toBe('Dangerous terrain · 1 fails · 2 dice: ⚁ ⚀ · 1 wound');
  });

  it('two rules share one pair of brackets', () => {
    const r = record('hit', 4, [D(6, true, 'Rending', 'Furious +1 hit'), D(6, true, 'Rending'), D(5, true)], basis('Quality', 4));
    expect(line(r).endsWith(' · 4 hits (2 sixes: Rending; 1 six: Furious +1 hit)')).toBe(true);
  });

  it('a roll-off reads each die after its team and the winner after the faces', () => {
    const r = { ...entry(record('roll-off', undefined, [D(4, false), D(1, false)]), 'roll-off'), dieLabels: ['Team 1', 'Team 2'], consequence: 'Team 1 wins' };
    expect(collapsed(r, pip)).toBe('Roll-off · Team 1 ⚃ · Team 2 ⚀ · Team 1 wins');
  });

  it('roll labels', () => {
    expect(['hit', 'defense', 'morale', 'casting', 'roll-off', 'dangerous-terrain', 'Regeneration'].map(RollWords.label))
      .toEqual(['Hit', 'Block', 'Morale', 'Casting', 'Roll-off', 'Dangerous terrain', 'Regeneration']);
  });
});
