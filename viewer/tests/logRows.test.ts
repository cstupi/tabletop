import { describe, expect, it } from 'vitest';
import type { ActivationBlock } from '../src/lib/play/combatLog';
import { FILTER_LABELS, faded, rowText, strike, trayLines, undoHere } from '../src/lib/play/logRows';
import { type LineEntry, type RollEntry } from '../src/lib/play/rollEntry';
import { Enemy, Frame, Round1, Unit, buildSample, ctx, finished, hit, started, step, undone } from './logSamples';
import type { LogEntry } from '../src/lib/play/rollEntry';

// The row text CombatLogPanel.cs and DiceTray.cs draw.
const rolled = () => {
  const { entries } = buildSample(Round1, started(Frame, 'advance', ctx(Unit, [Enemy], [], 'Advance')), step(Frame, 'roll-to-hit', [hit()]), finished(Frame));
  const block = entries.find((e): e is ActivationBlock => e.type === 'block')!;
  const roll = block.entries.find((e): e is RollEntry => e.type === 'roll')!;
  const round = entries.find((e): e is LineEntry => e.type === 'line')!;
  return { entries, block, roll, round };
};

describe('logRows', () => {
  it('a folded block shows its chain and an open one does not', () => {
    const { block } = rolled();
    expect(rowText(block, false)).toBe(`▸ ${block.header}  ·  Hit 1`);
    expect(rowText(block, true)).toBe(`▾ ${block.header}`);
    expect(FILTER_LABELS).toEqual(['All', 'Rolls', 'My units', 'This unit']);
  });

  it('a roll row is its collapsed line', () => {
    const { roll, round } = rolled();
    expect(rowText(roll, false)).toBe('▸ Hit · 4+ (AP(1)) → needs 5 · 2 dice: ⚅ ⚁ · 1 hit');
    expect(rowText(roll, true).startsWith('▾ Hit')).toBe(true);
    expect(rowText(round, false)).toBe('Round 1');
  });

  it('rows past a pending undo fade', () => {
    const { roll, round } = rolled();
    expect(faded(roll, null)).toBe(false);
    expect(faded(round, 1)).toBe(false);
    expect(faded(roll, 2)).toBe(true);                 // seq 3 is past 2
    expect(faded({ ...round, undone: true }, null)).toBe(true);
  });

  it('strike puts a combining stroke after each character', () => {
    expect(strike('ab')).toBe('a̶b̶');
    expect(strike('')).toBe('');
  });

  it('undo to here skips rolls, undone rows, undo markers and watchers', () => {
    const { block, roll, round } = rolled();
    expect(undoHere(block, false)).toBe(true);
    expect(undoHere(round, false)).toBe(true);
    expect(undoHere(roll, false)).toBe(false);
    expect(undoHere({ ...block, undone: true }, false)).toBe(false);
    expect(undoHere(block, true)).toBe(false);
    const { entries } = buildSample(Round1, started(Frame, 'x', ctx(Unit, [], [], 'Hold')), finished(Frame), undone(1));
    const marker = entries.find((e): e is LineEntry => e.type === 'line' && e.event.payload.kind === 'undone') as LogEntry;
    expect(undoHere(marker, false)).toBe(false);
  });

  it('the tray titles the latest roll and carries its collapsed line', () => {
    const { roll } = rolled();
    expect(trayLines(null)).toBeNull();
    const t = trayLines(roll)!;
    expect(t.title).toBe('Hit · 4+ (AP(1)) → needs 5');
    expect(t.faces.map((d) => d.raw)).toEqual([6, 2]);
    expect(t.result).toBe('1 hit');
    expect(t.success).toBe(true);
    expect(t.tooltip).toBe('Hit · 4+ (AP(1)) → needs 5 · 2 dice: ⚅ ⚁ · 1 hit');
    const labelled = trayLines({ ...roll, dieLabels: ['Team 1', 'Team 2'], consequence: 'Team 1 wins' })!;
    expect(labelled.faces).toEqual([]);                  // a roll-off: the team words go between the faces, so the tray shows the line
    expect(labelled.title).toBe(labelled.tooltip);
  });
});
