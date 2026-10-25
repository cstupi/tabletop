import { describe, expect, it } from 'vitest';
import { PendingDrops } from '../src/lib/play/pendingDrops';
import type { ModelRelocation } from '../src/lib/protocol/wire.generated';

const at = (x: number, y: number) => ({ kind: 'table', placement: { position: { x, y, z: 0 }, yaw: { degrees: 0 } } }) as const;
const move = (id: string, x: number, y: number): ModelRelocation => ({ modelId: id, from: at(0, 0), to: at(x, y) });

describe('PendingDrops', () => {
  it('a drop is held until its move\'s event and a refusal releases it', () => {
    const d = new PendingDrops();
    d.add('t1', [move('a', 3, 4), move('b', 5, 6)]);
    expect(d.holds('a')).toBe(true);
    expect(d.holds('b')).toBe(true);
    expect(d.consume(move('a', 3, 4))).toBe(true);
    expect(d.holds('a')).toBe(false);
    expect(d.holds('b')).toBe(true);
    d.add('t2', [move('c', 1, 1)]);
    expect(d.refuse('t2')).toEqual(['c']);
    expect(d.holds('c')).toBe(false);
    expect(d.holds('b')).toBe(true);
    expect(d.refuse('nothing')).toEqual([]);
  });

  it('consume is true only when the move landed where it was dropped', () => {
    const d = new PendingDrops();
    d.add('t1', [move('a', 3, 4)]);
    expect(d.consume(move('a', 9, 9))).toBe(false);
    expect(d.holds('a')).toBe(false);
    expect(d.consume(move('x', 1, 1))).toBe(false);
  });

  it('a later drop of the same model replaces the earlier one', () => {
    const d = new PendingDrops();
    d.add('t1', [move('a', 1, 1)]);
    d.add('t2', [move('a', 2, 2)]);
    expect(d.refuse('t1')).toEqual([]);
    expect(d.consume(move('a', 2, 2))).toBe(true);
  });
});
