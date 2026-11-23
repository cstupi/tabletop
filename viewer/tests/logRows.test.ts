import { describe, expect, it } from 'vitest';
import type { ActivationBlock } from '../src/lib/play/combatLog';
import { FILTER_LABELS, bandOf, faded, trayLines, undoHere } from '../src/lib/play/logRows';
import { type LineEntry, type RollEntry, face } from '../src/lib/play/rollEntry';
import { Enemy, Frame, GameSample, Round1, Unit, buildSample, ctx, finished, hit, started, step, undone } from './logSamples';
import type { LogEntry } from '../src/lib/play/rollEntry';

// The row text CombatLogPanel.cs and DiceTray.cs draw.
const rolled = () => {
  const { entries } = buildSample(Round1, started(Frame, 'advance', ctx(Unit, [Enemy], [], 'Advance')), step(Frame, 'roll-to-hit', [hit()]), finished(Frame));
  const block = entries.find((e): e is ActivationBlock => e.type === 'block')!;
  const roll = block.entries.find((e): e is RollEntry => e.type === 'roll')!;
  const round = entries.find((e): e is LineEntry => e.type === 'line')!;
  return { entries, block, roll, round };
};

describe('logRows band (CombatLogRows.Band)', () => {
  it('a block takes its actor\'s team band and any other entry none', () => {
    const { entries, names } = buildSample(Round1, started(Frame, 'advance', ctx(Unit, [Enemy], [], 'Advance')), finished(Frame));
    const block = entries.find((e) => e.type === 'block')!;
    expect(bandOf(block, names, GameSample)).toBe(1);
    expect(bandOf(entries.find((e) => e.type === 'line')!, names, GameSample)).toBe(0);
    expect(bandOf({ ...(block as ActivationBlock), actor: null }, names, GameSample)).toBe(0);
  });
});

describe('logRows', () => {
  it('the filters are LogFilter\'s, in order', () => {
    expect(FILTER_LABELS).toEqual(['All', 'Rolls', 'My units', 'This unit']);
  });

  it('rows past a pending undo fade', () => {
    const { roll, round } = rolled();
    expect(faded(roll, null)).toBe(false);
    expect(faded(round, 1)).toBe(false);
    expect(faded(roll, 2)).toBe(true);                 // seq 3 is past 2
    expect(faded({ ...round, undone: true }, null)).toBe(true);
  });

  it('undo to here skips rolls, undone rows, undo markers and watchers', () => {
    const { block, roll, round } = rolled();
    expect(undoHere(block, false)).toBe(true);
    expect(undoHere(round, false)).toBe(true);
    expect(undoHere(roll, false)).toBe(true);
    expect(undoHere({ ...roll, undone: true }, false)).toBe(false);
    expect(undoHere(roll, true)).toBe(false);
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

  it('a die is drawn at its raw face where the target holds the modifiers, at its total where the roll adds them (RollEntry.Face)', () => {
    const { roll } = rolled();
    const d = { ...roll.dice[0]!, raw: 5, modified: 4 };
    expect(face({ ...roll, record: { ...roll.record, roll: 'defense' } }, d)).toBe(5);
    expect(face({ ...roll, record: { ...roll.record, roll: 'casting', threshold: 4 } }, d)).toBe(4);
  });
});
